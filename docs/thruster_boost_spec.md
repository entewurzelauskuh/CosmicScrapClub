# Thruster Cube & Boost — Design Spec

Planning doc for the **Thruster cube** feature (`ROADMAP.md` → *Flight &
Movement*). Companion to `weapon_shooting_spec.md` — a feature deep-dive,
not the canonical product spec (`cube_fly_spec.md`).

Status: **planning**. Implementation split into two PRs (see §8).

---

## 1. Overview

The Thruster is the first **Utility** shape — a placeable that is
neither armour nor weapon. Geometrically it is a **cone**: a pyramid
with a circular base instead of a square one. The circular base is the
**placement face** (it attaches to the rest of the construct); the
apex / tip is the **exhaust nozzle**. Thrust acts in the direction
*opposite* the tip — i.e. out through the placement face.

In flight, holding **Left Ctrl** while commanding movement along a
direction that has thrusters aimed to push that way triggers a
**Boost**: that axis's acceleration is multiplied ×1.3 and the
construct's max-speed cap is multiplied ×1.3, for as long as the
**Boost** resource (a 0–100 meter) holds out.

Delivered in two PRs:

- **PR 1 — Thruster placeable.** Build-side only: the cone shape, the
  new "Utilities" toolbar category, the placeable prefab + assets.
  After PR 1 you can place thrusters in the hangar; they are inert in
  flight.
- **PR 2 — Boost mechanic.** Fly-side: the Boost resource, the
  Left-Ctrl input, the flight-force integration, and the HUD bar.

---

## 2. The Thruster shape

### 2.1 Geometry — `PrimitiveMeshes.Cone`

A new runtime-generated mesh alongside `TriangularPrism`,
`SquarePyramid`, `HollowCylinder`:

- Fits a 1×1×1 grid cell. Circular base at `y = −0.5`, radius `0.5`.
  Apex at `(0, +0.5, 0)`.
- `N = 24` segments around the circle.
- **Base**: a triangle fan — one centre vertex at `(0, −0.5, 0)` plus
  the `N` rim vertices → `N` triangles, wound for a `−Y` outward
  normal.
- **Sides**: `N` triangles, each `(rim[i], rim[i+1], apex)`, wound for
  outward-facing normals.
- Vertices duplicated per face so `RecalculateNormals` gives the base
  a flat `−Y` normal and the sides their own outward normals.
- Cached + shared via `MeshFilter.sharedMesh`, same as the other
  primitives.

### 2.2 `ShapeUtilityThruster.asset`

A new `ShapeDefinition` in `Assets/Shapes/`:

- `category = Utility` (the new category — see §3).
- `displayName = "Thruster"`.
- Valid attachment faces: **`faceNegY` only** — the circular base.
  All five other faces false. Identical face-validity to
  `ShapeWeaponPyramid` (apex-up cone, base-down).
- `coupledMaterial` → `ThrusterMatDef`.
- `prefab` → `PlacedThruster.prefab`.

Registered in `ShapeRegistry.asset` after the weapon shapes.

### 2.3 `PlacedThruster.prefab`

- `MeshFilter` with an **empty** mesh slot — `ThrusterMeshAuthor`
  populates it from `PrimitiveMeshes.Cone` at `Awake` (same pattern as
  `PlacedCylinder` + `CylinderMeshAuthor`).
- `BoxCollider` — cell-bounds, like the prism / pyramid / cylinder
  prefabs (cheap and correct for the grid; not a mesh collider).
- `CubeStats` — populated at spawn by `ThrusterMatDef.ApplyTo`.
- `ThrusterMeshAuthor`.
- `ThrusterBehavior` — **added in PR 2**, not PR 1 (the script doesn't
  exist until then).
- Layer `PlacedCube`.

### 2.4 `ThrusterMeshAuthor.cs`

Mirror of `PyramidMeshAuthor` / `CylinderMeshAuthor`: on `Awake`,
assigns `PrimitiveMeshes.Cone` to the `MeshFilter` (and `MeshCollider`
if one is present) **only when the slot is empty**, so a hand-authored
mesh would take precedence.

### 2.5 Materials

- **`ThrusterMatDef.asset`** (`Assets/Materials/Defs/`) — the coupled
  `MaterialDefinition`. HP / AV / Mass identical to the pyramid
  weapon's `PyramidWeaponMatDef` ("same in-game statistics … like the
  Pyramid").
