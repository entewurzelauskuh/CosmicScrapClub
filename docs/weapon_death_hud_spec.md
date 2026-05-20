# Weapon Toolbar Death Response — Design Spec

**Status:** Approved design, ready for implementation planning
**Date:** 2026-05-18
**Prerequisite:** none — independent of PR #30 and of the Boost Overboost Tuning work

## Overview

When weapon cubes on the construct are destroyed in flight, the Fly-scene
weapon toolbar should respond:

1. **Fully-dead type** — when every weapon cube of a type is destroyed, that
   type's toolbar button greys out and becomes unclickable.
2. **Partially-dead type** — when some-but-not-all cubes of a multi-instance
   type are destroyed, its button shows a small, slowly-flashing red **✕** in
   the bottom-right corner.
3. **Selection** — selection auto-switches away from a type that just fully
   died, and scroll-wheel / digit-key selection skip fully-dead types.

## Background — current systems

**Weapon grouping & selection — `FlyShootingController`.** Placed weapon cubes
are grouped by `ShapeDefinition` into `WeaponTypeGroup`s (`Types`). Each group's
`Instances` is a `List<WeaponBehavior>`. Selection state: `_selectedTypeIndex`,
with `SetSelected(i)` and `CycleSelected(delta)` (currently a plain
`mod count` wrap). Input: digit keys 1–9 (→ `SetSelected`), mouse scroll
(→ `CycleSelected`). `HandleFireInput` fires every `Instances` entry of the
selected group each frame Fire is held.

**Toolbar — `FlyWeaponToolbarController`.** Renders one button per
`WeaponTypeGroup`, with a reload bar above and a material swatch in the
top-right corner of each. Subscribes to `TypesChanged` (→ `RebuildButtons`) and
`SelectedChanged` (→ `ApplySelectedHighlight`, which paints button backgrounds).
Runs a per-frame `Update` refreshing reload-bar fill. Buttons are built by
`UIStyle.BuildLabeledButton`. No button is ever disabled today.

**Cube health & death.** Each weapon cube GameObject carries a `WeaponBehavior`
plus a sibling `CubeStats` (`healthPoints`). When HP reaches 0,
`CubeDamage.ApplyAndLog` triggers `CubeDeath.BeginDeath` — which detaches the
cube, disables its colliders, drifts it for ≈2 s, then destroys it. **There is
no cube-death event.** The dead `WeaponBehavior` stays in
`WeaponTypeGroup.Instances`: during the drift it is a live object with
`healthPoints == 0`; after destruction it becomes a Unity-null reference.

## Approach — polling

The toolbar and the selection logic both need to know how many instances of
each type are alive. Rather than add a death event to the damage pipeline,
`WeaponTypeGroup` exposes alive-counts computed on demand, and both consumers
poll them per-frame. The toolbar already runs a per-frame `Update`;
`FlyHpIndicator` already polls cube HP the same way. This needs **no changes**
to `CubeStats` / `CubeDamage` / `CubeDeath`, and it naturally supports future
repair stations (HP recovers → counts recover, with no extra wiring).

## Design

### Data model

**`WeaponBehavior`** — resolves and caches its sibling `CubeStats` on first
access (the lazy-cache pattern already used by `ThrusterBehavior.LocalThrustAxis`)
and exposes:

- `IsAlive` — `true` when the cached `CubeStats.healthPoints > 0`.

**`WeaponTypeGroup`** (defined in `FlyShootingController.cs`) — three computed
accessors:

- `AliveCount` — number of `Instances` that are non-null (Unity-null check —
  excludes destroyed cubes) and `IsAlive`.
- `IsFullyDead` — has at least one instance and `AliveCount == 0`.
- `IsPartiallyDead` — has more than one instance and
  `0 < AliveCount < Instances.Count`.

Each group always has ≥1 instance — `RegisterWeapons` only creates a group when
it has a member.

### Toolbar — `FlyWeaponToolbarController`

**Death-mark (✕) construction.** In `RebuildButtons`, per button, build a `Text`
child anchored to the button's bottom-right corner (mirroring the existing
top-right swatch): pivot/anchors `(1, 0)`, a small inset offset, a bold red "✕"
glyph, `raycastTarget = false`, disabled by default. Track them in a new
`_deathMarks` array, allocated and cleared alongside `_buttons` /
`_buttonBackgrounds` / `_reloadBars` / `_swatches`.

**Per-frame `RefreshWeaponStates()`** — called from `Update`, beside the
existing reload-bar loop. For each button index `i` with `group = Types[i]`:

