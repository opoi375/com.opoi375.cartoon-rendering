// Copyright (c) 2026 CartoonRendering. MIT License.

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CartoonRendering
{
    /// <summary>
    /// Adds the pixelate post process to a renderer
    /// (Renderer Features &gt; Add &gt; Pixelate Post Process Feature).
    ///
    /// All effect parameters are driven by the Volume system — add
    /// "CartoonRendering/Pixelate Post Process" to a Volume profile and raise
    /// Intensity above 0 to enable it. The material is resolved lazily from
    /// the PixelatePostProcess shader, so no asset setup is required.
    /// </summary>
    [DisallowMultipleRendererFeature("Pixelate Post Process")]
    public sealed class PixelatePostProcessFeature : ScriptableRendererFeature
    {
        private const string kShaderName = "CartoonRendering/PostProcessing/PixelatePostProcess";

        [Tooltip("Material using the PixelatePostProcess shader. " +
                 "If left empty, the feature builds one automatically at runtime.")]
        public Material material;

        [Tooltip("Injection point. AfterRenderingPostProcessing runs after URP's " +
                 "post chain (Bloom/Tonemapping/AA), so the effect sees the final image.")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

        [Tooltip("Also apply the effect to the Scene view camera.")]
        public bool showInSceneView = true;

        private PixelatePostProcessPass m_Pass;

        /// <inheritdoc/>
        public override void Create()
        {
            if (material == null)
            {
                Shader shader = Shader.Find(kShaderName);
                if (shader == null)
                {
                    Debug.LogError($"[Pixelate] Shader '{kShaderName}' not found. " +
                                   "Make sure Pixelate.shader is imported.");
                    return;
                }
                material = CoreUtils.CreateEngineMaterial(shader);
            }

            m_Pass = new PixelatePostProcessPass(material)
            {
                renderPassEvent = renderPassEvent,
            };
        }

        /// <inheritdoc/>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (m_Pass == null || material == null)
                return;

            if (renderingData.cameraData.cameraType == CameraType.Preview)
                return;

            m_Pass.Setup(showInSceneView, renderPassEvent);
            renderer.EnqueuePass(m_Pass);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            // Only destroy materials the feature created itself.
            CoreUtils.Destroy(material);
            material = null;
            m_Pass = null;
        }
    }
}
