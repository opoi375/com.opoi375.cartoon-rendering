# Changelog
## v1.9.0

### Removed
- **The 2D texture cloud layer**: a texture projected onto a virtual plane - no volume, no shading, no self-shadowing - which only fought the volumetric layer. The `cloudHeight` / `cloudThreshold` / `cloudScale` / `cloudIntensity` / `cloudTexture` fields and the skybox shader's `EvaluateClouds` / `CloudFBM` / `ValueNoise2D` and their properties are gone (`cloudColor` stays, as the cloud tint)
- **The previous volumetric implementation and its noise pipeline**: the `CloudNoise3D` / `CloudShape2D` assets, the two `Tools > Cloud > Bake ...` menus, `CloudNoiseTextureBaker`, and with them the whole class of "texture reference is null so nothing draws" silent failures

### Added
- **A discrete cloud-instance density model**: the world plane is split into cells (`volCloudSpacing`), each cloud gets its own hash deciding whether it exists and how big and thick it is, and density is the max over the candidate ellipsoid profiles. **The old model thresholded a continuous noise by coverage - a continuous random field has a percolation threshold (around 0.3), and past it the field becomes topologically connected, so the sky is inevitably one mass or one solid deck. That is a topology problem; tuning the noise and the erosion cannot fix it.** The cell model leaves gaps between clouds by construction
- Radius profile `(1 - h^2)^dome`: flat base (condensation plane), domed top, with `Dome` selecting stratus vs cumulus
- **Outline domain warp plus surface-band erosion** (procedural 3D value noise in the shader, so no baked texture and nothing that can go missing)
- **Large-scale weather modulation** (`Weather`): a low-frequency noise pushes the local coverage, so the sky has weather systems (a bank here, clear over there) instead of an even scatter
- **Seven debug views**: Coverage / Instance / Height / Edge / Depth / Scatter / **integral diagnostic** (R = real opacity, G = cloud brightness - one capture distinguishes "too thin" from "too dark")

### Fixed
- **Temporal accumulation washed the clouds into a translucent veil**: the history buffer is systematically dimmer than the current frame, so at weight 0.9 only ~10% of the clouds remained. A guard now requires at least one rendered frame before history is sampled, but the root cause is unresolved - `volCloudTemporalEnabled` is therefore **off by default**, and the known issue is called out in the tooltip and the docs
- **Alpha was cut by 35% even at noon**: `smoothstep(0.0, -0.12, _CartoonSunDirection.y)` had its parameters in the wrong order. The direction is *sun to scene*, so above the horizon `.y` is negative, and a reversed smoothstep saturates that to 1
- The debug view ranges were off by one (Coverage actually displayed Instance), and Instance only wrote its alive weight when a cloud was hit, keeping its default of 1.0 otherwise - an entirely white view with no information. It now queries the nearest cell directly
- The march range was tightened to `baseY*10`: at 24 km with 32 steps each step spanned ~700 m, so a 600 m cloud could be stepped straight over (an empty far field)
- Domain warp was permanently disabled by a lock: the first candidate that failed the "inside the cloud" test set `warpDone`, so no warp was ever computed and outlines stayed smooth ellipsoids
- The soft edge `rad*0.22` stretched a ~230 m gradient band onto a 1 km cloud, blurring the silhouette into a ball. Tightened to `rad*0.05`
- Nine dead keys removed from the presets (they referenced the deleted texture assets)
- The test assembly gained Core / Universal RP references: the newly added SSIL test uses `VolumeComponent` / `ScriptableRendererFeature` but `CartoonRendering.Editor.Tests.asmdef` did not reference the RP assemblies, which blocked compilation of the whole project

## v1.7.0

### Fixed
- **The noise generator could not produce cloud structure at all** (only visible after dumping the shipped volume to an image): the shape channel was a field of **soft blobs all at the same scale** - no large connected masses, no pinched cauliflower lobes. No lighting work can rescue that; it renders as cotton wool. Three causes:
  - `PerlinFbm(baseFreq 4, 6 octaves)` spread the energy across the mid and high octaves, so the one octave that is supposed to *be* the cloud mass never dominated. Now base frequency 2 with 3 octaves
  - **There was no billow at all.** The fold `1 - abs(2n - 1)` is what creates bulge-and-pinch; plain FBM at any octave count stays a Gaussian-ish soft ball
  - The sharp Worley cell boundaries (the other half of the creasing) were diluted away by the tie-break mix and then stretched flat by the CDF, leaving effectively just Perlin
