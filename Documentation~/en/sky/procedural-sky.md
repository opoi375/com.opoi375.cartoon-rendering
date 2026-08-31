# Procedural Cartoon Sky

The sky system pairs a `CartoonProceduralSky` ScriptableObject config with `CartoonSkyboxFeature` (a URP Render Feature).

## Why not RenderSettings.skybox

Unity 6 / URP 17's native skybox pipeline only draws the four built-in skybox shaders — custom shaders are **silently ignored**. This system renders a fullscreen triangle and reconstructs the world ray analytically (NDC → inverse VP), injected at `BeforeRenderingSkybox`. It also avoids the triangle-seam banding of camera-centred skybox cubes.

## Feature Set

- **Five-stop sky gradient**: Top / Middle / Bottom / Horizon / Background
- **Three palettes**: day / sunset / night, blended automatically from the sun elevation (`sunsetBlendStart`, `dayBlendEnd` control the transition ranges)
- **Sun disc**: adjustable size, inner/outer softness, can be hidden
- **Stars**: appear at night; color / intensity / size / density / scale, optional custom texture
- **2D clouds**: texture-driven stratus layer with height / threshold / scale / intensity
- **Volumetric clouds**: separate module — see [Volumetric Clouds](/en/sky/volumetric-clouds)
- **Gradient ramp**: an optional `gradientRamp` texture for fine gradient control

## Day-Night Cycle

`TimeOfDaySystem` derives a normalized time (0..1) from the directional light's rotation; the sky shader blends palettes accordingly.

**Just rotate the directional light** — sky, sun position, ambient light and stars all follow. The `CartoonProceduralSkyUpdater` component can sit on the light to sync ambient colors automatically.

## Setup

1. `Assets > Create > Cartoon Rendering > Procedural Sky`
2. Add **Cartoon Skybox Feature** to your URP Renderer and assign the asset
3. Rotate the directional light to see the cycle

Full parameter list: [Sky Parameter Reference](/en/reference/sky-parameters).
