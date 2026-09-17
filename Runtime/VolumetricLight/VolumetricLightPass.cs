// ============================================================================
// VolumetricLightPass.cs
// ----------------------------------------------------------------------------
// 体积光的 RenderGraph 通道，两个 Raster Pass：
//
//   Raymarch   —— 半分辨率光线步进，结果写进自己的 RT（RGB = 散射，A = 场景
//                 视图深度，留给双边上采样用）
//   Composite  —— 深度感知双边上采样回全分辨率，用 Blend One One 叠加到
//                 activeColor 上（debug 模式换成 Blend Off 的第三个 Pass）
//
// 为什么拿 activeColorTexture 当叠加目标是安全的：这里只"写"不"读"，
// 混合由 ROP 完成，不需要 `_CameraOpaqueTexture` 那种回读（后者在 RenderGraph
// 里没有依赖关系，会读到空缓冲）。
//
// 阴影关键字：_MAIN_LIGHT_SHADOWS / _MAIN_LIGHT_SHADOWS_CASCADE 虽然是 URP
// 设置的全局关键字，但这里仍然按 UniversalShadowData 显式设置材质的局部
// 关键字 —— 全局关键字的隐式回退在某些管线状态下会失效，一旦失效光柱会
// 整体消失、退化成均匀雾，很难查。用 Volume 的 debug = Shadow 可以验证。
// ============================================================================

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace CartoonRendering
{
    public sealed class VolumetricLightPass : ScriptableRenderPass
    {
        private const string kRaymarchPassName  = "VolumetricLight.Raymarch";
        private const string kCompositePassName = "VolumetricLight.Composite";
        private const string kDebugPassName     = "VolumetricLight.Debug";

        // Pass 索引，对应 VolumetricLight.shader
        private const int kPassRaymarch  = 0;
        private const int kPassComposite = 1;
        private const int kPassDebug     = 2;

        private static readonly int kVolTintId           = Shader.PropertyToID("_VolTint");
        private static readonly int kVolIntensityId      = Shader.PropertyToID("_VolIntensity");
        private static readonly int kVolDensityId        = Shader.PropertyToID("_VolDensity");
        private static readonly int kVolMaxDistanceId    = Shader.PropertyToID("_VolMaxDistance");
        private static readonly int kVolDistanceFadeId   = Shader.PropertyToID("_VolDistanceFade");
        private static readonly int kVolAnisotropyId     = Shader.PropertyToID("_VolAnisotropy");
        private static readonly int kVolHeightStartId    = Shader.PropertyToID("_VolHeightStart");
        private static readonly int kVolHeightFalloffId  = Shader.PropertyToID("_VolHeightFalloff");
        private static readonly int kVolShadowStrengthId = Shader.PropertyToID("_VolShadowStrength");
        private static readonly int kVolShadowBiasId     = Shader.PropertyToID("_VolShadowBias");
        private static readonly int kVolStepsId          = Shader.PropertyToID("_VolSteps");
        private static readonly int kVolJitterId         = Shader.PropertyToID("_VolJitter");
        private static readonly int kVolSoftnessId       = Shader.PropertyToID("_VolSoftness");
        private static readonly int kVolNoiseStrengthId  = Shader.PropertyToID("_VolNoiseStrength");
        private static readonly int kVolNoiseScaleId     = Shader.PropertyToID("_VolNoiseScale");
        private static readonly int kVolNoiseSpeedId     = Shader.PropertyToID("_VolNoiseSpeed");
        private static readonly int kVolBandingId        = Shader.PropertyToID("_VolBanding");
        private static readonly int kVolDebugId          = Shader.PropertyToID("_VolDebug");
        private static readonly int kVolTexelSizeId      = Shader.PropertyToID("_VolTexelSize");
        private static readonly int kVolTexId            = Shader.PropertyToID("_VolTex");

        // DrawProcedural 需要一个 PropertyBlock；它只在 render func 里同步使用，
        // 全屏三角形顶点着色器是自带的，不依赖 _BlitScaleBias，所以这里是空的。
        private static readonly MaterialPropertyBlock s_SharedPropertyBlock = new MaterialPropertyBlock();

        private static readonly ProfilingSampler kRaymarchSampler  = new ProfilingSampler(kRaymarchPassName);
        private static readonly ProfilingSampler kCompositeSampler = new ProfilingSampler(kCompositePassName);

        private Material m_Material;
        private int m_Divisor = 2;

        public VolumetricLightPass(Material material)
        {
            m_Material = material;
            // 声明依赖深度图：这样即使管线资产没开 Depth Texture，URP 也会为
            // 这个通道把 _CameraDepthTexture 生成出来
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        internal void SetMaterial(Material material)
        {
            m_Material = material;
        }

        internal void SetResolutionDivisor(int divisor)
        {
            m_Divisor = Mathf.Clamp(divisor, 1, 8);
        }

        /// <inheritdoc/>
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (m_Material == null || m_Material.shader == null)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData     = frameData.Get<UniversalCameraData>();
            UniversalShadowData shadowData     = frameData.Get<UniversalShadowData>();

            if (!resourceData.cameraDepthTexture.IsValid() || !resourceData.activeColorTexture.IsValid())
                return;

            // 预览 / 反射探针不需要体积光，跑了只是浪费
            var cameraType = cameraData.cameraType;
            if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
                return;

            var volume = VolumeManager.instance.stack.GetComponent<VolumetricLight>();
            if (volume == null || !volume.IsActive())
                return;

            int fullW = cameraData.cameraTargetDescriptor.width;
            int fullH = cameraData.cameraTargetDescriptor.height;
            int lowW  = Mathf.Max(1, fullW / m_Divisor);
            int lowH  = Mathf.Max(1, fullH / m_Divisor);

            var desc = new TextureDesc(lowW, lowH)
            {
                name            = "_VolumetricLightTex",
                format          = GraphicsFormat.R16G16B16A16_SFloat,
                filterMode      = FilterMode.Bilinear,
                wrapMode        = TextureWrapMode.Clamp,
                clearBuffer     = true,
                clearColor      = Color.clear,
                useDynamicScale = false,
            };
            TextureHandle lowRes = renderGraph.CreateTexture(desc);

            var volumeValues = VolumeParams.From(volume);
            var texelSize    = new Vector4(1f / lowW, 1f / lowH, lowW, lowH);

            // 阴影关键字从 shader 上取；shader 换了这些 LocalKeyword 也会跟着变
            var keywordShadows = new LocalKeyword(m_Material.shader, "_MAIN_LIGHT_SHADOWS");
            var keywordCascade = new LocalKeyword(m_Material.shader, "_MAIN_LIGHT_SHADOWS_CASCADE");
            bool supportsShadows = shadowData.supportsMainLightShadows;
            bool useCascade      = supportsShadows && shadowData.mainLightShadowCascadesCount > 1;

            // ---------------------------------------------------------------
            //  Pass 0：光线步进
            // ---------------------------------------------------------------
            using (var builder = renderGraph.AddRasterRenderPass<RaymarchPassData>(
                       kRaymarchPassName, out var data, kRaymarchSampler))
            {
                data.material      = m_Material;
                data.volumeValues  = volumeValues;
                data.keywordShadows = keywordShadows;
                data.keywordCascade = keywordCascade;
                data.useShadows    = supportsShadows;
                data.useCascade    = useCascade;

                builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                builder.SetRenderAttachment(lowRes, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);

                // 每个采样点都要读阴影图（全局贴图），RenderGraph 追踪不到它
                builder.UseAllGlobalTextures(true);
                // 要在 render func 里改材质关键字
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (RaymarchPassData d, RasterGraphContext ctx) =>
                {
                    PushGlobals(ctx.cmd, in d.volumeValues);

                    // 显式同步阴影关键字，不依赖全局关键字的隐式回退
                    ctx.cmd.SetKeyword(d.material, d.keywordShadows, d.useShadows && !d.useCascade);
                    ctx.cmd.SetKeyword(d.material, d.keywordCascade, d.useShadows && d.useCascade);

                    ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, kPassRaymarch,
                        MeshTopology.Triangles, 3, 1, s_SharedPropertyBlock);
                });
            }

            // ---------------------------------------------------------------
            //  Pass 1 / 2：上采样 + 合成（debug 时换成 Blend Off 的盖屏版本）
            // ---------------------------------------------------------------
            bool debugView = volume.debug.value != VolumetricLightDebug.Off;
            string passName = debugView ? kDebugPassName : kCompositePassName;
            int passIndex   = debugView ? kPassDebug : kPassComposite;

            using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>(
                       passName, out var data, kCompositeSampler))
            {
                data.material     = m_Material;
                data.lowRes       = lowRes;
                data.texelSize    = texelSize;
                data.volumeValues = volumeValues;
                data.passIndex    = passIndex;

                builder.UseTexture(lowRes, AccessFlags.Read);
                builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (CompositePassData d, RasterGraphContext ctx) =>
                {
                    PushGlobals(ctx.cmd, in d.volumeValues);
                    // 低分辨率 RT 走全局绑定：TextureHandle 不能直接赋给材质
                    ctx.cmd.SetGlobalTexture(kVolTexId, d.lowRes);
                    ctx.cmd.SetGlobalVector(kVolTexelSizeId, d.texelSize);

                    ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.passIndex,
                        MeshTopology.Triangles, 3, 1, s_SharedPropertyBlock);
                });
            }
        }

        // --------------------------------------------------------------------
        //  Volume 参数 → 全局 uniform
        // --------------------------------------------------------------------
        private struct VolumeParams
        {
            public Vector4 tint;
            public float intensity;
            public float density;
            public float maxDistance;
            public float distanceFade;
            public float anisotropy;
            public float heightStart;
            public float heightFalloff;
            public float shadowStrength;
            public float shadowBias;
            public float steps;
            public float jitter;
            public float softness;
            public float noiseStrength;
            public float noiseScale;
            public float noiseSpeed;
            public float banding;
            public float debug;

            public static VolumeParams From(VolumetricLight v)
            {
                VolumeParams p;
                p.tint           = v.tint.value;
                p.intensity      = v.intensity.value;
                p.density        = v.density.value;
                p.maxDistance    = v.maxDistance.value;
                p.distanceFade   = v.distanceFade.value;
                p.anisotropy     = v.anisotropy.value;
                p.heightStart    = v.heightStart.value;
                p.heightFalloff  = v.heightFalloff.value;
                p.shadowStrength = v.shadowStrength.value;
                p.shadowBias     = v.shadowBias.value;
                p.steps          = v.stepCount.value;
                p.jitter         = v.jitter.value;
                p.softness       = v.softness.value;
                p.noiseStrength  = v.noiseStrength.value;
                p.noiseScale     = v.noiseScale.value;
                p.noiseSpeed     = v.noiseSpeed.value;
                p.banding        = v.banding.value;
                p.debug          = (float)(int)v.debug.value;
                return p;
            }
        }

        private static void PushGlobals(RasterCommandBuffer cmd, in VolumeParams p)
        {
            cmd.SetGlobalVector(kVolTintId, p.tint);
            cmd.SetGlobalFloat(kVolIntensityId, p.intensity);
            cmd.SetGlobalFloat(kVolDensityId, p.density);
            cmd.SetGlobalFloat(kVolMaxDistanceId, p.maxDistance);
            cmd.SetGlobalFloat(kVolDistanceFadeId, p.distanceFade);
            cmd.SetGlobalFloat(kVolAnisotropyId, p.anisotropy);
            cmd.SetGlobalFloat(kVolHeightStartId, p.heightStart);
            cmd.SetGlobalFloat(kVolHeightFalloffId, p.heightFalloff);
            cmd.SetGlobalFloat(kVolShadowStrengthId, p.shadowStrength);
            cmd.SetGlobalFloat(kVolShadowBiasId, p.shadowBias);
            cmd.SetGlobalFloat(kVolStepsId, p.steps);
            cmd.SetGlobalFloat(kVolJitterId, p.jitter);
            cmd.SetGlobalFloat(kVolSoftnessId, p.softness);
            cmd.SetGlobalFloat(kVolNoiseStrengthId, p.noiseStrength);
            cmd.SetGlobalFloat(kVolNoiseScaleId, p.noiseScale);
            cmd.SetGlobalFloat(kVolNoiseSpeedId, p.noiseSpeed);
            cmd.SetGlobalFloat(kVolBandingId, p.banding);
            cmd.SetGlobalFloat(kVolDebugId, p.debug);
        }

        private class RaymarchPassData
        {
            public Material material;
            public VolumeParams volumeValues;
            public LocalKeyword keywordShadows;
            public LocalKeyword keywordCascade;
            public bool useShadows;
            public bool useCascade;
        }

        private class CompositePassData
        {
            public Material material;
            public TextureHandle lowRes;
            public Vector4 texelSize;
            public VolumeParams volumeValues;
            public int passIndex;
        }
    }
}