- **`ThrusterMat.mat`** (`Assets/Materials/`) — URP/Lit renderer
  material. Proposed colour: a cool blue-grey (engine-like), distinct
  from the rusty weapon materials and the A/B/C/D armour palette.

---

## 3. `ShapeCategory.Utility`

`ShapeCategory` gains a third member: `{ Armour, Weapon, Utility }`.

| Category | Material source | Toolbar |
|---|---|---|
| Armour | `MaterialRegistry` (A/B/C/D), per-shape memory | One button per shape + material flyout |
| Weapon | coupled `coupledMaterial` | One "Weapons" button + category flyout |
| Utility | coupled `coupledMaterial` | One "Utilities" button + category flyout |

### 3.1 The `coupledMaterial` refactor

`ShapeDefinition.weaponMaterial` is renamed → **`coupledMaterial`**.
Both Weapon and Utility shapes use it; the name shouldn't say "weapon".

Add `[UnityEngine.Serialization.FormerlySerializedAs("weaponMaterial")]`
to the renamed field so the existing `ShapeWeaponPyramid.asset` and
`ShapeWeaponCylinder.asset` migrate their serialized references
automatically on import — **no manual YAML edit, no broken refs**.
(Unity rewrites those `.asset` files on first import; they will show as
changed in PR 1's diff.)

### 3.2 `ResolveMaterial` and helpers

```
ResolveMaterial(index, registry):
    category == Armour  → registry.Get(index)
    otherwise           → coupledMaterial
```

- `IsWeapon` stays — `category == Weapon`.
- New `IsArmour` — `category == Armour`.
- New `UsesCoupledMaterial` — `category != Armour`. The toolbar and the
  save layer group on this rather than `IsWeapon`.

### 3.3 Save layer

`GameData`'s save/load path special-cases coupled-material shapes (it
stores / resolves their material via the shape, with a `−1` material
index sentinel, instead of a `MaterialRegistry` name lookup). That
check currently uses `IsWeapon`; it must become `UsesCoupledMaterial`
so Utility shapes are handled identically. A saved thruster therefore
round-trips by shape name with no material lookup — same as a weapon.

---

## 4. Build toolbar — the "Utilities" category

Today the toolbar is `[Cube] [Slope] [Weapons ▸] [Delete]` — Cube and
Slope are individual armour buttons; Weapons is one button collapsing
all weapon shapes behind a flyout.

Target: `[Cube] [Slope] [Weapons ▸] [Utilities ▸] [Delete]`.

### 4.1 Generalize the category flyout

The Weapons-button machinery in `BuildToolbarController` (`_weaponsButton`,
`_weaponsBackground`, `_weaponsSwatch`, `_weaponShapeIndices`,
`_weaponsFlyout`, `_weaponsFlyoutGroup`, `_weaponsFlyoutButtons`,
`_weaponsFlyoutBackgrounds`, `_weaponsFlyoutPinned`, `_weaponsPeekRoutine`,
`_lastArmedWeaponIndex`, and their peek / pin / Esc-close / M-toggle
logic) is refactored into a reusable **`CategoryFlyout`** unit,
instantiated once per non-armour category.

A `CategoryFlyout` owns: the toolbar button + its swatch, the flyout
panel + entry buttons + backgrounds, the peek-on-hover / click-to-pin /
Esc-close state, and the last-armed-shape memory for that category.

The toolbar build sequence becomes: armour shape buttons (unchanged) →
one `CategoryFlyout` per non-armour category present in the registry
(Weapons, then Utilities) → Delete button.

`M` toggles the flyout of the active shape's category. Digit keys and
Shift+digit are unchanged (armour shapes / armour materials only).

This is a refactor of a ~1000-line file — the riskiest part of PR 1.
The payoff: no copy-paste, and the future **Power** blocks drop into
the Utilities flyout as data-only additions.

---

## 5. The Boost resource

A single `float` in `[0, 100]`. It is the inverse-shaped sibling of the
(future) laser **Heat** meter: Heat starts at 0 and rises with use;
Boost starts at 100 and falls with use.

| Parameter | Starting value | Notes |
|---|---|---|
| Max | 100 | Boost begins each Fly session full. |
| Drain | 40 / sec | While *actively boosting* (see §6.2). |
| Regen — normal | 15 / sec | When not boosting and not overboosted. |
| Regen — overboosted | 6 / sec | The slow recovery rate. |
| Overboosted entry | boost reaches 0 | |
| Overboosted exit | boost regenerates back to 100 | Full recovery required. |

