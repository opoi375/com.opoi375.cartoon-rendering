# World Bend (Tiny-Planet View)

An Animal Crossing-style "round earth" effect: vertices are bent **downward by the square of their horizontal distance from the camera**, so the horizon curves away and the world feels like a small planet.

![Exaggerated curvature demo: the character sinks into the bent ground while objects using built-in shaders stay put](/worldbend/curvature-demo.png)

> Demo at `curvature = 0.005`: the character (package shader) has sunk below the "ground" with only the hat poking out; the grey cubes and default ground use URP's built-in Lit and are unaffected.

## How It Works

```hlsl
// Camera-centric downward bend (Shaders/Library/WorldBend.hlsl)
w = max(distance(posWS.xz, cameraPos.xz) - deadZone, 0)
posWS.y -= w * w * curvature     // curvature = 1 / (2 × planet radius)
```

- Ground directly under the camera stays put; distant geometry sinks → the horizon visually curves down
- `deadZone` keeps the near field flat to prevent close-range artifacts
- Optional normal tilting (`ApplyWorldBendNormal`) makes lighting follow the curved surface

Every geometry shader in the package (PBRToon family, water, grass, building) is hooked up in **all passes** — ShadowCaster, DepthOnly, DepthNormals, GBuffer and outline included — so shadows, AO and outlines never detach from the bent picture.

## Usage

Add the **World Bend Controller** component to any active object in the scene (`Add Component → Cartoon Rendering → World Bend Controller`):

| Parameter | Default | Description |
| --- | --- | --- |
| `curvature` | 0.0004 | World curvature = 1/(2×planet radius). `0.001` exaggerated ACNH feel (500 m), `0.0004` moderate (1250 m), `0.0001` subtle (5000 m), `0` off |
| `deadZone` | 10 | Radius around the camera that stays flat (meters) |
| `bendNormals` | true | Tilt normals so lighting follows the curvature |

The component is `[ExecuteAlways]`, so the Scene view previews the bend live in edit mode. At `curvature = 0` the cost is a single multiply-add. Disabling the component restores the global parameters automatically.

## Caveats

::: warning Visual-only effect
Collision, navigation and physics still run in the **flat**, unbent world (Animal Crossing does the same). A "sunk" character is purely visual — its logical position is unchanged.
:::

- **Only package shaders bend.** URP's built-in Lit and Unity Terrain are unaffected — swap the ground material to a package shader (e.g. `CartoonBuilding` or `PBRToon/Base`) to bend the ground too
- Frustum culling uses the original (unbent) bounds, so a little extra geometry is drawn in the distance (the safe direction — nothing is culled incorrectly)
- Skybox / volumetric clouds / post-processing are not bent (they are screen-space and shouldn't be)

## Integrating Custom Shaders

To make your own shaders support the bend, inject three lines into the vertex stage:

```hlsl
#include "../Library/WorldBend.hlsl"   // adjust the relative path

// In every pass's vertex function:
VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
posInputs.positionWS = ApplyWorldBend(posInputs.positionWS);
posInputs.positionCS = TransformWorldToHClip(posInputs.positionWS);
nrmInputs.normalWS   = ApplyWorldBendNormal(nrmInputs.normalWS, posInputs.positionWS);
```

Apply the same treatment to auxiliary passes (ShadowCaster / DepthOnly / DepthNormals), otherwise shadows and AO will detach.