- **Interactable** — `_buttons[i].interactable = !group.IsFullyDead`.
- **Background color** — a single consolidated rule with priority
  **dead > selected > idle**: fully-dead → `deadColor` (grey); else the selected
  index → `SelectedTypeColor`; else → `UIStyle.BackgroundIdle`.
  `RefreshWeaponStates` becomes the sole owner of button background color,
  replacing the `ApplySelectedHighlight`-only logic.
- **✕ visibility** — `_deathMarks[i].enabled = group.IsPartiallyDead`. While
  enabled, the mark's alpha pulses on a slow sine cycle (`deathMarkPulseSeconds`,
  default ≈1.0 s) between `deathMarkAlphaMin` and 1.0, driven by
  `Time.unscaledTime`.

**Button color transition.** To stop Unity's `Button` `ColorTint` transition
from fighting the manually-painted background, build buttons with
`transition = None`; the toolbar owns all coloring. `interactable = false` still
blocks clicks and raycasts regardless of transition mode.

`OnSelectedChanged` / `ApplySelectedHighlight` are no longer the owner of
background color — the selection highlight falls out of the consolidated rule in
`RefreshWeaponStates`. The `SelectedChanged` subscription may be dropped or kept
purely as a no-op trigger; the per-frame refresh is authoritative.

### Selection — `FlyShootingController`

Implementing the chosen "auto-switch + skip dead" behavior:

- **`SetSelected(int i)`** — add a guard: if `_types[i].IsFullyDead`, return
  without changing selection. This centralises the "cannot select a dead type"
  rule for digit keys and button clicks. (Auto-switch calls `SetSelected` with a
  live index, so the guard never blocks it.)
- **`CycleSelected(int delta)`** — step in `delta`'s direction past any
  `IsFullyDead` groups to the next live one (scan up to `Types.Count` steps). If
  no live type exists, leave selection unchanged.
- **Auto-switch** — a new per-frame check in `Update`: if `SelectedType` is
  non-null and `IsFullyDead`, switch to the nearest live type (reusing the
  cycle/scan logic). If no live type remains, selection stays as-is — the player
  simply cannot fire. This check runs after the pause and `HasWeapons` gates but
  is **not** gated by the pointer-over-UI early-return: a weapon dying must
  switch selection regardless of cursor position.
- **`HandleFireInput`** — gate firing on liveness:
  `if (w != null && w.IsAlive) w.TryFire(target)`. Prevents a dead weapon cube
  from continuing to fire during its ≈2 s death drift.

### New serialized fields

`FlyWeaponToolbarController`:

- `deadColor` — Color, default a desaturated grey
- `deathMarkColor` — Color, default red
- `deathMarkSize` — Vector2, default ≈16×16
- `deathMarkPulseSeconds` — float, default 1.0
- `deathMarkAlphaMin` — float, default 0.25

The `FlyWeaponToolbarController` MonoBehaviour block in `FlyScene.unity` is
updated to carry the new fields at their defaults.

## Files touched

- `Assets/Scripts/Fly/WeaponBehavior.cs` — cached `CubeStats`, `IsAlive`.
- `Assets/Scripts/Fly/FlyShootingController.cs` — `WeaponTypeGroup` alive-count
  accessors; `SetSelected` guard; `CycleSelected` skip; auto-switch in `Update`;
  `HandleFireInput` liveness gate.
- `Assets/Scripts/Fly/FlyWeaponToolbarController.cs` — `_deathMarks`,
  `RefreshWeaponStates`, consolidated background coloring, `transition = None`,
  new serialized fields.
- `Assets/Scenes/FlyScene.unity` — new serialized field values.

No prefab changes. No changes to `CubeStats` / `CubeDamage` / `CubeDeath`.

## Verification

FlyScene play-test with a construct carrying both a single-instance weapon type
and a multi-instance one (≥2 cubes of one weapon):

- Destroy one cube of the multi-instance type → its button shows the
  slowly-flashing red ✕; the button is still usable.
- Destroy the rest → the button greys and becomes unclickable, the ✕
  disappears; if it was the selected type, selection auto-switched to a live type.
- Destroy the single-instance type → its button greys (no ✕ — single instance).
- Scroll-wheel cycling and digit keys skip the greyed type.
- A weapon cube does not fire during its death-drift.
- Compile clean, no console errors.

## Scope

Independent of PR #30 and of the Boost Overboost Tuning PR — branches off
`main`, no shared files.

**Out of scope:** repair stations (ROADMAP "Ideas") — a future repair would
restore cube HP, and polling makes the button un-grey / the ✕ clear
automatically, but the repair feature itself is not part of this work. Removing
dead `WeaponBehavior`s from `Instances` is unnecessary — the polling accessors
handle null/destroyed entries.
