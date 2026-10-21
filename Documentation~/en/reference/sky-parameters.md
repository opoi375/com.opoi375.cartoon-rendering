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

| Parameter | Default | Notes |
| --- | --- | --- |
| `volumetricCloudsEnabled` | false | Master toggle |
| `volCloudShapeTex` | shipped | Shape noise 128^3: R = Perlin-Worley, GBA = Worley FBM @ f4/f8/f16 |
| `volCloudDetailTex` | shipped | Detail noise 32^3: high-frequency Worley FBM for rim erosion |
| `volCloudWeatherTex` | shipped | Weather map 512^2: R = coverage, G = cloud type, BA = domain warp |
| `volCloudShapeScale` | 1800 | **Metres per noise tile - the most direct "cloud size" knob.** Cumulus 1500~2500; 455 reads as smoke streaks |
| `volCloudCoverage` | 0.42 | Global coverage; **shifts** the weather map's local coverage (does not multiply) |
| `volCloudBaseHeight` | 900 | Cloud base (absolute world Y, metres) |
| `volCloudThickness` | 1100 | Layer thickness (metres) |
| `volCloudWeatherScale` | 0.00004 | Weather map world scale: 1 metre -> map tiles. 0.00004 -> one tile ~25 km |
| `volCloudWarp` | 0.12 | Domain warp strength (noise tiles) |
| `volCloudNoiseLod` | 0 | Noise mip. Raise to reduce shimmer at a cost in softness |
| `volCloudDensity` | 6.0 | Extinction |
| `volCloudDetail` | 0.5 | Rim erosion strength (cores untouched) |
| `volCloudMarchSteps` | 48 | View march steps (8~64) |
| `volCloudLightSteps` | 6 | Sun self-shadow steps (1~8) |
| `volCloudGForward` | 0.75 | Dual-lobe HG forward anisotropy |
| `volCloudGBackward` | -0.25 | Backward lobe g |
| `volCloudPhaseForward` | 0.65 | Forward lobe weight |
| `volCloudScatterOctaves` | 3 | Multiple-scattering octaves (1 = single scattering, grey cores) |
| `volCloudAerialPerspective` | 0.8 | Aerial perspective (extinction + inscatter tint) |
| `volCloudSunIntensity` | 1.5 | Direct strength (sun colour is luminance-normalised, 1.0 ~ pure white) |
| `volCloudAmbientIntensity` | 1.0 | Ambient (diffuse) strength. Bases that refuse to go dark are usually this being too high |
| `volCloudWindSpeed` | 7 | Wind speed (m/s) |
| `volCloudShadeSteps` | **0** | Cel band count. **0 = off (continuous gradients, the realistic default)**; 2~4 for stylised hard bands |
| `volCloudShadeSmooth` | 0.35 | Band-edge softness |
| `volCloudSilverIntensity` | 0.3 | Back-lit rim intensity (gated by voxel density) |
| `volCloudSilverPower` | 8 | Rim tightness |
| `volCloudTemporalEnabled` | **false** | Known issue: turning it on washes clouds into a translucent veil, see the docs |
| `volCloudTemporal` | 0.8 | Accumulation weight (only when the toggle above is on) |
| `volCloudDebug` | Off | Debug views: Coverage / RayCoverage / Height / Density / Depth / Shadow |

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
