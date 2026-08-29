// Copyright (c) 2026 CartoonRendering. MIT License.

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CartoonRendering
{
    /// <summary>
    /// Adds the underwater post process to a renderer. Add it via the menu
    /// "CartoonRendering &gt; Underwater &gt; Setup Underwater Post Process"
    /// (or manually: Renderer Features &gt; Add &gt; Underwater Post Process).
    ///
    /// The material is resolved lazily: if none is assigned, one is built
    /// from the UnderwaterPostProcess shader at runtime, so no asset setup
    /// is required beyond adding the feature.
    /// </summary>
    public sealed class UnderwaterPostProcessFeature : ScriptableRendererFeature
    {
        private const string kShaderName = "CartoonRendering/PostProcessing/UnderwaterPostProcess";

        [Tooltip("Material using the UnderwaterPostProcess shader. " +
                 "If left empty, the feature builds one automatically at runtime.")]
        public Material material;

        [Tooltip("Injection point. BeforeRenderingTransparents runs after opaques + " +
                 "skybox but BEFORE the post-processing chain, so the active " +
                 "colour texture still holds the scene (fetching the source there " +
                 "is reliable). Transparents (e.g. the water plane) render after " +
                 "this pass and stay clean.")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;

        private UnderwaterPostProcessPass m_Pass;

        /// <inheritdoc/>
        public override void Create()
        {
            // Force the early injection point during bring-up: the serialized
            // feature in the renderer asset may still hold the old event.
            m_Pass = new UnderwaterPostProcessPass(null)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents
            };
        }

        /// <inheritdoc/>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!TryResolveMaterial())
                return;

            renderer.EnqueuePass(m_Pass);
        }

        private bool TryResolveMaterial()
        {
            var shader = Shader.Find(kShaderName);
            if (shader == null)
                return false;

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
