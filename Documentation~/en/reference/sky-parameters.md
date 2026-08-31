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

| Parameter | Default | Description |
| --- | --- | --- |
| `volumetricCloudsEnabled` | false | Master switch |
| `volCloudNoiseTexture` | built-in | 128³ noise volume (re-bakable via tool) |
| `volCloudCoverage` | 0.5 | Cloud amount |
| `volCloudBaseHeight` | 600 | Cloud base altitude (m) |
| `volCloudThickness` | 700 | Slab thickness (m) |
| `volCloudScale` | 0.0005 | Inverse noise tile size (≈2000 m/tile) |
| `volCloudDensity` | 1.2 | Overall density |
| `volCloudMarchSteps` | 40 | March iterations (8–48) |
| `volCloudLightSteps` | 5 | Light march steps toward the sun |
| `volCloudDetail` | 0.35 | Detail erosion strength |
| `volCloudWindSpeed` | 12 | Wind speed |
| `volCloudShadeSteps` | 3 | Cel band count |
| `volCloudShadeSmooth` | 0.2 | Band edge softness |
| `volCloudSilverIntensity` | 0.6 | Silver lining intensity |
| `volCloudSilverPower` | 6 | Silver lining tightness |
| `volCloudTemporalEnabled` | true | **Temporal accumulation toggle** (off = no history buffers, frozen jitter) |
| `volCloudTemporal` | 0.8 | Blend weight (0–0.9; 0 disables blending) |

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
