// CartoonRendering/Building - stylised surface shader for buildings & props
// (Unity 6 / URP 17).
//
// Same full pipeline support as the PBRToon suite (Progressive Lightmapper
// baking, baked GI sampling, Forward+ additional lights, deferred GBuffer,
// shadowmask, APV), but with SMOOTH stylised lighting - NO cel steps:
//   - Wrapped Lambert main light (_LightWrap), shadow tint (_ShadowColor)
//   - Smooth point/spot lights (Forward+ cluster traversal)
//   - Optional normal map / rim / Blinn-Phong specular / emission
//   - Smooth baked GI with _BakedGIIntensity
//
// Deferred: the GBuffer pass pre-computes the full stylised colour and stores
// it in the lighting target (SV_Target3) with albedo/specular zeroed, so
// URP's deferred PBR light loop adds nothing and the stylised look survives.
Shader "CartoonRendering/Building"
{
    Properties
    {
        [Header(Base)]
        _BaseMap     ("Base Map", 2D) = "white" {}
        _BaseColor   ("Base Color", Color) = (1,1,1,1)
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha Clip", Float) = 0
        _Cutoff      ("Alpha Cutoff", Range(0,1)) = 0.5

        [Header(Normal)]
        [Toggle(_NORMALMAP)] _UseNormalMap ("Enable Normal Map", Float) = 0
        [NoScaleOffset][Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale   ("Normal Scale", Range(0,2)) = 1.0

        [Header(Stylised Lighting soft cel steps)]
        _LightWrap   ("Light Wrap (soft falloff)", Range(0,1)) = 0.25
        _ShadowColor ("Shadow Tint", Color) = (1,1,1,1)
        _ShadeSteps  ("Shade Steps (cel bands)", Range(1,8)) = 4.0
        _ShadeSmooth ("Shade Edge Softness (ease-in-out)", Range(0,0.5)) = 0.15

        [Header(Rim)]
        [Toggle(_RIM_ON)] _UseRim ("Enable Rim", Float) = 0
        _RimColor    ("Rim Color", Color) = (1,1,1,1)
        _RimPower    ("Rim Power", Range(0.5,8)) = 4.0
        _RimIntensity("Rim Intensity", Range(0,2)) = 0.0

        [Header(Specular)]
        [Toggle(_SPECULAR_ON)] _UseSpecular ("Enable Specular", Float) = 0
        _SpecularColor("Specular Color", Color) = (1,1,1,1)
        _SpecularPower("Specular Power", Range(8,256)) = 48.0

        [Header(Emission)]
        [Toggle(_EMISSION)] _UseEmission ("Enable Emission", Float) = 0
        [NoScaleOffset] _EmissionMap ("Emission Map", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,1)

        [Header(Unity 6 Light Baking)]
        _BakedGIIntensity ("Baked GI Intensity", Range(0,2)) = 1.0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" "IgnoreProjector"="True" "UniversalMaterialType"="Lit" }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/CartoonRendering/Shaders/Toon/PBRToon.hlsl"
        TEXTURE2D(_BaseMap);     SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap);     SAMPLER(sampler_BumpMap);
        TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);

        // NOTE: this layout must stay identical in every pass (SRP Batcher).
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;
            half   _Cutoff;
            half   _BumpScale;
            half   _LightWrap;
            half4  _ShadowColor;
            half   _ShadeSteps;
            half   _ShadeSmooth;
            half4  _RimColor;
            half   _RimPower;
            half   _RimIntensity;
            half4  _SpecularColor;
            half   _SpecularPower;
            half4  _EmissionColor;
            half   _BakedGIIntensity;
        CBUFFER_END
        ENDHLSL

        // ----------------------------------------------------------------
        // Forward pass
        // ----------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex   BuildingVert
            #pragma fragment BuildingFrag

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _NORMALMAP
            #pragma shader_feature_local_fragment _RIM_ON
            #pragma shader_feature_local_fragment _SPECULAR_ON
            #pragma shader_feature_local_fragment _EMISSION

            // Baked GI (Unity 6: lightmaps, LPPVs and Adaptive Probe Volumes)
            #pragma multi_compile _ LIGHTMAP_ON DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
            #pragma multi_compile_fragment _ USE_APV_PROBE_OCCLUSION

            // Shadows
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            // Forward+ cluster traversal
            #pragma multi_compile_fragment _ _CLUSTER_LIGHT_LOOP

            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fog

            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #include "CartoonBuildingLighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1; // static lightmap UV (Unity 6 baking)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float3 tangentWS    : TEXCOORD1;
                float3 bitangentWS  : TEXCOORD2;
                float3 positionWS   : TEXCOORD3;
                float3 normalWS     : TEXCOORD4;
                // xy = lightmap UV, zw = SH (mutually exclusive), w = fog
                float4 lightmapOrSH : TEXCOORD5;
            #if defined(USE_APV_PROBE_OCCLUSION)
                float4 probeOcclusion : TEXCOORD6; // Adaptive Probe Volumes
            #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings BuildingVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   nrmInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS  = posInputs.positionCS;
                output.positionWS  = posInputs.positionWS;
                output.normalWS    = nrmInputs.normalWS;
                output.tangentWS   = nrmInputs.tangentWS;
                output.bitangentWS = nrmInputs.bitangentWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapOrSH.xy);
                OUTPUT_SH4(posInputs.positionWS, output.normalWS, GetWorldSpaceNormalizeViewDir(posInputs.positionWS), output.lightmapOrSH.xyz, output.probeOcclusion);
                output.lightmapOrSH.w = ComputeFogFactor(posInputs.positionCS.z);
                return output;
            }

            half4 BuildingFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half alpha = baseTex.a * _BaseColor.a;
            #if defined(_ALPHATEST_ON)
                clip(alpha - _Cutoff);
            #endif
                half3 albedo = baseTex.rgb * _BaseColor.rgb;

                half3 N = SampleBuildingNormal(input.uv,
                                               normalize(input.normalWS),
                                               normalize(input.tangentWS),
                                               normalize(input.bitangentWS));

                half4 shadowMask = SAMPLE_SHADOWMASK(input.lightmapOrSH.xy);

            #if defined(USE_APV_PROBE_OCCLUSION)
                half4 probeOcc = input.probeOcclusion;
            #else
                half4 probeOcc = half4(1, 1, 1, 1);
            #endif

                half3 litColor = BuildingLighting(albedo, N, input.uv,
                                                  input.positionWS, input.lightmapOrSH.xy,
                                                  shadowMask, input.lightmapOrSH.xyz,
                                                  probeOcc, input.positionCS);

                // Fog stays in the forward pass only (URP applies fog after
                // the deferred lighting pass itself).
                litColor = MixFog(litColor, input.lightmapOrSH.w);

                return half4(litColor, alpha);
            }
            ENDHLSL
        }

        // ----------------------------------------------------------------
        // Shadow caster
        // ----------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   BuildingShadowVert
            #pragma fragment BuildingShadowFrag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_vertex _ LOD_FADE_CROSSFADE
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0;
                       UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0;
                       UNITY_VERTEX_INPUT_INSTANCE_ID
                       UNITY_VERTEX_OUTPUT_STEREO };

            V BuildingShadowVert(A i)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 positionWS = TransformObjectToWorld(i.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(i.normalOS);
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _MainLightPosition.xyz));
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                return o;
            }

            half4 BuildingShadowFrag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            #if defined(_ALPHATEST_ON)
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff);
            #endif
            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(i.positionCS);
            #endif
                return 0;
            }
            ENDHLSL
        }

        // ----------------------------------------------------------------
        // Depth only
        // ----------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex   BuildingDepthVert
            #pragma fragment BuildingDepthFrag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ _CLUSTER_LIGHT_LOOP

            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0;
                       UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 positionWS:TEXCOORD1;
                       UNITY_VERTEX_INPUT_INSTANCE_ID
                       UNITY_VERTEX_OUTPUT_STEREO };

            V BuildingDepthVert(A i)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                return o;
            }

            half4 BuildingDepthFrag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            #if defined(_ALPHATEST_ON)
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff);
            #endif
            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(i.positionCS);
            #endif
                return 0;
            }
            ENDHLSL
        }

        // ----------------------------------------------------------------
        // Depth + normals prepass (SSAO / decals)
        // ----------------------------------------------------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex   BuildingDNVert
            #pragma fragment BuildingDNFrag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE

            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"

            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0;
                       UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 normalWS:TEXCOORD1;
                       UNITY_VERTEX_INPUT_INSTANCE_ID
                       UNITY_VERTEX_OUTPUT_STEREO };

            V BuildingDNVert(A i)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS   = TransformObjectToWorldNormal(i.normalOS);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                return o;
            }

            half4 BuildingDNFrag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            #if defined(_ALPHATEST_ON)
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff);
            #endif
            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(i.positionCS);
            #endif
                return half4(PackGBufferNormal(normalize(i.normalWS)), 1.0h);
            }
            ENDHLSL
        }

        // ----------------------------------------------------------------
        // Deferred GBuffer - stores the pre-computed stylised colour.
        // ----------------------------------------------------------------
        Pass
        {
            Name "UniversalGBuffer"
            Tags { "LightMode"="UniversalGBuffer" }

            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex   BuildingGBufferVert
            #pragma fragment BuildingGBufferFrag
            #pragma exclude_renderers gles3
            #pragma target 4.5

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _NORMALMAP
            #pragma shader_feature_local_fragment _RIM_ON
            #pragma shader_feature_local_fragment _SPECULAR_ON
            #pragma shader_feature_local_fragment _EMISSION

            #pragma multi_compile _ LIGHTMAP_ON DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
            #pragma multi_compile_fragment _ USE_APV_PROBE_OCCLUSION
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #include "CartoonBuildingLighting.hlsl"

            // Deferred toon/stylised trick: albedo+specular zeroed with
            // ReceiveShadowsOff so URP's deferred light loop adds nothing;
            // the fully-lit colour goes straight into the lighting buffer.
            struct BuildingGBufferOutput
            {
                half4 albedo   : SV_Target0;
                half4 specular : SV_Target1;
                half4 normal   : SV_Target2;
                half4 lighting : SV_Target3;
            };

            BuildingGBufferOutput PackBuildingGBuffer(half3 litColor, half3 normalWS)
            {
                BuildingGBufferOutput o;
                o.albedo   = half4(0, 0, 0, PackGBufferMaterialFlags(kMaterialFlagReceiveShadowsOff));
                o.specular = half4(0, 0, 0, 0);
                o.normal   = half4(PackGBufferNormal(normalWS), 0.0h);
                o.lighting = half4(litColor, 1);
                return o;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float3 tangentWS    : TEXCOORD1;
                float3 bitangentWS  : TEXCOORD2;
                float3 positionWS   : TEXCOORD3;
                float3 normalWS     : TEXCOORD4;
                float4 lightmapOrSH : TEXCOORD5;
            #if defined(USE_APV_PROBE_OCCLUSION)
                float4 probeOcclusion : TEXCOORD6;
            #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings BuildingGBufferVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   nrmInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS  = posInputs.positionCS;
                output.positionWS  = posInputs.positionWS;
                output.normalWS    = nrmInputs.normalWS;
                output.tangentWS   = nrmInputs.tangentWS;
                output.bitangentWS = nrmInputs.bitangentWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapOrSH.xy);
                OUTPUT_SH4(posInputs.positionWS, output.normalWS, GetWorldSpaceNormalizeViewDir(posInputs.positionWS), output.lightmapOrSH.xyz, output.probeOcclusion);
                output.lightmapOrSH.w = 0.0h;
                return output;
            }

            BuildingGBufferOutput BuildingGBufferFrag(Varyings input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
            #if defined(_ALPHATEST_ON)
                clip(baseTex.a * _BaseColor.a - _Cutoff);
            #endif
                half3 albedo = baseTex.rgb * _BaseColor.rgb;

                half3 N = SampleBuildingNormal(input.uv,
                                               normalize(input.normalWS),
                                               normalize(input.tangentWS),
                                               normalize(input.bitangentWS));

                half4 shadowMask = SAMPLE_SHADOWMASK(input.lightmapOrSH.xy);

            #if defined(USE_APV_PROBE_OCCLUSION)
                half4 probeOcc = input.probeOcclusion;
            #else
                half4 probeOcc = half4(1, 1, 1, 1);
            #endif

                half3 litColor = BuildingLighting(albedo, N, input.uv,
                                                  input.positionWS, input.lightmapOrSH.xy,
                                                  shadowMask, input.lightmapOrSH.xyz,
                                                  probeOcc, input.positionCS);
                return PackBuildingGBuffer(litColor, N);
            }
            ENDHLSL
        }

        // ----------------------------------------------------------------
        // Meta pass - feeds the Progressive Lightmapper (Unity 6 baking).
        // ----------------------------------------------------------------
        Pass
        {
            Name "Meta"
            Tags { "LightMode"="Meta" }

            Cull Off

            HLSLPROGRAM
            #pragma vertex   BuildingMetaVert
            #pragma fragment BuildingMetaFrag

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature EDITOR_VISUALIZATION

            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/MetaPass.hlsl"
            // unity_LightmapST / unity_DynamicLightmapST come from Core.hlsl
            // (HLSLINCLUDE) - declaring them again here would redefine them.

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float2 uv1        : TEXCOORD1; // lightmap UV
                float2 uv2        : TEXCOORD2; // realtime GI UV
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                #ifdef EDITOR_VISUALIZATION
                float2 VizUV        : TEXCOORD1;
                float4 LightCoord   : TEXCOORD2;
                #endif
            };

            Varyings BuildingMetaVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionCS = UnityMetaVertexPosition(input.positionOS.xyz, input.uv1, input.uv2);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                #ifdef EDITOR_VISUALIZATION
                UnityEditorVizData(input.positionOS.xyz, input.uv, input.uv1, input.uv2, output.VizUV, output.LightCoord);
                #endif
                return output;
            }

            half4 BuildingMetaFrag(Varyings input) : SV_Target
            {
                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
            #if defined(_ALPHATEST_ON)
                clip(baseTex.a * _BaseColor.a - _Cutoff);
            #endif

                UnityMetaInput metaInput;
                metaInput.Albedo   = baseTex.rgb * _BaseColor.rgb;
            #if defined(_EMISSION)
                metaInput.Emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb
                                   * _EmissionColor.rgb;
            #else
                metaInput.Emission = half3(0, 0, 0);
            #endif
                #ifdef EDITOR_VISUALIZATION
                metaInput.VizUV      = input.VizUV;
                metaInput.LightCoord = input.LightCoord;
                #endif
                return UnityMetaFragment(metaInput);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
