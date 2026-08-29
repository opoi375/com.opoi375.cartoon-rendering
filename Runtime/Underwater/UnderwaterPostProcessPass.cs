// Copyright (c) 2026 CartoonRendering. MIT License.

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace CartoonRendering
{
    /// <summary>
    /// RenderGraph full-screen pass for the underwater post process.
    ///
    /// Source: the URP OPAQUE texture (_CameraOpaqueTexture). It is filled and
    /// dependency-tracked by URP itself, so reading it is reliable - unlike
    /// the active colour texture, which is an imported (external) resource and
    /// has NO render-graph dependency, so a copy of it can run before the
    /// opaque render and capture an empty buffer.
    ///
    /// The shader blends opaque pixels toward the underwater tint and outputs
    /// alpha 0 for sky / transparent pixels (no depth), so the skybox and
    /// transparents (e.g. the water plane) stay untouched. The pass runs at
    /// BeforeRenderingTransparents and blends over the active colour.
    /// </summary>
    public sealed class UnderwaterPostProcessPass : ScriptableRenderPass
    {
        private const string kPassName = "UnderwaterPostProcess";

        private static readonly int kColorId = Shader.PropertyToID("_UnderwaterColor");
        private static readonly int kTintStrengthId = Shader.PropertyToID("_TintStrength");
        private static readonly int kIntensityId = Shader.PropertyToID("_Intensity");

        private static readonly int kBlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");

        // DrawProcedural needs a property block; the block is only used
        // synchronously inside the render func, so one shared instance is safe.
        private static readonly MaterialPropertyBlock s_SharedPropertyBlock =
            new MaterialPropertyBlock();
        private static readonly ProfilingSampler kSampler = new ProfilingSampler(kPassName);

        private Material m_Material;

        public UnderwaterPostProcessPass(Material material)
        {
            m_Material = material;
            profilingSampler = kSampler;
        }

        internal void SetMaterial(Material material)
        {
            m_Material = material;
        }

        /// <inheritdoc/>
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourcesData = frameData.Get<UniversalResourceData>();
            if (!resourcesData.cameraOpaqueTexture.IsValid() || !resourcesData.activeColorTexture.IsValid())
                return;

            var underwater = VolumeManager.instance.stack.GetComponent<UnderwaterPostProcess>();
            if (underwater == null || m_Material == null || !underwater.IsActive())
                return;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                       kPassName, out var passData, profilingSampler))
            {
                passData.material = m_Material;
                passData.color = underwater.color.value;
                passData.tintStrength = underwater.tintStrength.value;
                passData.intensity = underwater.intensity.value;

                // Read the URP opaque texture (dependency tracked by URP) and
                // the depth texture (sky / transparent mask).
                builder.UseTexture(resourcesData.cameraOpaqueTexture, AccessFlags.Read);
                if (resourcesData.cameraDepthTexture.IsValid())
                    builder.UseTexture(resourcesData.cameraDepthTexture, AccessFlags.Read);

                builder.SetRenderAttachment(resourcesData.activeColorTexture, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext rgContext) =>
                {
                    // Keep Blit.hlsl's Vert UVs at full range (scaleBias 1,1,0,0).
                    s_SharedPropertyBlock.SetVector(kBlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));

                    data.material.SetColor(kColorId, data.color);
                    data.material.SetFloat(kTintStrengthId, data.tintStrength);
                    data.material.SetFloat(kIntensityId, data.intensity);

                    rgContext.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0,
                        MeshTopology.Triangles, 3, 1, s_SharedPropertyBlock);
                });
            }
        }

        private class PassData
        {
            public Material material;
            public Color color;
            public float tintStrength;
            public float intensity;
        }
    }
}
