# Cube Fly, also known as Cosmic Scrap Club

A small Unity 6.3 LTS / URP demonstrator. Pick a save slot in the
hangar selector, build a cube ship, then fly it — and shoot.

> Four scenes — Main Menu → Hangar Slot Selector → Hangar (BuildScene)
> ⇄ Flight (FlyScene) — with the construct carried across the BuildScene
> ⇄ FlyScene transitions by a static `GameData` class, persisted to disk
> per slot, and a persistent `DontDestroyOnLoad` corner button that
> toggles between Hangar and Flight.

The companion documents are:

- **[`cube_fly_spec.md`](cube_fly_spec.md)** — what the project is and what it does (the canonical product spec).
- **[`full_architecture.md`](full_architecture.md)** — how it is implemented (file-by-file architecture map).
- **[`weapon_shooting_spec.md`](weapon_shooting_spec.md)** — deep dive on the Fly-mode shooting system (weapons, projectiles, crosshair).
- **[`ROADMAP.md`](ROADMAP.md)** — what is shipped and what is planned next.
- **[`thruster_boost_spec.md`](thruster_boost_spec.md)** / **[`boost_overboost_tuning_spec.md`](boost_overboost_tuning_spec.md)** — design specs for the thruster cube and its boost mechanic.

---

## What's In Here

