# Editor Tools

The package ships editor utilities under the **Tools** and **CartoonRendering** menus.

## SDF Generator

Menu: **Tools > SDF > SDF Generator** (GPU-accelerated, Compute Shader baking)

- **Tab 1 "Mask → SDF"**: bakes a black-and-white mask into a normalized SDF texture (0.5 is the boundary) — for PBRToon face-shadow maps, dissolve effects, etc.
- **Tab 2 "Frames → Gradient"**: interpolates a set of SDF textures named with a `_frameNumber` suffix (e.g. `xxx_SDF_177`) into a gradient texture — for dissolve / burn / growth effects

## Cloud Noise Baker

Menu: **Tools > Cloud > Bake Cloud Noise 3D**

Generates the 128³ tileable Perlin-Worley noise volume (R = base shape, GBA = Worley detail) with a full mip chain and trilinear filtering, sampled by the volumetric clouds.

Re-bake after changing noise parameters; the clouds pick up the new texture automatically.

## Water Generators

- **CartoonRendering > Water > Create Water Plane**: create a water plane with cartoon water material
- Water Assets Generator: produces supporting water assets

## Sky Materials

**CartoonRendering > Sky** menu items create / repair sky materials, output into the project's `Assets/`.

## Grass Tools

Grass Field Tool: generate layouts and bake interaction data at edit time. See [Interactive Grass](/en/grass/).

## Underwater Setup

**CartoonRendering > Underwater > Setup Underwater Post Process**: adds the underwater post-process Feature to the current URP Renderer in one click.
