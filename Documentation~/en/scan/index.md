# Scan Effect

A self-centred scan / radar sweep. One full-screen post-process pass, hooked up as a URP Renderer Feature.

![Voxel lattice scan: 3D cubes on floor and walls, edge outlines, cyan tech look](/scan/voxel-scan.webp)

> **Status: working, both modes verified with screenshots.**
> The image above is a real capture of voxel mode in this package's demo scene
> (`pattern = Voxel`, `voxelSize = 1.2 m`).

## Implementation

```
ScanEffectController (in the scene, on the player/camera)
        │ writes only
        ▼
ScanEffectRuntime (static animation state: active / origin / forward / front / fadeOut)
        │ reads only                  ScanEffect (VolumeComponent: look parameters)
        ▼                                        │ reads only
ScanEffectFeature.AddRenderPasses ───────────────┘
        │ three gates must all pass before EnqueuePass
        ▼
ScanEffectPass.RecordRenderGraph
        │ UseTexture(Depth) + SetRenderAttachment(activeColor)
        ▼
ScanEffect.shader (fullscreen triangle: radius/projection → wavefront → detail → composite)
```

The separation of responsibilities is deliberate: **looks live in the Volume (artists), animation
lives in the runtime state (gameplay), the pass only reads**. Deleting the controller therefore
cannot break rendering, and swapping Volume presets cannot affect trigger logic.

### Two patterns

| Pattern | Distance metric | Use |
| --- | --- | --- |
| `Radial` | `d = length(posWS - origin)` | Sweep outward from a character (radar) |
| `Frontal` | `d = dot(posWS - origin, forward)`, then cull by `cosHalfAngle` | Push forward through the view |

With `Sector Angle = 360`, `cos(180°) = -1` so the sector cull naturally degenerates into "pass
everything" - both patterns share one formula.

### Voxel mode (solid lattice)

Besides the flat grid there is a **true 3D cubic lattice** scan: the world is quantised into cubes of
side `voxelSize`, and cells **light up one by one** as the wavefront passes, producing a surging
voxel sweep.

| | Basic wireframe | Voxel mode |
| --- | --- | --- |
| Geometry | Screen-aligned three-axis grid + cell dots | World-space cubes (`floor(p / voxelSize)`) |
| Edges | Single-axis lines | **Two or more axes near a face** -> the cube's 12 edges |
| Interior | none | Per-cell hash brightness, reads as a solid voxel block |
| Reveal | Reached when the wavefront passes | `reveal = (front - cellCentreDist) / (voxelSize * 1.5)`, layer by layer |
| Toggle | `voxelSize = 0` (default) | `voxelSize > 0` |

```hlsl
float3 f   = frac(p / cell);
float3 nrm = min(f, 1 - f) * cell;              // distance to each axis plane, in metres
float  w   = max(fwidth(p.x), 1e-4) * 1.2;      // derivative-based width: no moire at distance
float3 on  = 1 - smoothstep(0, w, nrm);         // is this axis near a face?

float s    = on.x + on.y + on.z;
float edge = saturate(s - 1.0);                 // >=2 axes -> cube edge
float face = saturate(s) - edge;                // exactly 1 axis -> grid face
```

Setting `voxelSize > 0` switches the shader to the voxel branch; `voxelFill` blends between hollow
wireframe and solid blocks, `voxelReveal` controls how softly cells light up.

::: tip Verified
The image above is the actual output of voxel mode: world-space cubes line up correctly in
perspective on the floor, walls and ceiling, with crisp edges, lighting up cell by cell as the
wavefront passes. Note that this is not a screen-space grid - rotate the camera and the lattice stays
glued to the world.
:::

### Compositing (why it never reads activeColor)

Same convention as the package's underwater post process: `Blend One OneMinusSrcAlpha` **without**
reading `activeColor` (in RenderGraph that is an imported external resource; reading it can land on
an empty buffer). So:

```
result = src.rgb + dst.rgb * (1 - src.a)
```

The shader encodes accordingly:

```
src.a   = a              <- scanned intensity, 0 = exactly the original image
src.rgb = tech * a + glow
```

Which composites to `result = dst*(1-a) + tech*a + glow` - "move towards the scan colour by `a`,
then add emission". **At `a = 0` the result is pixel-identical to the input**, so when the effect is
off or has not reached a pixel, nothing on screen changes.

### The five visual layers

