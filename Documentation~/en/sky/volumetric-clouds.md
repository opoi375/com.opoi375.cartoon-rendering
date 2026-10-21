# Volumetric Clouds

Ray-marched volumetric clouds in a half-resolution RT. Density is a **continuous 3D noise field**, following the same pipeline as Schneider's *Real-time Volumetric Cloudscapes of Horizon: Zero Dawn* and Guerrilla's **Nubis**.

![Volumetric clouds: cauliflower structure, lit crowns, dark bases, distant haze](/sky/volumetric-clouds.webp)

::: tip Why this was rewritten
An earlier version used "discrete cloud instances" (split the world into cells, place one ellipsoid cloud per cell) to avoid a continuous field percolating into one slab. **That approach cannot work**: a geometric ellipsoid with a radial profile always renders as a pill, and no amount of stacked noise fixes it.

In real implementations the **shape comes entirely from dilating and eroding a continuous 3D noise**, and the sense of separate clouds comes from the **weather map's local coverage**. This release follows that.
:::

## Density pipeline

```
weather(wuv)
  |-- R  -> local coverage
  |-- G  -> cloud type (stratus <-> cumulus; shapes the height band)
  +-- BA -> domain warp (pushes sample coordinates so edges curl)

shape  = tex3D(CloudShape3D, uvw)          R = Perlin-Worley, GBA = Worley FBM @ f4/f8/f16
fbmW   = dot(shape.gba, (0.625, 0.25, 0.125))
base   = remap(shape.r, -(1 - fbmW), 1)    <- dilate Perlin by Worley (the cauliflower masses)
base  *= heightGradient(type)              <- fade in at the base, out at the top
base   = remap(base, 1 - coverage, 1) * coverage
density = remap(base, detail * 0.22, 1)    <- detail noise only lifts the lower edge: rim erosion
```

All three maps are produced by **Tools > Clouds > Bake Noise** and ship inside the package:

| Asset | Size | Contents |
| --- | --- | --- |
| `CloudShape3D` | 128^3 | R = `Remap(perlinFBM, 0, 1, worleyFBM, 1)`; GBA = Worley FBM @ f4 / f8 / f16 |
| `CloudDetail3D` | 32^3 | High-frequency Worley FBM for fine bite on the rim |
| `CloudWeather2D` | 512^2 | R = coverage (cloud systems), G = cloud type, BA = domain warp |

::: warning Do not histogram-equalise (CDF)
A uniform distribution looks punchier, but it **flattens the Perlin-Worley mass structure** and the clouds dissolve into a uniform sheet of lint. That was the mistake of an earlier version.
:::

## Key parameters

| Parameter | Notes |
| --- | --- |
| **Shape Scale** | **Metres per noise tile - the most direct "how big are the clouds" knob.** Cumulus: 1500~2500. Too small reads as smoke streaks (455 m does exactly that); too large smears into one mass |
| **Coverage** | Global coverage. The actual coverage comes from the weather map's R channel; this value **shifts** it (it does not multiply) |
| **Base Height / Thickness** | Cloud layer extent (metres, absolute world Y) |
| **Weather Scale** | Weather map world scale. 0.00004 -> one tile is about 25 km (large systems); larger values break it up |
| **Warp** | Domain warp strength (noise tiles) |
| **Density** | Extinction |
| **Detail** | Rim erosion strength (cores are untouched) |
| **March / Light Steps** | View steps / sun self-shadow steps |
| **Noise LOD** | Noise mip. Raise it to reduce shimmer at a cost in softness |
| **G Forward / Backward / Phase Forward / Scatter Octaves** | Dual-lobe HG phase plus Wrenninge octave multiple scattering |
| **Aerial Perspective** | Distant cloud is extincted *and* re-tinted towards the sky instead of merely fading alpha |
| **Sun / Ambient Intensity** | Independent direct and diffuse strengths. The sun colour is **luminance-normalised** before use, so 1.0 ~ pure white |
| **Shade Steps / Smooth** | Cel banding. **Defaults to 0 (off)**: realism needs continuous gradients |
| **Temporal Enabled** | Temporal accumulation; see the known issues, off by default |
| **Debug** | Six debug views, below |

Full table in the [sky parameter reference](/en/reference/sky-parameters#volumetric-clouds).

## Debug views (`volCloudDebug`)

"No clouds" and "clouds drawn but invisible" cannot be told apart by eye. Every fatal bug in this rewrite was found with these views.

| View | Healthy looks like | What a broken result means |
| --- | --- | --- |
| `Coverage` | Large light/dark regions from the weather map | One flat grey = the weather map is not bound |
| `RayCoverage` | Discrete cloud masses | All black = density is always zero (threshold or coverage math) |
| `Height` | Vertical gradient | Flat colour = the height band cropped the clouds away |
| `Density` | Values inside the masses | All black = the 3D noise is not being sampled (scale or mip) |
| `Depth` | Has values | **Pure red = the ray never crosses the slab** (base too high / too thin) |
| `Shadow` | Bright bases, dark crowns | All dark = the self-shadow march is inactive |

## Known issues

- **Temporal accumulation (`volCloudTemporalEnabled`) washes the clouds into a translucent veil**, so it is off by default. The history buffer is systematically dimmer than the current frame and even weight 0.3 is visible. A guard was added so history is only sampled after at least one frame, but the root cause is unresolved.
- **Rims read as "smoke"** rather than fluffy cumulus.
- **The underside of the layer has a hard edge at very low elevation** (inherent to slab intersection).
- **Clouds cast no shadow on the ground.** The literature ranks this as the top "clouds look fake" cue; it needs a new pass plus hooks in several shared lighting files and is not implemented.
- Lighting remains a simplified model (Beer-Powder, dual-lobe HG, octave multiple scattering) without true multiple scattering or shadow cascades.

## Performance: shader compilation

`Worley3` is a 27-cell search. **Never mark it `[unroll]`** - it is inlined at the main march, the light march and the debug views, so unrolling copies 27x3 hashes into hundreds of call sites and the D3D11 compiler sits at 99% for minutes (measured: still running past 72 s). The current implementation moves all noise into baked textures, so the shader only samples textures.

## Pipeline

```
fullscreen triangle (half-res RT)
  -> intersect the ray with the cloud slab (baseY ~ baseY + thickness)
  -> fixed step count; per step: weather -> height-band cull -> 3D shape noise ->
     dilate -> coverage remap -> detail erosion
  -> secondary march towards the sun for optical depth -> dual-lobe HG x octave MS
  -> analytic homogeneous-slice integral L * (1 - Tstep) -> optional cel quantisation
  -> aerial perspective -> optional temporal accumulation
  -> bilinear upsample composited by the skybox pass
```
