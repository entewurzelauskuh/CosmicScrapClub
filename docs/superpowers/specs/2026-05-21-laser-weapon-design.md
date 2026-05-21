# Laser Weapon — Design Spec

**Status:** Approved design, ready for implementation planning
**Date:** 2026-05-21
**Branch:** `feat/laser-weapon` off `main`

## Overview

Phase 2 / item 2 of the Up Next timeline (`../../../ROADMAP.md` → *Up Next*).
The first **energy-type weapon** and the second power consumer — the
testbed for the projectile-vs-energy damage split landed in Power & Energy.

A laser cube fires a **continuous hitscan beam** along its fixed barrel
axis while LMB is held: a per-frame raycast from the barrel that deals
**energy** damage to the first cube it hits, drives a `LineRenderer` beam,
and is gated by two resources instead of a reload cooldown — a **heat**
value (shared per laser weapon-type) and **power** (drawn per-cube from the
`ConstructEnergySystem`). Short controlled bursts are sustainable; a held
beam overheats and locks out.

It slots into the existing weapon pipeline rather than a parallel system:
`LaserWeapon : WeaponBehavior`, dispatched by the same
`FlyShootingController` select-and-fire loop that already fires every cube
of the selected type each frame LMB is held.

## Background — what already exists

- **The weapon pipeline.** `WeaponBehavior` (abstract) owns a reload
  cooldown + `TryFire(target)`; `FlyShootingController` each frame Fire is
  held calls `TryFire` on every weapon of the selected type, grouped into
  `WeaponTypeGroup`s (one per `ShapeDefinition`). Concrete weapons
  (`PyramidWeapon`, `CylinderWeapon`) override `Fire` to spawn a projectile
  along `transform.up` (the `−Y`-mount / `+Y`-barrel convention).
- **The damage chokepoint.** `CubeDamage.ApplyAndLog(in HitContext)` — a
  `HitContext` carries `DamageType` (`Projectile` / `Energy` / `Kinetic`).
  The shield (`ConstructEnergySystem.ApplyToShield`) already applies the
  **×1.1 energy modifier** and overflow-to-HP; energy damage needs no new
  shield plumbing.
- **The hitscan helper.** `ProjectileHit.TrySweep(prev, curr, construct,
  out hit)` does a self-construct-filtered `Physics.RaycastNonAlloc` and
  returns the first non-self hit — reusable for the beam's per-frame
  raycast.
- **Power.** `ConstructEnergySystem` owns the net-rate power balance + the
  consumer cascade (shield kept, **weapons cut first**). The laser was
  designed in as the lower-priority consumer (draw 5 / mass 2).
