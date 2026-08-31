# Volumetric Clouds

Ray-marched cartoon volumetric clouds, rendered into a **half-resolution** RT and composited, with temporal accumulation to kill banding.

## Pipeline

```
Fullscreen triangle (half-res RT)
  → Ray vs horizontal slab [baseY, baseY+thickness] intersection
  → Fixed-step march sampling the density field
  → Density = 128³ Perlin-Worley noise × cumulus height profile − Worley detail erosion
  → Secondary march toward the sun → Beer transmittance + Powder edge boost
  → Cel-quantized accumulated luminance (cartoon bands)
  → Temporal accumulation (ping-pong history + reprojection blend)
  → Skybox pass bilinear upsample & composite
```

Works with the camera below, inside, or above the cloud layer.

## Key Parameters

| Parameter | Description |
| --- | --- |
| Coverage | Cloud amount (0 clear → 1 overcast) |
| Base Height / Thickness | Slab altitude range |
| Density | Overall density |
| March Steps | March iterations (8–48, quality/perf dial) |
| Shade Steps | Number of cel bands |
| Silver Intensity / Power | Backlit silver lining |
| **Temporal Enabled** | Temporal accumulation (TAA) toggle — see below |
| Temporal | Blend weight (higher = smoother, ghosting on fast moves) |

Full table: [Sky Parameter Reference](/en/reference/sky-parameters#volumetric-clouds).

## The TAA Toggle

The march uses a per-pixel hash jitter to decorrelate slices; temporal accumulation converges the residual noise over a few frames.

- **On (default)**: clean, smooth clouds after a few frames; game cameras only
- **Off**: no history buffers allocated (saves memory) and the jitter freezes into a stable static pattern (no shimmering without accumulation). Good for single-frame screenshots or when any ghosting is unacceptable

Scene-view / preview cameras always take the fresh-render path and never allocate history.

## Anti-banding Design (v1.0.1)

Far-distance slice banding was eliminated by:

1. **Tightened march range**: end clamped to the aerial-fade distance (baseY×16) — no wasted steps
2. **Distance-adaptive noise LOD**: per-step mip selection from step length and pixel footprint (Nyquist-biased); detail erosion fades with LOD
3. **Adaptive height-profile ramps**: cloud bottom/top ramps widen with step length so they stay continuously sampled at range
4. **Full-step jitter**: ±0.9 step per-pixel hash jitter

## Performance

- Half-resolution rendering with upsample compositing
- Default 40 march + 5 light steps run fine on mid-range GPUs
- Disabling Temporal frees the two 960×540 history buffers
