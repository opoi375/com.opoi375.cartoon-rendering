# Interactive Grass

GPU-instanced cartoon grass fields with **trample bending and automatic recovery**.

## How It Works

Per frame:

1. Stamp footprints from player Transforms into `GrassInteractionField` (CPU truth, unit-tested)
2. Tick recovery decay
3. `CommandBuffer.DrawProcedural` bakes footprints into an interaction RenderTexture
4. `Graphics.DrawMeshInstanced` submits grass instances → `CartoonGrass.shader` reads the interaction RT and bends blades

## Components

| Component / Tool | Description |
| --- | --- |
| `GrassField` | Scene component: assembles layout generation, interaction field and GPU submission |
| `GrassFieldGenerator` | Pure-logic layout generation (unit-testable) |
| `GrassInteractionPlanner` | Interaction collision planning |
| Grass Field Tool (Editor) | Editor window for generating / baking grass fields |

## Usage

1. Add a `GrassField` component to the scene
2. Configure bounds, density and the blade material (`CartoonRendering/CartoonGrass`)
3. Assign player / interactor Transforms to the interaction source list

Blades are stylized cartoon shapes with wind sway, color gradient and trample spring-back.
