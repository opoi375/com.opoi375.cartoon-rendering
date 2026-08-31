# Pixelate Post Process

`PixelatePostProcessFeature` quantizes the frame into chunky retro pixels.

## Features

- **Volume-driven**: all parameters live on the Volume Profile — add the **CartoonRendering/Pixelate Post Process** override and raise **Intensity** above 0 to enable
- **Zero asset setup**: the material is built lazily from the shader at runtime
- Blends with the Volume system for local / transitional effects (e.g. pixelate when entering an area)

## Setup

1. URP Renderer: **Add Renderer Feature > Pixelate Post Process Feature**
2. Scene Volume (Global or Local): Add Override > **CartoonRendering > Pixelate Post Process**
3. Tune Intensity and pixel block size
