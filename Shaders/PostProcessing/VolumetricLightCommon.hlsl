// ============================================================================
// VolumetricLightCommon.hlsl
// ----------------------------------------------------------------------------
// 体积光的共享实现：光线重建、3D 值噪声、主光阴影采样、双边上采样合成。
// 被 VolumetricLight.shader 的三个 Pass 共用。

#ifndef CARTOON_VOLUMETRIC_LIGHT_COMMON_INCLUDED
#define CARTOON_VOLUMETRIC_LIGHT_COMMON_INCLUDED

// ----------------------------------------------------------------------------
//  由 VolumetricLightPass 每帧写入的全局 uniform
// ----------------------------------------------------------------------------
float4 _VolTint;            // rgb = 散射着色
float  _VolIntensity;       // 总开关强度
float  _VolDensity;         // 散射系数
float  _VolMaxDistance;     // 最远步进距离（米）
float  _VolDistanceFade;    // 从最远距离的百分之几处开始淡出（0..1）
float  _VolAnisotropy;      // HG 相位函数 g（正数 = 前向散射）
float  _VolHeightStart;     // 高度雾起始高度（世界 Y）
float  _VolHeightFalloff;   // 高度雾衰减系数，0 = 关闭
float  _VolShadowStrength;  // 阴影对散射的压制比例
float  _VolShadowBias;      // 阴影采样沿光线方向的偏移
float  _VolSteps;           // 步进次数
float  _VolJitter;          // 步进起点抖动强度（消条带）
float  _VolNoiseStrength;   // 尘埃噪声强度
float  _VolNoiseScale;      // 噪声频率
float  _VolNoiseSpeed;      // 噪声漂移速度
float  _VolBanding;         // 卡通分层档数，< 1.5 表示关闭
float  _VolSoftness;        // 双边上采样的抽头间距（1 = 正好相邻）
float  _VolDebug;           // 0 = 正常，见 VolumetricLightDebug

float4 _VolTexelSize;       // xy = 1/低分辨率尺寸，zw = 低分辨率宽高

TEXTURE2D(_VolTex);
SAMPLER(sampler_VolTex);

// 深度存进 alpha 通道时的上限（天空像素的 eye depth 会非常大）
#define kVolMaxStoredDepth 1.0e4

// ----------------------------------------------------------------------------
//  视图空间光线重建
// ----------------------------------------------------------------------------
// 透视相机：从相机位置出发的视线
// 正交相机：沿视图 -Z 平行推进（此时相机位置不在光线上，不能拿它当原点）
struct VolRay
{
    float3 originWS;    // t = 近平面 处的世界坐标
    float3 dirWS;       // 世界空间单位方向
    float  tScene;      // 近平面 → 场景几何体 的距离
    float  eyeZ;        // 场景的视图空间深度（正数），用来做深度感知滤波
};

VolRay BuildVolRay(float2 uv, float rawDepth)
{
    VolRay r;

    float3 posWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
    float3 posVS = mul(UNITY_MATRIX_V, float4(posWS, 1.0)).xyz;

    r.eyeZ = -posVS.z;

    float tNear = _ProjectionParams.y;
    bool  persp = IsPerspectiveProjection();

    // z 加个下限，防止相机正好贴在几何体上时 normalize(0) 出 NaN
    float3 viewVec = float3(posVS.xy, min(posVS.z, -1.0e-4));
    float3 dirVS    = persp ? normalize(viewVec) : float3(0.0, 0.0, -1.0);
    float3 originVS = persp ? dirVS * tNear : float3(posVS.xy, -tNear);

    r.dirWS    = normalize(mul((float3x3)UNITY_MATRIX_I_V, dirVS));
    r.originWS = mul(UNITY_MATRIX_I_V, float4(originVS, 1.0)).xyz;
    r.tScene   = max((persp ? length(posVS) : r.eyeZ) - tNear, 0.0);

    return r;
}

