// ============================================================================
// WorldBend.hlsl —— 动森式"小星球"世界弯曲
//
// 原理：以相机为圆心，顶点按水平距离的平方向下弯曲，远处地平线自然垂下，
//       营造"站在圆形小星球上"的视角。纯视觉效果，不影响碰撞/逻辑。
//
// 用法（每个 Pass 的顶点阶段，在 GetVertexPositionInputs 之后注入）：
//     VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
//     posInputs.positionWS = ApplyWorldBend(posInputs.positionWS);
//     posInputs.positionCS = TransformWorldToHClip(posInputs.positionWS);
//
// 全局参数由 WorldBendController 推送（未挂载时默认为 0 = 不弯曲）：
//     _WorldBendCurvature  曲率 = 1/(2R)，R 为等效星球半径（米）
//     _WorldBendDeadZone   相机附近不弯曲的半径（米），防止近处穿帮
//     _WorldBendBendNormals 0/1，是否同步倾斜法线（光照跟随曲面）
// ============================================================================

#ifndef WORLD_BEND_INCLUDED
#define WORLD_BEND_INCLUDED

float _WorldBendCurvature;
float _WorldBendDeadZone;
float _WorldBendBendNormals;

// 计算某世界坐标点的下弯偏移量（纯量，正值 = 应下沉的高度）
float WorldBendOffset(float3 positionWS)
{
    float2 delta = positionWS.xz - _WorldSpaceCameraPos.xz;
    float  d     = length(delta);
    float  w     = max(d - _WorldBendDeadZone, 0.0);
    return w * w * _WorldBendCurvature;
}

// 顶点弯曲：返回下沉后的世界坐标
float3 ApplyWorldBend(float3 positionWS)
{
    positionWS.y -= WorldBendOffset(positionWS);
    return positionWS;
}

// 法线修正：曲面斜率 ≈ 2·curvature·w，沿径向倾斜法线
// （近似解，风格化渲染足够；_WorldBendBendNormals 为 0 时原样返回）
float3 ApplyWorldBendNormal(float3 normalWS, float3 positionWS)
{
    float2 delta  = positionWS.xz - _WorldSpaceCameraPos.xz;
    float  d      = length(delta);
    float  w      = max(d - _WorldBendDeadZone, 0.0);
    float  slope  = 2.0 * _WorldBendCurvature * w * _WorldBendBendNormals;
    float2 radial = d > 1e-4 ? delta / d : float2(0.0, 0.0);
    normalWS.xz  += radial * slope * normalWS.y;
    return normalize(normalWS);
}

#endif
