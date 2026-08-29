// Copyright (c) 2026 CartoonRendering. MIT License.

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace CartoonRendering
{
    /// <summary>
    /// Renders the cartoon procedural sky as a FULL-SCREEN TRIANGLE.
    ///
    /// History / why this approach:
    ///  1. <c>RenderSettings.skybox</c> is unusable: Unity 6 / URP 17's
    ///     native <c>CreateSkyboxRendererList</c> only draws the four
    ///     built-in skybox shaders; custom shaders are silently ignored.
    ///  2. A camera-centred cube mesh worked but produced severe banding:
    ///     with only 12 triangles, the view direction is barycentrically
    ///     interpolated per triangle, so gradient iso-lines break along the
    ///     cube's triangle edges when the camera rotates, and the Scene
    ///     view exposed the wireframe triangles.
    ///  3. The fullscreen triangle reconstructs the view direction of every
    ///     pixel analytically (NDC -> inverse VP -> world ray), which is
    ///     per-pixel exact and completely free of mesh artifacts.
    /// </summary>
    public sealed class CartoonSkyboxFeature : ScriptableRendererFeature
    {
        private const string kShaderName = "CartoonRendering/CartoonSkyboxFullscreen";
        private const string kCloudShaderName = "CartoonRendering/CartoonVolumetricClouds";
        private static readonly int kInvVPId = Shader.PropertyToID("_CartoonInvVP");
        private static readonly int kVolCloudRTId = Shader.PropertyToID("_CartoonVolumetricCloudRT");
        private static readonly int kHasVolCloudsId = Shader.PropertyToID("_CartoonHasVolumetricClouds");

        // Volumetric cloud material property IDs.
        private static readonly int kCloudSunDirId       = Shader.PropertyToID("_CartoonSunDirection");
        private static readonly int kCloudSunColorId     = Shader.PropertyToID("_CartoonSunColor");
        private static readonly int kCloudTintId         = Shader.PropertyToID("_CloudTint");
        private static readonly int kCloudShapeId        = Shader.PropertyToID("_CloudShapeParams");
        private static readonly int kCloudMarchId        = Shader.PropertyToID("_CloudMarchParams");
        private static readonly int kCloudWindId         = Shader.PropertyToID("_CloudWind");
        private static readonly int kCloudShadeId        = Shader.PropertyToID("_CloudShadeParams");
        private static readonly int kAmbDayTopId         = Shader.PropertyToID("_AmbDayTop");
        private static readonly int kAmbDayHorizonId     = Shader.PropertyToID("_AmbDayHorizon");
        private static readonly int kAmbSunsetTopId      = Shader.PropertyToID("_AmbSunsetTop");
        private static readonly int kAmbSunsetHorizonId  = Shader.PropertyToID("_AmbSunsetHorizon");
        private static readonly int kAmbNightTopId       = Shader.PropertyToID("_AmbNightTop");
        private static readonly int kAmbNightHorizonId   = Shader.PropertyToID("_AmbNightHorizon");
        private static readonly int kAmbBlendId          = Shader.PropertyToID("_AmbBlendParams");
        private static readonly int kCloudNoiseTexId     = Shader.PropertyToID("_CloudNoiseTex");
        private static readonly int kCloudHistoryTexId   = Shader.PropertyToID("_CloudHistoryTex");
        private static readonly int kCloudPrevVPId       = Shader.PropertyToID("_CloudPrevVP");
        private static readonly int kCloudHistoryWeightId= Shader.PropertyToID("_CloudHistoryWeight");
        private static readonly int kCloudFrameId        = Shader.PropertyToID("_CartoonCloudFrame");
        private static readonly int kCloudRTSizeId       = Shader.PropertyToID("_CloudRTSize");

        [Tooltip("Material using the CartoonRendering/CartoonSkyboxFullscreen shader. " +
                 "If left empty (or using an outdated shader), the feature builds one " +
                 "automatically at runtime.")]
        public Material skyMaterial;

        [Tooltip("Material using the CartoonRendering/CartoonVolumetricClouds shader. " +
                 "Built automatically at runtime when volumetric clouds are enabled " +
                 "on the CartoonProceduralSky asset.")]
        public Material cloudMaterial;

        [Tooltip("Render pass injection point. BeforeRenderingSkybox keeps the " +
                 "sky behind every other object.")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingSkybox;

        private CartoonSkyboxPass m_Pass;
        private Mesh m_FullscreenTriangle;

        // ---- Temporal cloud accumulation ----------------------------------
        // Per-camera ping-pong history buffers for the half-res cloud layer.
        // The cloud shader reprojects the previous frame onto the current
        // ray's slab anchor and blends; combined with an animated march
        // jitter, slice banding and edge dither converge away within a few
        // frames instead of freezing into a static screen-door pattern.
        private sealed class CloudHistory
        {
            public RTHandle A, B;
            public int Index;
            public Matrix4x4 PrevVP;
            public bool Valid;
            public int Width, Height;
            public RTHandle Current  => (Index & 1) == 0 ? A : B;
            public RTHandle Previous => (Index & 1) == 0 ? B : A;
        }
        private readonly System.Collections.Generic.Dictionary<EntityId, CloudHistory>
            m_CloudHistories = new System.Collections.Generic.Dictionary<EntityId, CloudHistory>();
        private static int s_CloudFrame;

        private CloudHistory GetCloudHistory(EntityId cameraId, int w, int h)
        {
            if (!m_CloudHistories.TryGetValue(cameraId, out var hist))
            {
                hist = new CloudHistory();
                m_CloudHistories[cameraId] = hist;
            }
            // FIXED-SIZE history buffers. Do NOT track the camera target
            // size: edit mode constantly alternates between Game-view and
            // on-demand screenshot resolutions, and every realloc CLEARS
            // the RTs - the blend then dragged everything towards an empty
            // history (clouds washed out). A fixed buffer is allocated once;
            // UV reprojection absorbs any resolution mismatch as mild,
            // self-healing blur.
            const int kHistW = 960, kHistH = 540;
            if (hist.A == null)
            {
                hist.A = RTHandles.Alloc(width: kHistW, height: kHistH,
                    colorFormat: UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat,
                    filterMode: FilterMode.Bilinear, wrapMode: TextureWrapMode.Clamp,
                    name: "CartoonCloudHistoryA");
                hist.B = RTHandles.Alloc(width: kHistW, height: kHistH,
                    colorFormat: UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat,
                    filterMode: FilterMode.Bilinear, wrapMode: TextureWrapMode.Clamp,
                    name: "CartoonCloudHistoryB");
                hist.Width = kHistW;
                hist.Height = kHistH;
                hist.Valid = false;
            }
            return hist;
        }

        /// <inheritdoc/>
        public override void Create()
        {
            m_Pass = new CartoonSkyboxPass(this)
            {
                renderPassEvent = renderPassEvent
            };
            EnsureMesh();
        }

        /// <inheritdoc/>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // Legacy (non-RenderGraph) path.
            if (!TryResolveMaterial())
                return;

            var cameraData = renderingData.cameraData;
            if (!ShouldRenderFor(cameraData.camera))
                return;

            m_Pass.ConfigureInput(ScriptableRenderPassInput.None);
            renderer.EnqueuePass(m_Pass);
        }

        internal bool TryResolveMaterial()
        {
            var shader = Shader.Find(kShaderName);
            if (shader == null)
                return false;

            // The serialized material might still reference an older skybox
            // shader (cube-mesh era). Replace its shader so existing scenes
            // keep working without manual re-assignment.
            if (skyMaterial != null && skyMaterial.shader != shader)
                skyMaterial.shader = shader;

            if (skyMaterial == null)
            {
                skyMaterial = new Material(shader) { name = "CartoonSky_Auto" };
                // Push the current sky asset values onto the auto material.
                CartoonProceduralSkyUpdater.Instance?.ApplyImmediate();
            }
            return true;
        }

        internal bool TryResolveCloudMaterial()
        {
            var shader = Shader.Find(kCloudShaderName);
            if (shader == null)
                return false;

            if (cloudMaterial != null && cloudMaterial.shader != shader)
                cloudMaterial.shader = shader;

            if (cloudMaterial == null)
                cloudMaterial = new Material(shader) { name = "CartoonClouds_Auto" };
            return true;
        }

        internal bool ShouldRenderFor(Camera camera)
        {
            // Only render the sky for cameras that want a sky background.
            if (camera == null)
                return false;

            return camera.clearFlags == CameraClearFlags.Skybox;
        }

        internal Mesh GetFullscreenTriangle()
        {
            EnsureMesh();
            return m_FullscreenTriangle;
        }

        private void EnsureMesh()
        {
            if (m_FullscreenTriangle != null)
                return;

            // The shader generates vertices from SV_VertexID, so the mesh
            // only needs to define the triangle topology / bounds.
            m_FullscreenTriangle = new Mesh
            {
                name = "CartoonSkyFullscreenTri",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = new[] { Vector3.zero, Vector3.zero, Vector3.zero },
                triangles = new[] { 0, 1, 2 },
                bounds = new Bounds(Vector3.zero, new Vector3(10f, 10f, 10f))
            };
        }

        protected override void Dispose(bool disposing)
        {
            foreach (var kv in m_CloudHistories)
            {
                RTHandles.Release(kv.Value.A);
                RTHandles.Release(kv.Value.B);
            }
            m_CloudHistories.Clear();

            if (m_FullscreenTriangle != null)
            {
                if (Application.isPlaying)
                    Destroy(m_FullscreenTriangle);
                else
                    DestroyImmediate(m_FullscreenTriangle);
                m_FullscreenTriangle = null;
            }
        }

        // =====================================================================
        // Pass
        // =====================================================================
        private sealed class CartoonSkyboxPass : ScriptableRenderPass
        {
            private readonly CartoonSkyboxFeature m_Feature;
            private static readonly ProfilingSampler kSampler =
                new ProfilingSampler("CartoonSkyboxFullscreen");

            public CartoonSkyboxPass(CartoonSkyboxFeature feature)
            {
                profilingSampler = kSampler;
                m_Feature = feature;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                if (!m_Feature.TryResolveMaterial() || !m_Feature.ShouldRenderFor(cameraData.camera))
                    return;

                var resources = frameData.Get<UniversalResourceData>();

                // ---- Volumetric cloud layer (half resolution) -------------
                // Rendered into its own RT first; the sky pass then samples
                // and composites it over the procedural sky. Disabled clouds
                // skip the pass entirely and flip the global toggle to 0.
                var sky = CartoonProceduralSkyUpdater.Instance != null
                          ? CartoonProceduralSkyUpdater.Instance.skyAsset : null;
                bool cloudsOn = sky != null && sky.volumetricCloudsEnabled
                                && m_Feature.TryResolveCloudMaterial();

                TextureHandle cloudRT = TextureHandle.nullHandle;
                Matrix4x4 vp = ComputeVP(cameraData);
                Matrix4x4 invVP = vp.inverse;

                // Temporal accumulation is only maintained for game cameras
                // (scene view / preview cameras render the layer fresh).
                CloudHistory hist = null;
                if (cloudsOn && cameraData.camera.cameraType == CameraType.Game)
                {
                    var tgt0 = cameraData.cameraTargetDescriptor;
                    hist = m_Feature.GetCloudHistory(cameraData.camera.GetEntityId(),
                        Mathf.Max(tgt0.width / 2, 4), Mathf.Max(tgt0.height / 2, 4));
                }

                if (cloudsOn)
                {
                    int cloudW, cloudH;
                    if (hist != null)
                    {
                        // Ping-pong: the pass blends the previous frame in
                        // the shader and writes into the other buffer.
                        cloudRT = renderGraph.ImportTexture(hist.Current);
                        cloudW = hist.Width;
                        cloudH = hist.Height;
                    }
                    else
                    {
                        var tgt = cameraData.cameraTargetDescriptor;
                        var desc = new TextureDesc(Mathf.Max(tgt.width / 2, 4),
                                                   Mathf.Max(tgt.height / 2, 4))
                        {
                            name = "_CartoonVolumetricCloudRT",
                            colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat,
                            depthBufferBits = DepthBits.None,
                            msaaSamples = MSAASamples.None,
                            clearBuffer = true,
                            clearColor = Color.clear,
                            // CRITICAL: default filter mode is Point - without
                            // bilinear the half-res upsample shows comb-strip
                            // stair-stepping on thin cloud edges.
                            filterMode = FilterMode.Bilinear
                        };
                        cloudRT = renderGraph.CreateTexture(desc);
                        cloudW = desc.width;
                        cloudH = desc.height;
                    }

                    using (var builder = renderGraph.AddRasterRenderPass<CloudPassData>(
                               "CartoonVolumetricClouds", out var cloudData))
                    {
                        cloudData.mesh = m_Feature.GetFullscreenTriangle();
                        cloudData.material = m_Feature.cloudMaterial;
                        cloudData.invVP = invVP;
                        cloudData.sky = sky;
                        cloudData.prevVP = hist != null ? hist.PrevVP : vp;
                        cloudData.historyWeight = hist != null && hist.Valid
                            ? Mathf.Clamp01(sky.volCloudTemporal) : 0.0f;
                        cloudData.prevHistory = hist != null ? hist.Previous : null;
                        cloudData.frame = s_CloudFrame = (s_CloudFrame + 1) & 1023;
                        cloudData.rtW = cloudW;
                        cloudData.rtH = cloudH;
                        builder.SetRenderAttachment(cloudRT, 0, AccessFlags.Write);
                        if (hist != null && hist.Valid)
                            builder.UseTexture(renderGraph.ImportTexture(hist.Previous),
                                               AccessFlags.Read);
                        builder.AllowPassCulling(false);
                        builder.SetRenderFunc(static (CloudPassData data, RasterGraphContext ctx) =>
                        {
                            PushCloudParams(data.material, data.sky, data.invVP);
                            data.material.SetMatrix(kCloudPrevVPId, data.prevVP);
                            data.material.SetFloat(kCloudHistoryWeightId, data.historyWeight);
                            data.material.SetFloat(kCloudFrameId, data.frame);
                            if (data.prevHistory != null)
                                data.material.SetTexture(kCloudHistoryTexId, data.prevHistory);
                            data.material.SetVector(kCloudRTSizeId,
                                new Vector4(data.rtW, data.rtH, 0.0f, 0.0f));
                            ctx.cmd.DrawMesh(data.mesh, Matrix4x4.identity, data.material, 0, 0);
                        });
                    }

                    if (hist != null)
                    {
                        // This frame's output becomes next frame's history.
                        hist.PrevVP = vp;
                        hist.Valid = true;
                        hist.Index++;
                    }
                }

                using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                           passName, out var passData, profilingSampler))
                {
                    passData.mesh = m_Feature.GetFullscreenTriangle();
                    passData.material = m_Feature.skyMaterial;
                    passData.invVP = invVP;
                    passData.cloudRT = cloudRT;
                    passData.hasClouds = cloudsOn;

                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
                    // The sky shader discards pixels with opaque geometry in
                    // front (via _CameraDepthTexture) - declare the dependency.
                    if (resources.cameraDepthTexture.IsValid())
                        builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    if (cloudsOn)
                        builder.UseTexture(cloudRT, AccessFlags.Read);
                    // The cloud RT / toggle are bound as GLOBALS from the
                    // command buffer (TextureHandle cannot be assigned to a
                    // material), so the pass must opt into global state edits.
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                    {
                        // Upload the per-frame inverse VP directly to the
                        // material; the shader uses it to un-project each
                        // pixel into a world-space view direction.
                        data.material.SetMatrix(kInvVPId, data.invVP);
                        if (data.hasClouds)
                        {
                            ctx.cmd.SetGlobalTexture(kVolCloudRTId, data.cloudRT);
                            ctx.cmd.SetGlobalFloat(kHasVolCloudsId, 1.0f);
                        }
                        else
                        {
                            ctx.cmd.SetGlobalFloat(kHasVolCloudsId, 0.0f);
                        }
                        ctx.cmd.DrawMesh(data.mesh, Matrix4x4.identity, data.material, 0, 0);
                    });
                }
            }

            // Pushes the volumetric cloud parameters from the sky asset onto
            // the cloud material. Called every frame inside the cloud pass so
            // Inspector tweaks on the ScriptableObject apply live.
            private static void PushCloudParams(Material mat, CartoonProceduralSky sky, Matrix4x4 invVP)
            {
                mat.SetMatrix(kInvVPId, invVP);

                // Same light resolution as CartoonProceduralSkyUpdater so the
                // clouds and the sky always agree on the sun direction.
                var updater = CartoonProceduralSkyUpdater.Instance;
                Light light = updater != null ? updater.sunLight : null;
                if (light == null)
                    light = RenderSettings.sun;
                Vector3 sunDir = TimeOfDaySystem.GetSunDirection(light);
                mat.SetVector(kCloudSunDirId, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0.0f));
                mat.SetColor(kCloudSunColorId, sky.sunColor);
                mat.SetColor(kCloudTintId, sky.cloudColor);

                mat.SetVector(kCloudShapeId, new Vector4(
                    sky.volCloudCoverage, sky.volCloudBaseHeight,
                    sky.volCloudThickness, sky.volCloudScale));
                mat.SetVector(kCloudMarchId, new Vector4(
                    sky.volCloudDensity, sky.volCloudMarchSteps,
                    sky.volCloudLightSteps, sky.volCloudDetail));
                Vector2 windDir = new Vector2(1.0f, 0.35f).normalized;
                mat.SetVector(kCloudWindId, new Vector4(
                    sky.volCloudWindSpeed, windDir.x, windDir.y, 0.0f));
                mat.SetVector(kCloudShadeId, new Vector4(
                    sky.volCloudShadeSteps, sky.volCloudShadeSmooth,
                    sky.volCloudSilverIntensity, sky.volCloudSilverPower));

                mat.SetColor(kAmbDayTopId, sky.topColor);
                mat.SetColor(kAmbDayHorizonId, sky.horizonColor);
                mat.SetColor(kAmbSunsetTopId, sky.sunsetTopColor);
                mat.SetColor(kAmbSunsetHorizonId, sky.sunsetHorizonColor);
                mat.SetColor(kAmbNightTopId, sky.nightTopColor);
                mat.SetColor(kAmbNightHorizonId, sky.nightHorizonColor);
                mat.SetVector(kAmbBlendId, new Vector2(sky.sunsetBlendStart, sky.dayBlendEnd));

                if (sky.volCloudNoiseTexture != null)
                    mat.SetTexture(kCloudNoiseTexId, sky.volCloudNoiseTexture);
            }

            private static Matrix4x4 ComputeVP(UniversalCameraData cameraData)
            {
                // Build the EXACT matrix used to rasterise this frame:
                // GetGPUProjectionMatrix(renderIntoTexture:true) applies the
                // platform's clip-space flip, and GetViewMatrix() matches the
                // fragment raster convention.
                var proj = GL.GetGPUProjectionMatrix(
                    cameraData.GetProjectionMatrix(), renderIntoTexture: true);
                return proj * cameraData.GetViewMatrix();
            }

            private static Matrix4x4 ComputeInverseVP(UniversalCameraData cameraData)
            {
                return ComputeVP(cameraData).inverse;
            }

            private class PassData
            {
                public Mesh mesh;
                public Material material;
                public Matrix4x4 invVP;
                public TextureHandle cloudRT;
                public bool hasClouds;
            }

            private class CloudPassData
            {
                public Mesh mesh;
                public Material material;
                public Matrix4x4 invVP;
                public CartoonProceduralSky sky;
                public Matrix4x4 prevVP;
                public float historyWeight;
                public RTHandle prevHistory;
                public int frame;
                public float rtW, rtH;
            }
        }
    }
}
