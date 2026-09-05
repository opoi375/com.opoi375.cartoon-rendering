# Changelog

## Unreleased

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
