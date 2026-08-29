using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CartoonRendering.Editor
{
    /// <summary>
    /// 卡通草地一键搭建工具：创建 GrassField 对象、材质与交互烘焙材质，
    /// 并自动绑定当前场景的 Main Camera / 玩家。
    /// 菜单：Tools → 草地 → 创建卡通交互草地
    /// </summary>
    public static class GrassFieldTool
    {
        private const string GrassShader = "CartoonRendering/Grass/CartoonGrass";
        private const string BakeShader = "CartoonRendering/Grass/GrassInteractionBake";
        private const string MaterialFolder = "Assets/CartoonRendering/Materials/Grass";

        [MenuItem("Tools/Grass/Create Cartoon Grass Field (Small Area)")]
        public static void CreateGrassField()
        {
            var grassObj = new GameObject("GrassField");
            var field = grassObj.AddComponent<GrassField>();

            // 默认小区域：8m×8m，方便先验证效果
            field.BoundsSize = new Vector2(8f, 8f);
            field.DensityPerSquareMeter = 4f;
            field.Seed = 42;
            field.MinHeight = 0.4f;
            field.MaxHeight = 0.9f;

            // 材质
            var grassMat = CreateMaterial("Grass_Cartoon", GrassShader);
            field.GrassMaterial = grassMat;

            var bakeMat = CreateMaterial("Grass_InteractionBake", BakeShader);
            field.InteractionBakeMaterial = bakeMat;

            // 交互源：优先玩家，其次 Main Camera
            var player = FindPlayer();
            if (player != null)
            {
                field.InteractionSource = player;
            }
            else
            {
                var cam = Camera.main;
                if (cam != null) field.InteractionSource = cam.transform;
            }

            EditorSceneManager.MarkSceneDirty(grassObj.scene);
            Selection.activeGameObject = grassObj;
            Debug.Log($"[GrassFieldTool] 已在场景创建 GrassField（8m×8m），交互源: {(field.InteractionSource != null ? field.InteractionSource.name : "未绑定")}");
        }

        /// <summary>
        /// 查找玩家：优先名字包含 Player/玩家 的 Transform，其次 CharacterController，
        /// 再退回 Camera.main。
        /// </summary>
        private static Transform FindPlayer()
        {
            var controllers = Object.FindObjectsByType<CharacterController>(FindObjectsInactive.Exclude);
            if (controllers.Length > 0)
            {
                return controllers[0].transform;
            }

            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude))
            {
                if (go.transform.parent == null && (go.name.Contains("Player") || go.name.Contains("玩家")))
                {
                    return go.transform;
                }
            }

            var cam = Camera.main;
            return cam != null ? cam.transform : null;
        }

        private static Material CreateMaterial(string name, string shaderName)
        {
            // 若已存在则复用
            string path = $"{MaterialFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[GrassFieldTool] 找不到 Shader: {shaderName}");
                return null;
            }

            var mat = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(mat, path);
            return AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        /// <summary>
        /// 创建单根草预览：放大显示一根草，方便检查草叶建模细节。
        /// </summary>
        [MenuItem("Tools/Grass/Create Single Blade Preview")]
        public static void CreateSingleBladePreview()
        {
            var go = new GameObject("GrassBladePreview");
            var preview = go.AddComponent<GrassBladePreview>();

            var mat = CreateMaterial("Grass_Cartoon", GrassShader);
            if (mat != null) preview.BladeMaterial = mat;

            // 放大到 2 倍，方便查看
            preview.Scale = 2f;

            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;
            Debug.Log("[GrassFieldTool] 已创建单根草预览（Scale=2），在 Scene 视图查看；选中时显示线框");
        }
    }
}
