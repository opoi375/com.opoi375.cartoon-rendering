// ============================================================================
// CartoonWaterSimple.shader
// ----------------------------------------------------------------------------
// Simple cartoon water surface with DISTANCE-BASED LOD TESSELLATION built in.
//
// The mesh density no longer matters: the hull/domain stages subdivide each
// triangle by an amount that falls off with distance from the camera, then
// displace the new vertices with the analytic wave field. A 4-vertex Quad
// produces exactly the same smooth surface as a 1000x1000 grid - the source
// mesh only provides the outer silhouette.
//
//   SubShader 1 (main)    : hardware tessellation + LOD factors
//   SubShader 2 (fallback): plain vertex displacement for platforms without
//                           tessellation support (old behaviour)
//
// Minimal baseline (no foam / decorations yet):
//   - analytic wave displacement + per-pixel wave normals
//   - quantised fresnel colour bands (deep -> shallow)
//   - sharp cartoon sun glint (hard-threshold Blinn-Phong)
//   - cartoon rim light (GetToonRimLight style)
// ============================================================================
Shader "CartoonRendering/ToonWater/Simple"
{
    Properties
    {
        [Header(Base Colour)]
        _DeepColor                          ("Deep Water", Color)         = (0.05, 0.25, 0.55, 1)
        _ShallowColor                       ("Shallow Water", Color)      = (0.25, 0.65, 0.85, 1)
        _ColorBands                         ("Colour Bands (0 = smooth)", Range(0, 4)) = 2
        _FresnelStrength                    ("Fresnel Strength", Range(0, 1)) = 0.6

        [Header(Waves)]
        _WaveHeight                         ("Wave Height", Range(0, 0.5)) = 0.06
        _WaveScale                          ("Wave Scale", Range(0.5, 30)) = 6.0
        _WaveSpeed                          ("Wave Speed", Range(0, 5))    = 0.6
        _WaveDir                            ("Wave Direction (xz)", Vector) = (1, 0, 0.6, 0)

        [Header(Tessellation LOD)]
        _TessMax                            ("Tessellation Max", Range(1, 64)) = 12
        _TessMin                            ("Tessellation Min", Range(1, 32)) = 2
        _TessMinDist                        ("Max Detail Distance", Range(1, 200)) = 20
        _TessMaxDist                        ("Min Detail Distance", Range(1, 500)) = 80

        [Header(Sun Glint)]
        _GlintColor                         ("Glint Color", Color)         = (1, 1, 0.95, 1)
        _GlintPower                         ("Glint Power", Range(1, 256)) = 64
        _GlintThreshold                     ("Glint Threshold", Range(0, 1)) = 0.6

        [Header(Rim)]
        _RimColor                           ("Rim Color", Color)           = (0.7, 0.9, 1, 1)
        _RimWidth                           ("Rim Width (0 sharp, 1 soft)", Range(0, 1)) = 0.3
        _RimIntensity                       ("Rim Intensity", Range(0, 4)) = 1.0

        [Header(Surface)]
        _Alpha                              ("Alpha", Range(0, 1))         = 0.92
    }

    // ==========================================================================
    // SubShader 1 - Hardware tessellation with distance LOD.
    // Note: Cull Off. Unity's Quad faces -Z and a Plane faces +Y, so a single
    // cull mode would silently hide one of them. Water is cheap to render
    // double-sided and the back face is mostly hidden by the front anyway.
    // ==========================================================================
    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma hull     hullmain
            #pragma domain   domainmain
            #pragma fragment frag
            #pragma target 5.0
            #pragma require tessellation

            #include "CartoonWaterCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            // Control point = hull shader input / output.
            struct ControlPoint
            {
                float4 positionOS : POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv         : TEXCOORD1;
            };

            struct PatchTess
            {
                float edge[3]   : SV_TessFactor;
                float inside    : SV_InsideTessFactor;
            };

            struct DomainOutput
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
            };

            // ------------------------------------------------------------------
            // Vertex: only converts to world space (used for the LOD distance
            // in the patch constant function). No displacement here - the
            // tessellator generates the real vertices.
            // ------------------------------------------------------------------
            ControlPoint vert(Attributes input)
            {
                ControlPoint output;
                output.positionOS = input.positionOS;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.uv         = input.uv;
                return output;
            }

            // ------------------------------------------------------------------
            // Distance LOD: factor falls from _TessMax (camera) to _TessMin
            // (beyond _TessMaxDist). fractional_odd partitioning keeps
            // transitions seamless.
            // ------------------------------------------------------------------
            float CalcTessFactor(float3 positionWS)
            {
                float dist = distance(positionWS, _WorldSpaceCameraPos);
                float range = max(_TessMaxDist - _TessMinDist, 0.001);
                float t = 1.0 - saturate((dist - _TessMinDist) / range);
                return lerp(_TessMin, _TessMax, t);
            }

            PatchTess patchConstant(InputPatch<ControlPoint, 3> patch)
            {
                PatchTess pt;
                float3 patchCenter = (patch[0].positionWS + patch[1].positionWS + patch[2].positionWS) * (1.0 / 3.0);
                float f = CalcTessFactor(patchCenter);
                pt.edge[0] = f;
                pt.edge[1] = f;
                pt.edge[2] = f;
                pt.inside  = f;
                return pt;
            }

            [domain("tri")]
            [partitioning("fractional_odd")]
            [outputtopology("triangle_cw")]
            [outputcontrolpoints(3)]
            [patchconstantfunc("patchConstant")]
            [maxtessfactor(64.0)]
            ControlPoint hullmain(InputPatch<ControlPoint, 3> patch, uint id : SV_OutputControlPointID)
            {
                return patch[id];
            }

            // ------------------------------------------------------------------
            // Domain: barycentric interpolation of the control points, then the
            // analytic wave displacement in world space.
            // ------------------------------------------------------------------
            [domain("tri")]
            DomainOutput domainmain(PatchTess tess,
                                    float3 bary : SV_DomainLocation,
                                    const OutputPatch<ControlPoint, 3> patch)
            {
                DomainOutput output;

                float3 posOS = patch[0].positionOS.xyz * bary.x
                             + patch[1].positionOS.xyz * bary.y
                             + patch[2].positionOS.xyz * bary.z;
                output.uv = patch[0].uv * bary.x
                          + patch[1].uv * bary.y
                          + patch[2].uv * bary.z;

                float t = _Time.y * _WaveSpeed;
                float3 posWS = TransformObjectToWorld(posOS);
                posWS.y += WaveHeight(posWS.xz, t) * _WaveHeight;

                output.positionWS = posWS;
                output.positionCS = TransformWorldToHClip(posWS);
                output.fogFactor  = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(DomainOutput input) : SV_Target
            {
                half3 color = ShadeWater(input.positionWS, input.fogFactor);
                return half4(color, _Alpha);
            }
            ENDHLSL
        }
    }

    // ==========================================================================
    // SubShader 2 - No tessellation fallback (old behaviour, plain vertex
    // displacement). Used automatically on platforms without tessellation
    // support; also a useful reference for how much the tessellation adds.
    // ==========================================================================
    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 3.5

            #include "CartoonWaterCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float t = _Time.y * _WaveSpeed;
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                posWS.y += WaveHeight(posWS.xz, t) * _WaveHeight;

                output.positionWS = posWS;
                output.positionCS = TransformWorldToHClip(posWS);
                output.uv         = input.uv;
                output.fogFactor  = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 color = ShadeWater(input.positionWS, input.fogFactor);
                return half4(color, _Alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