// ----------------------------------------------------------------------------
//  3D 值噪声（单倍频，8 次 hash）
// ----------------------------------------------------------------------------
float VolHash13(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

float VolValueNoise3D(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);      // 平滑插值

    float n000 = VolHash13(i + float3(0.0, 0.0, 0.0));
    float n100 = VolHash13(i + float3(1.0, 0.0, 0.0));
    float n010 = VolHash13(i + float3(0.0, 1.0, 0.0));
    float n110 = VolHash13(i + float3(1.0, 1.0, 0.0));
    float n001 = VolHash13(i + float3(0.0, 0.0, 1.0));
    float n101 = VolHash13(i + float3(1.0, 0.0, 1.0));
    float n011 = VolHash13(i + float3(0.0, 1.0, 1.0));
    float n111 = VolHash13(i + float3(1.0, 1.0, 1.0));

    float nx00 = lerp(n000, n100, f.x);
    float nx10 = lerp(n010, n110, f.x);
    float nx01 = lerp(n001, n101, f.x);
    float nx11 = lerp(n011, n111, f.x);

    return lerp(lerp(nx00, nx10, f.y), lerp(nx01, nx11, f.y), f.z);
}

// ----------------------------------------------------------------------------
//  主光阴影采样
// ----------------------------------------------------------------------------
// MAIN_LIGHT_CALCULATE_SHADOWS 由 Shadows.hlsl 依据 _MAIN_LIGHT_SHADOWS /
// _MAIN_LIGHT_SHADOWS_CASCADE 定义；关键字没接上时直接退化成"全亮"，
// 效果变成均匀体积雾 —— 用 Volume 的 debug = Shadow 能一眼看出来。
float SampleMainShadow(float3 posWS)
{
#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    // 沿指向光源的方向偏一点：步进采样点常常紧贴几何体，不偏会闪
    float3 biased = posWS + _MainLightPosition.xyz * _VolShadowBias;
    float  atten  = MainLightRealtimeShadow(TransformWorldToShadowCoord(biased));
    return lerp(1.0, atten, saturate(_VolShadowStrength));
#else
    return 1.0;
#endif
}

// ----------------------------------------------------------------------------
//  双边上采样 + 合成
// ----------------------------------------------------------------------------
// 深度差异越大权重越低，避免前景几何体的光柱糊到背景上（光晕渗色）。
float VolDepthWeight(float tapZ, float fullZ)
{
    float dz = (tapZ - fullZ) / max(_VolMaxDistance, 1.0);
    return 1.0 / (1.0 + dz * dz * 256.0);
}

half3 VolComposite(float2 uv)
{
    float fullZ = min(LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams), kVolMaxStoredDepth);

    float2 invLow = _VolTexelSize.xy;
    float  spread = max(_VolSoftness, 0.25);

    // 3x3 帐篷核 × 深度相似度。
    // 只用 4 抽头的话消不掉静态抖动产生的细密格纹，光柱上会看到明显的"布纹"。
    static const float kSpatial[9] =
    {
        0.30, 0.55, 0.30,
        0.55, 1.00, 0.55,
        0.30, 0.55, 0.30
    };

    half3 acc = half3(0.0, 0.0, 0.0);
    float wsum = 0.0;
    int   k = 0;

    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            float2 suv = uv + float2((float)x, (float)y) * spread * invLow;
            half4  s   = SAMPLE_TEXTURE2D(_VolTex, sampler_VolTex, suv);
            float  w   = kSpatial[k] * VolDepthWeight(s.a, fullZ);

            acc  += s.rgb * w;
            wsum += w;
            k++;
        }
    }

    acc /= max(wsum, 1.0e-5);

    // ---- 卡通分层：把亮度量化成若干档，得到漫画式的硬边光带 ----
    if (_VolBanding >= 1.5)
    {
        float steps = floor(_VolBanding + 0.5);
        float lum   = dot(acc, float3(0.2126, 0.7152, 0.0722));
        if (lum > 1.0e-5)
            acc *= (floor(lum * steps) / steps) / lum;
    }

    return acc;
}

#endif // CARTOON_VOLUMETRIC_LIGHT_COMMON_INCLUDED
