// ============================================================================
// SSILPass.cs
// ----------------------------------------------------------------------------
// SSIL 的 RenderGraph 通道，四个 Raster Pass：
//
//   Trace    —— 半分辨率可见性位掩码追踪（论文 Algorithm 1），结果写进自己的
//               RT（RGB = 间接光，A = 可见性，1 = 全开）
//   BlurH    —— 横向双边模糊（深度相似度权重，防止跨深度边缘渗色）
//   BlurV    —— 纵向双边模糊，写回 Trace 的 RT（乒乓）
//   Composite—— 深度感知双边上采样，用 Blend DstColor Zero 乘进 activeColor
//               （debug 模式换成 Blend Off 的第五个 Pass）
//
// 为什么乘法合成安全：Blend DstColor Zero = dst * src，只"写"不"读"
// activeColor，混合由 ROP 完成 —— 不需要对 activeColor 的回读（后者在
// RenderGraph 里没有依赖关系，会读到空缓冲，水下后处理在本包里踩过这个坑）。
//
// 输入依赖（构造时 ConfigureInput 显式声明，URP 会按需生成）：
//   Depth  —— 视图空间位置还原 + 双边模糊 / 上采样的深度权重
//   Normal —— 追踪需要法线（_CameraNormalsTexture，注意存的是世界空间，
//              shader 里转到视图空间）
//   Color  —— 不透明色拷贝（_CameraOpaqueTexture）当"直接光照缓冲"用：
//              论文用单独的直接光 G-Buffer，这里用已着色的场景色近似，
//              反弹过来的光自带固有色，卡通渲染下效果反而更讨喜。
//
// _SSILTex 是全局纹理（TextureHandle 没法直接赋给 MaterialPropertyBlock），
// 所以相关 Pass 都开了 AllowGlobalStateModification。法线图 / 不透明拷贝也
// 是按全局名采样的，RenderGraph 追踪不到，用 UseAllGlobalTextures 兜住。
// ============================================================================

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace CartoonRendering
{
    public sealed class SSILPass : ScriptableRenderPass
    {
        private const string kTracePassName = "SSIL.Trace";
        private const string kBlurHPassName = "SSIL.BlurH";
        private const string kBlurVPassName = "SSIL.BlurV";
        private const string kCompositeName = "SSIL.Composite";
        private const string kDebugPassName = "SSIL.Debug";

        // Pass 索引，对应 SSIL.shader
        private const int kPassTrace     = 0;
        private const int kPassBlurH     = 1;
        private const int kPassBlurV     = 2;
        private const int kPassComposite = 3;
        private const int kPassDebug     = 4;

        private static readonly int kSsilRadiusId           = Shader.PropertyToID("_SSILRadius");
        private static readonly int kSsilThicknessId        = Shader.PropertyToID("_SSILThickness");
        private static readonly int kSsilSamplesId          = Shader.PropertyToID("_SSILSamples");
        private static readonly int kSsilSlicesId           = Shader.PropertyToID("_SSILSlices");
        private static readonly int kSsilJitterId           = Shader.PropertyToID("_SSILJitter");
        private static readonly int kSsilMaxDistanceId      = Shader.PropertyToID("_SSILMaxDistance");
        private static readonly int kSsilDistanceFadeId     = Shader.PropertyToID("_SSILDistanceFade");
        private static readonly int kSsilAoStrengthId       = Shader.PropertyToID("_SSILAoStrength");
        private static readonly int kSsilIndirectStrengthId = Shader.PropertyToID("_SSILIndirectStrength");
        private static readonly int kSsilAoBandingId        = Shader.PropertyToID("_SSILAoBanding");
        private static readonly int kSsilBlurRadiusId       = Shader.PropertyToID("_SSILBlurRadius");
        private static readonly int kSsilDebugId            = Shader.PropertyToID("_SSILDebug");
        private static readonly int kSsilIndirectTintId     = Shader.PropertyToID("_SSILIndirectTint");
        private static readonly int kSsilTexelSizeId        = Shader.PropertyToID("_SSILTexelSize");
        private static readonly int kSsilTexId              = Shader.PropertyToID("_SSILTex");

        // DrawProcedural 需要一个 PropertyBlock；它只在 render func 里同步使用，
        // 全屏三角形顶点着色器是自带的，不依赖 _BlitScaleBias。
        private static readonly MaterialPropertyBlock s_SharedPropertyBlock = new MaterialPropertyBlock();

        private static readonly ProfilingSampler kTraceSampler     = new ProfilingSampler(kTracePassName);
        private static readonly ProfilingSampler kBlurHSampler     = new ProfilingSampler(kBlurHPassName);
        private static readonly ProfilingSampler kBlurVSampler     = new ProfilingSampler(kBlurVPassName);
        private static readonly ProfilingSampler kCompositeSampler = new ProfilingSampler(kCompositeName);

        private Material m_Material;
        private int m_Divisor = 2;

        public SSILPass(Material material)
        {
            m_Material = material;
            // 声明依赖：即使管线资产没开 Depth Texture / Opaque Texture，
            // URP 也会为这个通道把这些纹理生成出来
            ConfigureInput(ScriptableRenderPassInput.Depth |
                           ScriptableRenderPassInput.Normal |
                           ScriptableRenderPassInput.Color);
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

            if (!resourceData.cameraDepthTexture.IsValid() ||
                !resourceData.cameraNormalsTexture.IsValid() ||
                !resourceData.activeColorTexture.IsValid())
                return;

            // 预览 / 反射探针不需要 GI，跑了只是浪费
            var cameraType = cameraData.cameraType;
            if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
                return;

            var volume = VolumeManager.instance.stack.GetComponent<SSIL>();
            if (volume == null || !volume.IsActive())
                return;

            int fullW = cameraData.cameraTargetDescriptor.width;
            int fullH = cameraData.cameraTargetDescriptor.height;
            int lowW  = Mathf.Max(1, fullW / m_Divisor);
            int lowH  = Mathf.Max(1, fullH / m_Divisor);

            var desc = new TextureDesc(lowW, lowH)
            {
                name            = "_SSILTex",
                format          = GraphicsFormat.R16G16B16A16_SFloat,
                filterMode      = FilterMode.Bilinear,
                wrapMode        = TextureWrapMode.Clamp,
                clearBuffer     = true,
                clearColor      = Color.clear,
                useDynamicScale = false,
            };
            TextureHandle lowRes   = renderGraph.CreateTexture(desc);
            TextureHandle blurTemp = renderGraph.CreateTexture(desc);

            var volumeValues = VolumeParams.From(volume);
            var texelSize    = new Vector4(1f / lowW, 1f / lowH, lowW, lowH);

            // ---------------------------------------------------------------
            //  Pass 0：可见性位掩码追踪（半分辨率）
            // ---------------------------------------------------------------
            using (var builder = renderGraph.AddRasterRenderPass<TracePassData>(
                       kTracePassName, out var data, kTraceSampler))
            {
                data.material     = m_Material;
                data.volumeValues = volumeValues;

                builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                builder.UseTexture(resourceData.cameraNormalsTexture, AccessFlags.Read);
                if (resourceData.cameraOpaqueTexture.IsValid())
                    builder.UseTexture(resourceData.cameraOpaqueTexture, AccessFlags.Read);
                builder.SetRenderAttachment(lowRes, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.UseAllGlobalTextures(true);

                builder.SetRenderFunc(static (TracePassData d, RasterGraphContext ctx) =>
                {
                    var mpb = d.volumeValues.ToPropertyBlock(s_SharedPropertyBlock);
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, kPassTrace,
                        MeshTopology.Triangles, 3, 1, mpb);
                });
            }

            // ---------------------------------------------------------------
            //  Pass 1 / 2：双边模糊（乒乓）
            // ---------------------------------------------------------------
            AddBlurPass(renderGraph, kBlurHPassName, kBlurHSampler,
                lowRes, blurTemp, resourceData.cameraDepthTexture, texelSize, volumeValues, kPassBlurH);
            AddBlurPass(renderGraph, kBlurVPassName, kBlurVSampler,
                blurTemp, lowRes, resourceData.cameraDepthTexture, texelSize, volumeValues, kPassBlurV);

            // ---------------------------------------------------------------
            //  Pass 3 / 4：上采样 + 合成（debug 时换成 Blend Off 的盖屏版本）
            // ---------------------------------------------------------------
            bool debugView = volume.debug.value != SSILDebug.Off;
            string passName = debugView ? kDebugPassName : kCompositeName;
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
                builder.UseAllGlobalTextures(true);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (CompositePassData d, RasterGraphContext ctx) =>
                {
                    var mpb = d.volumeValues.ToPropertyBlock(s_SharedPropertyBlock);
                    mpb.SetVector(kSsilTexelSizeId, d.texelSize);
                    // 低分辨率 RT 走全局绑定：TextureHandle 不能直接赋给材质
                    ctx.cmd.SetGlobalTexture(kSsilTexId, d.lowRes);

                    ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.passIndex,
                        MeshTopology.Triangles, 3, 1, mpb);
                });
            }
        }

        private void AddBlurPass(
            RenderGraph renderGraph, string passName, ProfilingSampler sampler,
            TextureHandle source, TextureHandle destination, TextureHandle depthTexture,
            Vector4 texelSize, in VolumeParams volumeValues, int passIndex)
        {
            using (var builder = renderGraph.AddRasterRenderPass<BlurPassData>(
                       passName, out var data, sampler))
            {
                data.material     = m_Material;
                data.source       = source;
                data.texelSize    = texelSize;
                data.volumeValues = volumeValues;
                data.passIndex    = passIndex;

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(depthTexture, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.UseAllGlobalTextures(true);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (BlurPassData d, RasterGraphContext ctx) =>
                {
                    var mpb = d.volumeValues.ToPropertyBlock(s_SharedPropertyBlock);
                    mpb.SetVector(kSsilTexelSizeId, d.texelSize);
                    ctx.cmd.SetGlobalTexture(kSsilTexId, d.source);

                    ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.passIndex,
                        MeshTopology.Triangles, 3, 1, mpb);
                });
            }
        }

        // --------------------------------------------------------------------
        //  Volume 参数 → MaterialPropertyBlock
        // --------------------------------------------------------------------
        private struct VolumeParams
        {
            public float radius;
            public float thickness;
            public float samples;
            public float slices;
            public float jitter;
            public float maxDistance;
            public float distanceFade;
            public float aoStrength;
            public float indirectStrength;
            public float aoBanding;
            public float blurRadius;
            public float debug;
            public Vector4 indirectTint;

            public static VolumeParams From(SSIL v)
            {
                VolumeParams p;
                p.radius           = v.radius.value;
                p.thickness        = v.thickness.value;
                p.samples          = v.sampleCount.value;
                p.slices           = v.sliceCount.value;
                p.jitter           = v.jitter.value;
                p.maxDistance      = v.maxDistance.value;
                p.distanceFade     = v.distanceFade.value;
                p.aoStrength       = v.intensity.value * v.aoStrength.value;
                p.indirectStrength = v.intensity.value * v.indirectStrength.value;
                p.aoBanding        = v.banding.value;
                p.blurRadius       = v.blurRadius.value;
                p.debug            = (float)(int)v.debug.value;
                // Volume 面板里的颜色是用户看到的 sRGB 值，着色器工作在线性空间；
                // MaterialPropertyBlock.SetVector 不做 sRGB→线性转换（同 Underwater）
                p.indirectTint     = ToLinear(v.indirectTint.value);
                return p;
            }

            public MaterialPropertyBlock ToPropertyBlock(MaterialPropertyBlock mpb)
            {
                mpb.Clear();
                mpb.SetFloat(kSsilRadiusId, radius);
                mpb.SetFloat(kSsilThicknessId, thickness);
                mpb.SetFloat(kSsilSamplesId, samples);
                mpb.SetFloat(kSsilSlicesId, slices);
                mpb.SetFloat(kSsilJitterId, jitter);
                mpb.SetFloat(kSsilMaxDistanceId, maxDistance);
                mpb.SetFloat(kSsilDistanceFadeId, distanceFade);
                mpb.SetFloat(kSsilAoStrengthId, aoStrength);
                mpb.SetFloat(kSsilIndirectStrengthId, indirectStrength);
                mpb.SetFloat(kSsilAoBandingId, aoBanding);
                mpb.SetFloat(kSsilBlurRadiusId, blurRadius);
                mpb.SetFloat(kSsilDebugId, debug);
                mpb.SetVector(kSsilIndirectTintId, indirectTint);
                return mpb;
            }

            private static Vector4 ToLinear(Color c)
            {
                if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                    c = c.linear;
                return new Vector4(c.r, c.g, c.b, c.a);
            }
        }

        private class TracePassData
        {
            public Material material;
            public VolumeParams volumeValues;
        }

        private class BlurPassData
        {
            public Material material;
            public TextureHandle source;
            public Vector4 texelSize;
            public VolumeParams volumeValues;
            public int passIndex;
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
