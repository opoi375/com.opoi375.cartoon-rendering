// ============================================================================
// PBRToonHair.shader
// ----------------------------------------------------------------------------
// Cartoon hair material. Builds an anisotropic specular streak on top of
// the toon shading. Tangent is derived from a UV gradient in the vertex
// shader if no tangent stream is provided.
//
// Unity 6 (URP 17) baked-lighting support - key changes:
//   * Added full LightMode "Meta" pass (MetaInput/MetaFragment) so the
//     Progressive Lightmapper receives Albedo + Emission (with Alpha Clip).
//   * ForwardLit now samples Lightmap / Light Probes via the URP standard
//     macros DECLARE_LIGHTMAP_OR_SH / OUTPUT_LIGHTMAP_UV / OUTPUT_SH /
//     SAMPLE_GI, with keywords LIGHTMAP_ON / DIRLIGHTMAP_COMBINED /
//     LIGHTMAP_SHADOW_MIXING / SHADOWS_SHADOWMASK.
//   * Main light shadows use the shadowmask-aware GetMainLight() overload so
//     Shadowmask / Distance-Shadowmask modes behave like URP Lit.
//   * Baked GI is added as indirect light (cel-quantised via ToonQuantizeGI)
//     and scaled by the new _BakedGIIntensity property (0..2, default 1).
// ============================================================================
Shader "CartoonRendering/PBRToon/Hair"
{
    Properties
    {
        [MainTexture] _BaseMap            ("Base (hair) Map", 2D)        = "white" {}
        [MainColor]   _BaseColor          ("Hair Color", Color)          = (0.18, 0.10, 0.05, 1)

        _ShadowColor                       ("Shadow Color", Color)        = (0.1, 0.05, 0.05, 1)
        _ShadowSteps                       ("Shadow Steps", Range(0, 8))  = 2

        _TipColor                          ("Tip Color", Color)           = (0.45, 0.20, 0.10, 1)
        _TipGradient                       ("Tip Gradient", Range(0, 1)) = 0.6

        _SpecularColor                     ("Specular Color", Color)      = (1, 0.95, 0.85, 1)
        _SpecularPower                     ("Specular Power", Range(1, 256)) = 80
        _SpecularIntensity                 ("Specular Intensity", Range(0, 2)) = 1.0
        _HairShift                         ("Hair Shift", Range(-0.5, 0.5)) = 0.15

        [HDR] _EmissionColor               ("Emission", Color)            = (0,0,0,1)

        // Baked GI (lightmap / light probe) blending
        _BakedGIIntensity                  ("Baked GI Intensity", Range(0, 2)) = 1.0

        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Toggle(_ALPHATEST_ON)] _AlphaClip  ("Alpha Clip", Float)         = 0
        _Cutoff                            ("Alpha Cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Opaque"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Geometry"
        }

        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex   HairVert
            #pragma fragment HairFrag
            #pragma target 3.5

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP // Forward+

            // Unity 6 baked-lighting keywords
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK

            // Adaptive Probe Volumes (Unity 6)
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            #pragma shader_feature_local _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/CartoonRendering/Shaders/Toon/PBRToon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _ShadowColor;
                float  _ShadowSteps;
                float4 _TipColor;
                float  _TipGradient;
                float4 _SpecularColor;
                float  _SpecularPower;
                float  _SpecularIntensity;
                float  _HairShift;
                float4 _EmissionColor;
                float  _BakedGIIntensity;
                float  _Cutoff;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1; // static lightmap UV (Unity 6 baking)
                float4 tangentOS  : TANGENT;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 tangentWS  : TEXCOORD2;
                float3 bitangentWS: TEXCOORD3;
                float3 positionWS : TEXCOORD4;
                // Declares "float2 lightmapUV" (LIGHTMAP_ON) or "half3 vertexSH"
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 5);
            #ifdef USE_APV_PROBE_OCCLUSION
                float4 probeOcclusion : TEXCOORD6; // Adaptive Probe Volumes
            #endif
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                half3 vertexLight : TEXCOORD7; // per-vertex additional lights
            #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings HairVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS  = pos.positionCS;
                output.positionWS  = pos.positionWS;
                output.normalWS    = nrm.normalWS;
                output.tangentWS   = nrm.tangentWS;
                output.bitangentWS = nrm.bitangentWS;
                output.uv          = TRANSFORM_TEX(input.uv, _BaseMap);

                // Baked lighting: lightmap UVs or per-vertex SH (mutually exclusive)
                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
                OUTPUT_SH4(pos.positionWS, output.normalWS, GetWorldSpaceNormalizeViewDir(pos.positionWS), output.vertexSH, output.probeOcclusion);

                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                    output.vertexLight = VertexLighting(output.positionWS, output.normalWS);
                #endif
                return output;
            }

            half4 HairFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.uv;
                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
                half3 baseCol = baseTex.rgb;

                #if defined(_ALPHATEST_ON)
                    clip(baseTex.a - _Cutoff);
                #endif

                // Tip gradient - hair colour blends from root to tip in V.
                half3 albedo = lerp(baseCol, _TipColor.rgb, smoothstep(1.0 - _TipGradient, 1.0, uv.y));

                float3 N = normalize(input.normalWS);

                // ---------------------------------------------------------------
                // Main light. The shadowmask-aware GetMainLight() overload handles
                // LIGHTMAP_SHADOW_MIXING / SHADOWS_SHADOWMASK exactly like URP Lit.
                // ---------------------------------------------------------------
                half4 shadowMask = half4(1, 1, 1, 1);
                #if defined(SHADOWS_SHADOWMASK) && defined(LIGHTMAP_ON)
                    shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);
                #endif

                Light mainLight;
                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE) || defined(_MAIN_LIGHT_SHADOWS_SCREEN) || (defined(SHADOWS_SHADOWMASK) && defined(LIGHTMAP_ON))
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                    mainLight = GetMainLight(shadowCoord, input.positionWS, shadowMask);
                #else
                    mainLight = GetMainLight();
                #endif

                // Real-time cel shading (unchanged: keeps the cartoon look).
                float NdotL = saturate(dot(N, mainLight.direction));
                float lit = NdotL * mainLight.shadowAttenuation;
                float stepped = (_ShadowSteps > 0.5) ? QuantizeNdotL(lit, _ShadowSteps) : lit;
                half3 litColor = lerp(_ShadowColor.rgb * albedo, albedo, stepped);

                // ---------------------------------------------------------------
                // Baked GI (lightmap / light probes) as INDIRECT light. The GI
                // luminance is quantised into the shadow bands so pure-baked
                // scenes keep readable toon steps instead of smooth gradients.
                // ---------------------------------------------------------------
                // APV (Adaptive Probe Volumes) needs the per-pixel probe path.
                #if !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
                    half4 apvOcclusion = half4(1, 1, 1, 1);
                    half3 bakedGI = SAMPLE_GI(input.vertexSH, GetAbsolutePositionWS(input.positionWS), N,
                                              GetWorldSpaceNormalizeViewDir(input.positionWS), input.positionCS.xy,
                                              input.probeOcclusion, apvOcclusion);
                #else
                    half3 bakedGI = SAMPLE_GI(input.lightmapUV, input.vertexSH, N);
                #endif
                bakedGI = ToonQuantizeGI(bakedGI * _BakedGIIntensity, _ShadowSteps);
                litColor += albedo * bakedGI;

                // ---------------------------------------------------------------
                // Additional lights (point / spot) - cel-stepped diffuse per
                // light. Works in Forward, Forward+ and with mixed/baked shadows.
                // ---------------------------------------------------------------
                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                    litColor += albedo * input.vertexLight;
                #endif
                #if defined(_ADDITIONAL_LIGHTS)
                    // LIGHT_LOOP_BEGIN handles the Forward+ cluster traversal
                    // (plain GetAdditionalLightsCount() returns 0 there). The
                    // macro expects an "inputData" carrying screen UV + posWS.
                    InputData inputData = (InputData)0;
                    inputData.positionWS = input.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    LIGHT_LOOP_BEGIN(GetAdditionalLightsCount())
                        Light addLight = GetAdditionalLight(lightIndex, input.positionWS, shadowMask);
                        float addNdotL = saturate(dot(N, addLight.direction));
                        // Cel-step the angular term only; distance/shadow
                        // attenuation stays smooth to avoid range rings.
                        float addStep = (_ShadowSteps > 0.5) ? QuantizeNdotL(addNdotL, _ShadowSteps) : addNdotL;
                        litColor += albedo * addLight.color.rgb * addStep
                                  * addLight.distanceAttenuation * addLight.shadowAttenuation;
                    LIGHT_LOOP_END
                #endif

                // Anisotropic specular streak.
                float3 V = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 T = normalize(input.tangentWS);
                float3 B = normalize(input.bitangentWS);
                float3 L = mainLight.direction;
                float spec = HairAnisotropicSpec(T, B, N, L, V, _SpecularPower, _HairShift);
                spec = saturate(spec * _SpecularIntensity);
                litColor += _SpecularColor.rgb * spec;

                litColor += _EmissionColor.rgb;
                return half4(litColor, baseTex.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex   ShadowVert
            #pragma fragment ShadowFrag
            #pragma shader_feature_local _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _ShadowColor;
                float  _ShadowSteps;
                float4 _TipColor;
                float  _TipGradient;
                float4 _SpecularColor;
                float  _SpecularPower;
                float  _SpecularIntensity;
                float  _HairShift;
                float4 _EmissionColor;
                float  _BakedGIIntensity;
                float  _Cutoff;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            float3 _LightDirection;

            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };

            V ShadowVert(A i)
            {
                V o;
                float3 posWS = TransformObjectToWorld(i.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(i.normalOS);
                float4 posCS = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    posCS.z = min(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    posCS.z = max(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = posCS;
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                return o;
            }

            half4 ShadowFrag(V i) : SV_Target
            {
                #if defined(_ALPHATEST_ON)
                    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex   DepthVert
            #pragma fragment DepthFrag
            #pragma shader_feature_local _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _ShadowColor;
                float  _ShadowSteps;
                float4 _TipColor;
                float  _TipGradient;
                float4 _SpecularColor;
                float  _SpecularPower;
                float  _SpecularIntensity;
                float  _HairShift;
                float4 _EmissionColor;
                float  _BakedGIIntensity;
                float  _Cutoff;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };

            V DepthVert(A i)
            {
                V o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                return o;
            }

            half4 DepthFrag(V i) : SV_Target
            {
                #if defined(_ALPHATEST_ON)
                    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        // -------------------------------------------------------------------
        // GBuffer Pass (deferred). Writes the FULLY toon-shaded colour into
        // the lighting buffer (SV_Target3) with albedo/specular zeroed, so
        // URP's deferred PBR light loop adds nothing and the cel look is
        // preserved. Fog is applied later by the pipeline fog pass.
        // -------------------------------------------------------------------
        Pass
        {
            Name "UniversalGBuffer"
            Tags { "LightMode" = "UniversalGBuffer" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            // Deferred path is not supported on OpenGL-based APIs.
            #pragma exclude_renderers gles3 glcore

            #pragma vertex   HairGBufferVert
            #pragma fragment HairGBufferFrag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP // Forward+
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma shader_feature_local _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/CartoonRendering/Shaders/Toon/PBRToon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _ShadowColor;
                float  _ShadowSteps;
                float4 _TipColor;
                float  _TipGradient;
                float4 _SpecularColor;
                float  _SpecularPower;
                float  _SpecularIntensity;
                float  _HairShift;
                float4 _EmissionColor;
                float  _BakedGIIntensity;
                float  _Cutoff;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
                float4 tangentOS  : TANGENT;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 tangentWS  : TEXCOORD2;
                float3 bitangentWS: TEXCOORD3;
                float3 positionWS : TEXCOORD4;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 5);
            #ifdef USE_APV_PROBE_OCCLUSION
                float4 probeOcclusion : TEXCOORD6;
            #endif
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                half3 vertexLight : TEXCOORD7;
            #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings HairGBufferVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS  = pos.positionCS;
                output.positionWS  = pos.positionWS;
                output.normalWS    = nrm.normalWS;
                output.tangentWS   = nrm.tangentWS;
                output.bitangentWS = nrm.bitangentWS;
                output.uv          = TRANSFORM_TEX(input.uv, _BaseMap);

                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
                OUTPUT_SH4(pos.positionWS, output.normalWS, GetWorldSpaceNormalizeViewDir(pos.positionWS), output.vertexSH, output.probeOcclusion);

                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                    output.vertexLight = VertexLighting(output.positionWS, output.normalWS);
                #endif
                return output;
            }

            ToonGBufferFragOutput HairGBufferFrag(Varyings input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.uv;
                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
                half3 baseCol = baseTex.rgb;

                #if defined(_ALPHATEST_ON)
                    clip(baseTex.a - _Cutoff);
                #endif

                half3 albedo = lerp(baseCol, _TipColor.rgb, smoothstep(1.0 - _TipGradient, 1.0, uv.y));

                float3 N = normalize(input.normalWS);

                half4 shadowMask = half4(1, 1, 1, 1);
                #if defined(SHADOWS_SHADOWMASK) && defined(LIGHTMAP_ON)
                    shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);
                #endif

                Light mainLight;
                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE) || defined(_MAIN_LIGHT_SHADOWS_SCREEN) || (defined(SHADOWS_SHADOWMASK) && defined(LIGHTMAP_ON))
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                    mainLight = GetMainLight(shadowCoord, input.positionWS, shadowMask);
                #else
                    mainLight = GetMainLight();
                #endif

                // Same cel shading as the forward pass.
                float NdotL = saturate(dot(N, mainLight.direction));
                float lit = NdotL * mainLight.shadowAttenuation;
                float stepped = (_ShadowSteps > 0.5) ? QuantizeNdotL(lit, _ShadowSteps) : lit;
                half3 litColor = lerp(_ShadowColor.rgb * albedo, albedo, stepped);

                #if !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
                    half4 apvOcclusion = half4(1, 1, 1, 1);
                    half3 bakedGI = SAMPLE_GI(input.vertexSH, GetAbsolutePositionWS(input.positionWS), N,
                                              GetWorldSpaceNormalizeViewDir(input.positionWS), input.positionCS.xy,
                                              input.probeOcclusion, apvOcclusion);
                #else
                    half3 bakedGI = SAMPLE_GI(input.lightmapUV, input.vertexSH, N);
                #endif
                bakedGI = ToonQuantizeGI(bakedGI * _BakedGIIntensity, _ShadowSteps);
                litColor += albedo * bakedGI;

                // Additional lights (point / spot), cel-stepped - evaluated here
                // because the deferred light loop adds nothing (albedo = 0).
                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                    litColor += albedo * input.vertexLight;
                #endif
                #if defined(_ADDITIONAL_LIGHTS)
                    // Forward+ compatible traversal (see forward pass notes).
                    InputData inputData = (InputData)0;
                    inputData.positionWS = input.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    LIGHT_LOOP_BEGIN(GetAdditionalLightsCount())
                        Light addLight = GetAdditionalLight(lightIndex, input.positionWS, shadowMask);
                        float addNdotL = saturate(dot(N, addLight.direction));
                        // Cel-stepped angular term (see forward pass notes).
                        float addStep = (_ShadowSteps > 0.5) ? QuantizeNdotL(addNdotL, _ShadowSteps) : addNdotL;
                        litColor += albedo * addLight.color.rgb * addStep
                                  * addLight.distanceAttenuation * addLight.shadowAttenuation;
                    LIGHT_LOOP_END
                #endif

                // Anisotropic specular streak.
                float3 V = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 T = normalize(input.tangentWS);
                float3 B = normalize(input.bitangentWS);
                float3 L = mainLight.direction;
                float spec = HairAnisotropicSpec(T, B, N, L, V, _SpecularPower, _HairShift);
                spec = saturate(spec * _SpecularIntensity);
                litColor += _SpecularColor.rgb * spec;

                litColor += _EmissionColor.rgb;
                return PackToonGBuffer(litColor, N);
            }
            ENDHLSL
        }

        // -------------------------------------------------------------------
        // DepthNormals Pass - needed for SSAO and deferred decals.
        // -------------------------------------------------------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex   DNVert
            #pragma fragment DNFrag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _ShadowColor;
                float  _ShadowSteps;
                float4 _TipColor;
                float  _TipGradient;
                float4 _SpecularColor;
                float  _SpecularPower;
                float  _SpecularIntensity;
                float  _HairShift;
                float4 _EmissionColor;
                float  _BakedGIIntensity;
                float  _Cutoff;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD0; float2 uv:TEXCOORD1; };

            V DNVert(A i)
            {
                V o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                return o;
            }

            half4 DNFrag(V i) : SV_Target
            {
                #if defined(_ALPHATEST_ON)
                    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff);
                #endif
                #if defined(_GBUFFER_NORMALS_OCT)
                    float3 n = normalize(i.normalWS);
                    float2 oct = PackNormalOctQuadEncode(n);
                    float2 remapped = saturate(oct * 0.5 + 0.5);
                    return half4(PackFloat2To888(remapped), 0.0);
                #else
                    return half4(normalize(i.normalWS), 0.0);
                #endif
            }
            ENDHLSL
        }

        // -------------------------------------------------------------------
        // Meta Pass - feeds Albedo & Emission to the Progressive Lightmapper.
        // Uses the Unity 6 / URP 17 recommended MetaInput + MetaFragment path.
        // -------------------------------------------------------------------
        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }

            Cull Off

            HLSLPROGRAM
            #pragma vertex   MetaVert
            #pragma fragment MetaFrag
            #pragma shader_feature EDITOR_VISUALIZATION
            #pragma shader_feature_local_fragment _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _ShadowColor;
                float  _ShadowSteps;
                float4 _TipColor;
                float  _TipGradient;
                float4 _SpecularColor;
                float  _SpecularPower;
                float  _SpecularIntensity;
                float  _HairShift;
                float4 _EmissionColor;
                float  _BakedGIIntensity;
                float  _Cutoff;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct MetaAttributes
            {
                float4 positionOS : POSITION;
                float2 uv0        : TEXCOORD0;
                float2 uv1        : TEXCOORD1; // static lightmap UV
                float2 uv2        : TEXCOORD2; // dynamic lightmap UV
            };

            struct MetaVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            #ifdef EDITOR_VISUALIZATION
                float2 VizUV      : TEXCOORD1;
                float4 LightCoord : TEXCOORD2;
            #endif
            };

            MetaVaryings MetaVert(MetaAttributes input)
            {
                MetaVaryings output = (MetaVaryings)0;
                output.positionCS = UnityMetaVertexPosition(input.positionOS.xyz, input.uv1, input.uv2);
                output.uv = TRANSFORM_TEX(input.uv0, _BaseMap);
            #ifdef EDITOR_VISUALIZATION
                UnityEditorVizData(input.positionOS.xyz, input.uv0, input.uv1, input.uv2, output.VizUV, output.LightCoord);
            #endif
                return output;
            }

            half4 MetaFrag(MetaVaryings input) : SV_Target
            {
                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;

                #if defined(_ALPHATEST_ON)
                    clip(baseTex.a - _Cutoff);
                #endif

                // Replicate the root->tip gradient so the baked albedo matches
                // the visible surface colour. The anisotropic streak is a
                // runtime shading effect and is intentionally excluded.
                half3 albedo = lerp(baseTex.rgb, _TipColor.rgb,
                                    smoothstep(1.0 - _TipGradient, 1.0, input.uv.y));

                UnityMetaInput metaInput;
                metaInput.Albedo   = albedo;
                // HDR emission is passed through so it bakes into the lightmap.
                metaInput.Emission = _EmissionColor.rgb;
            #ifdef EDITOR_VISUALIZATION
                metaInput.VizUV      = input.VizUV;
                metaInput.LightCoord = input.LightCoord;
            #endif
                return UnityMetaFragment(metaInput);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
