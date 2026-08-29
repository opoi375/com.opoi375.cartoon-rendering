#ifndef CARTOON_WATER_ADVANCED_INCLUDED
#define CARTOON_WATER_ADVANCED_INCLUDED

// ============================================================================
// CartoonWaterAdvanced.hlsl
// ----------------------------------------------------------------------------
// Shared code for CartoonWaterAdvanced.shader - the fusion of:
//
//   CartoonWaterSimple (waves + tessellation)
//     - analytic wave field (WaveHeight / WaveGrad), world-space based so the
//       source mesh density does not matter, the tessellator supplies the
//       intermediate vertices
//     - hardware tessellation with distance LOD (hull/domain in the shader)
//
//   CartoonWater (depth-based cartoon shading)
//     - depth-based water colour (shallow -> deep gradient)
//     - scrolled, distorted noise foam with a depth-lowered cutoff
//     - anti-aliased foam edges (smoothstep band)
//
// The fusion: the depth shading runs on the displaced wave surface, so the
// wave crests push the water surface closer to the ground -> foam naturally
// gathers on crests and shorelines.
//
// One UnityPerMaterial CBUFFER keeps the SRP batcher happy across every pass.
// ============================================================================

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

CBUFFER_START(UnityPerMaterial)
    // Depth-based colour (Cartoon)
    float4 _DepthGradientShallow;
    float4 _DepthGradientDeep;
    float  _DepthMaxDistance;

    // Noise foam (Cartoon)
    float4 _SurfaceNoise_ST;
    float  _SurfaceNoiseCutoff;
    float4 _SurfaceNoiseScroll;

    // Distortion (Cartoon)
    float4 _SurfaceDistortion_ST;
    float  _SurfaceDistortionAmount;

    // Foam (Cartoon)
    float4 _FoamColor;
    float  _FoamDistance;

    // Waves (Simple)
    float  _WaveHeight;
    float  _WaveScale;
    float  _WaveSpeed;
    float4 _WaveDir;

    // Tessellation LOD (Simple)
    float  _TessMax;
    float  _TessMin;
    float  _TessMinDist;
    float  _TessMaxDist;

    float  _Alpha;
CBUFFER_END

TEXTURE2D(_SurfaceNoise);
SAMPLER(sampler_SurfaceNoise);
TEXTURE2D(_SurfaceDistortion);
SAMPLER(sampler_SurfaceDistortion);

// Anti-aliased foam edge band width.
#define SMOOTHSTEP_AA 0.01

// ----------------------------------------------------------------------------
// Wave field (from CartoonWaterSimple). World-space based so the source mesh
// density does not matter - the tessellator provides the intermediate
// vertices. _WaveDir is a (x, z) direction; it is normalised before use so
// any length works.
// ----------------------------------------------------------------------------
float2 SafeDir(float2 d)
{
    return length(d) > 1e-4 ? normalize(d) : float2(1.0, 0.0);
}

float WaveHeight(float2 wxz, float t)
{
    float h = 0.0;
    h += sin(dot(wxz, SafeDir(_WaveDir.xz)) * _WaveScale + t * 1.00) * 0.50;
    h += sin(dot(wxz, float2(-0.6, 1.0)) * _WaveScale * 1.70 + t * 1.31) * 0.25;
    return h;
}

float2 WaveGrad(float2 wxz, float t)
{
    float2 dirA = SafeDir(_WaveDir.xz);
    float2 dirB = float2(-0.6, 1.0);
    float2 g = 0.0;
    g += cos(dot(wxz, dirA) * _WaveScale + t * 1.00) * 0.50 * dirA * _WaveScale;
    g += cos(dot(wxz, dirB) * _WaveScale * 1.70 + t * 1.31) * 0.25 * dirB * _WaveScale * 1.70;
    return g;
}

// ----------------------------------------------------------------------------
// Custom alpha blending (from Cartoon): accumulates the combined alpha so the
// foam colour stays controllable.
// ----------------------------------------------------------------------------
float4 alphaBlend(float4 top, float4 bottom)
{
    float3 color = (top.rgb * top.a) + (bottom.rgb * (1 - top.a));
    float alpha = top.a + bottom.a * (1 - top.a);
    return float4(color, alpha);
}

// ----------------------------------------------------------------------------
// Depth-based cartoon water shading (the Cartoon shader's fragment body),
// shading the displaced, tessellated wave surface.
//
//   screenPosition - ComputeScreenPos(positionCS), used for the depth lookup
//   noiseUV        - tiled, scrolling noise UV (from the mesh UV)
//   distortUV      - tiled distortion UV
// ----------------------------------------------------------------------------
half4 ShadeCartoonWater(float4 screenPosition, float2 noiseUV, float2 distortUV)
{
    // 1. Depth difference (article part 1)
    float2 screenUV = screenPosition.xy / screenPosition.w;
    float existingDepth01 = SampleSceneDepth(screenUV);
    float existingDepthLinear = LinearEyeDepth(existingDepth01, _ZBufferParams);
    float waterDepth = screenPosition.w;
    float depthDifference = existingDepthLinear - waterDepth;

    // 2. Depth-based water colour
    float waterDepthDifference01 = saturate(depthDifference / _DepthMaxDistance);
    float4 waterColor = lerp(_DepthGradientShallow, _DepthGradientDeep, waterDepthDifference01);

    // 3. Surface distortion (perturbs the noise UV)
    float2 distortSample =
        (SAMPLE_TEXTURE2D(_SurfaceDistortion, sampler_SurfaceDistortion, distortUV).xy * 2.0 - 1.0)
        * _SurfaceDistortionAmount;

    // 4. Scrolling noise (article part 2.c)
    float2 noiseUVw = float2(
        noiseUV.x + _Time.y * _SurfaceNoiseScroll.x + distortSample.x,
        noiseUV.y + _Time.y * _SurfaceNoiseScroll.y + distortSample.y);
    float surfaceNoiseSample =
        SAMPLE_TEXTURE2D(_SurfaceNoise, sampler_SurfaceNoise, noiseUVw).r;

    // 5. Foam band: shallow water lowers the cutoff -> foam on shorelines and
    //    on wave crests (the crest lifts the surface so depthDifference shrinks)
    float foamDepthDifference01 = saturate(depthDifference / _FoamDistance);
    float surfaceNoiseCutoff = foamDepthDifference01 * _SurfaceNoiseCutoff;

    // 6. Anti-aliased foam edge
    float surfaceNoise = smoothstep(
        surfaceNoiseCutoff - SMOOTHSTEP_AA,
        surfaceNoiseCutoff + SMOOTHSTEP_AA,
        surfaceNoiseSample);

    // 7. Foam colour + custom blend, then global alpha
    float4 surfaceNoiseColor = _FoamColor;
    surfaceNoiseColor.a *= surfaceNoise;
    float4 finalColor = alphaBlend(surfaceNoiseColor, waterColor);
    finalColor.a *= _Alpha;

    return finalColor;
}

#endif // CARTOON_WATER_ADVANCED_INCLUDED
