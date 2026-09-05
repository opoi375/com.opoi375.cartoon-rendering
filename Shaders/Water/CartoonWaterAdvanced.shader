// ============================================================================
// CartoonWaterAdvanced.shader
// ----------------------------------------------------------------------------
// The "advanced wave" fusion shader. Combines two existing shaders:
//
//   CartoonWaterSimple  ->  hardware tessellation (distance LOD) + analytic
//                           wave displacement (real 3D surface undulation)
//   CartoonWater        ->  depth-based colour gradient + scrolled distorted
//                           noise foam, anti-aliased with smoothstep
//
// Because the shading runs on the displaced surface, wave crests lift the
// water and shrink the depth difference -> foam naturally gathers on crests
// and shorelines. The depth texture comes from _CameraDepthTexture, exactly
// like CartoonWater (camera URP "Depth Texture" = On, renderer "Copy Depth
// Mode" = After Opaques).
//
//   SubShader 1 (main)    : hardware tessellation + distance LOD
//   SubShader 2 (fallback): plain vertex displacement for platforms without
//                           tessellation support
// ============================================================================
Shader "CartoonRendering/ToonWater/Advanced"
{
    Properties
    {
        [Header(Depth Colors)]
        _DepthGradientShallow("Shallow Color", Color) = (0.325, 0.807, 0.971, 0.725)
        _DepthGradientDeep("Deep Color", Color) = (0.086, 0.407, 1, 0.749)
        _DepthMaxDistance("Depth Max Distance", Float) = 5

        [Header(Noise Waves)]
        _SurfaceNoise("Surface Noise", 2D) = "white" {}
        _SurfaceNoiseCutoff("Surface Noise Cutoff", Range(0, 1)) = 0.6
        _SurfaceNoiseScroll("Surface Noise Scroll Amount", Vector) = (0.03, 0.03, 0, 0)

        [Header(Surface Distortion)]
        _SurfaceDistortion("Surface Distortion", 2D) = "white" {}
        _SurfaceDistortionAmount("Surface Distortion Amount", Range(0, 1)) = 0.27

        [Header(Foam)]
        _FoamColor("Foam Color", Color) = (1, 1, 1, 1)
        _FoamDistance("Foam Distance", Range(0.01, 1)) = 0.15

        [Header(Waves)]
        _WaveHeight("Wave Height", Range(0, 0.5)) = 0.06
        _WaveScale("Wave Scale", Range(0.5, 30)) = 6.0
        _WaveSpeed("Wave Speed", Range(0, 5)) = 0.6
        _WaveDir("Wave Direction (xz)", Vector) = (1, 0, 0.6, 0)

        [Header(Tessellation LOD)]
        _TessMax("Tessellation Max", Range(1, 64)) = 12
        _TessMin("Tessellation Min", Range(1, 32)) = 2
        _TessMinDist("Max Detail Distance", Range(1, 200)) = 20
        _TessMaxDist("Min Detail Distance", Range(1, 500)) = 80

        [Header(Transparency)]
        _Alpha("Alpha", Range(0, 1)) = 1
    }

    // ==========================================================================
    // SubShader 1 - Hardware tessellation with distance LOD.
    // Cull Off so both Unity's Quad (-Z facing) and Plane (+Y facing) work.
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

            #include "CartoonWaterAdvanced.hlsl"
            #include "../Library/WorldBend.hlsl"

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
                float4 positionCS     : SV_POSITION;
                float4 screenPosition : TEXCOORD0;   // for the depth lookup
                float3 positionWS     : TEXCOORD1;   // displaced surface point
                float2 noiseUV        : TEXCOORD2;   // tiled noise UV
                float2 distortUV      : TEXCOORD3;   // tiled distortion UV
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
                float2 uv = patch[0].uv * bary.x
                          + patch[1].uv * bary.y
                          + patch[2].uv * bary.z;

                float t = _Time.y * _WaveSpeed;
                float3 posWS = TransformObjectToWorld(posOS);
                posWS.y += WaveHeight(posWS.xz, t) * _WaveHeight;
                posWS = ApplyWorldBend(posWS); // 世界弯曲

                output.positionWS     = posWS;
                output.positionCS     = TransformWorldToHClip(posWS);
                output.screenPosition = ComputeScreenPos(output.positionCS);
                output.noiseUV        = TRANSFORM_TEX(uv, _SurfaceNoise);
                output.distortUV      = TRANSFORM_TEX(uv, _SurfaceDistortion);
                return output;
            }

            half4 frag(DomainOutput input) : SV_Target
            {
                return ShadeCartoonWater(input.screenPosition, input.noiseUV, input.distortUV);
            }
            ENDHLSL
        }
    }

    // ==========================================================================
    // SubShader 2 - No tessellation fallback (plain vertex displacement), for
    // platforms without tessellation support.
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

            #include "CartoonWaterAdvanced.hlsl"
            #include "../Library/WorldBend.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS     : SV_POSITION;
                float4 screenPosition : TEXCOORD0;   // for the depth lookup
                float3 positionWS     : TEXCOORD1;   // displaced surface point
                float2 noiseUV        : TEXCOORD2;   // tiled noise UV
                float2 distortUV      : TEXCOORD3;   // tiled distortion UV
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                float t = _Time.y * _WaveSpeed;
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                posWS.y += WaveHeight(posWS.xz, t) * _WaveHeight;
                posWS = ApplyWorldBend(posWS); // 世界弯曲

                output.positionWS     = posWS;
                output.positionCS     = TransformWorldToHClip(posWS);
                output.screenPosition = ComputeScreenPos(output.positionCS);
                output.noiseUV        = TRANSFORM_TEX(input.uv, _SurfaceNoise);
                output.distortUV      = TRANSFORM_TEX(input.uv, _SurfaceDistortion);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return ShadeCartoonWater(input.screenPosition, input.noiseUV, input.distortUV);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
