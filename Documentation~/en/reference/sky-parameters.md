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

| Parameter | Default | Notes |
| --- | --- | --- |
| `volumetricCloudsEnabled` | false | Master toggle |
| `volCloudNoiseTexture` | shipped | 128^3 shape volume noise (R=shape, GBA=erosion). CDF-normalised, regenerable with the bake tool |
| `volCloudShapeTexture` | shipped | 512^2 weather / domain-warp map (R=large weather, G=mid, BA=warp). **Without it clouds spread evenly across the whole sky** |
| `volCloudCoverage` | 0.24 | Cloud amount. After CDF normalisation this is literally the fraction of sky covered |
| `volCloudWeatherScale` | 0.0002 | Weather map world scale; 0.0002 -> one tile spans 5 km |
| `volCloudWeatherContrast` | 0.55 | How far the weather term pushes coverage locally; 0 = uniform sky |
| `volCloudWeatherDetail` | 0.3 | Blend from large systems to individual clouds |
| `volCloudWarp` | 0.5 | Domain warp strength in noise tiles |
| `volCloudBaseHeight` | 800 | Slab base height (metres, absolute world Y) |
| `volCloudThickness` | 300 | Slab thickness (metres) |
| `volCloudScale` | 0.00065 | 3D shape scale; 0.00065 -> 1.5 km tile, ~400 m clouds |
| `volCloudDensity` | 1.7 | Overall extinction |
| `volCloudMarchSteps` | 32 | March steps (8~48) |
| `volCloudLightSteps` | 6 | Steps marched towards the sun |
| `volCloudDetail` | 0.8 | High-frequency erosion (cauliflower edges) |
| `volCloudWindSpeed` | 7 | Wind speed |
| `volCloudShadeSteps` | 3 | Cel band count |
| `volCloudShadeSmooth` | 0.35 | Cel band edge softness |
| `volCloudSilverIntensity` | 0.3 | Back-lit rim (gated by voxel density, so it cannot sprinkle dots outside the silhouette) |
| `volCloudSilverPower` | 8 | Rim tightness around the sun |
| `volCloudDebug` | Off | Debug views: Coverage / MarchRange / Field / Profile / Lod / Weather |
| `volCloudTemporalEnabled` | true | Temporal accumulation toggle (off frees history buffers, freezes jitter) |
| `volCloudTemporal` | 0.8 | Accumulation weight (0~0.9, 0 = no blending) |

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
