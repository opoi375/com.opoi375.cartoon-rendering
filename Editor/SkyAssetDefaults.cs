// Copyright (c) 2026 CartoonRendering. MIT License.
//
// 新建的 CartoonProceduralSky 资产里，两张云噪声图都是空的（CreateAssetMenu
// 不会填引用），而体积云一旦缺 3D 形状图就"什么都不画且不报错"。这里在编辑期
// 把包内自带的两张图补上；引用会随资产一起序列化，运行时不再经过这段代码。
//
// 只在字段为 null 时动手，并且只在真的改了之后 SetDirty —— 否则每次进编辑器
// 都会把全部天空预设标脏。

using UnityEditor;
using UnityEngine;

namespace CartoonRendering.EditorTools
{
    [InitializeOnLoad]
    internal static class SkyAssetDefaults
    {
        private const string kNoise3D   = "Packages/com.opoi375.cartoon-rendering/Textures/CloudNoise3D.asset";
        private const string kShape2D   = "Packages/com.opoi375.cartoon-rendering/Textures/CloudShape2D.asset";

        static SkyAssetDefaults()
        {
            // 域重载 / 打开工程时跑一次，晚一帧避开 AssetDatabase 尚未就绪的窗口。
            EditorApplication.delayCall += Fill;
        }

        internal static void Fill()
        {
            var noise  = AssetDatabase.LoadAssetAtPath<Texture3D>(kNoise3D);
            var shape  = AssetDatabase.LoadAssetAtPath<Texture2D>(kShape2D);
            if (noise == null && shape == null) return;

            int fixedCount = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:CartoonProceduralSky"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var sky  = AssetDatabase.LoadAssetAtPath<CartoonProceduralSky>(path);
                if (sky == null) continue;

                bool dirty = false;
                var so = new SerializedObject(sky);

                var noiseProp = so.FindProperty("volCloudNoiseTexture");
                if (noise != null && noiseProp != null && noiseProp.objectReferenceValue == null)
                {
                    noiseProp.objectReferenceValue = noise;
                    dirty = true;
                }
                var shapeProp = so.FindProperty("volCloudShapeTexture");
                if (shape != null && shapeProp != null && shapeProp.objectReferenceValue == null)
                {
                    shapeProp.objectReferenceValue = shape;
                    dirty = true;
                }

                if (dirty)
                {
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(sky);
                    fixedCount++;
                }
                so.Dispose();
            }

            if (fixedCount > 0)
                Debug.Log($"[SkyAssetDefaults] 为 {fixedCount} 个 CartoonProceduralSky 预设补上了缺少的云噪声引用。");
        }
    }
}
