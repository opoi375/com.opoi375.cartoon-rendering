#ifndef PBR_TOON_INCLUDED
#define PBR_TOON_INCLUDED

// ============================================================================
// PBRToon.hlsl
// ----------------------------------------------------------------------------
// Self-contained helpers for cartoon-style shading. Designed to compile with
// only the standard URP/Core ShaderLibrary headers - no DanbaidongRP
// modified libraries are required.
//
// Exposed entry points (kept stable for use across PBRToonBase / Eye /
// Face / Hair):
//   SampleShadowRamp(rampTex, rampSampler, ndotL[, bias])
//       - 1D row sample using the Y=0.5 line of the texture
//   SampleSpecularRamp(rampTex, rampSampler, specRange[, bias])
//       - 1D row sample used to quantise the Blinn-Phong half-vector dot
//   GetToonRimLight(normalWS, viewDirWS, width, intensity)
//       - Fresnel-style rim term that fades into shadow.
//   QuantizeNdotL(ndotL, steps)
//       - scalar band quantisation when no ramp texture is supplied
//   OutlineExtrude(positionOS, normalOS, width)
//       - inverted-hull vertex offset
//   StylisedSpecular(...)
//       - hard-threshold Blinn-Phong
//   HairAnisotropicSpec(...)
//       - Ward-like elongated highlight
// ============================================================================

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferCommon.hlsl"

// ----------------------------------------------------------------------------
// SampleShadowRamp - 1D ramp texture sampled with NdotL.
// Use the Y=0.125 row of the texture so the artist can pack multiple ramps
// (skin, hair, cloth) in a vertical strip.
// ----------------------------------------------------------------------------
float3 SampleShadowRamp(TEXTURE2D_PARAM(rampTex, rampSampler), float ndotL, float bias = 0.0)
{
    float u = saturate(ndotL + bias);
    return SAMPLE_TEXTURE2D(rampTex, rampSampler, float2(u, 0.125)).rgb;
}

// ----------------------------------------------------------------------------
// SampleSpecularRamp - same idea as SampleShadowRamp but for the spec
// highlight. Defaults to Y=0.375 which is the row below the shadow ramp so a
// 2-row texture covers both.
// ----------------------------------------------------------------------------
float3 SampleSpecularRamp(TEXTURE2D_PARAM(rampTex, rampSampler), float specRange, float bias = 0.0)
{
    float u = saturate(specRange + bias);
    return SAMPLE_TEXTURE2D(rampTex, rampSampler, float2(u, 0.375)).rgb;
}

// ----------------------------------------------------------------------------
// Quantise scalar NdotL into N discrete bands. Used when no ramp texture
// is supplied.
// ----------------------------------------------------------------------------
float QuantizeNdotL(float ndotL, float steps)
{
    float s = max(steps, 1.0);
    return floor(saturate(ndotL) * s) / s;
}

// ----------------------------------------------------------------------------
// GetToonRimLight - the canonical cartoon rim light. Width controls the
// sharpness of the Fresnel falloff; intensity is the final multiplier.
// Suppresses the rim in shadow regions so it reads as a true silhouette.
// ----------------------------------------------------------------------------
float3 GetToonRimLight(float3 normalWS, float3 viewDirWS, float width, float intensity)
{
    float NdotV = saturate(dot(normalWS, viewDirWS));
    float rim = 1.0 - NdotV;
    // Width 0 -> extremely sharp edge, width 1 -> soft glow over the full
    // hemisphere. Map to pow exponent.
    float exponent = lerp(16.0, 1.0, saturate(width));
    rim = pow(rim, exponent);
    return float3(rim, rim, rim) * intensity;
}

// Kept for backward compatibility with the previous code path.
float ComputeRimLight(float3 normalWS, float3 viewDirWS, float power, float intensity)
{
    float rim = 1.0 - saturate(dot(normalWS, viewDirWS));
    rim = pow(rim, max(power, 0.0001));
    return rim * intensity;
}

// ----------------------------------------------------------------------------
// OutlineExtrude - inverted-hull vertex offset.
// ----------------------------------------------------------------------------
float3 OutlineExtrude(float3 positionOS, float3 normalOS, float width)
{
    float3 n = normalize(normalOS);
    return positionOS + n * width * 0.01;
}

