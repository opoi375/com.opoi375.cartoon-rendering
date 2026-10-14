# Volumetric Clouds

Stylised ray-marched clouds rendered into a **half-resolution** RT, upsampled and composited, with temporal accumulation to kill banding.

![Volumetric clouds: separable cumulus, clean cel bands, back-lit rims](/sky/volumetric-clouds.webp)

## The noise is layered (this is the important part)

Whether clouds look good is almost entirely decided by the density field. **A single 3D noise cannot make good clouds** - it is ergodic, so spread over the sky it reads as one sheet of frosted glass. Four layers are stacked here, each responsible for exactly one thing:

| Layer | Source | What it owns |
| --- | --- | --- |
| Weather (2D, low freq) | `Textures/CloudShape2D.asset` R / G | **Which parts of the sky have clouds.** Groups them into systems with real blue sky between |
| Shape (3D, mid freq) | `Textures/CloudNoise3D.asset` R | The outline of one cloud (Perlin-Worley cumulus masses) |
| Domain warp (2D, low freq) | `CloudShape2D` B / A | Pushes the 3D sample coordinates so edges curl instead of being round |
| Erosion (3D, high freq) | `CloudNoise3D` G / B / A | Cauliflower bite on the silhouettes |

Both textures are baked by `Tools > Cloud > …`, both are CDF-normalised (next section), and both ship inside the package - no setup.

## Why CDF normalisation is mandatory

Density comes from `smoothstep(1 - coverage, 1 - coverage + soft, r)`, which only means anything if `r` spans 0..1.

The old bake combined its base shape with the Schneider Perlin-Worley term `pw = (perlin - (1 - worley)) / worley`, which **collapses to exactly 0** wherever `worley` is small - and measured on the shipped asset, about half of all voxels were 0. So coverage 0.5 (threshold 0.48) let almost nothing through and the sky rendered empty; pushing coverage to 0.95 squeezed out a few blobs.

The bake now ends with a CDF (histogram) remap onto a uniform distribution per channel. A monotone remap changes no spatial relationship, so connectivity survives untouched - all it buys is that **coverage is literally the fraction of sky covered**: 0.24 = 24%, what you slide is what you get.

Normalisation cannot fix *ties*: when half the samples share one value they stay one constant block after the remap, and sliding coverage past that value jumps the sky from 0% to 50% cloud. That is why `perlin` is mixed back into `pw` at 12% first - it leaves ordering information in the dead zone.

## What the noise itself looks like (the v1.7.0 lesson)

When clouds do not look like clouds, the first move is not lighting - it is **dumping the noise to an image and looking at it**. The shipped volume's shape channel used to be a field of **soft blobs all at one scale**: no large connected masses, no pinched cauliflower lobes. No lighting model can rescue that.

Three generator-side mistakes:

1. **Too many octaves, base frequency too high.** `PerlinFbm(baseFreq 4, 6 octaves)` spread the energy into the mid and high octaves, so the octave that is supposed to *be* the cloud mass never dominated. Base frequency 2 with 3 octaves restored the large-scale connectivity
2. **No billow.** `billow(n) = 1 - abs(2n - 1)` folds a smooth field into ridges: bulges get more convex, seams pinch shut. That fold is where bulge-and-pinch comes from; plain FBM stays a Gaussian soft ball at any octave count
3. The sharp **Worley cell boundaries were diluted away** (first by the tie-break mix, then stretched flat by the CDF), leaving effectively just Perlin

But billow belongs on the **crease field**, never on the **mass field**: billowing the mass turns it into a bimodal ridge field, and past the threshold only filaments survive, so clouds become wisps of gauze. The Nubis division of labour is:

| Field | Should look like | Owns |
| --- | --- | --- |
| Mass (Perlin, low freq, smooth) | Large, connected, soft-edged banks | Where clouds live and how big they are |
| Crease (Worley, billowed) | Sharp cell boundaries plus folded ridges | The cauliflower bulge and pinch |
| Detail (Worley, high freq) | Fine bite | Cauliflower erosion on the rim |

The bake also warps the sample coordinates once, because isotropic FBM yields round blobs while real cloud folds are sheared and stretched.

The test is simple: export the R channel as an image. If every visible feature is the same size, the noise is wrong - you should see small creases inside large banks, and fine bite inside those.

## Sampling must be world-anchored

The weather map originally re-centred on the camera like the 3D shape did, which pins the sample window at uv≈0 - the sky above you always reads the same corner of the map, so the weather term degenerates into a constant. That was diagnosed by the `Weather` debug view showing one flat grey value.

Both maps now use absolute world coordinates with `frac()` (identical under Repeat wrap, it just keeps the numbers in a float-friendly range). Where you fly determines the weather - that is the entire point of a weather map.

![Looking up: bright crowns, cold grey bases, clean bands](/sky/volumetric-clouds-overhead.webp)

## Key parameters

