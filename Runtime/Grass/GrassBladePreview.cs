using UnityEngine;

namespace CartoonRendering
{
    /// <summary>
    /// 单根草预览：把 GrassMeshBuilder.BuildBlade 生成的一根草放大显示，
    /// 方便检查单根草的建模细节（尖端/弧度/宽度）。调试用。
    /// </summary>
    [ExecuteAlways]
    public class GrassBladePreview : MonoBehaviour
    {
        [Header("草叶参数（unit 高度，scale 放大）")]
        [Range(2, 16)] public int Segments = 8;
        [Range(0.002f, 0.06f)] public float RootHalfWidth = 0.008f;
        [Range(0.005f, 0.08f)] public float MaxHalfWidth = 0.02f;
        [Range(0f, 1.2f)] public float BendAmount = 0.55f;
        [Range(0f, 6.2831853f)] public float DirectionAngle = 0f;

        [Header("显示")]
        [Range(0.1f, 5f)] public float Scale = 2f;
        public Material BladeMaterial;
        public bool ShowWireframe = true;

        private Mesh _mesh;
        private Material _wireMaterial;

        // Gizmos 每帧重绘，顶点/三角形数组只在上一次重建时取一次，别每帧再拷两份托管数组
        private Vector3[] _gizmoVerts;
        private int[] _gizmoTris;

        // 上一次重建所用的参数快照
        private int _builtSegments = -1;
        private float _builtRootHalfWidth = float.MinValue;
        private float _builtMaxHalfWidth = float.MinValue;
        private float _builtBendAmount = float.MinValue;
        private float _builtDirectionAngle = float.MinValue;

        private void OnEnable()
        {
            Rebuild();
        }

        private void Update()
        {
            // 只在参数真的变了才重建。这里原来是每帧无条件 Rebuild()，而 [ExecuteAlways]
            // 让 Update 在编辑模式的每一帧都跑 —— 编辑器循环就是这么被"每帧 DestroyImmediate
            // 一个 Mesh + 重新分配一个 Mesh"吃掉的（还会顺带把图形资源拆掉重建）。
            if (Segments == _builtSegments
                && RootHalfWidth == _builtRootHalfWidth
                && MaxHalfWidth == _builtMaxHalfWidth
                && BendAmount == _builtBendAmount
                && DirectionAngle == _builtDirectionAngle) return;
            Rebuild();
        }

        private void Rebuild()
        {
            if (_mesh != null)
            {
                DestroyImmediate(_mesh);
            }
            _mesh = GrassMeshBuilder.BuildBlade(
                Segments, RootHalfWidth, MaxHalfWidth, BendAmount, DirectionAngle);

            _builtSegments = Segments;
            _builtRootHalfWidth = RootHalfWidth;
            _builtMaxHalfWidth = MaxHalfWidth;
            _builtBendAmount = BendAmount;
            _builtDirectionAngle = DirectionAngle;

            // 一次取干净，交给 OnDrawGizmos 反复用
            _gizmoVerts = _mesh != null ? _mesh.vertices : null;
            _gizmoTris = _mesh != null ? _mesh.triangles : null;
        }

        private void OnRenderObject()
        {
            if (_mesh == null || BladeMaterial == null) return;
            var m = Matrix4x4.TRS(transform.position, transform.rotation,
                new Vector3(Scale, Scale, Scale));
            Graphics.DrawMesh(_mesh, m, BladeMaterial, 0);
        }

        private void OnDrawGizmos()
        {
            if (_mesh == null) return;
            if (!ShowWireframe) return;

            if (_wireMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default");
                _wireMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            Gizmos.color = new Color(1f, 0.6f, 0f, 1f);
            var m = Matrix4x4.TRS(transform.position, transform.rotation,
                new Vector3(Scale, Scale, Scale));

            var verts = _gizmoVerts;
            var tris = _gizmoTris;
            if (verts == null || tris == null) return;
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = m.MultiplyPoint3x4(verts[tris[i]]);
                Vector3 b = m.MultiplyPoint3x4(verts[tris[i + 1]]);
                Vector3 c = m.MultiplyPoint3x4(verts[tris[i + 2]]);
                Gizmos.DrawLine(a, b);
                Gizmos.DrawLine(b, c);
                Gizmos.DrawLine(c, a);
            }
        }
    }
}
