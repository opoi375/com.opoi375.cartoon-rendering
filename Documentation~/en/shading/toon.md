# Toon Characters (PBRToon)

PBRToon is a character-focused toon shader family. It keeps URP Lit.shader's pass structure but swaps the lighting model for cartoon lighting (`PBRToon.hlsl`).

## The Four Shaders

| Shader | Use |
| --- | --- |
| `CartoonRendering/PBRToonBase` | General: body, outfits, props. Base color + shadow ramp + rim + outline |
| `CartoonRendering/PBRToonFace` | Face: **SDF face-shadow map** support — smooth shading at any sun angle |
| `CartoonRendering/PBRToonEye` | Eyes: highlights, iris detail |
| `CartoonRendering/PBRToonHair` | Hair: anisotropic highlight, banded shading |

## Key Features

- **Shadow ramp**: lighting quantized into 2–3 bands with softable edges
- **Rim light**: view-dependent outline brightening
- **SDF face shadows**: a pre-baked SDF map replaces normal-based shadows so facial shading stays clean under any light direction (bake with the [SDF generator](/en/tools/#sdf-generator))
- **Full pipeline support**: Progressive Lightmapper baking (Meta pass), Lightmap / Light Probe sampling, Forward+ additional lights, Shadowmask, APV

## CartoonBuilding

`CartoonRendering/Building` shares the same pipeline support as PBRToon but uses **smooth stylized lighting — no cel steps**:

- Wrapped Lambert key light (`_LightWrap`), shadow tint (`_ShadowColor`)
- Smooth point/spot lights (Forward+ cluster traversal)
- Optional normal map / rim / Blinn-Phong specular / emission
- Smooth baked GI (`_BakedGIIntensity`)

Ideal for buildings and props that want a soft cartoon feel without hard bands.

## Tips

1. Prefer the dedicated Face / Eye / Hair shaders over Base — the results are noticeably better
2. Bake SDF face-shadow maps from mask textures via **Tools > SDF > SDF Generator**
3. Outline width falls off with camera distance, so distant characters stay clean
