// ============================================================================
// LedCurvedScreen.cs
// ----------------------------------------------------------------------------
// 程序化生成一块弧形 LED 屏的网格。
//
// 要点：UV.x 按【弧长】均匀展开，而不是按平面投影。
// 这样 LED 点在弧面上依然等距，文字不会被两头拉长。

using UnityEngine;

namespace CartoonRendering
{
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class LedCurvedScreen : MonoBehaviour
    {
        [Tooltip("弧面展开后的总宽度（世界单位，等于弧长）")]
        [SerializeField] float width = 12f;

        [Tooltip("屏幕高度（世界单位）")]
        [SerializeField] float height = 3f;

        [Tooltip("总张角（度）。正数 = 凹面朝向相机，负数 = 向外凸出")]
        [SerializeField, Range(-120f, 120f)] float arcAngle = 26f;

        [Tooltip("横向分段数，越大弧面越平滑")]
        [SerializeField, Range(2, 256)] int segments = 64;

        [SerializeField] bool rebuildOnValidate = true;

        Mesh _mesh;

        /// <summary>弧长（世界单位），可以当作屏幕的实际宽度用。</summary>
        public float ArcLength => Mathf.Max(width, 1e-3f);

        /// <summary>文字的宽高比，直接喂给材质的 _Aspect。</summary>
        public float Aspect => ArcLength / Mathf.Max(height, 1e-3f);

        void OnEnable()
        {
            Rebuild();
        }

        void OnValidate()
        {
            if (rebuildOnValidate) Rebuild();
        }

        [ContextMenu("Rebuild Mesh")]
        public void Rebuild()
        {
            var mf = GetComponent<MeshFilter>();
            if (mf == null) return;

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "LedCurvedScreen" };
                _mesh.MarkDynamic();
            }

            int n = Mathf.Clamp(segments, 2, 256);
            int cols = n + 1;
            var verts = new Vector3[cols * 2];
            var uvs = new Vector2[cols * 2];

            float angleRad = arcAngle * Mathf.Deg2Rad;
            // 弧长 = R * θ  →  R = width / θ；θ 可以取负数，半径符号跟着翻转
            float radius = Mathf.Abs(angleRad) < 1e-4f
                ? float.PositiveInfinity
                : width / angleRad;

            for (int i = 0; i < cols; i++)
            {
                float t = (float)i / n;                       // 0..1，沿弧长线性
                float a = (t - 0.5f) * angleRad;              // -θ/2 .. +θ/2

                float x, z;
                if (float.IsInfinity(radius))
                {
                    x = (t - 0.5f) * width;                   // 退化成平板
                    z = 0f;
                }
                else
                {
                    x = radius * Mathf.Sin(a);
                    z = -radius * (1f - Mathf.Cos(a));        // 两端朝 -Z（相机方向）收拢
                }

                verts[i * 2 + 0] = new Vector3(x, -height * 0.5f, z);
                verts[i * 2 + 1] = new Vector3(x, +height * 0.5f, z);
                uvs[i * 2 + 0] = new Vector2(t, 0f);
                uvs[i * 2 + 1] = new Vector2(t, 1f);
            }

            var tris = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                int b = i * 2;
                tris[i * 6 + 0] = b + 0;
                tris[i * 6 + 1] = b + 2;
                tris[i * 6 + 2] = b + 1;
                tris[i * 6 + 3] = b + 1;
                tris[i * 6 + 4] = b + 2;
                tris[i * 6 + 5] = b + 3;
            }

            _mesh.Clear();
            _mesh.vertices = verts;
            _mesh.uv = uvs;
            _mesh.triangles = tris;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();

            // 弧形屏要面向 -Z 的相机；不同 winding 约定下法线可能翻掉，这里自纠正
            var normals = _mesh.normals;
            if (normals.Length > 0 && normals[cols / 2].z > 0f)
            {
                for (int i = 0; i < tris.Length; i += 3)
                {
                    (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
                }
                _mesh.triangles = tris;
                _mesh.RecalculateNormals();
            }

            mf.sharedMesh = _mesh;
        }

        void OnDestroy()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh);
            _mesh = null;
        }
    }
}
