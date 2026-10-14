# Cartoon Water & Underwater

## Two Water Shaders

![Cartoon water: analytic waves + depth foam, with reeds and cottages at the shore](/water/surface.webp)

### CartoonWaterSimple — Waves

Built-in **distance-LOD tessellation**: hull/domain stages subdivide triangles by camera distance, then displace vertices with an analytic wave field.

> Mesh density no longer matters — a 4-vertex Quad produces the same smooth surface as a 1000×1000 grid. The source mesh only provides the silhouette.

A fallback SubShader degrades to plain vertex displacement on platforms without tessellation.

### CartoonWaterAdvanced — Waves + Foam Fusion

Adds `CartoonWater` shading on top of Simple:

- Depth-difference color gradient (`_CameraDepthTexture`)
- Scrolled, distorted noise foam with smoothstep anti-aliasing

Because shading runs on the displaced surface, **crests lift the water and shrink the depth difference — foam naturally gathers on crests and shorelines**.

## Usage

1. Menu **CartoonRendering > Water > Create Water Plane** for one-click setup
2. Or manually: assign `CartoonRendering/CartoonWaterAdvanced` to any plane mesh
3. Foam requires **Depth Texture** enabled in the URP Asset

Three sample materials ship in `Materials/CartoonWater*.mat`.

## Underwater Post Process

`UnderwaterPostProcessFeature`: the underwater look once the camera goes below the surface.

It is **not** "a sheet of teal". Two independent terms are stacked:

| Term | Parameters | What it does |
|---|---|---|
| Per-channel absorption (out-scattering) | `absorptionRate` × `absorptionColor` | `T = exp(-σ·d)`. Red is eaten first, then green, then blue - distant geometry drifts blue-green **on its own**, without being painted over |
| In-scattering | `fogDensity` / `color` / `deepColor` / `tintStrength` | The water's own lit colour added back. Shallow vs deep fog colour is interpolated by **how far below the surface** the pixel is, so looking down and looking across are two different colours |

![Same scene: volume off on the left, underwater post process on the right](/water/underwater-compare.webp)

![Underwater post process: colder with depth, distant objects collapsing into blue-green fog](/water/underwater.webp)

The demo scene places a row of spheres that share one material and differ **only in distance**, so distance is the only variable. Sampling sphere centres:

| Distance | No effect | Underwater | Saturation |
|---|---|---|---|
| 15 m | (232,133,64) | (126, 88, 43) | 0.72 → 0.66 |
| 26 m | (206,118,55) | ( 90, 84, 56) | 0.73 → 0.38 |
| 38 m | (167, 95,45) | ( 22, 43, 40) | 0.73 → 0.20 |

At 38 m blue exceeds red - that is exactly the order in which water eats light.

### Extras

- `causticsIntensity` / `causticsScale` / `causticsSpeed` / `causticsDepthFade`: caustic bands locked to underwater surfaces. The normal is estimated from **screen-space derivatives of the world position** and blended triplanar - no normal texture needed. Faded in over the first 0.6 m below the surface (light that just entered the water has not focused into bands yet; without this the surface itself catches a full-strength pattern and the ceiling appears to glow) and gone entirely at `causticsDepthFade` metres - the bottom of a deep pool should not look lit by a lamp
- `waveStrength` / `waveScale` / `waveSpeed`: screen-space refraction wobble, applied to opaque pixels only (reads `_CameraOpaqueTexture`). Geometry right in front of the camera does not wobble; it grows with distance
- `vignette`: scaled by the local **fog amount**, so it only shows up in murky water
- `banding`: quantise the fog mix into N steps for hard cartoon depth bands (< 1.5 = off)
- `waterSurfaceY`: **must** match the real water surface - every "how deep below the surface" term is measured against it
- `debug`: Depth / Fog / Caustics / Absorption / SkyMask views

### Setup

1. **Tools > Underwater > Setup Underwater Post Process** - installs the Feature on the active URP renderer and creates a ready-configured `Underwater Zone`
2. Move / scale the zone's BoxCollider so it fills the water body below the surface. The Volume system fades the effect in and out by `blendDistance`; `intensity` defaults to 0, so nothing happens without a zone
3. Want a side-by-side immediately: **Tools > Underwater > Create Demo Scene**

The material is built lazily at runtime - no asset setup.

### Requirements and troubleshooting

The effect needs both the depth and the opaque texture. Missing either one means "you see nothing", and up to v1.4.1 it failed **silently**:

- **Depth Texture** must be enabled (pipeline asset or camera). No depth, no distance gradient; when it is unavailable the pass now skips and logs a warning instead of quietly drawing nothing
- The default injection point is **BeforeRenderingPostProcessing**: the water surface is a transparent object, so the pass has to run after it for the surface to be inside the fog
- The pass declares `ConfigureInput(Depth | Color)`. **Without the dependency RenderGraph never binds the depth texture**, `SampleSceneDepth` returns 0, every pixel is classified as sky, alpha comes out 0 - the effect draws nothing at all and reports no error
- Injected before the post stack, the destination differs from the camera-rendered scene textures by one Y flip on D3D / Metal (URP's own fullscreen passes handle this with `_BlitScaleBias`). This pass computes it from `SystemInfo.graphicsUVStartsAtTop`. The symptom is distinctive: **fog on near geometry while the distance stays clear**, plus a vertically mirrored ghost
- Order of investigation: **Debug > Sky Mask** (all white = depth never reached the pass) → **Debug > Fog** (there must be a near-to-far gradient; without one it is a flat tint) → **Dump State** (renderer / camera / volume / shader status)

> Up to v1.4.1 this feature was stuck on the depth dependency: the Feature was installed, the volume was active, and the frame did not change at all.