- **Billow applied to the wrong layer**: the first attempt billowed the mass field, which turned it into a bimodal ridge field - past the threshold only filaments survive, so clouds became wisps of gauze. Per Nubis the mass field must stay **smooth** (it only decides where the banks are and how big), and the **creases come from the billowed Worley dilator**
- **Multiple scattering was not normalised**: more octaves brightened the entire sky, and at 3 octaves the cloud base was lifted to 5x single scattering, destroying the bright-crown / dark-base contrast that is the strongest 3D cue cumulus have. The sum is now divided by its zero-optical-depth value, so 1 means fully transparent
- **The energy-conserving integral divided by sigma twice**: `L` is already incident radiance (the emission term is j = sigma*L), so dividing again re-introduced 1/sigma - roughly 20x over-exposure, which saturated the cel quantiser into one flat white sheet
- **Cloud bases were lit far too bright**: the ambient term only interpolated horizon to zenith, and the horizon colour is the brightest band in the sky, so the signature "white crown on a dark flat base" disappeared. Added a height term that darkens towards the base (a cloud base looks at a small solid angle of sky and is occluded by itself)
- **A grey outline around every cloud** (the main source of the sticker look): the threshold band was too wide and the powder floor too high; both tightened

### Added
- **A scattering model**: dual-lobe Henyey-Greenstein phase function (`volCloudGForward` / `volCloudGBackward` / `volCloudPhaseForward`) plus Wrenninge-style octave multiple scattering (`volCloudScatterOctaves`). The previous lighting had **no angular dependence whatsoever**, while "clouds flare towards the sun" is the single strongest realism cue
- **Weather now selects cloud type** (Nubis dimensional profile): `volCloudTypeContrast` / `volCloudTypeBias`. The weather map no longer only decides *how much* cloud, it decides whether this patch is a flattened stratus sheet or a vertically grown cumulus. Both keep a flat base (a cloud base is a condensation surface) - what changes is how far the cloud grows upward
- **`volCloudBaseCoverageBoost`**: extra coverage near the cloud base. Tiled 3D noise carves odd shapes out of distant low-angle clouds; firming up the base suppresses it
- **`volCloudAerialPerspective`**: distant cloud is extincted *and* re-tinted towards the sky colour instead of merely fading alpha. Fading alpha alone leaves a translucent white patch hanging in the sky, unrelated to the atmosphere behind it
- **Domain warp inside the bake**: sample coordinates are pushed by a second noise field first. Without it, isotropic FBM produces round blobs, while real cloud folds are sheared and stretched

### Changed
- Detail erosion switched from plain subtraction to the Nubis form `Remap(Base, Detail, 1, 0, 1) = (Base - Detail) / (1 - Detail)`. Subtraction drags the interior down with the edges (thick clouds go grey); Remap keeps Base=1 at 1, so solid cores are untouched and only the rim is chewed - which is what makes the bite read as cauliflower. It is also monotone, so the CDF normalisation's "coverage = literal fraction" property survives
- High-frequency detail now fades out with LOD to a 35% floor instead of to zero: with no remaining bite, distant clouds lose every scale reference, and small features are precisely what puts the large ones in perspective
- Colour and transmittance share one `sigma * ds`. The old code used `dn^2 * sigma * 2.2` for colour and a separately hand-tuned factor for alpha, i.e. two independent knobs pretending to be one quantity
- Defaults recalibrated for the new noise: `coverage` 0.26→0.22, `scale` 0.00048→0.00042, `detail` 0.7→0.4, `weatherContrast` 0.55→0.6, `typeBias` 0→0.25. The new mass field is far more connected, so the same numbers now cover much more sky

## v1.6.0

