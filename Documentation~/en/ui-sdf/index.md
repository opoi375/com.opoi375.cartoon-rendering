# SDF UI Graphics

Signed-distance-field UI graphics: edges stay razor sharp at any scale, with boolean ops and morphing built in.

## Built-in Shaders

| Shader | Contents |
| --- | --- |
| `SDFShapesExample` | Primitives: rounded rect, circle, … |
| `SDFBooleansExample` | Boolean ops: union / intersection / subtraction |
| `SDFMetaballsExample` | Metaball blending |
| `SDFTicketExample` | Ticket shapes (perforated edges and combos) |

The shared function library lives in `Shaders/UI/SDF2D.hlsl` — include it to write your own UI shapes.

## Typical Usage

```hlsl
#include "Packages/com.opoi375.cartoon-rendering/Shaders/UI/SDF2D.hlsl"

float d = sdRoundedBox(uv, halfSize, radius);   // distance field
float a = smoothstep(fwidth(d), 0.0, d);        // anti-aliased edge
color.a *= a;
```

## With the SDF Generator

Need to turn bitmap masks into SDF textures (dissolve, burn, face-shadow maps)? Use the [SDF Generator](/en/tools/#sdf-generator).
