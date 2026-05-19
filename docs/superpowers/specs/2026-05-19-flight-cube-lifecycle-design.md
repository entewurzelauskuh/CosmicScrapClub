# Flight Cube Lifecycle — Design Spec

**Status:** Approved design, ready for implementation planning
**Date:** 2026-05-19
**Branch:** `feat/flight-cube-lifecycle` off `main`

## Overview

Three related changes to how construct cubes behave during and after a Fly
session, bundled into one working package:

1. **Issue 1 — Hangar reset.** Pressing the Fly-scene "Hangar" button must
   re-enter BuildScene with the *saved* construct, not the one damaged in
   flight.
2. **Issue 2 — flight-side cascade cleanup.** When a cube that connects
   others to the alpha is destroyed in flight, the now-disconnected cubes
   must also be destroyed — mirroring the BuildScene delete tool — and the
   construct's mass / inertia recomputed.
3. **Spec B — weapon death HUD.** The Fly-scene weapon toolbar responds to
   weapon-cube destruction. Designed separately in `weapon_death_hud_spec.md`
   (already approved); included here only for bundling and one cross-item
   note.

The three ship as separate commits in one PR.

## Background — current systems

`GameData` (static, `Assets/Scripts/Core/GameData.cs`) is the canonical
in-memory construct: a `List<Placement>` plus a `Dictionary<Vector3Int,
Placement>`, persisting across scene loads. It is populated by HangarSelect
(`LoadFromSave`) and edited by BuildScene; FlyScene's
`FlyController.BuildConstruct` reads it to spawn the flyable construct.

`SaveManager` writes `GameData` to one of three slot files
(`slot{N}.json`). BuildScene autosaves to `GameData.ActiveSlot` on every
edit; FlyScene never writes to disk.

`SceneSwitcher.Toggle()` is the sole Build↔Fly transition — wired to the
single corner button owned by the `DontDestroyOnLoad` `UIManager`
("Fly!" in BuildScene, "Hangar" in FlyScene).

In flight, a cube reaching 0 HP routes through `CubeDamage.ApplyAndLog`,
which for a player-construct cube calls `GameData.Remove(cell)`, kicks off
`CubeDeath.BeginDeath` (detach → disable colliders → drift ≈2 s → destroy),
and raises `CubeDeath.CubeDied`. `FlyController.OnCubeDied` handles that
event by calling `ResolveRigidbody` (recomputes `Rigidbody.mass` and the
mass-derived flight factors).

`BuildManager.RemoveDanglingCubes` already does a face-aware flood-fill
(BFS from the origin across `GameData.HasFaceConnection` edges) to delete
cubes orphaned by a delete-tool removal.

## Issue 1 — Hangar reset

### Problem

`CubeDamage` mutates the static `GameData` when cubes die in flight. On
Hangar → BuildScene, `BuildManager` reinstantiates from that mutated
`GameData` and shows the damaged construct. Worse, the next BuildScene edit
autosaves the damaged construct over the slot file. The slot file itself is
correct until then — FlyScene never writes to disk.

### Design — snapshot & restore

`GameData` captures the construct when a Fly session begins and restores it
when the session ends:

- `static List<Placement> _flightSnapshot` — null when no snapshot is held.
- `static ShipClass _flightSnapshotShipClass` — the class captured alongside.
- `public static void CaptureFlightSnapshot()` — copies `_placedCubes` into
  a fresh `_flightSnapshot` list and records `ActiveShipClass`. Logs the
  cube count.
- `public static void RestoreFlightSnapshot()` — if `_flightSnapshot` is
  null, logs "no snapshot" and returns (**guard: never wipe the
  construct**). Otherwise clears `_placedCubes` / `_byCell`, replays the
  snapshot placements into both directly (no `TryAdd` — bypasses
  validation/logging), restores `ActiveShipClass`, and sets
  `_flightSnapshot = null` so the snapshot is consumed.

`SceneSwitcher.Toggle()` drives the pairing:

```csharp
if (current == BuildSceneName)    GameData.CaptureFlightSnapshot();
else if (current == FlySceneName) GameData.RestoreFlightSnapshot();
```

Build→Fly captures the pristine construct; Fly→Build restores it before
BuildScene loads. Because BuildScene only ever sees the restored
`GameData`, its autosave can no longer write a damaged construct to disk.

### Edge cases

- **Dev "Play directly on FlyScene":** no Build→Fly toggle ran, so
  `_flightSnapshot` is null; `RestoreFlightSnapshot` no-ops. The dev path
  keeps showing the in-memory construct — acceptable, non-catastrophic.
- **Alpha dies → GameOverMenu → MainMenu:** no Fly→Build toggle, so the
  snapshot is never restored; it goes stale but is harmless — the next
  BuildScene entry is via HangarSelect, which overwrites `GameData`
  wholesale (`LoadFromSave` / `Clear`).
- **Repeated Build→Fly→Build:** each Build→Fly re-captures (GameData is
  pristine, the prior restore fixed it); each Fly→Build restores + consumes.

### Files

- `Assets/Scripts/Core/GameData.cs` — snapshot fields + the two methods.
- `Assets/Scripts/Core/SceneSwitcher.cs` — capture/restore calls in `Toggle`.

No scene or prefab changes.

## Issue 2 — flight-side cascade cleanup

### Problem