### Fixed
- **Volumetric clouds drew almost nothing even when enabled**: the shape volume combined its noise with the Schneider Perlin-Worley term `pw = (perlin - (1 - worley)) / worley`, which **collapses to exactly 0** wherever `worley` is small - measured on the shipped asset, about half of all voxels were 0, while the coverage threshold sat above 0.48, so nothing got through. The result was "Feature installed, volume on, not a single cloud in the sky" with no error. The bake now ends with a CDF histogram normalisation that remaps each channel onto a uniform distribution, which puts `coverage` back to meaning "fraction of sky covered"
- **Normalisation cannot fix ties**: when half the samples share one value they remain a single constant block after the remap, so sliding `coverage` past that value jumped the sky from 0% to 50% cloud. `perlin` is now mixed back into `pw` at 12% to leave ordering information in the dead zone
- **The weather layer degenerated into a constant**: the 2D weather map originally re-centred on the camera like the 3D shape, pinning the sample window at uv≈0 so the sky overhead always read the same corner of the tile. Both maps now sample absolute world coordinates with `frac()` (equivalent under Repeat wrap, it only keeps the values in a float-friendly range)
- **A wrong reference type left the weather map null**: references to generated textures (`AssetDatabase.CreateAsset`) must use `type: 2`; writing `type: 3` as for imported assets resolves to null and the weather layer silently stopped working
- **White salt grains across the far field**: the LOD contrast compensation was uncapped, so at lod 6 a heavily blurred field got amplified 3x. The boost is now capped at 1.8x and the threshold band widens with lod at the same time
- **The silver lining sprinkled dots outside the silhouette**: thin edge voxels received full-intensity sun and deposited bright colour with a tiny alpha, and it survived temporal convergence (it was not dither, it was simply wrong). The silver term is now gated by voxel density
- **A cloud-free stripe along the horizon**: `horizonFade`'s `smoothstep(0.02, 0.14)` hollowed out 0-8° of sky while the flat cloud layer is off by default. Those low rays already start beyond the aerial-perspective fade distance, so the window is narrowed to `0.010~0.075` instead
- **Newly created sky assets showed no clouds**: `CreateAssetMenu` does not fill texture references, and with `volCloudNoiseTexture` null the noise samples as 0 forever, so density never crossed the threshold - silently. The shipped references are now filled in at edit time, and the runtime logs one warning per missing map (not per frame)
- The baker's trilinear interpolation used `w` as the weight for the final lerp instead of `v`; fixed

### Added
- **`Textures/CloudShape2D.asset`** (512², shipped): the density field went from "one 3D noise" to four stacked layers - weather (which part of the sky has clouds) → shape (the outline of one cloud) → domain warp (curling the edges) → high-frequency erosion (cauliflower bite). A single 3D noise is ergodic: spread over the sky it can only produce one sheet of frosted glass
- **`Tools > Cloud > Bake Cloud Shape Maps`** to re-bake the weather map; both bakes now run the CDF normalisation
- **Four new cloud parameters**: `volCloudShapeTexture`, `volCloudWeatherScale`, `volCloudWeatherContrast`, `volCloudWeatherDetail`, `volCloudWarp`
- **`volCloudDebug` with six views**: Coverage / MarchRange / Field / Profile / Lod / Weather. "No clouds" and "clouds drawn but invisible" cannot be told apart by eye - all three root causes in this release were found this way
- The debug path gained the `Weather` channel, and "the ray never crosses the slab" now renders pure red so it cannot be confused with the pure black of "crossed it, density zero"

### Changed
- Volumetric cloud defaults retuned for *separable* cumulus: `coverage` 0.5→0.24, `baseHeight` 600→800, `thickness` 700→300, `scale` 0.0005→0.00065, `density` 1.2→1.7, `marchSteps` 40→32, `detail` 0.35→0.8, `silverIntensity` 0.6→0.3, `windSpeed` 12→7. The shipped and sample sky presets were updated in step
- The cloud-base height ramp narrowed from 0.22 to 0.12 - cumulus need a flat floor, and a rounded base has no character
- The volume's Perlin FBM went from 5 to 6 octaves

## v1.5.0

