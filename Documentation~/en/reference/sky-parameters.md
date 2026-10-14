# Sky Parameter Reference

Complete parameter table of the `CartoonProceduralSky` ScriptableObject (defaults are the shipped presets).

## Sun

| Parameter | Default | Description |
| --- | --- | --- |
| `sunColor` | warm white | Sun color |
| `sunSize` | 0.04 | Disc angular size |
| `sunInnerBound` | 0.2 | Inner bound (solid disc fraction) |
| `sunOuterBound` | 0.8 | Outer bound (softness range) |
| `includeSunDisc` | true | Draw the sun disc |

## Stars

| Parameter | Default | Description |
| --- | --- | --- |
| `starColor` | cool white | Star color |
| `starIntensity` | 1.5 | Intensity |
| `starSize` | 0.5 | Size |
| `starScale` | 1.0 | Distribution scale |
| `starDensity` | 0.5 | Density (sparse ↔ dense) |
| `starTexture` | — | Optional custom star texture |

## Clouds (2D stratus)

| Parameter | Default | Description |
| --- | --- | --- |
| `cloudColor` | white | Cloud color |
| `cloudHeight` | 60 | Apparent height |
| `cloudThreshold` | 0.45 | Coverage threshold |
| `cloudScale` | 1.0 | Scale |
| `cloudIntensity` | 1.0 | Intensity |
| `cloudTexture` | — | Cloud noise texture |

## Volumetric Clouds

> Rewritten in v1.8.0: density is now built from **discrete cloud instances** (the
> world plane is split into cells and each cloud is placed on its own). The old
> model thresholded a continuous noise by coverage - past the percolation threshold
> (~0.3) the field is topologically connected, so the sky is inevitably one mass.
> That is topology, not tuning.
> The 2D texture cloud layer was removed at the same time.

| Parameter | Default | Notes |
| --- | --- | --- |
| `volumetricCloudsEnabled` | false | Master toggle |
| `volCloudSpacing` | 950 | Cell size in metres - sets the gaps between clouds |
| `volCloudCoverage` | 0.62 | Whether a cell holds a cloud. Fades in smoothly, pushed by Weather |
| `volCloudSize` | 0.55 | Radius as a fraction of the cell (x2 ~ the narrowest gap) |
| `volCloudSizeJitter` | 0.6 | Radius randomness |
| `volCloudWeather` | 0.4 | Large-scale weather modulation (wavelength = 8 cells). 0 = even scatter |
| `volCloudBaseHeight` | 750 | Cloud base (absolute world Y, metres) |
| `volCloudThickness` | 420 | Maximum thickness of one cloud; each varies by Height Jitter |
| `volCloudHeightJitter` | 0.6 | Thickness randomness |
| `volCloudDome` | 0.5 | Dome exponent: 0.25 = wide/flat (stratus), 0.7 = round (cumulus) |
| `volCloudWarp` | 0.55 | Outline domain warp (pushes samples so edges curl) |
| `volCloudNoiseFreq` | 0.002 | Warp / erosion noise frequency (cycles per metre) |
| `volCloudDetail` | 0.7 | Edge erosion (surface band only, cores untouched) |
| `volCloudDensity` | 1.9 | Extinction |
| `volCloudMarchSteps` | 32 | View march steps (8~64) |
| `volCloudLightSteps` | 4 | Sun self-shadow steps (1~8) |
| `volCloudGForward` | 0.75 | Dual-lobe HG forward anisotropy |
| `volCloudGBackward` | -0.25 | Backward lobe g |
| `volCloudPhaseForward` | 0.65 | Forward lobe weight |
| `volCloudScatterOctaves` | 3 | Multiple-scattering octaves (1 = single scattering, grey cores) |
| `volCloudAerialPerspective` | 0.8 | Aerial perspective on clouds (extinction + inscatter tint) |
| `volCloudWindSpeed` | 7 | Wind speed (m/s) |
| `volCloudShadeSteps` | 3 | Cel band count (<=1.5 disables quantisation) |
| `volCloudShadeSmooth` | 0.35 | Band-edge softness |
| `volCloudSilverIntensity` | 0.3 | Back-lit rim intensity (gated by voxel density) |
| `volCloudSilverPower` | 8 | Rim tightness |
| `volCloudTemporalEnabled` | **false** | Known issue: turning it on washes clouds into a translucent veil, see the docs |
| `volCloudTemporal` | 0.8 | Accumulation weight (only when the toggle above is on) |
| `volCloudDebug` | Off | Debug views: Coverage / Instance / Height / Edge / Depth / Scatter / integral diagnostic |

## Sky Gradient (three palettes)

- **Day**: `topColor` / `middleColor` / `bottomColor` / `horizonColor` / `backgroundColor`
- **Sunset**: `sunsetTopColor` / `sunsetMiddleColor` / `sunsetHorizonColor`
- **Night**: `nightTopColor` / `nightMiddleColor` / `nightHorizonColor` / `nightBackgroundColor`
- `gradientSoftness` (0.15): softness between stops
- `sunsetBlendStart` (0.05) / `dayBlendEnd` (0.35): palette transition ranges
- `gradientRamp`: optional ramp texture for fine control

## Overall

| Parameter | Default | Description |
| --- | --- | --- |
| `intensity` | 1.0 | Global sky brightness |
