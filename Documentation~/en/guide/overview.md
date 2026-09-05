# Overview

**Cartoon Rendering** (`com.opoi375.cartoon-rendering`) is a stylized cartoon rendering toolkit for Unity 6 / URP 17, distributed as a UPM package via Git.

It packs a complete cartoon rendering pipeline into one package — from characters to environment, from sky to water — with a consistent look across all modules.

## Modules

| Module | Contents | Details |
| --- | --- | --- |
| Toon characters | PBRToon Base / Face / Eye / Hair shaders + lighting library | [Details](/en/shading/toon) |
| Procedural sky | Fullscreen skybox Render Feature, day-night cycle, stars, 2D clouds | [Details](/en/sky/procedural-sky) |
| Volumetric clouds | Ray-marched clouds with cel shading + temporal accumulation | [Details](/en/sky/volumetric-clouds) |
| Cartoon water | Simple (tessellated waves) / Advanced (waves + foam fusion) | [Details](/en/water/) |
| Underwater | URP post-process Render Feature | [Details](/en/water/#underwater-post-process) |
| Pixelate | Volume-driven pixelation post process | [Details](/en/effects/) |
| Interactive grass | GPU grass fields with trample & recovery | [Details](/en/grass/) |
| World bend | Animal Crossing-style tiny-planet view, hooked into every pass | [Details](/en/world-bend/) |
| SDF UI | Rounded rects / boolean ops / metaballs UI materials | [Details](/en/ui-sdf/) |
| Editor tools | SDF generator, cloud noise baker, water generators | [Details](/en/tools/) |

## Design Goals

- **URP-native**: built on Render Feature / RenderGraph / Volume — no pipeline hacks
- **Performance-first**: half-res volumetric clouds with temporal accumulation; tessellation instead of high-poly water meshes; GPU-instanced grass
- **Modular**: every piece works standalone; parameters live on ScriptableObjects / Volumes
- **Engineered**: separate runtime/editor assemblies (asmdef), heavily commented, TDD-tested core logic

## Requirements

- Unity **6000.5** or newer
- Universal Render Pipeline **17.5.0+** (installed automatically as a dependency)

## License

MIT License. The package ships no third-party paid assets.
