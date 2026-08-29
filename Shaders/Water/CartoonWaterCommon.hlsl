#ifndef CARTOON_WATER_COMMON_INCLUDED
#define CARTOON_WATER_COMMON_INCLUDED

// ============================================================================
// CartoonWaterCommon.hlsl
// ----------------------------------------------------------------------------
// Shared code for the CartoonWaterSimple shader:
//   - UnityPerMaterial CBUFFER (identical layout in every pass so the SRP
//     batcher stays happy)
//   - analytic wave field (used by both the tessellated geometry displacement
//     and the per-pixel normal shading, so they always agree)
//   - ShadeWater(): the basic cartoon water shading body, shared by the
//     tessellation pass and the no-tessellation fallback pass
//
// Minimal baseline: waves + fresnel colour bands + sun glint + rim light.
// ============================================================================

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "../Toon/PBRToon.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _DeepColor;
    float4 _ShallowColor;
    float  _ColorBands;
    float  _FresnelStrength;

    float  _WaveHeight;
    float  _WaveScale;
    float  _WaveSpeed;
    float4 _WaveDir;

    float4 _GlintColor;
    float  _GlintPower;
    float  _GlintThreshold;

    float4 _RimColor;
    float  _RimWidth;
    float  _RimIntensity;

    float  _Alpha;

    // Tessellation LOD
    float  _TessMax;
    float  _TessMin;
    float  _TessMinDist;
    float  _TessMaxDist;
CBUFFER_END

// ----------------------------------------------------------------------------
// Wave field. World-space based so the density of the source mesh does not
// matter - the tessellator provides the intermediate vertices.
// ----------------------------------------------------------------------------
float2 SafeDir(float2 d)
{
    return length(d) > 1e-4 ? normalize(d) : float2(1.0, 0.0);
}

float WaveHeight(float2 wxz, float t)
{
    float h = 0.0;
    h += sin(dot(wxz, SafeDir(_WaveDir.xy)) * _WaveScale + t * 1.00) * 0.50;
    h += sin(dot(wxz, float2(-0.6, 1.0)) * _WaveScale * 1.70 + t * 1.31) * 0.25;
    return h;
}

float2 WaveGrad(float2 wxz, float t)
{
    float2 dirA = SafeDir(_WaveDir.xy);
    float2 dirB = float2(-0.6, 1.0);
    float2 g = 0.0;
    g += cos(dot(wxz, dirA) * _WaveScale + t * 1.00) * 0.50 * dirA * _WaveScale;
    g += cos(dot(wxz, dirB) * _WaveScale * 1.70 + t * 1.31) * 0.25 * dirB * _WaveScale * 1.70;
    return g;
}

// ----------------------------------------------------------------------------
// ShadeWater - the basic cartoon water shading body. Shared by both the
// tessellation pass and the no-tessellation fallback pass.
//   positionWS  - world position of the surface point (already displaced)
//   fogFactor   - precomputed fog factor
// ----------------------------------------------------------------------------
half3 ShadeWater(float3 positionWS, float fogFactor)
{
    float t = _Time.y * _WaveSpeed;
    float2 wxz = positionWS.xz;

    // Water surface normal from the analytic wave gradient. The gradient is
    // scaled by _WaveHeight so the shading matches the actual displacement.
    float2 grad = WaveGrad(wxz, t) * _WaveHeight;
    float3 N = normalize(float3(-grad.x, 1.0, -grad.y));
    float3 V = normalize(GetWorldSpaceViewDir(positionWS));

    // Base colour: deep -> shallow blended by fresnel, then quantised into
    // cartoon bands (0 = keep smooth).
    float NdotV = saturate(dot(N, V));
    float fresnel = pow(1.0 - NdotV, 3.0) * _FresnelStrength;
    float mix01 = saturate(fresnel);
    if (_ColorBands > 0.5)
        mix01 = QuantizeNdotL(mix01, _ColorBands);
    half3 color = lerp(_DeepColor.rgb, _ShallowColor.rgb, mix01);

    // Sun glint: sharp cartoon highlight.
    Light mainLight = GetMainLight();
    float NdotL = saturate(dot(N, mainLight.direction));
    float3 H = normalize(mainLight.direction + V);
    float spec = pow(saturate(dot(N, H)), _GlintPower);
    float glint = smoothstep(_GlintThreshold - 0.03, _GlintThreshold + 0.03, spec);
    color += _GlintColor.rgb * glint * NdotL;

    // Rim light (cartoon fresnel, GetToonRimLight style).
    float rim = 1.0 - NdotV;
    float exponent = lerp(16.0, 1.0, saturate(_RimWidth));
    rim = pow(rim, exponent) * _RimIntensity;
    color += _RimColor.rgb * rim;

    return MixFog(color, fogFactor);
}

#endif // CARTOON_WATER_COMMON_INCLUDED
