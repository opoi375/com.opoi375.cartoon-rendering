using System.Collections.Generic;
using UnityEngine;

namespace CartoonRendering
{
    /// <summary>
    /// 单根草叶 mesh 构建器（纯 C#，无 MonoBehaviour 依赖）。
    ///
    /// 把"一根草"的几何生成独立出来，便于：
    ///   - 单根草预览（GrassBladePreview 放大查看）
    ///   - 草簇复用（GrassField 用多片不同朝向的草叶组成簇）
    ///   - 单元测试（尖端/顶点数/弯曲单调性等纯几何断言）
    ///
    /// 几何约定（unit blade，高 1）：
    ///   - 根在局部 (0,0,0)，尖端在 y=1（沿 +Y 生长）
    ///   - 叶片是单面曲面：每段 2 顶点（左右），尖端收成一点
    ///   - 沿 +Z 预弯（bendAmount 二次弧线），实例 scale 决定最终尺寸
    ///   - TEXCOORD0.x = 方向角编码（用于簇内叶片扇开），
    ///     TEXCOORD0.y = 高度比例 0..1（shader 渐变/弯曲）
    ///   - TEXCOORD1.x = 风相位（每片不同）
    /// </summary>
    public static class GrassMeshBuilder
    {
        /// <summary>
        /// 生成单根草叶 mesh。
        /// </summary>
        /// <param name="segments">弧形分段数（≥2，越多越平滑）</param>
        /// <param name="rootHalfWidth">叶根半宽</param>
        /// <param name="maxHalfWidth">叶身最大半宽（约 30% 高度处）</param>
        /// <param name="bendAmount">预弯量 0..1（尖端 +Z 偏移 = bendAmount × 高）</param>
        /// <param name="directionAngle">叶片朝向角（绕 Y，弧度）；单根草可传任意值</param>
        /// <param name="windPhase">风相位（同角度可传不同值产生差异）</param>
        public static Mesh BuildBlade(
            int segments, float rootHalfWidth, float maxHalfWidth,
            float bendAmount, float directionAngle, float windPhase = 0f)
        {
            segments = Mathf.Max(segments, 2);
            int vertsPerBlade = segments * 2 + 2;
            int trisPerBlade = segments * 2;

            var vertices = new Vector3[vertsPerBlade];
            var normals = new Vector3[vertsPerBlade];
            var uvs = new Vector2[vertsPerBlade];
            var windUvs = new Vector2[vertsPerBlade];
            var triangles = new int[trisPerBlade * 3];

            Vector2 dir = new Vector2(Mathf.Cos(directionAngle), Mathf.Sin(directionAngle));
            // 叶片宽度轴 = 朝向的垂直方向；弯曲沿朝向方向。
            // 修复：旧版顶点不随 directionAngle 旋转，簇内所有叶片共面重叠成一片。
            Vector2 perp = new Vector2(-dir.y, dir.x);

            // 分段顶点（左右成对）
            for (int s = 0; s <= segments; s++)
            {
                float t = (float)s / segments; // 0(根)..1(尖)
                float height = t;

                float halfWidth = BladeHalfWidth(t, rootHalfWidth, maxHalfWidth, s == segments);
                // 弧形弯曲：尖端偏移 = bendAmount × 高 × t²（二次弧线）
                float bendOffset = bendAmount * t * t;

                // 把 (宽度轴, 弯曲轴) 的局部坐标旋转到朝向 dir 的平面内
                int leftIdx = s * 2;
                int rightIdx = s * 2 + 1;
                vertices[leftIdx] = new Vector3(
                    perp.x * -halfWidth + dir.x * bendOffset, height,
                    perp.y * -halfWidth + dir.y * bendOffset);
                vertices[rightIdx] = new Vector3(
                    perp.x * halfWidth + dir.x * bendOffset, height,
                    perp.y * halfWidth + dir.y * bendOffset);

                var n = new Vector3(dir.x, 0.35f, dir.y).normalized;
                normals[leftIdx] = n;
                normals[rightIdx] = n;

                uvs[leftIdx] = new Vector2(directionAngle / 6.2831853f, t);
                uvs[rightIdx] = new Vector2(directionAngle / 6.2831853f, t);
                windUvs[leftIdx] = new Vector2(windPhase, 0f);
                windUvs[rightIdx] = new Vector2(windPhase + 0.3f, 0f);
            }

            // 三角形：每段左右两点 + 下一段左右两点 → 2 三角形
            for (int s = 0; s < segments; s++)
            {
                int l0 = s * 2;
                int r0 = s * 2 + 1;
                int l1 = (s + 1) * 2;
                int r1 = (s + 1) * 2 + 1;

                int t = s * 2 * 3;
                triangles[t + 0] = l0;
                triangles[t + 1] = l1;
                triangles[t + 2] = r0;
                triangles[t + 3] = r0;
                triangles[t + 4] = l1;
                triangles[t + 5] = r1;
            }

            var mesh = new Mesh
            {
                name = "GrassBlade",
                hideFlags = HideFlags.HideAndDontSave
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, windUvs);
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 尖叶轮廓的半宽：根部窄 → 约 30% 高度处最宽 → 尖端收成一点。
        /// 收窄段用 (1-u)^1.5，尖端尖锐自然。
        /// </summary>
        public static float BladeHalfWidth(float t, float rootHalfWidth, float maxHalfWidth, bool isTip)
        {
            t = Mathf.Clamp01(t);
            if (isTip)
            {
                return 0f;
            }
            if (t < 0.3f)
            {
                // 根部 → 最宽处：线性变宽
                return Mathf.Lerp(rootHalfWidth, maxHalfWidth, t / 0.3f);
            }
            // 最宽处 → 尖端：快速收窄
            float u = (t - 0.3f) / 0.7f;
            return maxHalfWidth * Mathf.Pow(1f - u, 1.5f);
        }

        /// <summary>
        /// 构建一个草簇 mesh：多片草叶均匀扇开 360°。
        /// </summary>
        public static Mesh BuildTuft(
            int blades, int segments, float rootHalfWidth, float maxHalfWidth,
            float bendAmount, float rootSpread = 0f)
        {
            blades = Mathf.Max(blades, 1);
            int vertsPerBlade = segments * 2 + 2;
            int trisPerBlade = segments * 2;

            var vertices = new List<Vector3>(blades * vertsPerBlade);
            var normals = new List<Vector3>(blades * vertsPerBlade);
            var uvs = new List<Vector2>(blades * vertsPerBlade);
            var windUvs = new List<Vector2>(blades * vertsPerBlade);
            var triangles = new List<int>(blades * trisPerBlade * 3);

            for (int b = 0; b < blades; b++)
            {
                float angle = (float)b / blades * 2f * Mathf.PI;
                float phase = angle * 5.0f + b * 1.7f;

                // 根部沿叶片朝向向外散开，避免所有叶片共根挤成一束
                Vector3 rootOffset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * rootSpread;

                // 用 BuildBlade 相同的几何，但合并顶点索引
                int baseIndex = vertices.Count;
                var blade = BuildBlade(segments, rootHalfWidth, maxHalfWidth, bendAmount, angle, phase);
                var bladeVerts = blade.vertices;
                if (rootSpread > 0f)
                {
                    for (int i = 0; i < bladeVerts.Length; i++)
                    {
                        bladeVerts[i] += rootOffset;
                    }
                }
                vertices.AddRange(bladeVerts);
                normals.AddRange(blade.normals);
                var bladeUvs = new Vector2[blade.uv.Length];
                var bladeWind = new Vector2[blade.uv2.Length];
                for (int i = 0; i < blade.uv.Length; i++)
                {
                    bladeUvs[i] = blade.uv[i];
                    bladeWind[i] = blade.uv2[i];
                }
                uvs.AddRange(bladeUvs);
                windUvs.AddRange(bladeWind);
                foreach (var tri in blade.triangles)
                {
                    triangles.Add(tri + baseIndex);
                }
            }

            var mesh = new Mesh
            {
                name = "GrassTuft",
                hideFlags = HideFlags.HideAndDontSave
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, windUvs);
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