When a cube that bridges others to the alpha is destroyed in flight,
`CubeDamage` removes only that cube. Cubes behind it stay parented to the
construct — graph-disconnected from the alpha but still flying along. The
BuildScene delete tool already handles the equivalent case via
`RemoveDanglingCubes`; FlyScene has no equivalent.

### Design

**Shared flood-fill.** Extract the BFS from
`BuildManager.RemoveDanglingCubes` into `GameData`:

- `public static HashSet<Vector3Int> GetCellsConnectedToOrigin(
  ShapeRegistry shapeRegistry)` — BFS from the origin (the alpha) across
  `HasFaceConnection` edges; returns the visited set (origin included).

`BuildManager.RemoveDanglingCubes` is refactored to call this for the
visited set; its snapshot + `RemoveCell` loop (which also destroys the
`_spawned` GameObjects) is unchanged. Pure DRY — build-side behaviour is
identical.

**FlyScene cascade.** `FlyController.OnCubeDied()` — today just
`ResolveRigidbody()` — gains the cascade. By the time `CubeDied` fires, the
dead cube is already out of `GameData`, so:

1. `connected = GameData.GetCellsConnectedToOrigin(shapeRegistry)`.
2. Collect every `GameData.PlacedCubes` cell **not** in `connected` into an
   orphan-cell list (collected before mutating `GameData`).
3. For each orphan cell:
   - `GameData.Remove(cell)`.
   - Find its cube GameObject — one `GetComponentsInChildren<PlacedCubeData>()`
     pass on `construct`, matched by `cell`. (Cubes already dying are
     reparented out of `construct` by `CubeDeath`, so they are not matched.)
   - On the GameObject: set `CubeStats.healthPoints = 0`, then run
     `CubeDeath` — `GetComponent<CubeDeath>() ?? AddComponent<CubeDeath>()`,
     then `BeginDeath(construct.position)`. Same drift-outward animation as
     a shot-down cube.
4. `ResolveRigidbody()` once — recomputes `Rigidbody.mass` and rebuilds the
   inertia tensor for the lighter construct.

### Why it is safe

- The cascade kills orphans by calling `CubeDeath.BeginDeath` *directly*.
  `BeginDeath` does not raise `CubeDied` (only `CubeDamage` does), so the
  handler does not re-enter — **no recursion**.
- A single flood-fill finds *all* orphans at once, transitively, regardless
  of how long the disconnected chain is.
- `ResolveRigidbody` runs exactly once per `OnCubeDied`, whether or not
  there were orphans (preserves the existing F2 mass-recompute behaviour).
- Zeroing each orphan's `healthPoints` means Spec B's liveness polling sees
  an orphaned weapon as dead immediately.

### Files

- `Assets/Scripts/Core/GameData.cs` — `GetCellsConnectedToOrigin`.
- `Assets/Scripts/Build/BuildManager.cs` — `RemoveDanglingCubes` refactor.
- `Assets/Scripts/Fly/FlyController.cs` — `OnCubeDied` cascade.

No scene or prefab changes.

### Out of scope

- Game-over when the cascade strips the construct down to the bare alpha —
  an alpha-only construct stays flyable, as today.
- Orphan cubes deal no collision damage as they drift — `CubeDeath` disables
  their colliders, same as any dying cube.

## Spec B — weapon death HUD

Designed in full in `weapon_death_hud_spec.md` (status: approved). No design
changes here. Implemented as that spec specifies: `WeaponBehavior.IsAlive`,
`WeaponTypeGroup` alive-count accessors, the toolbar death-mark ✕ /
grey-out / consolidated coloring, and selection auto-switch + skip-dead.

**Cross-item note:** Issue 2 zeroes an orphaned cube's `healthPoints`, so an
orphaned weapon greys out through Spec B's normal polling — no extra wiring
between the two.

### Files

Per `weapon_death_hud_spec.md`: `WeaponBehavior.cs`,
`FlyShootingController.cs`, `FlyWeaponToolbarController.cs`,
`FlyScene.unity`.

## Delivery

- **Branch:** `feat/flight-cube-lifecycle` off `main`.
- **Commits**, in dependency order: design doc → Issue 1 → Issue 2 → Spec B.
- **One PR** for all three, Copilot review requested.

## Verification

No automated test framework in this project — verification is the Unity
compile-check (`refresh_unity` + `read_console`) plus FlyScene play-tests:

- **Issue 1:** fly a multi-cube construct, take cube losses (fly through the
  AutoTurrets' line of fire), press Hangar → BuildScene shows the full
  original construct, mass/HP readout intact. Edit + re-fly still works.
- **Issue 2:** build a construct with a single "bridge" cube linking a tail
  section to the alpha; fly; destroy the bridge → the tail cubes drift away
  with the CubeDeath animation; the cascade is logged; mass/inertia
  recompute (logged by `ResolveRigidbody`).
- **Spec B:** per the verification section of `weapon_death_hud_spec.md`.
- All: compile clean, no console errors (MCP bridge "Client handler exited"
  lines are not errors).

## Files touched (all three items)

- `Assets/Scripts/Core/GameData.cs`
- `Assets/Scripts/Core/SceneSwitcher.cs`
- `Assets/Scripts/Build/BuildManager.cs`
- `Assets/Scripts/Fly/FlyController.cs`
- `Assets/Scripts/Fly/WeaponBehavior.cs`
- `Assets/Scripts/Fly/FlyShootingController.cs`
- `Assets/Scripts/Fly/FlyWeaponToolbarController.cs`
- `Assets/Scenes/FlyScene.unity`
