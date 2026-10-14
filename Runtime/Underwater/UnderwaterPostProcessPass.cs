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
    /// Writes into the active colour with <c>Blend One OneMinusSrcAlpha</c> and
    /// never reads it: in RenderGraph the active colour is an imported resource
    /// without a dependency, so a read of it can land on an empty buffer (the
    /// volumetric light pass in this package documents the same trap). The
    /// blend equation is what lets the shader express per-channel absorption
    /// plus in-scattering without a destination read - see the shader header.
    ///
    /// The pass DOES declare <see cref="ScriptableRenderPassInput.Depth"/> and
    /// <c>Color</c>. That is not decoration: without the dependency the depth
    /// texture is never bound, <c>SampleSceneDepth</c> returns 0, every pixel is
    /// classified as sky, and the effect silently draws nothing at all - which
    /// is exactly how this feature shipped broken. <c>Debug/SkyMask</c> exists to
    /// catch that in one look.
    /// </summary>
    public sealed class UnderwaterPostProcessPass : ScriptableRenderPass
    {
        private const string kPassName = "UnderwaterPostProcess";

        private static readonly int kIntensityId         = Shader.PropertyToID("_UWIntensity");
        private static readonly int kFogColorId          = Shader.PropertyToID("_UWFogColor");
        private static readonly int kDeepColorId         = Shader.PropertyToID("_UWDeepColor");
        private static readonly int kDepthToFullColorId  = Shader.PropertyToID("_UWDepthToFullColor");
        private static readonly int kTintStrengthId      = Shader.PropertyToID("_UWTintStrength");
        private static readonly int kFogDensityId        = Shader.PropertyToID("_UWFogDensity");
        private static readonly int kAbsorptionId        = Shader.PropertyToID("_UWAbsorption");
        private static readonly int kAbsorptionRateId    = Shader.PropertyToID("_UWAbsorptionRate");
        private static readonly int kSurfaceYId          = Shader.PropertyToID("_UWSurfaceY");
        private static readonly int kCausticsId          = Shader.PropertyToID("_UWCaustics");
        private static readonly int kCausticsScaleId     = Shader.PropertyToID("_UWCausticsScale");
        private static readonly int kCausticsSpeedId     = Shader.PropertyToID("_UWCausticsSpeed");
        private static readonly int kCausticsDepthFadeId = Shader.PropertyToID("_UWCausticsDepthFade");
        private static readonly int kWaveStrengthId      = Shader.PropertyToID("_UWWaveStrength");
        private static readonly int kWaveScaleId         = Shader.PropertyToID("_UWWaveScale");
        private static readonly int kWaveSpeedId         = Shader.PropertyToID("_UWWaveSpeed");
        private static readonly int kVignetteId          = Shader.PropertyToID("_UWVignette");
        private static readonly int kBandingId           = Shader.PropertyToID("_UWBanding");
        private static readonly int kDebugId             = Shader.PropertyToID("_UWDebug");
        private static readonly int kHasOpaqueId         = Shader.PropertyToID("_UWHasOpaque");
        private static readonly int kSceneScaleBiasId    = Shader.PropertyToID("_UWSceneScaleBias");

        // DrawProcedural needs a property block; it is only used synchronously
        // inside the render func, so one shared instance is safe.
        private static readonly MaterialPropertyBlock s_SharedPropertyBlock = new MaterialPropertyBlock();
        private static readonly ProfilingSampler kSampler = new ProfilingSampler(kPassName);

        private static bool s_WarnedMissingDepth;

        private Material m_Material;

        public UnderwaterPostProcessPass(Material material)
        {
            m_Material = material;
            profilingSampler = kSampler;

            // Depth   → 雾与焦散都要从深度还原世界位置
            // Color   → 不透明图（逐通道吸收与扭曲要回读不透明像素）
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Color);
        }

        internal void SetMaterial(Material material)
        {
            m_Material = material;
        }

        /// <inheritdoc/>
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (m_Material == null)
                return;

            UniversalResourceData resourcesData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData      = frameData.Get<UniversalCameraData>();

            if (!resourcesData.activeColorTexture.IsValid())
                return;

            // 预览 / 反射探针里跑一遍只会白花钱，而且探针会拿到不相干的深度
            var cameraType = cameraData.cameraType;
            if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
                return;

            if (!resourcesData.cameraDepthTexture.IsValid())
            {
                // 没有深度就没有距离梯度，画出来只是一层平涂 —— 宁可什么都不画，
                // 也要把原因喊出来：这个效果以前就是静默失效的。
                if (!s_WarnedMissingDepth)
                {
                    s_WarnedMissingDepth = true;
                    Debug.LogWarning("[Underwater] 拿不到场景深度图，水下效果跳过。" +
                                     "请在 URP 管线资产里打开 Depth Texture，" +
                                     "或在相机上勾上 Depth Texture。");
                }
                return;
            }
            s_WarnedMissingDepth = false;

            var underwater = VolumeManager.instance.stack.GetComponent<UnderwaterPostProcess>();
            if (underwater == null || !underwater.IsActive())
                return;

            bool hasOpaque = resourcesData.cameraOpaqueTexture.IsValid();

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                       kPassName, out var passData, profilingSampler))
            {
                passData.material = m_Material;
                passData.settings = underwater;
                passData.hasOpaque = hasOpaque;

                builder.UseTexture(resourcesData.cameraDepthTexture, AccessFlags.Read);
                if (hasOpaque)
                    builder.UseTexture(resourcesData.cameraOpaqueTexture, AccessFlags.Read);

                builder.SetRenderAttachment(resourcesData.activeColorTexture, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext rgContext) =>
                {
                    var s = data.settings;
                    var m = data.material;
                    var mpb = s_SharedPropertyBlock;

                    mpb.SetFloat(kIntensityId, s.intensity.value);
                    // 必须自己转线性：Volume 里存的是用户看到的 sRGB 值，而
                    // MaterialPropertyBlock.SetColor 不会做 sRGB→线性转换
                    // （不像 Inspector 上的 Color 字段）。不转的话雾色会亮 3-4 倍，
                    // 整屏变成一层白雾而不是深水。
                    mpb.SetColor(kFogColorId, ToLinear(s.color.value));
                    mpb.SetColor(kDeepColorId, ToLinear(s.deepColor.value));
                    mpb.SetFloat(kDepthToFullColorId, s.depthToFullColor.value);
                    mpb.SetFloat(kTintStrengthId, s.tintStrength.value);
                    mpb.SetFloat(kFogDensityId, s.fogDensity.value);

                    // 吸收权重不是颜色：走 Vector4，免得被当成 sRGB 转一次线性
                    var a = s.absorptionColor.value;
                    mpb.SetVector(kAbsorptionId, new Vector4(a.r, a.g, a.b, 0f));
                    mpb.SetFloat(kAbsorptionRateId, s.absorptionRate.value);

                    mpb.SetFloat(kSurfaceYId, s.waterSurfaceY.value);
                    mpb.SetFloat(kCausticsId, s.causticsIntensity.value);
                    mpb.SetFloat(kCausticsScaleId, s.causticsScale.value);
                    mpb.SetFloat(kCausticsSpeedId, s.causticsSpeed.value);
                    mpb.SetFloat(kCausticsDepthFadeId, s.causticsDepthFade.value);

                    mpb.SetFloat(kWaveStrengthId, s.waveStrength.value);
                    mpb.SetFloat(kWaveScaleId, s.waveScale.value);
                    mpb.SetFloat(kWaveSpeedId, s.waveSpeed.value);
                    mpb.SetFloat(kVignetteId, s.vignette.value);
                    mpb.SetFloat(kBandingId, s.banding.value);
                    mpb.SetFloat(kDebugId, (float)s.debug.value);
                    mpb.SetFloat(kHasOpaqueId, data.hasOpaque ? 1f : 0f);
                    mpb.SetVector(kSceneScaleBiasId, s_SceneScaleBias);

                    rgContext.cmd.DrawProcedural(Matrix4x4.identity, m, 0,
                        MeshTopology.Triangles, 3, 1, mpb);
                });
            }
        }

        // 注入在 post stack 前面时，要写的目标与相机渲染出来的场景纹理差一次
        // Y 翻转（见 shader 里的 UWSceneUV）。静态算一次即可：图形 API 的 uv
        // 约定不会在运行期变。
        private static readonly Vector4 s_SceneScaleBias =
            SystemInfo.graphicsUVStartsAtTop ? new Vector4(1f, -1f, 0f, 1f)
                                           : new Vector4(1f,  1f, 0f, 0f);

        /// <summary>
        /// Volume 面板里的颜色是用户视角的 sRGB；着色器工作在线性空间。
        /// 吸收权重不走这个函数 —— 它是逐通道系数，不是颜色。
        /// </summary>
        static Color ToLinear(Color c)
        {
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                return c.linear;
            return c;
        }

        private class PassData
        {
            public Material material;
            public UnderwaterPostProcess settings;
            public bool hasOpaque;
        }
    }
}
