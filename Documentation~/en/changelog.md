# Changelog

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