| Layer | Formula / technique | Turn it off with |
| --- | --- | --- |
| Wavefront | `exp(-((d - front)/lineWidth)²)`, thin shell with a screen-derivative floor to avoid shimmer | very large `lineWidth` |
| Trail | `exp(-(front - d)/trailLength)`, behind the wavefront only | `trailLength = 0` |
| Scanned detail | Three-axis grid (`fwidth`-based AA) + cell-centre dots + a Y-scrolling scanline | `detailIntensity = 0` |
| Edge outline | Depth Roberts cross / own depth, relative so near edges stay thin and far ones do not smear | `edgeIntensity = 0` or quality Low |
| Glow | Wavefront + trail + outline, in `edgeColor`, handed to the pipeline Bloom | `glowIntensity = 0` |

**Noise disturbance** jitters the wavefront radius per direction (`ScanFbm`) so it is not a perfect
compass circle. Quality High adds a second, finer octave.

**Restore**: `age = (front - d) / speed`, then `restore = curve(age / restoreTime)` with three
curves (linear / smooth / quadratic).

### Quality tiers

| Tier | Noise | Depth outline | Grid/dots | Use |
| --- | --- | --- | --- | --- |
| Low | off | off | simplified (no dot fade) | mobile / many concurrent scans |
| Medium | single octave | off | full | default |
| High | two octaves | on | full | PC / close-ups |

Tiers never change the API, only sampling and optional extras. When `IsActive()` is false the pass
is not enqueued at all (zero cost).

### Triggers

| Trigger | Entry point | Notes |
| --- | --- | --- |
| Manual | `controller.Trigger()` | code / UnityEvent |
| Skill | `controller.TriggerSkill()` | same animation, radius x1.6 and speed x1.3; the multipliers ease back afterwards so repeated casts do not stack |
| Auto demo | `autoDemo` + `autoInterval` | loops, for previewing / capture |
| Hotkey | `hotkey` (F5) / `toggleKey` (F6) | works with both the new Input System and the legacy Input Manager |

### Compatibility with the existing post chain

- Injects at `BeforeRenderingPostProcessing` by default: after this package's Underwater / volumetric
  light, so the glow still goes through Bloom / Tonemap.
- When inactive it **does not enqueue the pass** and does not request a depth dependency - no effect
  on ordinary rendering.
- Only adds and moves towards the scan colour; never reads `activeColor`, so it cannot fight with the
  underwater pass, volumetric light or SSIL over the same buffer.
- Preview and reflection cameras are skipped.
- If the depth texture is unavailable it logs once and skips instead of silently drawing a flat tint.

## Key parameters

### Master

| Parameter | Default | Notes |
| --- | --- | --- |
| `intensity` | 1 | Master strength; 0 means the pass is not enqueued |
| `enabled` | true | Master switch, toggled with F6 |
| `pattern` | Radial | Radial sweep / forward push |
| `quality` | Medium | Quality tier |

### Extent

| Parameter | Default | Notes |
| --- | --- | --- |
| `radius` | 40 | Max scan radius (m), Radial |
| `distance` | 80 | Max push distance (m), Frontal |
| `speed` | 45 | Scan speed (m/s) |
| `sectorAngle` | 360 | Full sector angle (deg). 360 = full circle; Frontal usually 30~70 |

### Look

| Parameter | Default | Notes |
| --- | --- | --- |
| `lineWidth` | 1.2 | Wavefront thickness (m) |
| `color` | cyan | Primary scan colour (grid / dots / tint) |
| `edgeColor` | bright cyan-white | Accent colour (wavefront / outline / glow) |
| `edgeIntensity` | 1 | Edge highlight strength |
| `glowIntensity` | 1.2 | Glow strength |
| `trailLength` | 6 | Trail length (m) |
| `gridDensity` | 0.2 | Grid density (cells per metre); 0.2 = 5 m cells |
| `dotDensity` | 0.5 | Dot density (dots per metre) |
| `detailIntensity` | 1 | Total strength of the scanned detail |

### Voxel style

| Parameter | Default | Notes |
| --- | --- | --- |
| `voxelSize` | 0 | Cube side in metres. **> 0 switches to the solid lattice**; 0 = basic wireframe. Typically 1~3 |
| `voxelFill` | 0.45 | Interior fill. 0 = hollow wireframe, 1 = solid blocks (per-cell hash shading) |
| `voxelReveal` | 3 | Wavefront reveal width (m). Smaller snaps cells on harder, larger fades them in |

### Behaviour

| Parameter | Default | Notes |
| --- | --- | --- |
| `restoreTime` | 0.8 | Restore time (s) |
| `restoreCurve` | Smooth | Linear / Smooth / Quadratic |
| `noise` | 0.15 | Wavefront noise disturbance |
| `attenuation` | 1 | Distance falloff exponent, 0 = none |

