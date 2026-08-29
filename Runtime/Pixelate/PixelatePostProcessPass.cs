// Copyright (c) 2026 CartoonRendering. MIT License.

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace CartoonRendering
{
    /// <summary>
    /// Render Graph pass for the pixelate post process. Reads all effect
    /// parameters from the <see cref="PixelatePostProcess"/> volume component
    /// on the camera's volume stack every frame; does nothing while the
    /// component is missing or inactive (Intensity = 0).
    /// </summary>
    internal sealed class PixelatePostProcessPass : ScriptableRenderPass
    {
        private static class ShaderIds
        {
            public static readonly int PixelSize      = Shader.PropertyToID("_PixelSize");
            public static readonly int ColorLevels    = Shader.PropertyToID("_ColorLevels");
            public static readonly int DitherStrength = Shader.PropertyToID("_DitherStrength");
            public static readonly int BayerMode      = Shader.PropertyToID("_BayerMode");
            public static readonly int Intensity      = Shader.PropertyToID("_Intensity");
        }

        // Keywords matching [KeywordEnum] / shader_feature_local in the shader
        private const string kKeyword4x4 = "_BAYERMODE_BAYER4X4";
        private const string kKeyword8x8 = "_BAYERMODE_BAYER8X8";

        private class PassData
        {
            public Material material;
            public TextureHandle source;
        }

        private readonly Material m_Material;
        private bool m_ShowInSceneView;

        public PixelatePostProcessPass(Material material)
        {
            m_Material = material;
            profilingSampler = new ProfilingSampler("Pixelate Post Process");
        }

        public void Setup(bool showInSceneView, RenderPassEvent passEvent)
        {
            m_ShowInSceneView = showInSceneView;
            renderPassEvent = passEvent;
        }

        // Unity 6 uses Render Graph; the compatibility-mode path is not implemented.
        [Obsolete("Compatibility mode path is not implemented (Unity 6 uses Render Graph).", true)]
        public void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
        }

        /// <inheritdoc/>
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            if (cameraData.isPreviewCamera)
                return;
            if (cameraData.isSceneViewCamera && !m_ShowInSceneView)
                return;

            // ---------- Read parameters from the Volume stack ----------
            VolumeStack stack = VolumeManager.instance.stack;
            PixelatePostProcess volume = stack.GetComponent<PixelatePostProcess>();
            if (volume == null || !volume.IsActive())
                return;

            m_Material.SetFloat(ShaderIds.Intensity, volume.intensity.value);
            m_Material.SetFloat(ShaderIds.PixelSize, volume.pixelSize.value);
            m_Material.SetFloat(ShaderIds.ColorLevels, volume.colorLevels.value);
            m_Material.SetFloat(ShaderIds.DitherStrength, volume.ditherStrength.value);
            m_Material.SetFloat(ShaderIds.BayerMode, (float)volume.bayerMode.value);

            if (volume.bayerMode.value == PixelateBayerMode.Bayer8x8)
            {
                m_Material.DisableKeyword(kKeyword4x4);
                m_Material.EnableKeyword(kKeyword8x8);
            }
            else
            {
                m_Material.DisableKeyword(kKeyword8x8);
                m_Material.EnableKeyword(kKeyword4x4);
            }

            // ---------- Render Graph: copy camera color -> pixelate -> write back ----------
            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid())
                return;

            TextureDesc desc = renderGraph.GetTextureDesc(source);
            desc.name = "_PixelateTarget";
            desc.clearBuffer = false;
            desc.msaaSamples = MSAASamples.None;   // post-process target needs no MSAA
            desc.depthBufferBits = 0;              // no depth attachment
            TextureHandle destination = renderGraph.CreateTexture(desc);

            using (IRasterRenderGraphBuilder builder =
                   renderGraph.AddRasterRenderPass<PassData>("Pixelate Post Process", out PassData passData, profilingSampler))
            {
                passData.material = m_Material;
                passData.source = source;

                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                {
                    // Blitter.BlitTexture binds source as _BlitTexture and draws a
                    // fullscreen triangle with pass 0 of the material.
                    Blitter.BlitTexture(ctx.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                });
            }

            // Redirect the pixelated result as the input of subsequent passes
            // (final blit to backbuffer).
            resourceData.cameraColor = destination;
        }
    }
}
