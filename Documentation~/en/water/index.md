# Cartoon Water & Underwater

## Two Water Shaders

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

`UnderwaterPostProcessFeature`: cartoon fog once the camera goes below the surface.

**Setup**: menu **CartoonRendering > Underwater > Setup Underwater Post Process**, or add the Renderer Feature manually. The material is built lazily at runtime — no asset setup needed.

Fog color, depth density and friends live on the Feature / Volume.
