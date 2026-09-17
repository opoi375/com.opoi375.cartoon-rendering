---
layout: home

hero:
  name: Cartoon Rendering
  text: Cartoon rendering toolkit for Unity URP
  tagline: Toon character shading · Procedural sky · Volumetric clouds · Cartoon water · Interactive grass — an all-in-one stylized rendering package
  image:
    src: /logo.png
    alt: Cartoon Rendering
  actions:
    - theme: brand
      text: Quick Start
      link: /en/guide/quickstart
    - theme: alt
      text: GitHub
      link: https://github.com/opoi375/com.opoi375.cartoon-rendering

features:
  - icon: 🎨
    title: Toon Characters
    details: PBRToon family (body / face / eye / hair) with shadow ramps, rim light, SDF face shadows, full baked-lighting & Forward+ support
    link: /en/shading/toon
  - icon: 🌅
    title: Procedural Cartoon Sky
    details: Five-stop gradient with day / sunset / night palettes, sun, stars, 2D clouds — day-night cycle follows the directional light
    link: /en/sky/procedural-sky
  - icon: ☁️
    title: Volumetric Clouds
    details: Ray-marched 128³ Perlin-Worley clouds with cel shading, silver lining and temporal accumulation, rendered at half resolution
    link: /en/sky/volumetric-clouds
  - icon: 🌊
    title: Cartoon Water
    details: Distance-LOD tessellation + analytic waves + depth-based foam, plus an underwater post process
    link: /en/water/
  - icon: 🌿
    title: Interactive Grass
    details: GPU-instanced grass fields that bend under characters and recover over time, with editor baking tools
    link: /en/grass/
  - icon: 🌍
    title: World Bend
    details: Animal Crossing-style tiny-planet view — distant vertices sink with distance squared, all passes hooked so shadows stay attached, tunable curvature / dead zone / normal correction
    link: /en/world-bend/
  - icon: 🔦
    title: Volumetric Light (God Rays)
    details: Screen-space ray marching with main-light shadow map sampling — real scattering, not a radial-blur fake. Half resolution with a depth-aware upsample, plus cartoon banding
    link: /en/volumetric-light/
  - icon: 🔠
    title: LED Dot-Matrix Text
    details: Render any text as an LED dot-matrix sign — fully procedural round dots, fwidth anti-aliasing, runtime text-mask baking, HDR glow with scan line and per-dot flicker
    link: /en/led/
  - icon: 🧩
    title: SDF Toolchain
    details: UI SDF materials plus a GPU SDF generator (mask→SDF, frames→gradient) for dissolve / burn / growth effects
    link: /en/ui-sdf/
---
