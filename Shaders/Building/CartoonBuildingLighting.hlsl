// Shared lighting helpers for CartoonRendering/Building (Unity 6 / URP 17).
// Included by the ForwardLit and UniversalGBuffer passes AFTER
// Lighting.hlsl / ProbeVolumeVariants.hlsl. Relies on the textures and the
// UnityPerMaterial cbuffer declared in the shader's HLSLINCLUDE block.
//
// Everything here is SMOOTH - no cel quantisation (buildings/props style).
#ifndef CARTOON_BUILDING_LIGHTING_INCLUDED
#define CARTOON_BUILDING_LIGHTING_INCLUDED

half3 SampleBuildingNormal(float2 uv, half3 N, half3 T, half3 B)
{
#if defined(_NORMALMAP)
    half4 nTex = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv);
    half3 nTS  = UnpackNormalScale(nTex, _BumpScale);
    return normalize(TransformTangentToWorld(nTS, half3x3(T, B, N)));
#else
    return N;
#endif
}

// Soft cel quantisation: ease-in-out (smoothstep) transitions between bands
// instead of a hard floor() step. softness in [0, 0.5]; 0 = hard toon step,
// 0.5 = fully smooth ramp within each band.
half SoftQuantizeSteps(half x, half steps, half softness)
{
    half scaled = saturate(x) * steps;
    half band   = floor(scaled);
    half frac   = scaled - band;
    half soft   = smoothstep(0.5h - softness, 0.5h + softness, frac);
    return (band + soft) / steps;
}

// Stylised lighting core. Wrapped Lambert main light with soft cel steps +
// smooth baked GI + additional lights (Forward+ cluster compatible).
half3 BuildingLighting(half3 albedo, half3 N, float2 baseUV,
                       float3 positionWS, float2 lightmapUV,
                       half4 shadowMask, half3 sh,
                       half4 probeOcclusion, float4 positionCS)
{
    Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS), positionWS, shadowMask);

    // Wrapped Lambert: pulls the terminator towards the dark side for a soft
    // painterly falloff, then quantised into ease-in-out cel bands.
    // Only the angular term is stepped; shadow attenuation stays smooth.
    half ndl  = dot(N, mainLight.direction);
    half wrap = saturate((ndl + _LightWrap) / (1.0h + _LightWrap));
    wrap = SoftQuantizeSteps(wrap, _ShadeSteps, _ShadeSmooth);
    half directTerm = wrap * mainLight.shadowAttenuation;

    // Baked GI - smooth (no GI quantisation on architecture).
    // APV is only sampled when there is NO lightmap (same rule as URP Lit).
#if !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
    half4 apvOcclusion = half4(1, 1, 1, 1);
    half3 bakedGI = SAMPLE_GI(sh, GetAbsolutePositionWS(positionWS), N,
                              GetWorldSpaceNormalizeViewDir(positionWS), positionCS.xy,
                              probeOcclusion, apvOcclusion);
#else
    half3 bakedGI = SAMPLE_GI(lightmapUV, sh, N);
#endif
    bakedGI *= _BakedGIIntensity;

    // Stylised shadow tint: the unlit side blends smoothly towards
    // _ShadowColor as directTerm fades out.
    half3 litModel = lerp(bakedGI * _ShadowColor.rgb,
                          bakedGI + mainLight.color.rgb * directTerm,
                          directTerm);
    half3 litColor = albedo * litModel;

    // Additional point/spot lights (Forward+ compatible) - soft cel steps on
    // the angular term only; distance/shadow attenuation stays smooth so no
    // concentric range rings appear on large flat surfaces.
#ifdef _ADDITIONAL_LIGHTS
    // The macro references a local variable literally named "inputData".
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);

    LIGHT_LOOP_BEGIN(GetAdditionalLightsCount())
        Light addLight = GetAdditionalLight(lightIndex, positionWS, shadowMask);
        half addNdotL = dot(N, addLight.direction);
        half addWrap  = saturate((addNdotL + _LightWrap) / (1.0h + _LightWrap));
        addWrap = SoftQuantizeSteps(addWrap, _ShadeSteps, _ShadeSmooth);
        litColor += albedo * addLight.color.rgb * addWrap
                  * addLight.distanceAttenuation * addLight.shadowAttenuation;
    LIGHT_LOOP_END
#endif

    half3 V = normalize(GetWorldSpaceViewDir(positionWS));

    // Optional Blinn-Phong specular (main light only).
#if defined(_SPECULAR_ON)
    half3 H = normalize(mainLight.direction + V);
    half spec = pow(saturate(dot(N, H)), _SpecularPower);
    litColor += _SpecularColor.rgb * spec * mainLight.shadowAttenuation;
#endif

    // Optional fresnel rim.
#if defined(_RIM_ON)
    half rim = pow(1.0h - saturate(dot(N, V)), _RimPower);
    litColor += _RimColor.rgb * rim * _RimIntensity;
#endif

    // Emission.
#if defined(_EMISSION)
    litColor += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, baseUV).rgb
              * _EmissionColor.rgb;
#endif

    return litColor;
}

#endif // CARTOON_BUILDING_LIGHTING_INCLUDED
