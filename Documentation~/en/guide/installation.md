# Installation

## Via Git URL (recommended)

1. Open **Window > Package Manager**
2. Click **+** → **Add package from git URL…**
3. Enter:

```
https://github.com/opoi375/com.opoi375.cartoon-rendering.git
```

Pin a version (recommended for production):

```
https://github.com/opoi375/com.opoi375.cartoon-rendering.git#v1.0.1
```

## Via Local Disk

If you cloned the repository: **Add package from disk…** and select the `package.json` at the repo root.

## Post-install Checklist

1. Verify URP ≥ 17.5.0 (pulled in automatically)
2. Sky / volumetric clouds require a Render Feature on your **URP Renderer**:
   - Open your Renderer asset (e.g. `PC_Renderer`)
   - **Add Renderer Feature > Cartoon Skybox Feature**
3. Water foam and the underwater effect need the camera **Depth Texture** enabled in the URP Asset

## Package Layout

| Folder | Contents |
| --- | --- |
| `Runtime/` | Runtime scripts (Render Features, sky, underwater, grass…) |
| `Editor/` | Editor tools (SDF baking, water generation, grass tools…) |
| `Shaders/` | Shaders and HLSL libraries |
| `Materials/` | Sample materials |
| `Data/` | Sky configuration ScriptableObjects and other assets |
| `Textures/` | Pre-baked noise volumes, LUTs |

## Upgrading

Git URL installs: select the package in Package Manager → **Update**, or change the URL suffix to a newer tag.

Check the [changelog](/en/changelog) before upgrading.
