// ============================================================================
// UnderwaterPostProcess.shader
// ----------------------------------------------------------------------------
// URP 水下后期（RenderGraph 单 Pass）。
//
// 画面由两个互相独立的项拼出来 —— 这两项分开写，才是"水"和"一层青色蒙版"的
// 区别：
//
//   1) 逐通道吸收（out-scattering）  T = exp(-absorptionRate·absorptionColor·d)
//      红光在水中先被吃掉，绿其次，蓝最后。所以远处的物体不是"变淡"，而是
//      自己就往蓝绿偏 —— 不需要靠雾色硬涂。
//   2) 内散射（in-scattering）        f = tintStrength·(1 - exp(-fogDensity·d))
//      水体本身被照亮后加进来的那层颜色。浅水色与深水色按"在水面下多深"插值
//      （水面以下越深，太阳光越少），所以低头看脚下和远望是两种颜色。
//
// 混合模式为什么是 Blend One OneMinusSrcAlpha：
//   这个通道【不能读 activeColor】—— 它在 RenderGraph 里是外部导入资源，没有
//   依赖关系，读了可能拿到空缓冲（本包体积光那边踩过，注释写得很清楚）。
//   One OneMinusSrcAlpha 的语义是 result = src + dst·(1-src.a)，于是：
//       src.a   = f                                  → 整屏按雾量衰减
//       src.rgb = opaqueDistorted·T + fog·f - opaque·(1-f)
//   合起来 result = (dst - opaque)·(1-f) + opaque·T + fog·f
//   也就是：不透明像素享受【逐通道吸收 + 扭曲】，叠在上面的透明层（水面本身、
//   粒子）享受【标量雾】。src.rgb 里那一项是负的，但硬件加完 dst 之后结果恒非负，
//   所以不会被无符号浮点缓冲截掉。
//
// 焦散为什么用法线三向投影：水下的亮带必须"贴在表面上"才像光，屏幕空间的
// 一层花纹一眼假。这里不额外要法线图 —— 世界坐标对屏幕坐标求导（ddx/ddy）
// 叉乘出来的就是这个像素的表面法线，一分钱不花。
//
// 顶点着色器是本文件自带的：不用 Blit.hlsl 就不用拖进 TextureXR（两者都会
// 定义 FRAMEBUFFER_INPUT_X_* 会撞出重复宏警告），也不用每帧喂 _BlitScaleBias。
//
// 【重要】这个通道必须声明 ConfigureInput(Depth | Color)。C# 侧（UnderwaterPostProcessPass）
// 已经声明了 —— 以前没声明，于是 RenderGraph 根本没把深度图绑进来，
// SampleSceneDepth 恒为 0，全部像素被判成"天空"直接 return alpha 0：
// 效果一次都没有画出来，而且不报任何错。Debug/SkyMask 视图就是用来当场
// 验这一条的（整屏白 = 深度没接上）。
// ============================================================================
Shader "CartoonRendering/PostProcessing/UnderwaterPostProcess"
{
    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        ZWrite Off
        ZTest Always
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // ---------------------------------------------------------------
        //  自带全屏三角形顶点着色器
        // ---------------------------------------------------------------
        struct UWAttributes
        {
            uint vertexID : SV_VertexID;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct UWWaryings
        {
            float4 positionCS : SV_POSITION;
            float2 uv         : TEXCOORD0;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        UWWaryings UWVert(UWAttributes input)
        {
            UWWaryings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            // vertexID 0/1/2 → uv (0,0) (2,0) (0,2) → 覆盖整个裁剪空间的大三角形
            float2 uv = float2((input.vertexID << 1) & 2, input.vertexID & 2);
            output.positionCS = float4(uv * 2.0 - 1.0, UNITY_NEAR_CLIP_VALUE, 1.0);
            output.uv = uv;
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "UnderwaterPostProcess"

            // result = src + dst·(1 - src.a)，见文件头
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex   UWVert
            #pragma fragment UWFrag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            // ---- 由 UnderwaterPostProcessPass 每帧写入（Volume 值）----
            float  _UWIntensity;         // 总开关强度，0 = 关
            float4 _UWFogColor;          // 浅水雾色
            float4 _UWDeepColor;         // 深水雾色
            float  _UWDepthToFullColor;  // 水面下多深完全转成深水色（米）
            float  _UWTintStrength;      // 雾量上限 0..1
            float  _UWFogDensity;        // 内散射系数（每米）
            float3 _UWAbsorption;        // 逐通道吸收权重（不是颜色，不做 sRGB 转换）
            float  _UWAbsorptionRate;    // 吸收基准（每米）
            float  _UWSurfaceY;          // 水面的世界 Y
            float  _UWCaustics;          // 焦散强度
            float  _UWCausticsScale;     // 焦散频率（每米周期数）
            float  _UWCausticsSpeed;     // 焦散漂移速度
            float  _UWCausticsDepthFade; // 焦散存活深度（米）
            float  _UWWaveStrength;      // 扭曲幅度（UV 单位）
            float  _UWWaveScale;         // 扭曲频率
            float  _UWWaveSpeed;         // 扭曲速度
            float  _UWVignette;          // 暗角
            float  _UWBanding;           // 卡通分层档数，< 1.5 = 关
            float  _UWDebug;             // 见 UnderwaterDebug
            float  _UWHasOpaque;         // 1 = 不透明图可用（能做逐通道吸收与扭曲）
            float4 _UWSceneScaleBias;    // 屏幕 uv → 场景纹理 uv（见 UWSceneUV）

            // 天空判定：没有场景几何体的像素
            bool UWIsSky(float rawDepth)
            {
            #if UNITY_REVERSED_Z
                return rawDepth < 1e-5;
            #else
                return rawDepth > 1.0 - 1e-5;
            #endif
            }

            /// 屏幕 uv → 场景纹理（深度图 / 不透明图）的采样坐标。
            ///
            /// 本通道注入在 BeforeRenderingPostProcessing，此时要写的目标与相机
            /// 直接渲染出来的场景纹理在 D3D/Metal 上差一次 Y 翻转（URP 自己的全屏
            /// pass 全都靠 _BlitScaleBias 处理这件事，而那个全局值只保证在 URP 的
            /// blit 里是对的，不能拿来就用）。不修正的症状很典型：雾会糊在【近处】、
            /// 远处反而清楚，还会把画面上下镜像叠进合成式里。
            /// 值由 C# 侧按 SystemInfo.graphicsUVStartsAtTop 算好传进来。
            ///
            /// 注意：深度、不透明图、以及喂给 ComputeWorldSpacePosition 的 NDC
            /// 必须用【同一个】翻转后的 uv，否则重建出来的世界坐标和颜色对不上。
            float2 UWSceneUV(float2 uv)
            {
                return uv * _UWSceneScaleBias.xy + _UWSceneScaleBias.zw;
            }

            /// 焦散图案：三组不同方向的波叠加，取"合力为零"的那几条线 ——
            /// 水面把光聚成网状亮带就是这个形态。pow 把亮带收窄。
            float UWCausticPattern(float2 p, float t)
            {
                float a = sin(dot(p, float2( 0.866, 0.500)) + t);
                float b = sin(dot(p, float2(-0.500, 0.866)) * 1.31 - t * 0.83);
                float c = sin(dot(p, float2( 0.342,-0.940)) * 0.77 + t * 1.17);
                float m = clamp(1.0 - abs(a + b + c) * 0.42, 0.0, 1.0);
                return m * m * m;
            }

            half4 UWFrag(UWWaryings input) : SV_Target
            {
                float2 uv = input.uv;
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // 之后所有"读场景"的坐标都走 sceneUV；只有暗角这类
                // 纯屏幕空间的项继续用目标自己的 uv。
                float2 sceneUV = UWSceneUV(uv);

                float rawDepth = SampleSceneDepth(sceneUV);
                bool  hasScene = !UWIsSky(rawDepth);

                float3 posWS;
                float  dist;
                if (hasScene)
                {
                    dist  = min(LinearEyeDepth(rawDepth, _ZBufferParams), 5000.0);
                    posWS = ComputeWorldSpacePosition(sceneUV, rawDepth, UNITY_MATRIX_I_VP);
                }
                else
                {
                    // 没有几何体（天空，或者只被透明水面盖住）：拿“这段水柱有多长”当距离。
                    // 抬头看：正上方水柱短，光透得过来（Snell 窗口）；平视则水柱极长，全是雾。
                    // 比“无脑按无穷远铺满雾”更像水，也不会把水面整块抹平。
                    float3 nearPos = ComputeWorldSpacePosition(sceneUV, UNITY_NEAR_CLIP_VALUE, UNITY_MATRIX_I_VP);
                    float3 rayDir  = normalize(nearPos - _WorldSpaceCameraPos);
                    dist = rayDir.y > 1e-3
                        ? min((_UWSurfaceY - _WorldSpaceCameraPos.y) / rayDir.y, 5000.0)
                        : 5000.0;
                    posWS = _WorldSpaceCameraPos + rayDir * dist;   // 落在水面上
                }

                float depthBelow = max(0.0, _UWSurfaceY - posWS.y);

                // ---- 1. 内散射雾量（标量）----
                float f = _UWTintStrength * (1.0 - exp(-_UWFogDensity * dist));
                if (_UWBanding >= 1.5)
                {
                    float n = floor(_UWBanding + 0.5);
                    f = floor(clamp(f, 0.0, 1.0) * n) / n;
                }
                f = clamp(f, 0.0, 1.0) * clamp(_UWIntensity, 0.0, 4.0);

                // ---- 2. 逐通道吸收 ----
                float3 sigma = max(_UWAbsorption, 0.0) * max(_UWAbsorptionRate, 0.0);
                float3 T = exp(-sigma * dist);

                // ---- 3. 雾色按"在水面下多深"下沉 ----
                float deepT = clamp(depthBelow / max(_UWDepthToFullColor, 1e-3), 0.0, 1.0);
                float3 fog  = lerp(_UWFogColor.rgb, _UWDeepColor.rgb, deepT);

                // ---- 调试视图：直接替换屏幕内容（alpha = 1 → dst 被清零）----
                if (_UWDebug > 0.5)
                {
                    float3 vis = float3(0.0, 0.0, 0.0);
                    if (_UWDebug < 1.5)      vis = clamp(dist / 60.0, 0.0, 1.0).xxx;
                    else if (_UWDebug < 2.5) vis = f.xxx;
                    else if (_UWDebug < 3.5)
                    {
                        float3 wdbg = abs(normalize(cross(ddy(posWS), ddx(posWS))));
                        wdbg /= max(wdbg.x + wdbg.y + wdbg.z, 1e-4);
                        float tdbg = _Time.y * _UWCausticsSpeed * _UWCausticsScale;
                        float cdbg = UWCausticPattern(posWS.zy * _UWCausticsScale, tdbg) * wdbg.x
                                   + UWCausticPattern(posWS.xz * _UWCausticsScale, tdbg) * wdbg.y
                                   + UWCausticPattern(posWS.xy * _UWCausticsScale, tdbg) * wdbg.z;
                        vis = cdbg * clamp(1.0 - depthBelow / max(_UWCausticsDepthFade, 1e-3), 0.0, 1.0).xxx;
                        if (!hasScene) vis = float3(0.0, 0.0, 0.0);
                    }
                    else if (_UWDebug < 4.5) vis = T;
                    else                     vis = hasScene ? float3(0.0, 0.0, 0.0) : float3(1.0, 1.0, 1.0);
                    return half4(vis, 1.0);
                }

                // ---- 没有场景深度（天空 / 只有透明物体）：整像素铺雾 ----
                if (!hasScene)
                    return half4(fog * f, f);

                // ---- 4. 焦散：贴在表面上，随深度消失 ----
                float3 nrm = normalize(cross(ddy(posWS), ddx(posWS)));
                float3 w = abs(nrm);
                w /= max(w.x + w.y + w.z, 1e-4);

                float ct = _Time.y * _UWCausticsSpeed * _UWCausticsScale * 6.28318;
                // scale 的语义是【每米周期数】，sin() 里要的是弧度，不乘 2π 会把
                // 0.55 当成 0.55 弧度/米 → 周期 11 米，亮带糊成一整片白。
                float3 p = posWS * (_UWCausticsScale * 6.28318);
                float caustic = UWCausticPattern(p.zy, ct) * w.x
                              + UWCausticPattern(p.xz, ct) * w.y
                              + UWCausticPattern(p.xy, ct) * w.z;
                float caustFade = clamp(1.0 - depthBelow / max(_UWCausticsDepthFade, 1e-3), 0.0, 1.0);
                // 焦散不能贴在【水面上】：刚离开水面的光还没被聚成亮带，
                // 而水面本身（透明，但会写深度）会接到一份满强度的花纹，
                // 看起来就是天花板在发白 —— 水面下头 0.6 米内淡入。
                caustFade *= smoothstep(0.0, 0.6, depthBelow);
                float3 caustics = caustic * caustFade * max(_UWCaustics, 0.0)
                                * lerp(fog, float3(0.30, 0.52, 0.60), 0.7) * 0.35;
                caustics *= clamp(_UWIntensity, 0.0, 4.0);

                // ---- 5. 合成 ----
                float3 src;
                if (_UWHasOpaque > 0.5)
                {
                    float3 opaquePlain = SampleSceneColor(sceneUV);
                    float3 opaqueHere  = opaquePlain;

                    if (_UWWaveStrength > 0.0)
                    {
                        // 贴脸的几何体不要抖（抖起来像坏掉的屏幕），随距离长出来
                        float t   = _Time.y * _UWWaveSpeed;
                        float s   = _UWWaveScale;
                        float2 w1 = float2(sin(uv.y * 24.0 * s + t * 1.7), sin(uv.x * 21.0 * s - t * 1.3));
                        float2 w2 = float2(sin(uv.x * 11.0 * s - t * 0.7), sin(uv.y *  9.0 * s + t * 0.9));
                        float2 off = (w1 * 0.7 + w2 * 0.35) * _UWWaveStrength
                                   * clamp(dist * 0.25, 0.0, 1.0) * clamp(_UWIntensity, 0.0, 4.0);
                        opaqueHere = SampleSceneColor(sceneUV + off * float2(_UWSceneScaleBias.x, _UWSceneScaleBias.y));
                    }

                    // result = (dst - opaque)·(1-f) + opaque·T + fog·f  →  见文件头
                    src = opaqueHere * T + fog * f - opaquePlain * (1.0 - f);
                }
                else
                {
                    // 不透明图不可用：退化成标量雾（仍然有距离梯度，只是没有
                    // 逐通道吸收与扭曲）。管线资产把 Opaque Texture 打开即可恢复。
                    src = fog * f;
                }

                // 焦散加在雾之上：远处雾厚，本来就不该看得见亮带
                src += caustics * (1.0 - f);

                // 暗角按雾量生效，只在浑水里有存在感
                float2 vc = uv - 0.5;
                src -= f * _UWVignette * dot(vc, vc) * 1.6;

                return half4(src, f);
            }
            ENDHLSL
        }
    }
}