// ----------------------------------------------------------------------------
// Stylised specular - Blinn-Phong with a hard threshold.
// ----------------------------------------------------------------------------
float StylisedSpecular(float3 normalWS, float3 lightDirWS, float3 viewDirWS,
                       float power, float threshold)
{
    float3 H = normalize(lightDirWS + viewDirWS);
    float spec = saturate(dot(normalWS, H));
    spec = pow(spec, max(power, 1.0));
    return step(threshold, spec);
}

// ----------------------------------------------------------------------------
// HairAnisotropicSpec - Ward-like elongated highlight. shift is the same
// field exposed as _HairShift in PBRToonHair.shader.
// ----------------------------------------------------------------------------
float HairAnisotropicSpec(float3 T, float3 B, float3 N, float3 L, float3 V, float power, float shift)
{
    float3 H = normalize(L + V);
    float TdotH = dot(T, H);
    float BdotH = dot(B, H);
    float NdotH = dot(N, H);
    float NdotL = saturate(dot(N, L));
    float NdotV = saturate(dot(N, V));

    float aniso = pow(max(1.0 - TdotH * TdotH - BdotH * BdotH, 0.0), power * 0.5);
    aniso *= exp(-2.0 * shift * shift * (1.0 - NdotH * NdotH));

    float denom = 4.0 * 3.14159 * sqrt(NdotL * NdotV);
    return aniso / max(denom, 1e-4);
}

// ----------------------------------------------------------------------------
// ToonQuantizeGI - quantise baked GI (lightmap / light probe) luminance into
// toon bands while preserving hue and HDR head-room. This keeps readable
// cartoon shadow steps even in pure-baked scenes, instead of smooth PBR
// gradients. steps < 0.5 (= ramp mode) keeps the smooth gradient untouched.
// ----------------------------------------------------------------------------
half3 ToonQuantizeGI(half3 gi, float steps)
{
    float lum = Luminance(gi);
    if (steps < 0.5 || lum <= 1e-4)
        return gi;

    // ceil() maps luminance into 1/steps..1 bands (never fully black while
    // there is bounce light); HDR range above 1.0 is kept untouched so
    // strong bounce light is not clamped away.
    float band = ceil(saturate(lum) * steps) / steps;
    band += max(lum - 1.0, 0.0);
    return gi * (band / lum);
}

#endif // PBR_TOON_INCLUDED

#ifndef PBR_TOON_DEFERRED_INCLUDED
#define PBR_TOON_DEFERRED_INCLUDED

// ----------------------------------------------------------------------------
// Deferred (UniversalGBuffer) support.
//
// URP's deferred light loop applies standard PBR lighting from the GBuffer,
// which would destroy the cel look. To keep the toon style in deferred mode
// we write the FULLY toon-shaded colour into the lighting buffer (SV_Target3)
// and zero albedo/specular, so the deferred light loop contributes nothing.
// The world-space normal is still written for SSAO / deferred decals.
// Fog is intentionally NOT applied here - URP applies fog as a post pass in
// deferred mode.
// ----------------------------------------------------------------------------
struct ToonGBufferFragOutput
{
    half4 gBuffer0 : SV_Target0; // albedo(=0)   + materialFlags
    half4 gBuffer1 : SV_Target1; // specular(=0) + occlusion
    half4 gBuffer2 : SV_Target2; // packed normal + smoothness
    half4 color    : SV_Target3; // lighting buffer: pre-lit toon colour
};

ToonGBufferFragOutput PackToonGBuffer(half3 toonColor, half3 normalWS)
{
    ToonGBufferFragOutput output;
    uint flags = kMaterialFlagReceiveShadowsOff | kMaterialFlagSpecularHighlightsOff;
    output.gBuffer0 = half4(0.0, 0.0, 0.0, PackGBufferMaterialFlags(flags));
    output.gBuffer1 = half4(0.0, 0.0, 0.0, 1.0);
    output.gBuffer2 = half4(PackGBufferNormal(normalWS), 0.0);
    output.color    = half4(toonColor, 1.0);
    return output;
}

#endif // PBR_TOON_DEFERRED_INCLUDED