## Acceptance checklist

### Wiring

- [ ] After `Tools > Scan > Setup Scan Effect`, the renderer asset's `m_RendererFeatures` and
      `m_RendererFeatureMap` have the **same length** (adding to the list without refreshing the map
      fails silently)
- [ ] A `Scan Effect` component appears on the Volume Profile (not an empty `fileID: 0` reference)
- [ ] The URP asset has `m_RequireDepthTexture = 1`

### Function

- [ ] `Trigger()` runs one full scan and restores the original look afterwards
- [ ] `TriggerSkill()` is visibly larger and faster than the manual version
- [ ] `autoDemo` loops at the configured interval; turning it off stops it
- [ ] F5 triggers, F6 toggles, with neither the new Input System nor the legacy Input Manager throwing
- [ ] `centerMode = Target` centres on the character; an empty Target falls back to the camera

### Visuals

- [ ] The wavefront is a **thin shell**, not a solid glowing mass
- [ ] There is a trail behind the wavefront, dimmer than the wavefront itself
- [ ] The grid is **three-axis** (including vertical); dots only appear at cell centres
- [ ] Distant grid fades out through sub-pixel AA - **no moire, no shimmer**
- [ ] Scanned geometry gets an outline (quality Medium and above)
- [ ] Restore is smooth, with **no hard cut**
- [ ] After toggling off (F6) the image is **pixel-identical** to before

### Compatibility

- [ ] Underwater scene with both effects on: correct order, no overwrite, no blowout
- [ ] In a clouds/volumetric-light-only scene, disabling the scan changes nothing
- [ ] Works with `m_RequireOpaqueTexture = 0` (this pass does not use it)
- [ ] Reflection probes and preview windows never show the scan

### Performance

- [ ] When off, the `ScanEffect` pass is **absent** from the Frame Debugger
- [ ] When on, exactly one draw call and no extra RT allocation
- [ ] Low / Medium / High show a measurable GPU-time difference (only High enables depth outlining)

## Known issues

### Resolved: why nothing drew at all

Two root causes, both worth remembering:

| # | Cause | Symptom |
| --- | --- | --- |
| 1 | **Volume parameter YAML written in the wrong shape** | Parameters were initially written as a single line `diagnosticSolid: 1`. Volume parameters are nested `VolumeParameter` objects (`m_OverrideState` / `m_Value`); a single line is silently ignored by Unity - so the forced-solid-red never actually applied and several rounds of diagnosis were misled. Switching to the nested shape produced red immediately |
| 2 | **No Volume in the scene, or the Volume's profile lacks the component** | `VolumeManager.stack.GetComponent<ScanEffect>()` returns null -> the pass is not enqueued -> nothing appears at all |

Code changes that followed:

- `ScanEffectFeature` now **logs once** when a scan is running but the Volume stack has no component,
  explaining how to fix it, instead of silently drawing nothing.
- `Tools > Scan > Setup Scan Effect` now refreshes `m_RendererFeatureMap` unconditionally, adds the
  component to scene Volumes, and **creates a Global Volume when the scene has none**.

### Another trap: the Y flip

The pass originally copied the underwater post process's
`graphicsUVStartsAtTop ? (1,-1,0,1) : (1,1,0,0)`. Measured result: **the whole screen flipped
vertically** - depth was sampled at mirrored coordinates, world positions were wrong, the distance
test collapsed, and every pixel was classified as "already scanned". This pass writes
`activeColorTexture`, whose depth is in the same orientation, so it **must not flip**; it now uses the
identity transform.

### Fixed: tri-state diagnostic push

`diagnosticSolid` was a `BoolParameter` and the C# side pushed `? 1f : 0f`, so setting it to 2
("green when not active") still took the "unconditional solid red" branch. It is now
`ClampedFloatParameter(0..2)` pushed as-is. Worth remembering: **when you change a shader's
diagnostic branch, change the C# push type too**, otherwise the diagnostic tool itself lies to you.**

### Still unverified

- Overlap with the underwater pass / volumetric light in the same scene is untested.

### Diagnostic tool

`Tools > Scan > Diagnose Draw (RenderTexture)` runs four probes (this shader vs the underwater
control x DrawProcedural vs Blit), enough to tell "the shader outputs no pixels" from "the RenderGraph
injection point is wrong".

Caveat: it reads back its own temporary RT, which is not the Game View presentation path, so it only
proves "the material can output pixels" - not that the injection point is right.

