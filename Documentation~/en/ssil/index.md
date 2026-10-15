# Screen-Space Indirect Light (SSIL)

`CartoonRendering/PostProcessing/SSIL` implements the paper *Screen Space Indirect Lighting with Visibility Bitmask* (Therrien / Levesque / Gilet, arXiv:2301.11376): a single screen-space pass that produces **ambient occlusion (AO)** *and* **colored indirect light** at the same time. Purely screen-space, zero precomputation, fixed cost — scale sample counts and resolution to whatever hardware you target.

Two things set it apart from a plain SSAO / GTAO post process:

- **Fixed surface thickness.** Traditional methods treat the depth buffer as a height field with "infinite thickness" behind every hit, so thin geometry — fences, railings, ladders — over-darkens everything behind it and drags halos around, which you can only paper over with falloff heuristics. This method gives every sample a constant thickness, so light passes *behind* thin objects: what's behind a fence stays as bright as it should.
- **The visibility bitmask knows directions.** Each hemisphere slice is discretised into 32 sectors recorded in a single `uint`. Because the mask knows *which* directions are open, the same march can gather the color bouncing off neighbouring surfaces — indirect light that carries the objects' albedo and grounds the whole image.

## Highlights

- **AO + indirect light in one march** — paper Algorithm 1: a sample contributes light in proportion to the sectors it covers that no closer sample has occluded, times the light buffer and two cosine terms
- **No over-darkening behind thin objects** — constant-thickness modelling, no falloff hacks needed
- **Multiplicative composite** — `scene × (1 + indirect) × AO`, not additive fog; cranking the indirect strength never turns the screen milky
- **Cel banding** — quantise AO visibility into N steps for hard-edged comic shadows (same knob feel as the volumetric light banding)
- **Half-res march + bilateral blur + depth-aware upsample** — depth-similarity weights everywhere, occlusion never bleeds across depth edges
- **Distance fade** — fades out where the sampling density runs thin instead of sprinkling noise in the distance
- **Debug views** — inspect visibility or indirect contribution directly

## Quick Setup

1. Install the feature: menu **Tools > SSIL > Setup In Renderer**
   (or Add "Screen Space Indirect Light" manually in Renderer Features)
2. Put a **Global Volume** in the scene and **Add Override > CartoonRendering > Screen Space Indirect Light**
3. Raise **Intensity** from 0 to about 1
4. To see the indirect light alone, zero **AO Strength** and switch **Debug** to Indirect

> **Intensity defaults to 0**, like every Volume component in this package: `VolumeManager` returns a default instance even for components no Volume overrides, so a non-zero default would apply without any Volume present.

## Parameters

### Main

| Parameter | Description |
| --- | --- |
| `Intensity` | Master strength, 0 = off (default) |
| `AO Strength` | Ambient occlusion strength. 1 = full AO, 0 = indirect light only |
| `Indirect Strength` | Indirect light strength (multiplicative, safe to push) |
| `Indirect Tint` | Tint for the bounced light. White keeps the source colors; push warm / cool for stylisation |

### Sampling

| Parameter | Description |
| --- | --- |
| `Radius` | Sampling radius in world meters |
| `Thickness` | Surface thickness in world meters. Core knob: smaller lets light pass thin objects more easily; larger leaks light at thick-object edges |
| `Sample Count` | March steps per slice. 4 is the balance point; raise with the radius |
| `Slice Count` | Screen directions (slices). 4–6 is plenty, 8 is near-converged |
| `Jitter` | Dither strength. Seeded per pixel coordinate, so static images don't shimmer |

### Range

| Parameter | Description |
| --- | --- |
| `Max Distance` | Effect range in meters |
| `Distance Fade` | Where the fade-out starts, as a fraction of max distance |

### Quality & Style

| Parameter | Description |
| --- | --- |
| `Blur Radius` | Blur step in low-res texels. A blur is required for smooth results |
| `Banding` | Cel banding steps. 0 = smooth, 2–4 works well |
| `Debug` | `Visibility` shows occlusion (white = open), `Indirect` shows the gathered bounce light |

## How it works

For every pixel, several **slice directions** are marched across the screen plane:

1. Each sample has a front-face angle θf and a back-face angle θb (the front face pushed back along the view by `Thickness`) — the span between them is the solid angle the sample covers;
2. That angular span is mapped to a **bit range** among the 32 sectors and set in the slice's bitmask;
3. The sample contributes light in proportion to **the sectors it covers that were still unoccluded, over 32** — samples hidden behind closer geometry contribute nothing — then its sectors are merged into the mask;
4. At the end of a slice, `AO = 1 - bits set / 32`.

Angles are centred on the **projected normal** and cover the full hemisphere; anything below the tangent plane (behind the surface) is clamped to the edge — that's what gives you "infinite thickness only up to grazing".

## Known limitations

- Purely screen space: lights and occluders that leave the screen are missing information (true of the paper as well)
- Low sample counts are noisy — a blur is **mandatory**; raise `Sample Count` / `Slice Count` for cleaner results
- Tune to your scene scale: bigger worlds want a bigger `Radius`, dense small scenes a smaller `Thickness`
- The "direct light buffer" is the opaque scene color copy (`_CameraOpaqueTexture`); transparent objects don't bounce light
