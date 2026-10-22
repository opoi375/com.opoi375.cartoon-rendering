// Copyright (c) 2026 CartoonRendering. MIT License.

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace CartoonRendering
{
    /// <summary>
    /// RenderGraph full-screen pass for the scan effect.
    ///
    /// 与 UnderwaterPostProcessPass 同一套约定，理由见那边的长注释：
    ///   * 只写 activeColor，不读它（<c>Blend One OneMinusSrcAlpha</c>）；
    ///   * 必须声明 <see cref="ScriptableRenderPassInput.Depth"/>，否则深度图
    ///     不会被绑进来、<c>SampleSceneDepth</c> 恒为 0、整屏被判成天空，
    ///     效果一次都不出现而且不报错；
    ///   * 屏幕 uv 与场景纹理 uv 差一次 Y 翻转，按
    ///     <c>SystemInfo.graphicsUVStartsAtTop</c> 静态算一次。
    /// </summary>
    public sealed class ScanEffectPass : ScriptableRenderPass
    {
        private const string kPassName = "ScanEffect";

        private static readonly int kOriginId         = Shader.PropertyToID("_ScanOrigin");
        private static readonly int kForwardId        = Shader.PropertyToID("_ScanForward");
        private static readonly int kActiveId         = Shader.PropertyToID("_ScanActive");
        private static readonly int kModeId           = Shader.PropertyToID("_ScanMode");
        private static readonly int kCosHalfAngleId   = Shader.PropertyToID("_ScanCosHalfAngle");
        private static readonly int kColorGlowId      = Shader.PropertyToID("_ScanColorGlow");
        private static readonly int kColorEdgeId      = Shader.PropertyToID("_ScanColorEdge");
        private static readonly int kRadiusId         = Shader.PropertyToID("_ScanRadius");
        private static readonly int kDistanceId       = Shader.PropertyToID("_ScanDistance");
        private static readonly int kLineWidthId      = Shader.PropertyToID("_ScanLineWidth");
        private static readonly int kEdgeIntensityId  = Shader.PropertyToID("_ScanEdgeIntensity");
        private static readonly int kGlowIntensityId  = Shader.PropertyToID("_ScanGlowIntensity");
        private static readonly int kTrailLengthId    = Shader.PropertyToID("_ScanTrailLength");
        private static readonly int kGridDensityId    = Shader.PropertyToID("_ScanGridDensity");
        private static readonly int kDotDensityId     = Shader.PropertyToID("_ScanDotDensity");
        private static readonly int kDetailId         = Shader.PropertyToID("_ScanDetailIntensity");
        private static readonly int kRestoreTimeId    = Shader.PropertyToID("_ScanRestoreTime");
        private static readonly int kNoiseId          = Shader.PropertyToID("_ScanNoise");
        private static readonly int kAttenuationId    = Shader.PropertyToID("_ScanAttenuation");
        private static readonly int kQualityId        = Shader.PropertyToID("_ScanQuality");
        private static readonly int kRestoreCurveId   = Shader.PropertyToID("_ScanRestoreCurve");
        private static readonly int kTimeId           = Shader.PropertyToID("_ScanTime");
        private static readonly int kIntensityId      = Shader.PropertyToID("_ScanIntensity");
        private static readonly int kFadeOutId        = Shader.PropertyToID("_ScanFadeOut");
        private static readonly int kSceneScaleBiasId = Shader.PropertyToID("_ScanSceneScaleBias");
        private static readonly int kViewSizeId       = Shader.PropertyToID("_ScanViewSize");
        private static readonly int kVoxelSizeId     = Shader.PropertyToID("_ScanVoxelSize");
        private static readonly int kVoxelFillId     = Shader.PropertyToID("_ScanVoxelFill");
        private static readonly int kVoxelRevealId   = Shader.PropertyToID("_ScanVoxelReveal");
        private static readonly int kDiagId           = Shader.PropertyToID("_ScanDiag");

        private static readonly MaterialPropertyBlock s_SharedPropertyBlock = new MaterialPropertyBlock();
        private static readonly ProfilingSampler kSampler = new ProfilingSampler(kPassName);

        private static bool s_WarnedMissingDepth;

        private Material m_Material;

        public ScanEffectPass(Material material)
        {
            m_Material = material;
            profilingSampler = kSampler;

            // Depth → 要世界坐标才知道"这个像素离扫描中心多远"
            // Color → 与包内其他后期保持一致（本通道自己并不读）
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

            // 预览 / 反射探针不跑：反射探针里的"扫描"既没人看，深度也不相干
            var cameraType = cameraData.cameraType;
            if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
                return;

            if (!resourcesData.cameraDepthTexture.IsValid())
            {
                if (!s_WarnedMissingDepth)
                {
                    s_WarnedMissingDepth = true;
                    Debug.LogWarning("[ScanEffect] 拿不到场景深度图，扫描特效跳过。" +
                                     "请在 URP 管线资产里打开 Depth Texture，" +
                                     "或在相机上勾上 Depth Texture。");
                }
                return;
            }
            s_WarnedMissingDepth = false;

            var scan = VolumeManager.instance.stack.GetComponent<ScanEffect>();
            if (scan == null || !scan.IsActive())
                return;


            if (!ScanEffectRuntime.active)
                return;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                       kPassName, out var passData, profilingSampler))
            {
                passData.material = m_Material;
                passData.settings = scan;
                passData.cameraPos = cameraData.camera.transform.position;
                passData.viewSize = new Vector4(cameraData.cameraTargetDescriptor.width,
                                                cameraData.cameraTargetDescriptor.height, 0f, 0f);

                builder.UseTexture(resourcesData.cameraDepthTexture, AccessFlags.Read);
                builder.SetRenderAttachment(resourcesData.activeColorTexture, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext rgContext) =>
                {
                    var s = data.settings;
                    var mpb = s_SharedPropertyBlock;

                    // ---- 动画状态 ----
                    mpb.SetVector(kOriginId,
                        new Vector4(ScanEffectRuntime.origin.x, ScanEffectRuntime.origin.y,
                                    ScanEffectRuntime.origin.z, ScanEffectRuntime.front));
                    var f = ScanEffectRuntime.forward;
                    mpb.SetVector(kForwardId, new Vector4(f.x, f.y, f.z, ScanEffectRuntime.progress));
                    mpb.SetFloat(kActiveId, 1f);
                    // Voxel 复用 Radial 的距离度量（四周扩散的立体方格）：
                    // 两者的差别只在细节层画什么（立方格 vs 平面网格）。
                    bool radialLike = s.pattern.value != ScanPattern.Frontal;
                    mpb.SetFloat(kModeId, radialLike ? 0f : 1f);
                    float halfAngle = Mathf.Clamp(s.sectorAngle.value, 1f, 360f) * 0.5f;
                    // 360 度 = 整圈：cos(180) = -1，扇形裁切自然退化成"全通过"
                    mpb.SetFloat(kCosHalfAngleId, Mathf.Cos(halfAngle * Mathf.Deg2Rad));

                    // ---- 外观 ----
                    // 必须自己转线性：Volume 里存的是用户看到的 sRGB，而
                    // MaterialPropertyBlock.SetColor 不做 sRGB→线性（水下后期踩过，
                    // 不转的话颜色会亮 3~4 倍）
                    mpb.SetColor(kColorGlowId, ToLinear(s.color.value));
                    mpb.SetColor(kColorEdgeId, ToLinear(s.edgeColor.value));

                    mpb.SetFloat(kRadiusId, s.radius.value);
                    mpb.SetFloat(kDistanceId, s.distance.value);
                    mpb.SetFloat(kLineWidthId, s.lineWidth.value);
                    mpb.SetFloat(kEdgeIntensityId, s.edgeIntensity.value);
                    mpb.SetFloat(kGlowIntensityId, s.glowIntensity.value);
                    mpb.SetFloat(kTrailLengthId, s.trailLength.value);
                    mpb.SetFloat(kGridDensityId, s.gridDensity.value);
                    mpb.SetFloat(kDotDensityId, s.dotDensity.value);
                    mpb.SetFloat(kDetailId, s.detailIntensity.value);
                    mpb.SetFloat(kRestoreTimeId, s.restoreTime.value);
                    mpb.SetFloat(kNoiseId, s.noise.value);
                    mpb.SetFloat(kAttenuationId, s.attenuation.value);
                    mpb.SetFloat(kQualityId, (float)s.quality.value);
                    mpb.SetFloat(kRestoreCurveId, (float)s.restoreCurve.value);
                    mpb.SetFloat(kTimeId, ScanEffectRuntime.time);
                    mpb.SetFloat(kIntensityId, s.intensity.value);
                    mpb.SetFloat(kFadeOutId, ScanEffectRuntime.fadeOut);
                    mpb.SetVector(kSceneScaleBiasId, s_SceneScaleBias);
                    mpb.SetVector(kViewSizeId, data.viewSize);
                    mpb.SetFloat(kDiagId, s.diagnosticSolid.value ? 1f : 0f);
                    mpb.SetFloat(kVoxelSizeId, s.voxelSize.value);
                    mpb.SetFloat(kVoxelFillId, s.voxelFill.value);
                    mpb.SetFloat(kVoxelRevealId, s.voxelReveal.value);

                    rgContext.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0,
                        MeshTopology.Triangles, 3, 1, mpb);
                });
            }
        }

        // 这个通道写的是 activeColorTexture，深度纹理与它同向，所以片元 uv 直接
        // 就是采样 uv，【不需要】Y 翻转。
        // （原先照抄了水下后期那套 graphicsUVStartsAtTop 翻转，实测整屏上下颠倒：
        //   采样深度拿到了镜像位置 → 世界坐标错 → 距离判断全崩 → 全屏都被判成"扫过"。
        //   水下那套是它自己历史实现的选择，不是这个通道的约定。）
        private static readonly Vector4 s_SceneScaleBias = new Vector4(1f, 1f, 0f, 0f);

        /// <summary>
        /// Volume 面板里的颜色是用户视角的 sRGB；着色器工作在线性空间。
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
            public ScanEffect settings;
            public Vector3 cameraPos;
            public Vector4 viewSize;
        }
    }
}
