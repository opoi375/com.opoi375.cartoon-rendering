# Volumetric Light (God Rays)

`CartoonRendering/PostProcessing/VolumetricLight` ray-marches in screen space, sampling the main light's shadow map at every step and accumulating in-scattering with Beer's law — the visible shafts you get when sunlight passes through window slits, tree canopies or cloud gaps.

This is not a screen-space radial blur fake: every step genuinely asks "is this point in space occluded?", so the shaft shape is driven entirely by scene geometry and lighting — move an occluder and the shafts follow.

## Features

- **Real volumetric scattering** — view-ray marching + main light shadow map sampling, not a post-process glow
- **Half-resolution + depth-aware bilateral upsample** — a 3×3 tent kernel weighted by depth similarity, so foreground silhouettes don't bleed halos
- **Henyey-Greenstein phase function** — looking into the light naturally builds a forward-scattering bloom; tunable anisotropy
- **Height fog** — exponential height falloff keeps shafts near the ground instead of hazing the whole sky
- **Dust noise** — optional 3D value noise gives the shafts a wispy / motes look, with drift
- **Cartoon banding** — quantize the scattering into N levels for a comic-style hard-edged shaft, the stylized option of this package
- **Debug views** — one click to inspect the shadow sampling / step count / scene depth; a broken shadow hookup is immediately obvious

## Quick setup

1. Install the feature into the renderer: **Tools > Volumetric Light > Setup In Renderer**
   (or manually: Renderer Features > Add > Volumetric Light)
2. Add a **Global Volume** to the scene and **Add Override > CartoonRendering > Volumetric Light**
3. Raise **Intensity** from 0 to around 1
4. Make sure the scene has a **main directional light** with shadows enabled (Main Light Shadows on the URP asset)
5. Add some **Bloom**, threshold around 0.9, and the shafts will glow

> **Intensity defaults to 0.** This is deliberate: `VolumeManager` returns a default instance for components that no Volume overrides, so a non-zero default would mean "the effect turns on even with no Volume anywhere", affecting every scene.

## Parameter reference

### Core

| Parameter | Description |
| --- | --- |
| `Intensity` | Master intensity, 0 = off (default) |
| `Tint` | Scattering color, usually the light color |
| `Density` | Scattering coefficient (extinction per meter). 0.01 – 0.05 is the usual range |
| `Anisotropy` | HG phase `g`. Positive = forward scattering (brightest looking into the light), 0 = isotropic |

### Range & height fog

| Parameter | Description |
| --- | --- |
| `Max Distance` | Farthest march distance — the main performance knob |
| `Distance Fade` | Fraction of the max distance where the fade-out starts, avoids a hard edge |
| `Height Start` | Height where the height fog starts (world Y, usually ground level) |
| `Height Falloff` | Density drops to 1/e every 1/this many meters. 0 disables height fog |

### Shadows

| Parameter | Description |
| --- | --- |
| `Shadow Strength` | How much shadows suppress the scattering. 1 = sharpest, 0 = ignore shadows (uniform fog) |
| `Shadow Bias` | Offset of the sample point toward the light. Too small flickers with moiré, too large lifts the shafts off the ground |

### Quality

| Parameter | Description |
| --- | --- |
| `Step Count` | March steps. 24 – 36 is the sweet spot |
| `Jitter` | March-start jitter strength, breaks up step banding. Static jitter — the upsample smooths it |
| `Softness` | Upsample tap spacing. 1 = adjacent texels; larger acts as a blur |

### Dust noise

| Parameter | Description |
| --- | --- |
| `Noise Strength` | 0 = homogeneous medium (clean but "plastic"), 0.2 – 0.4 reads as airborne dust |
| `Noise Scale` | Noise frequency (per meter); 0.05 – 0.2 looks like air dust |
| `Noise Speed` | Drift speed (m/s). 0 = completely static |

### Stylized

| Parameter | Description |
| --- | --- |
| `Banding` | Cartoon band count. 0 = off, 4 – 6 gives comic-style hard-edged shafts |

## Debug views