- **The solid-cylinder mesh + author.** `PrimitiveMeshes.SolidCylinder`
  (the reactor's mesh) + `ReactorMeshAuthor` assign a runtime-generated
  solid cylinder to an empty `MeshFilter`. The laser barrel reuses the same
  mesh (scaled thin).
- **The HUD bar pattern.** `FlyBoostBar` is a vertical bar left of the
  crosshair (fill + critical throb + an "Overboosted!" 3× flash). The heat
  bar mirrors it on the right.

## Approach — extend the existing pipeline

`LaserWeapon : WeaponBehavior` per cube; reuse `FlyShootingController`'s
existing select-and-dispatch loop (it already fires every cube of the
selected type each frame LMB is held — exactly a continuous beam). Add the
shared per-type heat to that loop, gate the laser type on heat + power, and
expose heat to the HUD. Power gates through the existing
`ConstructEnergySystem`.

Rejected: a dedicated `ConstructLaserSystem` (duplicates the
selection / fire-held / per-type dispatch FlyShootingController already
does — two parallel fire pipelines), and generalising `WeaponBehavior` into
projectile+beam modes (a sizable refactor of working code for one beam
weapon — YAGNI).

## Design

### Components

**`Assets/Scripts/Fly/LaserWeapon.cs`** — `: WeaponBehavior`, per cube.
`reloadSeconds = 0` so the dispatcher fires it every frame; heat + power
gate it instead. Each fire:

- Raycast from the barrel — origin `transform.position`, direction
  `transform.up` (fixed barrel axis, NOT crosshair-tracked) — to
  `transform.position + transform.up * range` (range 100, tunable), via
  `ProjectileHit.TrySweep` for self-construct filtering. Single-target
  (first hit).
- Drive a `LineRenderer` (on the cube) from the barrel to the hit point, or
  to max range when nothing is hit. Enabled only on a frame the beam fires.
- Accumulate a tick timer; every `tickInterval` (0.1 s, tunable) build a
  `HitContext` with `DamageType.Energy` and the per-tick raw `damage`, and
  route through `CubeDamage.ApplyAndLog` (so the shield takes ×1.1, overflow
  HP armour-aware).
- Expose `IsBeaming` (fired this frame) so the dispatcher can tick shared
  heat, and a method to stop the beam (called when not dispatched).

`LaserWeapon` overrides the projectile assumption: the base `damage` field
is the **per-tick** raw damage; there is no `projectilePrefab`.

**`Assets/Scripts/Fly/FlyShootingController.cs`** (modify) — its existing
`HandleFireInput` loop fires every alive weapon of the selected type each
frame LMB is held. Extend:

- Per-laser-type **shared heat** state (stored on the type's
  `WeaponTypeGroup`). Each frame, for the selected type if it `IsLaser`:
  evaluate fire intent (Fire held + not overheated + power available);
  dispatch the laser cubes that get powered; **rise** heat if any beamed,
  else **cool**.
- Heat also cools for non-selected laser types each frame (you can switch
  away from a hot laser and it keeps cooling) — tick every laser-type group.
- **Overheat lockout:** at heat ≥ 100 the type is locked (no fire) until
  heat cools back to 0, then unlocks; entering lockout fires the HUD's
  "Overheated!" flash once.
- **Power allocation:** read `ConstructEnergySystem.AvailableForWeapons`;
  power `floor(available / laserDrawPerCube)` of the laser cubes that want
  to fire this frame (5 each), cut the rest — the cube-count that fires is
  whatever the spare-after-shield budget covers.
- Expose for the HUD: the **selected** type's heat fraction, whether it's a
  laser, and whether it's overheated (+ an edge event for the flash).

**`WeaponTypeGroup`** (modify, same file) — add `Heat` (0–100) + helpers;
`IsLaser` (first instance `is LaserWeapon`); `ReadyFraction` returns
`1 − Heat/100` for a laser type (so the existing toolbar reload bar shows
remaining heat capacity and drains as the laser heats) and the existing
reload-based value otherwise.

**`Assets/Scripts/Fly/ConstructEnergySystem.cs`** (modify) — add
`public float AvailableForWeapons => Mathf.Max(0f, _totalOutput −
(_shieldPowered ? _shieldDraw : 0f));` — the spare power after the shield's
higher-priority claim (so a shield that is offline because it's unaffordable
frees its budget for the laser). No other power changes; the laser is the
weapon-tier consumer the cascade was already built for.

**`Assets/Scripts/Fly/FlyHeatBar.cs`** (new HUD) — a vertical bar to the
**right** of the crosshair, mirroring `FlyBoostBar` on the left. Reads the
selected type's heat from `FlyShootingController`. Fill grows with heat
(0 → empty, 100 → full); colour shifts toward / pulses red as it nears
overheat (the boost-bar critical throb pattern). Plus an "Overheated!" 3×
flash above the crosshair on the lockout edge (the `FlyBoostBar`
"Overboosted!" pattern). The whole element is hidden unless the selected
weapon type is a laser. Built under `FlyHud.Instance.Root`.

**`Assets/Scripts/Core/SolidCylinderMeshAuthor.cs`** — rename of
`ReactorMeshAuthor` (it assigns `PrimitiveMeshes.SolidCylinder`; both the
reactor and the laser barrel use it). Update `PlacedReactor.prefab`'s
MonoBehaviour reference to the renamed script.

### Firing & beam

The laser fires along its **fixed barrel axis** (`transform.up`) — unlike
the frontal pyramid, it never tracks the crosshair (roadmap: "one
direction"). A laser cube placed facing backward beams backward. The beam
is a per-frame raycast (range 100); the `LineRenderer` visualises it
barrel→hit (or barrel→max-range). Single-target: only the first cube hit
takes damage.

### Damage (energy, ticked)

Damage is applied in **fixed ticks** (every `tickInterval` 0.1 s), not
per-frame, because the subtractive-armour formula (`effective = max(0,
raw − AV)`) would zero out tiny per-frame amounts. Each tick's raw `damage`
(6, tunable → ~60 raw DPS at 10 ticks/s) routes through
`CubeDamage.ApplyAndLog` as `DamageType.Energy`:

- vs **shields**: ×1.1 (the energy modifier already in
  `ConstructEnergySystem.ApplyToShield`), overflow to HP.
- vs **HP**: armour-aware (`CubeStats.TakeDamage`) like any non-kinetic
  hit.

**Armour interaction (intended):** as with every weapon, the per-tick raw
must exceed a cube's AV to penetrate. The laser therefore shreds shields
and light / AV-0 targets (the world target cubes are AV 0) and is weak
against heavy armour — a clear energy-weapon niche. (If a future tuning pass
wants energy to *melt* armour, flipping the laser's `HitContext` to
`HitFlags.BypassArmour` is a one-line change; v1 keeps energy armour-aware
per the roadmap.)

### Heat (shared per laser type)

One 0–100 heat pool per laser weapon-type, owned by `FlyShootingController`
(stored on the `WeaponTypeGroup`):

- **Rise** `heatRisePerSecond` (50, tunable → ~2 s of sustained fire to
  overheat) on any frame ≥1 laser of the type beams. Flat — independent of
  how many laser cubes fire (the boost-meter precedent).
- **Cool** `heatFallPerSecond` (30) when not firing and not overheated.
- **Overheat:** at 100, the type locks out (no fire); the HUD flashes
  "Overheated!" ×3; heat then cools at the slower `heatFallOverheatedPerSecond`
  (15) until it reaches 0, then unlocks. So bursts sustain, held beams
  overheat, and recovery from a full overheat is punishingly slow.

All instances of a type share the pool and overheat together.

### Power gating

The laser is the weapon-tier consumer (lower priority than the shield).
Each frame the laser type wants to fire, `FlyShootingController` reads
`ConstructEnergySystem.AvailableForWeapons` and powers
`floor(available / 5)` of the alive laser cubes wanting to fire (5 each);
the remainder are cut for the frame. So when reactors die or the shield
claims the budget, the laser is what stops first — the cascade the energy
system was built for. The steady `Power:` readout (output − shield draw)
already shows the spare-for-weapons headroom, so a player who can't fire
sees they're short.

### The cube — geometry, shape, stats

A **thin barrel**: the `SolidCylinder` mesh on a child transform scaled to
`(0.3, 1, 0.3)` (narrow, full cell height), so it reads as a focused
emitter and is visually distinct from the fat rocket-launcher cylinder.
Mounted `−Y` (the only valid face), barrel `+Y` — same convention as the
cylinder weapon. A full-cell `BoxCollider` (1×1×1) so it occupies its cell
for placement + targeting like the reactor. A `LineRenderer` on the root for
the beam.

- `ShapeWeaponLaser.asset` — `ShapeDefinition`, **Weapon** category,
  `faceNegY` only, coupled `LaserMatDef`, prefab `PlacedLaser`.
- `LaserMatDef.asset` + `LaserMat.mat` — coupled material, distinct emissive
  colour (hot red/orange), with both `_BaseColor` and `_Color` set (the
  swatch fix from the Power & Energy PR). Stats: **HP 40, AV 0, mass 2**.
- `PlacedLaser.prefab` — `CubeStats` + `PlacedCubeData` + `LaserWeapon`
  (per-tick `damage` 6, `range` 100, `tickInterval` 0.1, power draw 5) +
  `LineRenderer` + full-cell `BoxCollider` + a barrel child
  (`SolidCylinderMeshAuthor` + `MeshRenderer`, scaled `(0.3, 1, 0.3)`).
- Appended to `ShapeRegistry.asset`, so it shows in the build toolbar's
  **Weapons** category flyout and the Fly weapon toolbar. Old saves load
  unchanged (shapes are stored by name; no schema bump).

### HUD summary

| Element | Where | Shows |
|---|---|---|
| Heat bar (`FlyHeatBar`) | vertical, **right** of the crosshair (mirror of `FlyBoostBar`) | heat 0→100 fill, reddening / throbbing near overheat; hidden unless a laser is selected |
| "Overheated!" flash | above the crosshair | 3× flash on the lockout edge |
| Weapon toolbar bar | existing per-type bar | `1 − heat/100` for a laser (drains as it heats) |
| `Power:` readout | bottom-left (existing) | spare-for-weapons headroom — informs when the laser can't fire |

## Files touched

**Create:**
- `Assets/Scripts/Fly/LaserWeapon.cs` (+ `.cs.meta`)
- `Assets/Scripts/Fly/FlyHeatBar.cs` (+ `.cs.meta`)
- `Assets/Materials/LaserMat.mat`, `Assets/Materials/Defs/LaserMatDef.asset` (+ `.meta`)
- `Assets/Prefabs/PlacedLaser.prefab` (+ `.meta`)
- `Assets/Shapes/ShapeWeaponLaser.asset` (+ `.meta`)

**Modify:**
- `Assets/Scripts/Fly/FlyShootingController.cs` — shared heat, laser gating, HUD exposure (incl. `WeaponTypeGroup` heat + `IsLaser` + laser `ReadyFraction`).
- `Assets/Scripts/Fly/ConstructEnergySystem.cs` — add `AvailableForWeapons`.
- `Assets/Scripts/Core/ReactorMeshAuthor.cs` → rename to `SolidCylinderMeshAuthor.cs` (+ update `PlacedReactor.prefab`).
- `Assets/Shapes/ShapeRegistry.asset` — append the laser shape.
- `Assets/Scenes/FlyScene.unity` — add `FlyHeatBar` to the `FlyHUD` GameObject.

## Delivery

- **Branch:** `feat/laser-weapon` off `main`.
- **Commits**, in dependency order (each compiles + is independently
  reviewable):
  1. **Laser core + power hook.** `LaserWeapon`; `ConstructEnergySystem.AvailableForWeapons`;
     `FlyShootingController` + `WeaponTypeGroup` shared-heat + laser gating
     + power allocation. (Verified once the cube exists in commit 2.)
  2. **Laser cube.** `SolidCylinderMeshAuthor` rename; `LaserMat` /
     `LaserMatDef`; `PlacedLaser.prefab`; `ShapeWeaponLaser`; `ShapeRegistry`
     append. Makes the laser placeable + fireable.
  3. **HUD.** `FlyHeatBar` (heat bar right of crosshair + Overheated flash);
     add it to `FlyScene`. Toolbar `ReadyFraction` change rides in commit 1.
- **One PR**, Copilot review, user play-test.

## Verification

No automated test framework — per-commit Unity compile-check (`refresh_unity`
+ `read_console` filtered to `Assets/Scripts`) plus a manual play-test:

- **Build:** Laser appears in the **Weapons** flyout; mounts `−Y` only;
  renders as a thin barrel distinct from the rocket cylinder; mass cap
  counts it (mass 2); stat readout shows it.
- **Fire:** hold LMB with a laser selected → continuous beam along the
  barrel axis (not crosshair-tracked); a `LineRenderer` connects barrel→hit;
  AV-0 world cubes take energy DPS and die; the beam stops on release /
  deselect.
- **Heat:** the right-of-crosshair bar fills while firing, overheats at ~2 s
  sustained → "Overheated!" ×3 + lockout, then slow cool to 0 before it can
  fire again; short bursts never lock out; the toolbar bar mirrors it.
- **Power:** with insufficient spare power (e.g. shields claiming the
  budget) the laser doesn't fire / fewer cubes fire; adding reactors lets
  more lasers beam. Killing reactors mid-flight cuts the laser before the
  shield (cascade).
- **Energy vs shield:** a laser hitting a shielded enemy/target drains the
  shield faster than projectile damage (×1.1).
- Compile clean per commit; no `Assets/Scripts` errors / warnings.

## Out of scope (deferred)

- **Beam particle / glow VFX** — Extended VFX pass (roadmap item 4); v1 is
  a `LineRenderer`.
- **Beam piercing / multi-target, charge-up, alt-fire, per-cube heat** — not
  planned.
- **Energy melting armour (`BypassArmour`)** — v1 keeps energy armour-aware;
  a future tuning lever, not this feature.