- **Shape × Material decoupled.** Two orthogonal axes: a `ShapeRegistry` (Cube, Slope, Pyramid weapon, Cylinder weapon, Thruster utility) and a `MaterialRegistry` (A / B / C / D). Each placement records both indices. Adding a new shape or a new armour material is a data-only change.
- **Slope shape with face-validity.** Slopes only attach where they actually have a real surface — the cut-away faces refuse adjacency. Both shape and neighbour are checked, so a slope's hypotenuse can't pretend to be a square face.
- **Weapon shapes.** Pyramid (machine gun, fires bullets) and Cylinder (rocket launcher, fires two-phase rockets). Weapon shapes carry their own coupled material; the regular material flyout is suppressed for them.
- **90°-stepped per-placement rotation.** `R` rotates around Z, `T` around X. Each placement remembers its placed pose.
- **Delete tool.** Non-allocating red `MaterialPropertyBlock` hover tint plus an automatic flood-fill cleanup of any cube disconnected from the alpha cube.
- **Ship classes.** Allrounder / Tank / Scout, chosen from a dropdown in BuildScene and stored per save slot. Each sets the alpha cube's HP, the construct mass cap, and a flight movement multiplier (Allrounder 100 HP / 100 cap / ×1.0, Tank 200 / 180 / ×0.7, Scout 60 / 60 / ×1.4).
- **Mass budget (cap set by ship class).** Every placed cube has `HP / Armour / Mass` placeholder stats sourced from its `MaterialDefinition`. Going over the active class's cap rejects the placement and shows a fading red message. In flight, the construct's total mass is the `Rigidbody.mass`, so heavier ships accelerate and turn slower through real physics (`F = ma`, `τ = Iα`).
- **Live `Mass: X / cap` and `HP: Y`** readouts in the bottom-left of the build UI (the cap is the active ship class's).
- **Red arrow indicator** that auto-reparents to the frontmost cube so you can tell which way the ship is pointed.
- **Save / load to 3 slots.** The hangar slot picker shows cube count, mass, HP, and "last edited N ago" per slot. BuildScene autosaves to the armed slot on every construct change (debounced 0.25 s). Atomic on-disk writes via `File.Replace` with a rename-to-bak fallback for runtimes that don't support it.
- **ESC pause overlay.** Self-bootstrapping DDOL singleton. ESC pauses anywhere in BuildScene / FlyScene; `Menu` returns to Main Menu, `Back to Desktop` quits.
- **Rigidbody-driven flight.** The construct is a non-kinematic `Rigidbody` with the cube colliders forming a compound body. 6-axis thrust via `AddForce`, pitch / yaw / roll via `AddTorque`. Mass affects flight through real physics — heavier ships accelerate and turn slower. The ship physically bounces off the ground and world cubes instead of phasing through them.
- **Thruster cube & boost.** A placeable Utility cone thruster (the third toolbar category). Holding **Left Ctrl** while commanding thrust along a thrustered axis boosts that axis — ×1.3 acceleration and ×1.3 max-speed — draining a 0–100 Boost meter; emptying it triggers an overboost lockout until the meter recovers. A `FlyBoostBar` HUD bar (with a red critical-zone throb) shows the meter.
- **Adaptive third-person camera.** RMB free-look with snap-back; the follow speed scales with the construct's angular velocity so the camera stays glued during sharp turns.
- **HUD readouts.** Bottom-left of the Fly screen shows live `Speed` and `HP` (current / initial).
- **Shooting (Fly mode).** LMB fires every weapon of the currently selected type. Digits 1–9 and mouse wheel cycle the active type. A screen-space crosshair projects `construct.forward * 100` so the on-screen reticle and the actual aim agree.
- **Projectile hit registration.** Bullets and rockets do per-frame swept raycasts (not Unity triggers) so they don't tunnel through cubes at high speed. Self-construct hits are filtered. Damage routes through `CubeStats.TakeDamage` with the documented `effective = max(0, raw − armourValue)` formula.
- **Cube destruction & death animation.** When a cube's HP hits zero it detaches, disables its colliders, drifts outward at ~2 u/s for 2 s, then despawns. Player-construct cubes are removed from `GameData` at the same time so the mass budget and Hangar re-entry stay consistent.
- **Crash damage.** `OnCollisionEnter` on the construct's Rigidbody applies kinetic damage scaled to the normal-component impact speed. Both sides of the collision take damage. Bypasses armour because armour mitigates penetration, not raw kinetic energy.
- **End-of-run.** When the alpha cube (the construct's anchor) reaches 0 HP, a "Construct Destroyed" overlay shows and the run ends back at the main menu.
- **Basic world map.** 200×200 ground plane plus 20 rusty-orange target dummies in `FlyScene` so you have something to fly around, crash into, and shoot at.
- **File logging** to `Application.persistentDataPath/Logs/CubeFly_<timestamp>.log` alongside the Editor console.

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
persistent UI on entry. BuildScene entered this way has no armed save
slot (`GameData.ActiveSlot = -1`); autosave is disabled and a warning
is logged. To exercise the save path, start from `MainMenu` and walk
through the slot picker.

---

## Controls

### Main Menu

| Input | Action |
|-------|--------|
| `Hangar` | Open the slot picker (HangarSelect). |
| `Settings` | Placeholder. |
| `Exit` | Quit (or stop Editor play mode). |

### Hangar Slot Selector (HangarSelect)

| Input | Action |
|-------|--------|
| Card buttons | **Start new construct** (empty slot) or **Continue** (filled slot). |
| `1` / `2` / `3` | Same as clicking slot 1 / 2 / 3. |
| `Delete` (per-slot) | Inline-confirm: first click changes the label to **Yes, delete** + reveals a Cancel button. Second click commits. Auto-cancels after 5 s. |
| `Cancel` button or `Esc` | Return to Main Menu. |

### Hangar (BuildScene)

| Input | Action |
|-------|--------|
| LMB | In **Place** tool: place the currently selected shape at the preview cell. In **Delete** tool: remove the cube under the cursor. |
| `R` | Rotate the next placement 90° around Z. |
| `T` | Rotate the next placement 90° around X. |
| Toolbar (bottom) | Click a shape button to select it (auto-switches to Place). Hover a shape button (armour shape) to peek its material flyout; click a swatch to arm that (shape, material). Click **Delete** to switch to the delete tool. |
| `M` | Toggle the relevant flyout for the active shape's category — armour: per-shape material flyout; weapon: weapons flyout. |
| Digits `1`–`9` (no modifier) | Arm an **armour shape** by toolbar slot order (the on-screen left-to-right order, not the registry index). Weapons are reached through the weapons flyout. |
| `Shift`+Digit `1`–`9` | While an armour shape is active, set its **material** by registry index. |
| Right-mouse drag | Orbit the build camera. |
| Mouse wheel | Zoom the build camera. |
| `Esc` | Open the pause overlay. |
| Top-right `Fly!` button | Switch to FlyScene. |

Placement rules: each placement is only accepted when **both** sides of the
shared face are backed by a real surface — the new piece's face toward the
neighbour AND the neighbour's face toward the new cell must both be valid in
the placement's current rotation. For a cube-only construct this reduces to
the familiar "any face-adjacent cube" rule (cubes have all six faces valid).
Going over the mass cap (100) rejects the placement and pops a red "Too
much mass!" message at the top of the screen.

### Flight (FlyScene)

| Input | Action |
|-------|--------|
| `W` / `S` | Thrust forward / backward (local +Z / −Z). |
| `A` / `D` | Strafe left / right (local −X / +X). |
| `Space` / `C` | Thrust up / down (local +Y / −Y). |
| `↑` / `↓` | Pitch nose up / down (local X). |
| `←` / `→` | Yaw left / right (world Y, so it stays intuitive when pitched). |
| `Q` / `E` | Roll anti-clockwise / clockwise (local Z). |
| `Left Ctrl` (held) | Boost — on axes with a contributing thruster cube, ×1.3 acceleration and ×1.3 max-speed while the Boost meter holds out. |
| RMB (held) + mouse | Free-look. Releasing snaps the camera back behind the ship. |
| **LMB (held)** | **Fire** every weapon of the active weapon type. Per-weapon reload throttles the rate. |
| **Digits `1`–`9`** | Select weapon type by index in the bottom toolbar. |
| **Mouse wheel** | Cycle weapon type (one notch = one cycle; edge-detected so a Windows ±120 notch and a small trackpad swipe behave the same). |
| `Esc` | Open the pause overlay. |
| Top-right `Hangar` button | Switch back to BuildScene. |

Heavier ships are sluggish because the construct is a `Rigidbody`:
more mass means less acceleration for the same thrust force (`F = ma`)
and slower rotation for the same torque (`τ = Iα`). A
`rotationMassCompensation` knob keeps heavy ships turnable and a
`maxAngularSpeed` cap stops light ones from spinning out.

### Pause Overlay (BuildScene / FlyScene)

| Input | Action |
|-------|--------|
| `Esc` | Open or close the overlay. (Closing acts as Resume — no dedicated Resume button.) |
| `Menu` button | Load Main Menu. |
| `Back to Desktop` button | Quit (or stop Editor play mode). |

While open, `Time.timeScale = 0` freezes physics + FixedUpdate; the
overlay's full-screen panel intercepts pointer events so nothing
beneath it can be clicked.

---

## Project Layout (one-liners)

```
Assets/
  Scenes/         MainMenu, HangarSelect, BuildScene, FlyScene (+ template's SampleScene, unused)
  Scripts/Core/   GameData, ConstructSave, ShipClass, CubeStats, CubeDeath,
                  ShapeDefinition, ShapeRegistry,
                  MaterialDefinition, MaterialRegistry, SaveManager, PauseMenu, GameOverMenu,
                  PrimitiveMeshes, PrismMeshAuthor, PyramidMeshAuthor, CylinderMeshAuthor,
                  ThrusterMeshAuthor, UIManager, UIStyle, UIBootstrap, SceneSwitcher,
                  FileLogHandler, LogBootstrapper
  Scripts/Build/  BuildManager, CubePreview, BuildCamera, BuildToolbarController,
                  CategoryFlyout, BuildShipClassController,
                  BuildIndicatorController, PlacedCubeData
  Scripts/Fly/    FlyController, FlyCamera, FlyCrosshair,
                  FlyShootingController, FlyWeaponToolbarController,
                  FlyCrashHandler, FlySpeedIndicator, FlyHpIndicator, FlyBoostBar,
                  CubeDamage, ProjectileHit,
                  WeaponBehavior, ThrusterBehavior, PyramidWeapon, CylinderWeapon,
                  Bullet, Rocket
  Scripts/HangarSelect/ HangarSelectController
  Scripts/MainMenu/     MainMenuController
  Shapes/         ShapeRegistry + ShapeCube, ShapeSlope,
                  ShapeWeaponPyramid, ShapeWeaponCylinder, ShapeUtilityThruster
  Materials/Defs/ MaterialRegistry + MaterialA/B/C/D,
                  PyramidWeaponMatDef, CylinderWeaponMatDef, ThrusterMatDef
  Materials/      Per-prefab URP/Lit materials (AlphaCube, Placed*, Preview,
                  Bullet, Rocket, weapon-shape, ThrusterMat, AlphaCubeIndicator,
                  Ground, WorldTargetCube)
  PhysicMaterials/ GroundPhysMat, WorldTargetCubePhysMat (bounce / friction)
  Prefabs/        AlphaCube, PlacedCube[A–D], PlacedPrism, PlacedPyramid,
                  PlacedCylinder, PlacedThruster, PreviewCube, AlphaCubeIndicator,
                  Ground, WorldTargetCube
  Prefabs/Projectiles/ Bullet, Rocket
  Input/          CubeFlyInputActions (.inputactions + hand-rolled C# wrapper)
  UI/             UICanvas, UIBootstrap prefabs
  Settings/       URP render-pipeline assets
ProjectSettings/  Layers (PlacedCube/AlphaCube/PreviewCube), build list, New Input System on
Packages/         manifest.json + lockfile (the MCP package's source is git-ignored)
Logs/             Runtime log files (git-ignored)
Saves/            Per-slot ConstructSave JSON in Editor / project dev (git-ignored).
                  Built players use Application.persistentDataPath/saves/ instead.
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
- The per-slot save directory `Saves/` (Editor / project dev). Built players write to `Application.persistentDataPath/saves/` instead.

`Packages/manifest.json` and `Packages/packages-lock.json` **are**
committed so package versions stay reproducible across machines.

---

## Troubleshooting

- **Empty / invisible UI text on first open.** The project deliberately uses *legacy* `UnityEngine.UI.Text` with `LegacyRuntime.ttf` so it works without the user importing TMP Essentials. If something looks wrong, confirm `LegacyRuntime.ttf` resolved (it ships with Unity).
- **`BuildManager: Custom layers not found` warning.** The shipped `TagManager.asset` registers `PlacedCube`/`AlphaCube`/`PreviewCube` at indices 6/7/8. If the warning still appears (e.g. layers were renamed), `BuildManager` falls back to "all layers minus Ignore Raycast and PreviewCube" — gameplay continues to work, but you may want to restore the layer names.
- **`ActiveSlot is unset — autosave disabled` warning.** You pressed Play directly on `BuildScene` instead of entering through HangarSelect. Expected during dev iteration; saves are skipped for that session.
- **Pressing Play in `FlyScene` directly with no construct.** Supported. The flight scene rebuilds whatever is in `GameData` and the alpha cube; if `GameData` is empty you fly the alpha cube alone (no weapons).
