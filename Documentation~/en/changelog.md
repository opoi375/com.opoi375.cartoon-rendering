# Changelog
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
