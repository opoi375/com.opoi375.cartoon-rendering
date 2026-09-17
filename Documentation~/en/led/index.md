# LED Dot-Matrix Text

`CartoonRendering/LED/DotMatrix Text` renders any text as an LED dot-matrix sign — every glyph is assembled from round glowing dots on a regular grid, with bloom, a scan line and per-dot flicker.

The text is not *drawn*, it is *dotted*: a text mask decides which LEDs on the grid light up.

## Features

- **Fully procedural dots** — each dot comes from `length(frac(uv)) + smoothstep`; no dot-matrix texture needed
- **Analytic anti-aliasing** — `fwidth` softens the dot edge by screen-space derivative, so the grid never shimmers into moiré at distance or at grazing angles
- **Runtime text changes** — `LedTextMaskBaker` uses `Font.GetCharacterInfo` to blit glyph bitmaps straight into a mask texture; no font asset, no extra camera required
- **Scroll / scan / flicker** — mask UV scroll, horizontal scan line, dual-sine per-dot flicker
- **HDR glow** — lit dots are HDR so Bloom produces a real halo; unlit dots stay faintly visible to keep the panel readable

## Quick setup

1. Build a screen (flat or curved):
   - Manually: add `LedCurvedScreen` — it generates a curved mesh procedurally with **arc-length** uniform UVs
   - Or run **Tools > LED > Create Demo Scene** for a complete demo scene in one click
2. Create a material with the **CartoonRendering/LED/DotMatrix Text** shader
3. Add `LedTextMaskBaker` to the object and type your text. Leave the material list empty to auto-grab the Renderer's material
4. After editing the text, right-click the component → **Rebake** (or enable `bakeOnEnable`)
5. Add **Bloom** to a Volume Profile, threshold around 0.8

## Where the text mask comes from

| Option | When to use |
| --- | --- |
| `LedTextMaskBaker` (recommended) | Text changes at runtime, or you need CJK. Set `osFontName` to an OS font name such as `Microsoft YaHei` |
| A white-on-black PNG | Fixed text, least setup. Set compression to **None** on import, otherwise glyph edges get artifacts |

The mask is a single-channel (R8) texture — white means lit.

`LedTextMaskBaker` writes `_Aspect` from the mask aspect ratio. If the same object also has `LedCurvedScreen`, it additionally fits the mask height to the screen aspect so the dots stay perfectly round.

## Parameter reference

### Grid

| Parameter | Description |
| --- | --- |
| `_Cols` | LED count across the screen — higher means a denser grid |
| `_Aspect` | Screen aspect ratio W/H. Written by the baker; if you set it by hand it must be correct or the dots turn into ellipses |
| `_DotRadius` | Dot radius in cell units; 0.36 – 0.42 looks natural |
| `_DotSoftness` | Dot edge softening, works together with the `fwidth` anti-aliasing |

### Mask & scroll

| Parameter | Description |
| --- | --- |
| `_MaskThreshold` / `_MaskSoftness` | Mask binarization threshold and transition width |
| `_MaskTransform` | Mask tiling / offset (XY = Tiling, ZW = Offset) |
| `_Scroll` | UV scroll speed. **Leave at 0 by default** — `_Time` keeps running in edit mode, so a non-zero value slowly scrolls the text off screen |

### Flicker & scan line

| Parameter | Description |
| --- | --- |
| `_FlickerAmount` | Flicker strength. Each dot gets an independent random phase plus dual-sine, so the panel never blinks in unison |
| `_FlickerSpeed` / `_FlickerRatio` | Base frequency and second-frequency multiplier |
| `_ScanIntensity` / `_ScanSpeed` / `_ScanWidth` | Horizontal scan line |

### Color

| Parameter | Description |
| --- | --- |
| `_OnColor` / `_OnIntensity` | Lit dot color (HDR) and intensity, feeds Bloom |
| `_OffColor` | Unlit dot color. **Don't set it to pure black** — keeping the dark dot grid visible is what makes it read as an LED panel |
| `_PanelColor` | Panel color between the dots |

## Tools

| Menu | What it does |
| --- | --- |
| **Tools > LED > Create Demo Scene** | Creates and saves a demo scene to `Assets/Scenes/LEDDotMatrix.unity` |
| **Tools > LED > Build In Current Scene** | Rebuilds in the current scene only, without writing a scene file |
| **Tools > LED > Dump Mask PNG** | Diagnostics: exports the baked mask to `Assets/CartoonRendering/LED/` and logs material / mesh state |

Generated materials and the Volume Profile land in `Assets/CartoonRendering/LED/`.

## How it works

1. **UV gridding** — `floor(uv × gridCount)` gives each LED's cell id, `frac` gives the local coordinate inside the cell (origin at cell center)
2. **Procedural round dots** — draw a circle inside the cell; the screen-space derivative from `fwidth` decides the edge hardness
3. **Mask sampling** — sample the text mask at the **cell center**, with 2×2 supersampling so thin strokes are not missed
4. **UV scroll** — add `_Time.y × _Scroll` to the mask sample coordinate
5. **Dual-sine flicker** — two frequencies summed, with a per-dot phase from `frac(sin(dot(cell, …)))`

### About the curved screen

`LedCurvedScreen` lays out UV.x along **arc length** rather than a planar projection, so dots stay equidistant across the curve and the text is not stretched at the ends. A positive `arcAngle` means the surface is concave toward the camera; negative curves away. Mesh winding is auto-corrected, so the panel never disappears into back-face culling.