| Parameter | Notes |
| --- | --- |
| Coverage | After CDF normalisation this is the literal fraction. 0.2~0.3 = separable cumulus, 0.5+ starts fusing into a deck |
| **Weather Scale** | Weather map world scale. 0.0002 → one tile spans 5 km, systems about 1.5 km |
| **Weather Contrast** | How far the weather term pushes coverage locally. 0 = uniform sky (old behaviour), 1+ = strong clumping |
| **Weather Detail** | Blend from large systems to individual clouds |
| **Warp** | Domain warp strength in noise tiles |
| Base Height / Thickness | Slab. Keep the thickness modest if you want islands |
| Scale | 3D shape scale. 0.00065 → 1.5 km tile, ~400 m clouds |
| **G Forward / Backward / Phase Forward** | Dual-lobe HG phase: clouds flare towards the sun and the dark side takes shape |
| **Scatter Octaves** | Multiple-scattering octaves. 1 = single scattering only, which burns cores to flat grey |
| **Type Contrast / Bias** | Weather selects cloud TYPE (stratus <-> cumulus), not just amount |
| **Base Coverage Boost** | Extra coverage at the cloud base, suppressing odd distant shapes at low elevation |
| **Aerial Perspective** | Distant cloud is extincted and re-tinted towards the sky instead of only fading alpha |
| Density | Overall extinction |
| Detail | High-frequency erosion strength |
| March Steps / Light Steps | 8~48 / 2~8 |
| Shade Steps / Smooth | Cel band count and band-edge softness |
| Silver Intensity / Power | Back-lit rim |
| **Debug** | Six debug views, below |
| Temporal Enabled / Temporal | Accumulation toggle and weight |

Full table in the [sky parameter reference](/en/reference/sky-parameters#volumetric-clouds).

## Debug views (`volCloudDebug`)

"No clouds" and "clouds are drawn but invisible" cannot be told apart by eye, so the intermediate values get drawn instead:

| View | Healthy looks like | What a broken result means |
| --- | --- | --- |
| `Coverage` | Solid cloud masses | All black = the density model never got fed (threshold or noise distribution) |
| `MarchRange` | Has values | **Pure red = the ray never crosses the slab** (base too low / too thin) |
| `Field` | Grainy light and dark | Flat uniform grey = the noise was blurred away by mip selection |
| `Profile` | Bright mid-slab | All black = the height profile cropped the clouds away entirely |
| `Lod` | Low near, high far | High everywhere = too few march steps, detail is gone |
| `Weather` | Large light/dark regions | One flat grey = the weather map is not connected (reference lost, or scale too small) |

## Pipeline

```
fullscreen triangle (half-res RT)
  -> intersect the ray with the horizontal slab [baseY, baseY + thickness]
  -> fixed step count, sampling the density field per step
  -> weather(2D) modulates coverage -> shape(3D) -> domain warp -> erosion -> height profile
  -> secondary march towards the sun -> Beer transmittance + Powder edge term
  -> cel-quantise the accumulated brightness
  -> temporal accumulation (ping-pong history + reprojection blend)
  -> bilinear upsample composited by the skybox pass
```

Works with the camera below, inside or above the slab.

## Anti-banding

1. **Tight march range**: the end is clamped to the aerial-perspective fade distance (baseY×16), so no step is spent on invisible space
2. **Footprint-matched noise LOD** per step (Nyquist biased)
3. **LOD contrast compensation**: taking a mip pulls the field towards its mean 0.5, and with a fixed threshold **distant clouds vanish because they were blurred below it**. The deviation is scaled back up with lod (capped at 1.8x - uncapped it scatters white salt grains across the far field); the threshold band widens at the same time so leftover noise does not read as a jagged edge
4. **Height ramps widen with step length** so they stay continuously sampled at distance
5. **Full-step jitter**: ±0.9 step per-pixel hash
6. **Silver lining gated by density**: thin edge voxels catching full sun sprinkle white dots outside the silhouette, and they survive temporal convergence (it is not dither, it is simply wrong)

The horizon band is handled by aerial perspective rather than a hard window: the old `smoothstep(0.02, 0.14)` hollowed out 0-8° of sky, and since the flat cloud layer is usually off, that left a stripe with no clouds in it at all.

## Temporal accumulation toggle

Clouds jitter the march per pixel and let temporal accumulation converge the noise over a few frames.

- **On (default)**: clean, smooth cloud surfaces once moving; game cameras only
- **Off**: no history buffers allocated (saves memory) and the jitter freezes into a static pattern. Good for single-frame captures or when ghosting is unwelcome

Scene view and preview cameras always take the fresh-render path and never allocate history.

## Performance

- Half-resolution with bilinear upsample
- 32 march steps + 6 light steps by default, fine on mid-range GPUs
- Disabling Temporal frees both history buffers
- The weather layer costs one extra 2D fetch per step - negligible