**Tools > Volumetric Light > Debug/** has four modes (they also replace the screen rather than adding to it):

| Mode | What it shows |
| --- | --- |
| `Shadow` | **The important one**: the main light shadow sampling result. You need to see light/dark regions — **a pure white screen means shadows are not working and the effect has already degraded into uniform fog** |
| `Steps` | Step-count heat map |
| `Scene Depth` | Scene distance normalized by the max march distance, to check the march range |

Troubleshooting order: `Shadow` → check the shadows, `Scene Depth` → check the range, `Off` → back to normal.

## Tools

| Menu | What it does |
| --- | --- |
| **Tools > Volumetric Light > Setup In Renderer** | Writes VolumetricLightFeature into the active URP renderer in one click |
| **Tools > Volumetric Light > Create Demo Scene** | Creates and saves a demo scene to `Assets/Scenes/VolumetricLight.unity` |
| **Tools > Volumetric Light > Build In Current Scene** | Rebuilds in the current scene only, without writing a scene file |
| **Tools > Volumetric Light > Dump State** | Diagnostics: logs renderer / main light / pipeline / Volume / shader state |
| **Tools > Volumetric Light > Debug/…** | Switch debug views |

The demo scene's materials and Volume Profile land in `Assets/CartoonRendering/VolumetricLight/`.

## Indoor shafts in practice (factory demo)

The demo project's `Assets/Scenes/FactoryInterior.unity` (builder scripts under "Factory Interior Demo Scripts" in [Editor Tools](/en/tools/)) applies these settings to an interior scene. Traps worth knowing:

- **Keep the main light `Mixed`.** The effect computes shafts from the main light's **realtime shadow map**; if you set the sun to `Baked` there is no realtime shadow at runtime, visibility reads as "lit" everywhere, and the shafts disappear entirely — degrading into uniform fog. `Mixed + Baked Indirect` is the right combo: indirect light is baked, direct light and shadows stay realtime
- **Be generous with indirect intensity.** Measured at `Indirect Scale = 1.0` the lightmap values were only ~0.05, invisible on dark surfaces; 3 – 4 is what lifts shadowed walls from pure black to dark grey
- **Don't look straight into the sun.** The HG forward-scattering peak washes the whole frame out in cream white; viewing from the side is what gives you distinct, individual shafts
- **Keep the camera in shadow.** When the camera itself stands in lit space, the fog all along the view ray is bright, and the shafts have no contrast against the background
- **Multi-pane windows** (mullions + transoms) slice the incoming light into a regular array of shafts — the most attractive configuration for volumetric light
- **A low sun angle** (~26° elevation) is what makes the shafts long enough to fill the room

## How it works

1. **Ray reconstruction** — restore the view-space position from the depth buffer and build a world-space ray, with separate paths for perspective and orthographic (an ortho camera's position is not on the ray, so it cannot be the origin)
2. **March** — over `[near plane, min(scene distance, max distance)]`, transform each world position to main-light shadow coordinates and sample visibility
3. **Beer's law** — `T *= exp(-σ·dt)`, accumulating `light × visibility × σ × T`
4. **HG phase function** — normalized by 4π so `g = 0` yields 1
5. **Jitter** — a static IGN offset on the march start. Per-frame jitter flickers without TAA; a static one is smoothed away by the upsample
6. **Upsample** — scattering goes into a half-resolution RT whose alpha channel also stores the scene view depth, so the composite can weight taps by depth difference

### About the shadow keywords

`_MAIN_LIGHT_SHADOWS` / `_MAIN_LIGHT_SHADOWS_CASCADE` are **global** keywords set by URP. Besides declaring the `multi_compile`, this package also **explicitly sets the material's local keywords** from `UniversalShadowData`. The implicit global-keyword fallback can fail in some pipeline states, and when it does the shafts vanish entirely and the effect degrades into uniform fog — with no error of any kind, which makes it very hard to diagnose.

That is exactly why the `Debug/Shadow` view exists.

## Performance

The cost is `pixels × steps × shadow samples`, so:

- **Resolution**: the feature's `Resolution` defaults to `Half`. `Full` is four times the cost and is only worth it when shaft edges must be perfectly clean
- **Steps**: `Step Count` is a linear cost
- **Max distance**: `Max Distance` decides how many steps are wasted on invisible distance
- **Noise**: with `Noise Strength > 0` every step evaluates an extra 3D value noise — set it to 0 if you don't need the dust
- **Zero cost when inactive**: at `Intensity = 0` the pass returns immediately — no RT allocation, no draws submitted

## Known limitations

- **Main directional light only.** Spot / point lights would require looping over additional lights and sampling their own shadow slices; this package does not cover that yet
- **Requires URP's depth texture.** The pass declares the dependency via `ConfigureInput`, so it is generated even if the pipeline asset has Depth Texture off
- Orthographic cameras work (the ray reconstruction branches), but the demo and all the tuning were done with perspective
