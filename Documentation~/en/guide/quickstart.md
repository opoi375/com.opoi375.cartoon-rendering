# Quick Start

The three most common scenarios, two minutes each.

## 1. Toon-shade a Character

1. Point the character's materials at:
   - Body / outfit → `CartoonRendering/PBRToonBase`
   - Face → `CartoonRendering/PBRToonFace` (with an SDF face-shadow map)
   - Eyes → `CartoonRendering/PBRToonEye`
   - Hair → `CartoonRendering/PBRToonHair`
2. Tune **Shadow Color / Ramp** for the light-dark bands and **Rim** for rim lighting

Full parameter list: [Toon Characters](/en/shading/toon).

## 2. Enable the Cartoon Sky

1. On your URP Renderer: **Add Renderer Feature > Cartoon Skybox Feature**
2. Create a config asset via `Assets > Create > Cartoon Rendering > Procedural Sky`
3. Assign it to the Feature's **Sky** field (or leave empty — a default material is built at runtime)
4. Rotate the scene's directional light — the sky cycles through day → sunset → night automatically

Volumetric clouds: tick **Volumetric Clouds Enabled** on the sky asset. See [Volumetric Clouds](/en/sky/volumetric-clouds).

## 3. Drop in Cartoon Water

Menu **CartoonRendering > Water > Create Water Plane** (or manually assign `CartoonRendering/CartoonWaterAdvanced` to a Quad).

Tessellation handles mesh density — a 4-vertex Quad looks identical to a 1000×1000 grid; the mesh only provides the silhouette.

Foam requires the camera Depth Texture. See [Cartoon Water](/en/water/).