### Fixed
- **The underwater pass never drew a single pixel**: `UnderwaterPostProcessPass` read `_CameraDepthTexture` without declaring `ConfigureInput(Depth)`, so RenderGraph never bound the depth texture, `SampleSceneDepth` returned 0, every pixel was classified as sky and alpha came out 0. The Feature was installed and the volume active, yet the frame did not change - with no error either. The pass now declares `Depth | Color`, and skips with a warning instead of failing silently when depth is unavailable
- **`Data/Underwater/UnderwaterZoneProfile.asset` was an empty profile**: `UnderwaterSetup` created the override with `profile.Add<T>()`, which only builds an in-memory instance and serializes as `fileID: 0` - fine in the current session, gone after reopening the scene. Now uses `VolumeProfileFactory.CreateVolumeComponent<T>()` so it becomes a real sub-asset
- **Injection point was hard-coded**: `Feature.Create()` always forced `BeforeRenderingTransparents`, so the Inspector value did nothing. The serialized value is respected now, defaulting to `BeforeRenderingPostProcessing` (the water surface is transparent, so the pass has to run after it to fog the surface too)
- **Scene textures were Y-flipped**: injected before the post stack, the destination differs from the camera-rendered depth / opaque textures by one flip on D3D/Metal. The symptom was fog landing on *near* geometry while the distance stayed clear, plus a vertically mirrored ghost. `_UWSceneScaleBias` is now derived from `SystemInfo.graphicsUVStartsAtTop` and shared by the depth read, the opaque read and the world-position reconstruction
- **Fog colour was 3-4x too bright**: `MaterialPropertyBlock.SetColor` does not convert sRGB to linear (unlike an Inspector Color field), so the fog read as a sheet of white mist. Conversion is now explicit
- **Caustic frequency and brightness**: `causticsScale` is documented as cycles per metre but was used as radians per metre (0.55 gave an 11 m period), turning the bands into one white wash, and the brightness coefficient overwhelmed the fog. Now converted with 2π, dimmed to a sane range, and faded in over the first 0.6 m below the surface - otherwise the surface itself caught a full-strength pattern and the ceiling glowed
- **The pass was enqueued even when inactive**: with declared dependencies that made unrelated scenes pay for a depth / opaque copy every frame. `AddRenderPasses` now checks the volume's `IsActive()`

### Changed
- Editor tool menus moved under **`Tools > Underwater/`** (the docs previously said `CartoonRendering > Underwater > …`, which never matched the code)
- The renderer is resolved from `GraphicsSettings.currentRenderPipeline` instead of a hard-coded `Assets/Settings/CartoonRP_Renderer.asset`, so the package works in any project
- The shader no longer depends on `Blit.hlsl` / `TextureXR.hlsl`; it uses the package's own fullscreen triangle, like the volumetric light shader
- The old "flat tint over the whole screen" behaviour is now "fog must grow with distance": `color` / `tintStrength` still exist, but as two parameters of the new fog model

### Added
- **Underwater volume parameters grew from 3 to 19**: per-channel absorption (`absorptionRate` / `absorptionColor`), in-scattering fog (`fogDensity` / `deepColor` / `depthToFullColor`), caustics (intensity / frequency / speed / depth fade), refraction wobble, fog-scaled vignette, cartoon `banding`, `waterSurfaceY`, and five debug views
- **`Tools > Underwater > Create Demo Scene`**: a tank plus a row of spheres sharing one material and differing only in distance (15/20/26/32/38 m), a water surface and a local volume zone - built so that distance is the only variable
- **`Tools > Underwater > Dump State`**: logs renderer / camera / pipeline / volume / shader status and the sphere-row distances
- **`Tools > Underwater/Debug/*`**: Sky Mask / Fog / Depth / Caustics / Absorption debug view menus


## v1.4.1

### Fixed
- **Interaction texture silently downgraded on some platforms**: `GrassField` created its interaction RT as `R8`, which defaults to sRGB — platforms without `R8_SRGB` support fall back to `RGBA32` and log a warning. Now created explicitly with `RenderTextureReadWrite.Linear`
- **`WorldBendController` used an inconsistent namespace**: it was the only script in the package under `Opoi375.CartoonRendering`, so scripts that merely `using CartoonRendering` could not find the type. Unified to `CartoonRendering`

### Changed
- `GrassField` gained a `VerboseDebug` toggle (default **off**). It used to log footprint / interaction strength every 2 seconds unconditionally, which spammed the console in real runs
- Re-tuned sky presets: volumetric cloud parameters in `CartoonProceduralSky.asset` and the sun direction / time of day in `CartoonSky.mat`

