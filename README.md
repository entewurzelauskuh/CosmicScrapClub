# Cube Fly, also known as Cosmic Scrap Club

A small Unity 6.3 LTS / URP demonstrator. Build a cube ship in the
hangar, then fly it.

> Three scenes — Main Menu → Hangar (BuildScene) → Flight (FlyScene) —
> with cube placements carried across the transitions by a static
> `GameData` class and a persistent `DontDestroyOnLoad` corner button
> that toggles between Hangar and Flight.

The companion documents are:

- **[`cube_fly_spec.md`](cube_fly_spec.md)** — what the project is and what it does (the canonical product spec).
- **[`full_architecture.md`](full_architecture.md)** — how it is implemented (file-by-file architecture map).

---

## What's In Here

- Four cube types (A / B / C / D) defined by ScriptableObjects, so adding a fifth type is a data-only change.
- 90°-stepped per-placement rotation (`R` rotates around Z, `T` around X) — each cube remembers its placed pose.
- A delete tool with a non-allocating red `MaterialPropertyBlock` hover tint and an automatic flood-fill cleanup of any cube that gets disconnected from the alpha cube.
- A mass budget (cap 100). Every placed cube has `HP / Armour / Mass` placeholder stats. Going over the cap rejects the placement and shows a fading red message; total mass smoothly slows the ship's acceleration **and** rotation rates in flight.
- Live `Mass: X / 100` and `HP: Y` readouts in the bottom-left of the build UI.
- A red arrow indicator that auto-reparents to the frontmost cube so you can tell which way the ship is pointed.
- 6-axis thrust + pitch / yaw / roll + RMB free-look camera with snap-back.
- File logging to `Logs/runtime-<timestamp>.log` alongside the Editor console.

The project is intentionally MonoBehaviour-driven (no DOTS / ECS) — it is small enough that data-oriented patterns would be overkill.

---

## Requirements

- **Unity 6.3 LTS** (tested on `6000.3.11f1`). Newer 6.x patch versions should work; older versions may not.
- The project ships a Universal Render Pipeline (URP) asset; no extra setup needed beyond opening the project.

The shipped `Packages/manifest.json` pins:

- `com.unity.render-pipelines.universal` (URP 17.3)
- `com.unity.inputsystem` ≥ 1.19
- `com.coplaydev.unity-mcp` — pulled from its upstream git URL on clone. The embedded copy under `Packages/com.coplaydev.unity-mcp/` is git-ignored; Unity re-fetches it on first open. (Optional — only used for editor tooling. Drop the entry from `manifest.json` if you do not want it.)
- The standard Universal 3D template runtime packages (uGUI, TMP, etc.).

---

## Getting Started

### 1. Clone

```sh
git clone <this-repo-url>
cd "<repo-folder>"
```

### 2. Open in Unity

1. Launch **Unity Hub**.
2. **Add → Add project from disk** → pick the cloned folder.
3. Open it with Unity 6.3 LTS. The first open will:
   - Reimport assets (one-time, takes a few minutes).
   - Re-fetch git-pinned packages (e.g. the MCP-for-Unity package).
   - Generate the IDE solution / `.csproj` files locally (these are git-ignored).

### 3. Press Play

The build settings have `MainMenu` registered as the first scene
(index 0), so pressing **Play** anywhere drops you into the menu.

You can also press Play directly on `BuildScene` or `FlyScene` — both
gameplay scenes contain a `UIBootstrap` that lazily instantiates the
persistent UI on entry.

---

## Controls

### Hangar (BuildScene)

| Input | Action |
|-------|--------|
| LMB | In **Place** tool: place a cube at the preview cell. In **Delete** tool: remove the cube under the cursor. |
| `R` | Rotate the next placement 90° around Z. |
| `T` | Rotate the next placement 90° around X. |
| Toolbar (bottom) | Click a cube type (A / B / C / D) to select it (switches to Place tool). Click **Delete** to switch to the delete tool. |
| Right-mouse drag | Orbit the build camera. |
| Mouse wheel | Zoom the build camera. |
| Top-right `Fly!` button | Switch to FlyScene. |

Placement rules: cubes must be face-adjacent to the alpha cube or to
another placed cube. Going over the mass cap (100) rejects the
placement and pops a red "Too much mass!" message at the top of the
screen.

