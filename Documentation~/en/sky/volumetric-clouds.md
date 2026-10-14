# Volumetric Clouds

Ray-marched cartoon clouds rendered into a half-resolution RT. Clouds in the sky now come from **this layer only**.

![Volumetric clouds: discrete cumulus with blue sky between them](/sky/volumetric-clouds.webp)

::: warning Rewritten in v1.8.0
This version **also removed** the old 2D texture cloud layer (a texture projected onto a virtual plane - no volume, no shading, no self-shadowing) and the previous volumetric implementation. Sky clouds are now volumetric-only.
:::.

## Why the clouds used to be "one blob"

The old model thresholded a 3D noise by a coverage value. A continuous random field has a **percolation threshold** (around 0.3): once coverage passes it, the field becomes **topologically connected**, so no amount of extra noise, erosion or shading could stop the sky from reading as one mass or one slab. **That is a topology problem, not a parameter problem.**

The new model builds density out of **discrete cloud instances**:

```
Split the world plane into cells (volCloudSpacing)
  -> at most one cloud per cell (its own hash decides: does it exist, how big, how thick)
  -> density = max over candidate ellipsoid profiles
  -> radius profile (1 - h^2)^dome: flat base (condensation plane), domed top
  -> outline warped by low-frequency FBM + high-frequency erosion on the surface band
```

Cells leave **gaps by construction**, and every cloud has a different radius and thickness - which is what makes a sky read as "several clouds" instead of "a sheet".

## Key parameters

| Parameter | Notes |
| --- | --- |
| **Spacing** | Cell size in metres. Sets the gaps between clouds |
| **Coverage** | Whether a cell holds a cloud. Fades in smoothly (no popping) and is pushed around by the weather field |
| **Size** | Cloud radius as a fraction of the cell. Roughly x2 = the narrowest gap between two clouds |
| **Size Jitter** | Radius randomness. 0 = all clouds identical |
| **Weather** | Large-scale weather modulation (wavelength = 8 cells). Gives the sky weather systems (banks here, clear over there) instead of even scatter |
| **Base Height / Thickness** | Cloud base and the maximum thickness of one cloud. Each cloud thins via Height Jitter |
| **Dome** | Dome exponent: 0.25 = wide and flat (stratus), 0.7 = round (cumulus). The base is always flat |
| **Warp** | Outline domain warp - pushes sample positions so edges curl instead of being round |
| **Noise Freq** | Frequency of the warp / erosion noise. 0.002 suits ~500 m clouds; too high reads as acne, too low as big blobs |
| **Detail** | Edge erosion. 0 = smooth ellipsoid, 1 = heavy cauliflower bite |
| **Density / March Steps / Light Steps** | Extinction and step counts (quality / cost) |
| **G Forward / Backward / Phase Forward / Scatter Octaves** | Dual-lobe HG phase function plus Wrenninge octave multiple scattering |
| **Aerial Perspective** | Distant cloud is extincted *and* re-tinted towards the sky, not merely faded |
| **Wind Speed** | Wind. The whole cell grid translates |
| **Shade Steps / Smooth / Silver \*** | Cel banding and back-lit rim |
| **Temporal Enabled** | See "Known issues" - off by default |
| **Debug** | Seven debug views, below |

Full table in the [sky parameter reference](/en/reference/sky-parameters#volumetric-clouds).

## Debug views (`volCloudDebug`)

"No clouds" and "clouds drawn but invisible" cannot be told apart by eye. All three bugs in this rewrite were found with these views.

| View | Healthy looks like | What a broken result means |
| --- | --- | --- |
| `Coverage` | Clearly separate ellipse-shaped clouds with black between them | One connected sheet = spacing too small or size too large |
| `Instance` | Mostly bright (cells are alive) with scattered dark cells | Nearly all dark = coverage too low |
| `Height` | Vertical gradient | Flat colour = the profile is not applied |
| `Edge` | A ring along each outline | Missing = no erosion, outlines will be smooth ellipsoids |
| `Depth` | Has values | **Pure red = the ray never crosses the slab** (base too high / too thin) |
| `Scatter` | Varies with angle | Flat = the phase function is inactive |
| `Integral diagnostic` (item 7) | **Pure yellow = opaque and bright** | Reddish = too thin; greenish = too dark. One capture tells you which |

## Known issues

- **Temporal accumulation (`volCloudTemporalEnabled`) washes the clouds into a translucent veil, so it is off by default.** The history buffer is systematically dimmer than the current frame; even weight 0.3 is visible. A guard was added so history is only sampled after at least one frame has been rendered, but the root cause is unresolved - leave it off until it is fixed.
- Cloud interiors are flat: the cel banding (3 steps by default) collapses internal shading into large uniform areas. Lower `Shade Steps` or raise `Shade Smooth` for more interior variation.
- Clouds cast no shadow on the ground. The literature ranks this as the top "clouds look fake" cue; it needs a new pass plus hooks in several shared lighting files and is not implemented yet.

## Pipeline

```
fullscreen triangle (half-res RT)
  -> intersect the ray with the cloud band (baseY ~ baseY + max thickness)
  -> fixed step count; each step queries 3x3 cells for the nearest cloud and
     evaluates its ellipsoid profile plus erosion
  -> secondary march towards the sun for self-shadowing -> dual-lobe HG x multiple scattering
  -> energy-conserving homogeneous-slice integral (L is already incident radiance,
     so the slice contribution is L * (1 - Tstep))
  -> aerial perspective (tinted towards the sky at the representative depth)
  -> cel quantisation of the accumulated brightness (once, not per slice)
  -> bilinear upsample composited by the skybox pass
```
