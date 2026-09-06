# World Bend (Tiny-Planet View)

An Animal Crossing-style "round earth" effect: vertices are bent **downward by the square of their horizontal distance from the camera**, so the horizon curves away and the world feels like a small planet.

![Exaggerated curvature demo: the character sinks into the bent ground while objects using built-in shaders stay put](/worldbend/curvature-demo.png)

> Demo at `curvature = 0.005`: the character (package shader) has sunk below the "ground" with only the hat poking out; the grey cubes and default ground use URP's built-in Lit and are unaffected.

![Full effect: curved ground arc, dipped sky horizon, clouds gathering toward the horizon](/worldbend/planet-look.png)

> Full effect (exaggerated `curvature = 0.005`): the ground arcs like a planet, the sky gradient's horizon band dips to meet it, volumetric clouds gather softly toward the horizon without vanishing, and the character (hair included) sinks with the surface.

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

### Sky / Cloud Linkage

| Parameter | Default | Description |
| --- | --- | --- |
| `skyHorizonDistance` | 200 | Effective visible ground distance (m), used to derive the sky horizon dip so it aligns with the ground's edge |
| `maxSkyDip` | 0.15 | Maximum sky horizon dip (≈8.5°); prevents extreme curvature from dragging the whole gradient into the nadir color band |
| `cloudBendScale` | 0.35 | Weakened curvature factor for clouds. The cloud layer is much higher than the ground — full-strength droop would sink it out of view, so distant clouds gather softly toward the horizon instead |
| `cloudMaxDroop` | 300 | Maximum cloud droop (m); distant clouds rest on a lowered but bounded shell |

Only the sky gradient **bands** dip; the sun, stars and 2D clouds keep their true directions (lighting is unchanged, so nothing contradicts). The volumetric cloud base droops per-sample by horizontal distance, and the slab intersection plane shifts down accordingly to avoid clipping.

The component is `[ExecuteAlways]`, so the Scene view previews the bend live in edit mode. At `curvature = 0` the cost is a single multiply-add. Disabling the component restores the global parameters automatically.

## Caveats

::: warning Visual-only effect
Collision, navigation and physics still run in the **flat**, unbent world (Animal Crossing does the same). A "sunk" character is purely visual — its logical position is unchanged.
:::

- **Only package shaders bend.** URP's built-in Lit and Unity Terrain are unaffected — swap the ground material to a package shader (e.g. `CartoonBuilding` or `PBRToon/Base`) to bend the ground too
- Frustum culling uses the original (unbent) bounds, so a little extra geometry is drawn in the distance (the safe direction — nothing is culled incorrectly)
- The sky and volumetric clouds follow the bend via the controller linkage (see above) — no extra setup needed

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