- **Overboosted** disables boosting entirely; `"Overboosted!"` flashes
  3× near the crosshair (mirrors the planned `"Overheated!"`).
- Overboosted clears only at boost == 100 — you over-extended, now wait
  it out. Same "punish the over-use" shape the laser heat will use.
- All starting values are tuning placeholders; expect in-editor
  iteration.

---

## 6. Boost activation & flight integration

### 6.1 Thruster thrust direction

A thruster's exhaust points along its local `+Y` (cone apex,
`transform.up`). Thrust is the opposite: **`−transform.up`**.

Placements are 90°-stepped, so a thruster's thrust direction is exactly
one of the construct's six local axes (`±X / ±Y / ±Z`).

`ThrusterBehavior` (on `PlacedThruster.prefab`, added PR 2) is
collected by `FlyController.BuildConstruct` into a list — the same
pattern as `_spawnedWeapons`. Each exposes its thrust direction in the
**construct's local frame**, snapped to one of the six axes.

### 6.2 Activation rule (evaluated per `FixedUpdate`)

A thruster contributes this frame **iff all three hold**:

1. **Left Ctrl** is held (`Boost` input action).
2. Boost resource `> 0` and not overboosted.
3. The player is commanding movement along that thruster's thrust axis
   — the matching component of `_thrustInput` is non-zero and the
   **same sign** as the thrust direction.

This is the Space-Engineers rule: a thruster fires only when you
actually command thrust the way it pushes. Left Ctrl with no matching
directional input does nothing and drains no boost.

### 6.3 Effect

- **Per-axis acceleration ×1.3 (flat).** For each of the 6 input axes:
  if ≥1 thruster contributes on that axis, that axis's thrust force is
  multiplied ×1.3. Flat — the number of aligned thrusters does *not*
  matter (decision: dropped the "more thrusters = more accel"
  stacking).
- **Max-speed lift ×1.3.** While ≥1 thruster contributes anywhere, the
  construct is *actively boosting*: the linear-velocity clamp ceiling
  rises to `maxSpeed × 1.3`. When boosting ends, the ceiling returns to
  `maxSpeed` and any excess speed is eased back down — fast, but not an
  instant snap (see §6.4).
- **Boost drains** at 40/sec while actively boosting; otherwise it
  regenerates (§5).

### 6.4 `FlyController` integration

`FixedUpdate` already assembles `worldThrust` from `_thrustInput` and
applies it via `AddForce`. The changes:

- Determine, per axis, whether a thruster is contributing — scale that
  axis's component of the thrust vector ×1.3 before the `AddForce`.
- The max-speed clamp reads `boosting ? maxSpeed * 1.3 : maxSpeed`.
- Tick Boost drain / regen and the overboosted state machine.

The Boost resource lives on `FlyController` (flight is intrinsic to it)
and is exposed read-only — `BoostFraction` (0–1) and `IsOverboosted` —
for the HUD bar.

**Post-boost over-cap decay.** While boosting, the velocity-clamp
ceiling is `maxSpeed × 1.3`. When boosting ends, the ceiling drops to
`maxSpeed`, but excess velocity is **not** hard-snapped down — the
construct's speed is eased toward `maxSpeed` at a fast over-cap decay
rate (a serialized `overCapDecaySpeed`, tuned so the drop reads as
quick but not abrupt — a fraction of a second from `maxSpeed × 1.3`
back to `maxSpeed`). The hard clamp still applies as a true ceiling at
the *active* cap (so thrust can't push past `maxSpeed × 1.3` while
boosting, or past `maxSpeed` otherwise); the smooth decay only governs
the transition from boosted speed back down to normal speed.

---

## 7. HUD — crosshair-flanking resource bars

Two thin **vertical bars** flanking the crosshair: **Boost on the
left**, **Heat on the right**.

- **Fill** shows the resource level — Boost: `fill = boost / 100`;
  Heat: `fill = heat / 100`.
- **Opacity ramps with use.** At rest each bar is near-invisible; in
  use it fades toward opaque:
  - Boost: `alpha = 1 − boost/100` — full (100, unused) → transparent;
    drained → opaque.
  - Heat: `alpha = heat/100` — cold (0, unused) → transparent; hot →
    opaque.
- **Screen-fixed near screen centre** — NOT parented to the dynamic
  crosshair reticle. The reticle drifts as the construct turns (it
  tracks `construct.forward`); resource bars that jump around with it
  would be unreadable. The bars sit at fixed offsets left/right of
  screen centre.
