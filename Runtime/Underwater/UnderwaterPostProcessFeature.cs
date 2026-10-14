// Copyright (c) 2026 CartoonRendering. MIT License.

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CartoonRendering
{
    /// <summary>
    /// Adds the underwater post process to a renderer. Add it via the menu
    /// <c>Tools &gt; Underwater &gt; Setup Underwater Post Process</c>
    /// (or manually: Renderer Features &gt; Add &gt; Underwater Post Process).
    ///
    /// The material is resolved lazily: if none is assigned, one is built from
    /// the UnderwaterPostProcess shader at runtime, so no asset setup is needed
    /// beyond adding the feature.
    /// </summary>
    public sealed class UnderwaterPostProcessFeature : ScriptableRendererFeature
    {
        private const string kShaderName = "CartoonRendering/PostProcessing/UnderwaterPostProcess";

        [Tooltip("Material using the UnderwaterPostProcess shader. " +
                 "If left empty, the feature builds one automatically at runtime.")]
        public Material material;

        [Tooltip("Injection point. The default runs after transparents and " +
                 "before the post-processing chain, so the water surface itself " +
                 "(a transparent object) is inside the fog too. Moving it before " +
                 "the transparents leaves the surface clean - that used to be " +
                 "forced in Create() and could not be changed from the Inspector.")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        private UnderwaterPostProcessPass m_Pass;

        /// <inheritdoc/>
        public override void Create()
        {
            // 事件点用 Inspector / 序列化里的值，不再在代码里硬覆盖
            m_Pass = new UnderwaterPostProcessPass(null)
            {
                renderPassEvent = renderPassEvent
            };
        }

        /// <inheritdoc/>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // 不激活就根本不进渲染图：这个通道声明了 Depth + Color 依赖，
            // 每帧都挂上去会让不相关的场景白付一次深度图 / 不透明图的拷贝。
            var underwater = VolumeManager.instance.stack.GetComponent<UnderwaterPostProcess>();
            if (underwater == null || !underwater.IsActive())
                return;

            if (!TryResolveMaterial())
                return;

            renderer.EnqueuePass(m_Pass);
        }

        private bool TryResolveMaterial()
        {
            var shader = Shader.Find(kShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[Underwater] 找不到 shader：{kShaderName}");
                return false;
            }

            // The serialized material might reference an older shader; keep it
            // in sync so existing setups survive shader edits.
            if (material != null && material.shader != shader)
                material.shader = shader;

            if (material == null)
                material = new Material(shader) { name = "UnderwaterPostProcess_Auto" };

            m_Pass.SetMaterial(material);
            return true;
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            // The auto-built material is intentionally kept: it is cheap,
            // reused every frame, and disposed with the feature object.
        }
    }
}