### Flight (FlyScene)

| Input | Action |
|-------|--------|
| `W` / `S` | Thrust forward / backward (local +Z / −Z). |
| `A` / `D` | Strafe left / right (local −X / +X). |
| `Space` / `C` | Thrust up / down (local +Y / −Y). |
| `↑` / `↓` | Pitch nose up / down (local X). |
| `←` / `→` | Yaw left / right (world Y, so it stays intuitive when pitched). |
| `Q` / `E` | Roll anti-clockwise / clockwise (local Z). |
| RMB (held) + mouse | Free-look. Releasing snaps the camera back behind the ship. |
| Top-right `Hangar` button | Switch back to BuildScene. |

Heavier ships are sluggish: acceleration and rotation rates scale by
`Lerp(1.0, 0.1, Clamp01((mass − 10) / 90))`, so total mass 10 = no
slowdown, total mass 100 = 90% slow.

---

## Project Layout (one-liners)

```
Assets/
  Scenes/         MainMenu, BuildScene, FlyScene (+ template's SampleScene, unused)
  Scripts/Core/   GameData, UIManager, UIStyle, CubeStats, CubeTypeDefinition,
                  CubeTypeRegistry, FileLogHandler, LogBootstrapper, SceneSwitcher,
                  UIBootstrap
  Scripts/Build/  BuildManager, CubePreview, BuildCamera, BuildToolbarController,
                  BuildIndicatorController, PlacedCubeData
  Scripts/Fly/    FlyController, FlyCamera
  Scripts/MainMenu/ MainMenuController
  CubeTypes/      CubeTypeRegistry + CubeTypeA/B/C/D ScriptableObjects
  Prefabs/        AlphaCube, PlacedCube[A–D], PreviewCube, AlphaCubeIndicator
  Materials/      Per-prefab URP/Lit materials (+ ext/ PBR set; not currently used)
  Input/          CubeFlyInputActions (.inputactions + hand-rolled C# wrapper)
  UI/             UICanvas, UIBootstrap prefabs
  Settings/       URP render-pipeline assets
ProjectSettings/  Layers (PlacedCube/AlphaCube/PreviewCube), build list, New Input System on
Packages/         manifest.json + lockfile (the MCP package's source is git-ignored)
Logs/             Runtime log files (git-ignored)
```

See [`full_architecture.md`](full_architecture.md) for the file-by-file
breakdown.

---

## What Is Not Committed

The `.gitignore` excludes:

- Unity caches and generated folders (`Library/`, `Temp/`, `obj/`, `Build/`, `Builds/`, `Logs/`, `MemoryCaptures/`, `Artifacts/`, `UserSettings/`).
- Generated IDE / solution files (`*.csproj`, `*.sln`, `*.slnx`, `.vs/`, `.vscode/`, `.idea/`).
- Crash dumps (`mono_crash*.json`).
- Builds (`*.apk`, `*.aab`, `*.exe`, `*.app`, `*.unitypackage`).
- OS metadata (`.DS_Store`, `Thumbs.db`, …).
- Agent / tooling local state (`.claude/settings.local.json`, `.cursor/`).
- The embedded copy of the MCP-for-Unity package (`Packages/com.coplaydev.unity-mcp/`) — `manifest.json` already pins it to the upstream git URL.

`Packages/manifest.json` and `Packages/packages-lock.json` **are**
committed so package versions stay reproducible across machines.

---

## Troubleshooting

- **Empty / invisible UI text on first open.** The project deliberately uses *legacy* `UnityEngine.UI.Text` with `LegacyRuntime.ttf` so it works without the user importing TMP Essentials. If something looks wrong, confirm `LegacyRuntime.ttf` resolved (it ships with Unity).
- **`BuildManager: Custom layers not found` warning.** The shipped `TagManager.asset` registers `PlacedCube`/`AlphaCube`/`PreviewCube` at indices 6/7/8. If the warning still appears (e.g. layers were renamed), `BuildManager` falls back to "all layers minus Ignore Raycast and PreviewCube" — gameplay continues to work, but you may want to restore the layer names.
- **Pressing Play in `FlyScene` directly with no construct.** Supported. The flight scene rebuilds whatever is in `GameData` and the alpha cube; if `GameData` is empty you fly the alpha cube alone.