## v1.4.0

### Added
- **Volumetric light (god rays)**: screen-space ray marching with main-light shadow map sampling — real scattering, not a radial-blur fake
  - Half-resolution march (Beer's law + Henyey-Greenstein phase function) → 3×3 tent kernel bilateral upsample weighted by depth similarity → additive composite. The scattering's alpha channel doubles as scene depth for the depth-aware filter
  - `VolumetricLight` volume component: intensity / density / anisotropy / max distance / height fog / shadow strength / step count / jitter / dust noise / cartoon banding. `Intensity` defaults to 0 so the effect never turns on without an explicit Volume override
  - Built-in debug views — the `Shadow` mode tells you whether the main light shadow keywords are wired up; when they are not, the effect silently degrades into uniform fog
  - Editor tools under `Tools > Volumetric Light > …` (install feature / demo scene / diagnostics / debug views)

### Fixed
- **VolumeProfile sub-assets were never persisted**: both demo scene builders used `profile.Add<T>()`, which only creates an in-memory instance — it serialized as `fileID: 0`, so the overrides looked fine in the session but vanished as soon as the scene was reopened
- **Demo scene builders could hang the editor**: `SaveCurrentModifiedScenesIfUserWantsTo()` pops a modal dialog when called from a script, blocking the main thread forever if nobody clicks it

## v1.3.0

### Added
- **LED dot-matrix text**: render any text as an LED dot-matrix sign — dots are fully procedural (UV gridding + circle per cell), `fwidth` analytic anti-aliasing, HDR glow with scan line and dual-sine per-dot flicker
  - `LedTextMaskBaker` bakes text into a single-channel mask texture at runtime; no font asset, no extra camera, supports CJK and runtime text changes
  - `LedCurvedScreen` generates a curved screen mesh with arc-length uniform UVs, so dot spacing stays even across the curve
  - Editor tools under `Tools > LED > …`; generated assets land in `Assets/CartoonRendering/LED/`

## v1.2.0

### Added
- World Bend sky/cloud linkage: sky gradient horizon dips with curvature (maxSkyDip clamp), volumetric cloud base droops with distance (cloudBendScale weakening + cloudMaxDroop clamp), new skyHorizonDistance parameter


## v1.1.1

### Fixed
- PBRToonHair ForwardLit / GBuffer vertex stages missed the world-bend injection (tangent normal variant), so hair did not sink with the body when bent

## v1.1.0

### Added
- **World Bend (tiny-planet view)**: Animal Crossing-style round-earth effect — distant vertices sink with the square of horizontal distance from the camera
  - New shared library `Shaders/Library/WorldBend.hlsl` (vertex bend + normal correction)
  - New `WorldBendController` component (curvature / dead zone / normal correction, live edit-mode preview, zero cost at zero curvature)
  - All geometry shaders in the package (PBRToon×4, water×3, grass, building) hooked in every pass, including ShadowCaster / DepthOnly / DepthNormals / GBuffer / outline

## v1.0.1

### Fixed
- **Far-distance volumetric cloud banding** (slice/terrace artifacts):
  - March range clamped to the aerial-fade distance (baseY×16) — no more wasted steps in invisible space
  - Distance-adaptive noise LOD: per-step mip selection from step length and pixel footprint (Nyquist-biased); detail erosion fades with LOD
  - Cloud bottom/top height-profile ramps widen with step length, staying continuously sampled at range
  - March jitter widened to ±0.9 step length

### Added
- `volCloudTemporalEnabled`: temporal accumulation (TAA) toggle. When off, no history buffers are allocated and the march jitter freezes to a stable pattern

### Changed
- CloudNoise3D volume re-baked with a full mip chain (128³→1³) and trilinear filtering

## v1.0.0

First public release:

- PBRToon toon character shader family (Base / Face / Eye / Hair)
- Cartoon building shader (smooth stylized lighting, full baked/Forward+/deferred support)
- Procedural cartoon sky (day-night cycle, stars, 2D clouds)
- Volumetric clouds (ray marching + temporal accumulation)
- Cartoon water Simple / Advanced + underwater post process
- Pixelate post process
- Interactive grass
- SDF UI graphics + SDF generator and other editor tools