- `"Overboosted!"` / `"Overheated!"` flash text appears near the
  crosshair, 3×.

PR 2 implements the **Boost bar** (left). The **Heat bar** (right) is
built and wired with the **Laser Weapon** roadmap item — see §9.

---

## 8. Files

### PR 1 — Thruster placeable

**New:**
- `Assets/Scripts/Core/ThrusterMeshAuthor.cs` (+ `.meta`)
- `Assets/Shapes/ShapeUtilityThruster.asset` (+ `.meta`)
- `Assets/Materials/Defs/ThrusterMatDef.asset` (+ `.meta`)
- `Assets/Materials/ThrusterMat.mat` (+ `.meta`)
- `Assets/Prefabs/PlacedThruster.prefab` (+ `.meta`)

**Modified:**
- `Assets/Scripts/Core/PrimitiveMeshes.cs` — add `Cone`.
- `Assets/Scripts/Core/ShapeDefinition.cs` — `Utility` category;
  `weaponMaterial` → `coupledMaterial` + `[FormerlySerializedAs]`;
  `ResolveMaterial`; `IsArmour` / `UsesCoupledMaterial` helpers.
- `Assets/Scripts/Core/GameData.cs` — save/load coupled-material path
  `IsWeapon` → `UsesCoupledMaterial`.
- `Assets/Scripts/Build/BuildToolbarController.cs` — generalize the
  category flyout, add the Utilities category.
- `Assets/Shapes/ShapeRegistry.asset` — register `ShapeUtilityThruster`.
- `Assets/Shapes/ShapeWeaponPyramid.asset`,
  `ShapeWeaponCylinder.asset` — auto-migrated field rename
  (Unity-rewritten on import).

### PR 2 — Boost mechanic

**New:**
- `Assets/Scripts/Fly/ThrusterBehavior.cs` (+ `.meta`)
- `Assets/Scripts/Fly/FlyBoostBar.cs` (+ `.meta`)

**Modified:**
- `Assets/Scripts/Input/CubeFlyInputActions.cs` — new `Boost` action,
  bound to Left Ctrl.
- `Assets/Scripts/Fly/FlyController.cs` — collect `ThrusterBehavior`s;
  Boost resource + drain/regen/overboosted state; per-axis ×1.3;
  max-speed lift.
- `Assets/Prefabs/PlacedThruster.prefab` — add `ThrusterBehavior`.
- `Assets/Scenes/FlyScene.unity` — add `FlyBoostBar` to `FlyHUD`.

---

## 9. Open questions / deferred

- **Heat bar timing.** This spec designs both crosshair bars together
  so the layout is settled once. PR 2 implements only the **Boost**
  bar; the **Heat** bar lands with the Laser Weapon roadmap item.
  Alternative: build an inert Heat bar now (permanently transparent).
  Recommendation: **defer** — an always-invisible bar adds nothing and
  the alpha curve can't be verified without a heat resource. Flag if
  you'd rather reserve the inert slot in PR 2.
- **Boost tuning** — §5 values, and the `overCapDecaySpeed` of §6.4,
  are starting points; expect in-editor iteration.

---

## 10. Test plan

### PR 1
- Build scene: a "Utilities" button sits in the toolbar after
  "Weapons"; its flyout lists "Thruster".
- Placing a Thruster: cone renders, attaches only on its circular base
  face, carries HP/AV/Mass from `ThrusterMatDef`.
- Weapons flyout still works (the generalized flyout didn't regress
  it); armour buttons + material flyouts + digit keys unchanged.
- A saved construct with a thruster round-trips (save → reload).
- Existing weapon shapes still resolve their coupled material (the
  `FormerlySerializedAs` migration worked).

### PR 2
- Place a thruster with its tip pointing backward (thrust = forward).
  Fly forward (`W`), hold Left Ctrl → noticeable acceleration boost,
  speed climbs past the normal cap.
- Boost bar (left of crosshair) fades in as boost drains; invisible at
  full boost.
- Hold boost to 0 → `"Overboosted!"` flashes 3×, boosting cuts out,
  boost regenerates slowly until full.
- Left Ctrl with no directional input, or input with no aligned
  thruster → no boost, no drain.
- Thrusters work on all 6 axes (forward/back, strafe, vertical).
- Releasing Ctrl reverts the max-speed cap.
