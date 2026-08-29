// =============================================================================
// CartoonRendering SDF 2D 函数库（UI / 全屏特效通用）
//
// 约定：
//   - 所有 sd* 函数为有符号距离场（Signed Distance Field）：
//       d < 0  → 形状内部
//       d = 0  → 边界
//       d > 0  → 形状外部，|d| ≈ 到边界的最近距离
//   - p 为以形状中心为原点的局部坐标（调用方负责把 UV 转成居中坐标，
//     例如：float2 p = uv - 0.5; p.x *= aspect;）
//   - 所有距离单位与 p 一致（UV 单位），尺寸参数与分辨率无关
// =============================================================================
#ifndef CARTOONRENDERING_SDF2D_INCLUDED
#define CARTOONRENDERING_SDF2D_INCLUDED

// -----------------------------------------------------------------------------
// 基础形状 SDF
// -----------------------------------------------------------------------------

// 圆形：r = 半径
float sdCircle(float2 p, float r)
{
    return length(p) - r;
}

// 方形（轴对齐矩形）：b = 半宽半高 (half extents)
float sdBox(float2 p, float2 b)
{
    float2 d = abs(p) - b;
    return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
}

// 圆角方形：b = 半宽半高，r = 圆角半径
float sdRoundedBox(float2 p, float2 b, float r)
{
    float2 q = abs(p) - b + r;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

// 圆环：r = 中心圆半径，w = 环的半宽
float sdRing(float2 p, float r, float w)
{
    return abs(length(p) - r) - w;
}

// 线段（胶囊体的骨架）：a、b 为两端点，配合 -r 即成胶囊
float sdSegment(float2 p, float2 a, float2 b)
{
    float2 pa = p - a;
    float2 ba = b - a;
    float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
    return length(pa - ba * h);
}

// 胶囊：a、b 为两端点，r = 半径
float sdCapsule(float2 p, float2 a, float2 b, float r)
{
    return sdSegment(p, a, b) - r;
}

// 等边三角形：r = 外接圆半径（iq 公式）
float sdEquilateralTriangle(float2 p, float r)
{
    const float k = 1.7320508; // sqrt(3)
    p.x = abs(p.x) - r;
    p.y = p.y + r / k;
    if (p.x + k * p.y > 0.0) p = float2(p.x - k * p.y, -k * p.x - p.y) / 2.0;
    p.x -= clamp(p.x, -2.0 * r, 0.0);
    return -length(p) * sign(p.y);
}

// -----------------------------------------------------------------------------
// 布尔运算（硬边）
// -----------------------------------------------------------------------------

// 并集：d1 ∪ d2
float opUnion(float d1, float d2) { return min(d1, d2); }

// 差集：d1 - d2（从 d1 中挖掉 d2）
float opSubtract(float d1, float d2) { return max(d1, -d2); }

// 交集：d1 ∩ d2
float opIntersect(float d1, float d2) { return max(d1, d2); }

// -----------------------------------------------------------------------------
// 平滑布尔运算（融球 / 融合过渡）
// k = 融合半径：k 越大过渡带越宽、粘连越明显；k = 0 退化为硬边
// -----------------------------------------------------------------------------

// 平滑并集（融球效果的核心）：两个形状靠近时自动"粘连"
float opSmoothUnion(float d1, float d2, float k)
{
    float h = clamp(0.5 + 0.5 * (d2 - d1) / k, 0.0, 1.0);
    return lerp(d2, d1, h) - k * h * (1.0 - h);
}

// 平滑差集：从 d1 中圆滑地挖掉 d2
float opSmoothSubtract(float d1, float d2, float k)
{
    float h = clamp(0.5 - 0.5 * (d1 + d2) / k, 0.0, 1.0);
    return lerp(d1, -d2, h) + k * h * (1.0 - h);
}

// 平滑交集：两个形状的圆滑相交区域
float opSmoothIntersect(float d1, float d2, float k)
{
    float h = clamp(0.5 - 0.5 * (d2 - d1) / k, 0.0, 1.0);
    return lerp(d2, d1, h) + k * h * (1.0 - h);
}

// -----------------------------------------------------------------------------
// 渲染辅助：把距离场转成可绘制的 alpha / 颜色权重
// aa = 抗锯齿宽度，调用方建议传 fwidth(d) * 1.2 ~ 1.5（屏幕空间自适应）
// -----------------------------------------------------------------------------

// 抗锯齿填充：内部 1，外部 0，边界过渡带宽约 2*aa
float sdfFill(float d, float aa)
{
    return 1.0 - smoothstep(-aa, aa, d);
}

// 抗锯齿描边：以边界为中心、宽度 width 的边线
float sdfStroke(float d, float width, float aa)
{
    return 1.0 - smoothstep(-aa, aa, abs(d) - width * 0.5);
}

// 外侧发光：从边界向外指数衰减，radius 控制衰减距离
float sdfGlow(float d, float radius)
{
    return exp(-max(d, 0.0) / max(radius, 1e-4));
}

#endif // CARTOONRENDERING_SDF2D_INCLUDED
