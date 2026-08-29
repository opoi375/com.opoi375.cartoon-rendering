// ============================================================================
// CartoonWaterPlaneCreator.cs
// ----------------------------------------------------------------------------
// Editor tool: creates a subdivided water plane sized in WORLD units.
//
// Why not Unity's built-in Plane? The CartoonWaterSimple shader displaces
// vertices in WORLD space, so a Plane scaled to city scale (100m+) ends up
// with 1 vertex every ~10m - the waves (wavelength ~1m at default _WaveScale)
// can no longer be expressed by the geometry. This tool generates a grid at
// the correct world-space density (segment spacing is derived from size and
// segment count, and vertices are placed in world units, no scaling).
//
// Usage: GameObject > CartoonRendering > Water Plane (subdivided)
// ============================================================================

using UnityEditor;
using UnityEngine;

namespace CartoonRendering.Editor.Water
{
    public class CartoonWaterPlaneCreator : EditorWindow
    {
        private enum WaterMaterial
        {
            Simple = 0,   // CartoonWaterSimple.mat (waves + glint + rim)
            Cartoon = 1,  // CartoonWater.mat (article: depth colour + noise foam)
        }

        private const string SimpleMaterialPath = "Packages/com.opoi375.cartoon-rendering/Materials/CartoonWaterSimple.mat";
        private const string CartoonMaterialPath = "Packages/com.opoi375.cartoon-rendering/Materials/CartoonWater.mat";

        // A wave at _WaveScale = 6 has wavelength 2*PI/6 ~= 1.05m. Keep the
        // segment spacing below ~0.4m (>= 2-3 samples per wavelength) so crests
        // and troughs are properly resolved.
        private float _size = 200f;      // world metres per side
        private int _segments = 512;     // quads per side -> 0.39m spacing
        private WaterMaterial _material = WaterMaterial.Cartoon;

        [MenuItem("GameObject/CartoonRendering/Water Plane (subdivided)")]
        private static void OpenWindow()
        {
            GetWindow<CartoonWaterPlaneCreator>("Water Plane");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Water Plane", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            _size = EditorGUILayout.FloatField("Size (world m)", Mathf.Max(_size, 1f));
            _segments = EditorGUILayout.IntField("Segments", Mathf.Clamp(_segments, 1, 2048));
            _material = (WaterMaterial)EditorGUILayout.EnumPopup("Material", _material);

            float spacing = _size / _segments;
            EditorGUILayout.HelpBox(
                $"Vertex spacing: {spacing:F2} m (wave wavelength ~1.05 m at default _WaveScale).\n" +
                (spacing <= 0.4f
                    ? "Good density for the default wave settings."
                    : "Consider more segments or a larger _WaveScale for this size."),
                spacing <= 0.4f ? MessageType.Info : MessageType.Warning);

            EditorGUILayout.Space(8);

            if (GUILayout.Button("Create Water Plane", GUILayout.Height(28)))
                CreatePlane();
        }

        private void CreatePlane()
        {
            var go = new GameObject("Cartoon Water");
            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            string materialPath = _material == WaterMaterial.Cartoon
                ? CartoonMaterialPath
                : SimpleMaterialPath;
            renderer.sharedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            filter.sharedMesh = BuildPlaneMesh(_size, _segments);

            Undo.RegisterCreatedObjectUndo(go, "Create water plane");
            Selection.activeGameObject = go;
        }

        private static Mesh BuildPlaneMesh(float size, int segments)
        {
            int res = segments + 1;
            int vertCount = res * res;
            int quadCount = segments * segments;

            var vertices = new Vector3[vertCount];
            var normals = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];
            var triangles = new int[quadCount * 6];

            float half = size * 0.5f;
            float step = size / segments;

            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    int i = z * res + x;
                    vertices[i] = new Vector3(-half + x * step, 0f, -half + z * step);
                    normals[i] = Vector3.up;
                    uvs[i] = new Vector2((float)x / segments, (float)z / segments);
                }
            }

            // Two triangles per quad, CCW when viewed from +Y (up).
            int t = 0;
            for (int z = 0; z < segments; z++)
            {
                for (int x = 0; x < segments; x++)
                {
                    int i0 = z * res + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + res;
                    int i3 = i2 + 1;

                    triangles[t++] = i0;
                    triangles[t++] = i2;
                    triangles[t++] = i1;
                    triangles[t++] = i1;
                    triangles[t++] = i2;
                    triangles[t++] = i3;
                }
            }

            var mesh = new Mesh { name = "CartoonWater_Grid" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            return mesh;
        }
    }
}
