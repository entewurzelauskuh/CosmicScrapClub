# Power & Energy — Design Spec

**Status:** Approved design, ready for implementation planning
**Date:** 2026-05-21
**Branch:** `feat/power-and-energy` off `main`

## Overview

Phase 2 / item 1 of the Up Next timeline (`../../../ROADMAP.md` → *Up Next*).
The foundation block that turns the construct from "cubes with HP" into
"cubes that produce, consume, and defend with energy." Three new concepts:

- a **reactor cube** that produces power,
- a **shield generator cube** that consumes power and adds a shared shield
  pool absorbing damage before HP, and
- a construct-wide **power balance** that shuts consumers off in priority
  order when reactors die.

It is also where the **projectile-vs-energy damage split** finally does
something: shields take −10% from projectile sources and +10% from energy
sources.

The **Laser** (the first energy weapon, and the second power consumer) is
the *next* roadmap item, not this one. Here the shield is the only
consumer, but the power cascade is structured so the laser slots in as a
lower-priority consumer later (it draws 5 power / mass 2 — recorded here so
the cascade design accounts for it).

## Background — what already exists

- **Damage types are done.** `HitContext` (Core) already carries
  `DamageType` (`Projectile` / `Energy` / `Kinetic`) and `HitFlags`
  (`None` / `BypassArmour`). Bullets/rockets build `Projectile` contexts,
  crashes build `Kinetic` + `BypassArmour`. Shields read `context.Type`
  directly — no new damage plumbing.
- **The "special cube" pattern.** `ThrusterBehavior` (Fly) is a passive
  descriptor with no `Update`; `FlyController.BuildConstruct` collects every
  thruster into `_spawnedThrusters` and a central system (FlyController's
  boost logic) drives behaviour. Reactor + shield cubes follow this exact
  shape.
- **The damage chokepoint.** Every hit flows through
  `CubeDamage.ApplyAndLog(in HitContext)` (Fly) before touching
  `CubeStats`. That is the single place a shield needs to intercept.
- **The mesh-author pattern.** Non-cube shapes get a runtime mesh from
  `PrimitiveMeshes` assigned by a `*MeshAuthor` MonoBehaviour
  (`CylinderMeshAuthor`, `ConeMeshAuthor` via `ThrusterMeshAuthor`, etc.)
  that only fills an empty `MeshFilter`/`MeshCollider` slot. Every
  non-armour shape uses local **−Y** as its single valid attachment face
  (pyramid base, cone base, cylinder bottom — confirmed
  `ShapeWeaponCylinder` is `faceNegY` only).
- **The HUD has a home.** Post-consolidation, HUD elements attach to
  `FlyHud.Instance.Root` (sortingOrder 100). The boost bar
  (`FlyBoostBar`) is the model for a new resource bar.
- **Backward compatibility.** A construct with no reactor/shield cubes has
  no power system and behaves exactly as today. `PlacementRecord` stores
  shapes by name, so appending new shapes to `ShapeRegistry` leaves old
  saves loadable with **no schema bump**.

## Approach — one `ConstructEnergySystem` component

A single new MonoBehaviour on `CubeConstruct`, sibling to `FlyController`,
owns the whole subsystem: power balance, the shared shield pool, regen, and
the consumer cascade. `FlyController.BuildConstruct` collects reactor +
shield cubes into lists and hands them over via `RegisterCubes(...)`
(mirrors how it hands weapons to `FlyShootingController.RegisterWeapons`).
`CubeDamage` resolves the system from the hit cube
(`GetComponentInParent<ConstructEnergySystem>()`) to run the shield step.

Rejected alternatives: bolting power/shield onto `FlyController` (already
~640 lines; the audit's arch-rec 4 already flags extracting boost *out* of
it) and splitting into two components (power + shield are tightly coupled —
the shield reads power state every recompute and both react to the same
cube-death event — so two components only add cross-wiring).

## Design

### Components

**`Assets/Scripts/Fly/ReactorBehavior.cs`** — passive descriptor (no
`Update`), the `ThrusterBehavior` pattern. Serialized `[SerializeField]
float output = 10f;` exposed as `public float Output`. `public bool IsAlive`
resolves and caches a sibling `CubeStats` and returns `healthPoints > 0`
(copied from `WeaponBehavior.IsAlive`).

**`Assets/Scripts/Fly/ShieldBehavior.cs`** — passive descriptor. Serialized
`float draw = 20f;` and `float contribution = 50f;`, exposed as `Draw` /
`Contribution`. Same `IsAlive` pattern.

**`Assets/Scripts/Fly/ConstructEnergySystem.cs`** — the central system on
`CubeConstruct`. Responsibilities:

- `RegisterCubes(IEnumerable<ReactorBehavior>, IEnumerable<ShieldBehavior>)`
  — called once by `FlyController.Start` after `BuildConstruct`. Stores the
  lists, then calls `RecomputePower()`.
- `RecomputePower()` — recomputes the power balance and shield ceiling.
  Public so `FlyController.OnCubeDied` can call it after the disconnect
  cascade settles (see *Cube death*). Logic:
  - `totalOutput = Σ output of alive reactors`
  - `shieldDraw = Σ draw of alive shields`; `shieldMax = Σ contribution of
    alive shields`
  - Consumers are allocated power in priority order — **shield system
    first, energy weapons (laser, later) last** — by walking consumers
    high-priority-first and powering each only while the running claimed
    total stays ≤ `totalOutput`. The shield system is a single
    all-or-nothing consumer and claims first, so `_shieldPowered =
    totalOutput >= shieldDraw`. (Once the laser exists it claims the
    *remainder*: `laserPowered = (totalOutput − shieldDraw) >= laserDraw`,
    which is why a weapon is what gets cut first under contention.)
  - `NetPower` is the player-facing **demand balance**, NOT the
    post-cascade surplus: `NetPower = totalOutput − shieldDraw` (later also
    minus the laser's draw while it is firing). A negative value means the
    build is under-powered (and the cascade has shed consumers to cope) — so
    1 reactor + 1 shield reads `−10`, telling the player they're 10 short;
    2 reactors + 1 shield reads `0`. This is the number the HUD shows.
  - If `!_shieldPowered`, collapse the pool: `_shieldPoints = 0`. Else clamp
    to the (possibly reduced) ceiling: `_shieldPoints = Min(_shieldPoints,
    shieldMax)`.
- `Update()` — ticks shield regen only. `_timeSinceDamage += Time.deltaTime`;
  once `_shieldPowered && _timeSinceDamage >= regenDelaySeconds (5)` and
  `_shieldPoints < shieldMax`, raise `_shieldPoints` toward `shieldMax` at
  `regenRate (20/s)`. (`Time.deltaTime` is 0 while paused — regen pauses
  with the game, matching the boost bar.)
- `public float ApplyToShield(float amount, DamageType type)` — the damage
  chokepoint entry. Always resets `_timeSinceDamage = 0` (the construct was
  hit, so regen restarts even when the shield is down). If `_shieldPowered
  && _shieldPoints > 0`: `scaled = amount * TypeModifier(type)`; `absorbed =
  Min(scaled, _shieldPoints)`; `_shieldPoints −= absorbed`; return the
  overflow `scaled − absorbed`. Otherwise return `amount` unchanged (no
  shield → full overflow).
- `TypeModifier(DamageType)` — `Projectile → 0.9`, `Energy → 1.1`, `Kinetic
  → 1.0`. Tunable via serialized fields.
- Read-only props for the HUD: `ShieldPoints`, `ShieldMax`, `NetPower`,
  `ShieldActive` (`_shieldPowered`), `HasShieldCubes`, `HasPowerCubes`
  (any reactor or shield present).

Serialized tunables on the component (defaults): `regenRate = 20`,
`regenDelaySeconds = 5`, the three type modifiers. Per-cube values
(`output`, `draw`, `contribution`) live on the cube behaviours so they can
be authored per shape/material.

### Power model (instantaneous net rate)

`NetPower = Σ(alive reactor Output) − Σ(draw of powered consumers)`. No
battery, no stored charge: destroy a reactor and the balance recomputes
that frame. The shield system is a single all-or-nothing consumer that
powers on iff `totalOutput ≥ shieldDraw`. Power is recomputed on `Start`
and after every cube death — not per frame (reactors/shields only change on
death today; the laser will add fire/cease recompute points later).

### Shield mechanics

One shared pool for the whole construct. `ShieldMax = Σ(alive shield
Contribution)` (+50 each). Absorbs before HP using the type scale
(`Projectile ×0.9`, `Energy ×1.1`, `Kinetic ×1.0`); a hit larger than the
remaining pool spills the (already-scaled) overflow through to the struck
cube's HP. **Collapses to 0** the instant the construct goes power-negative
(reactor lost, or shields over-built). Regenerates toward `ShieldMax` at
**20 pts/sec** starting **5 s after the last hit to any construct cube**
(any hit — absorbed or not — resets the timer; no regen while unpowered).

### Damage interception (`CubeDamage.ApplyAndLog`)

Insert one step before the existing HP application:

```csharp
CubeStats stats = context.Target;
if (stats == null) return 0f;

float amount = context.Amount;
ConstructEnergySystem energy = stats.GetComponentInParent<ConstructEnergySystem>();
if (energy != null)
    amount = energy.ApplyToShield(amount, context.Type);   // resets regen, absorbs, returns overflow

// existing path, now applied to the (possibly reduced) overflow `amount`:
bool bypassArmour = (context.Flags & HitFlags.BypassArmour) != 0;
float applied = bypassArmour ? stats.TakeRawDamage(amount) : stats.TakeDamage(amount);
```

World target cubes have no `ConstructEnergySystem` ancestor → `energy ==
null` → unchanged behaviour. The log line gains the shield contribution
(e.g. `shield absorbed X, Y to HP`). If the overflow is 0 the cube takes no
HP damage and the existing death/over-kill logic is naturally skipped.

### New cubes — geometry, category, stats

Both are **Utility**-category shapes (reuse the Thruster's `CategoryFlyout`
machinery — they appear in the existing "Utilities" flyout; no new
`ShapeCategory` enum value or toolbar wiring). Both use local **−Y** as
their single valid attachment face (`faceNegY` only), like every other
non-armour shape.

**Reactor** — a **solid cylinder**: same outer dimensions and placement
behaviour as the cylinder weapon (radius 0.5, height 1, axis +Y, fills the
cell), but solid (capped top + bottom discs, no inner wall). Needs:
- `PrimitiveMeshes.SolidCylinder` — mirror of `HollowCylinder` minus the
  inner wall: a smooth-normal outer wall ring + a +Y top disc fan + a −Y
  bottom disc fan.
- `ReactorMeshAuthor` — mirror of `CylinderMeshAuthor`, assigns
  `PrimitiveMeshes.SolidCylinder` to an empty `MeshFilter`/`MeshCollider`.
- `PlacedReactor.prefab` — `CubeStats` + `ReactorBehavior` + `PlacedCubeData`
  + `MeshFilter`/`MeshRenderer` + `ReactorMeshAuthor` + a 1×1×1 collider
  (cylinder fills the cell, so the existing full-cell collider convention
  holds).

**Shield** — a **small cube**: a regular cube with every edge length halved
(0.5 × 0.5 × 0.5). It is grounded and centred on its single valid
placement face: the 0.5 cube mesh **and** its matching `BoxCollider` are
offset to local `(0, −0.25, 0)` inside the prefab, so the cube spans
`y ∈ [−0.5, 0]` (flush against the cell's −Y boundary) and `x, z ∈
[−0.25, 0.25]` (centred). `CubeStats` / `ShieldBehavior` / `PlacedCubeData`
sit on the prefab root, which still occupies the cell centre like every
other shape (the grid footprint stays one 1×1×1 cell; only the mesh +
collider are the half-size offset). Rotation (R/T) reorients the −Y face +
the offset together, so the cube always grounds + centres on whatever
surface it attaches to. Uses the **built-in Unity cube mesh** (no new
`PrimitiveMeshes` entry).
- `PlacedShield.prefab` — `CubeStats` + `ShieldBehavior` + `PlacedCubeData`
  + the offset half-size cube mesh + offset 0.5 `BoxCollider`.

**Coupled materials** (the non-armour `coupledMaterial` pattern): new
`ReactorMatDef` / `ShieldMatDef` `MaterialDefinition`s carrying the stat
values + a distinct emissive colour (reactor warm/amber, shield cyan), plus
matching URP/Lit `.mat` assets. Both new `ShapeDefinition`s
(`ShapeUtilityReactor`, `ShapeUtilityShield`) append to `ShapeRegistry`.

Starter stats (all tunable):

| Cube | HP | AV | Mass | Power | Shield | Geometry | Valid face |
|---|---|---|---|---|---|---|---|
| Reactor | 60 | 5 | **10** | **+10 output** | — | solid cylinder | −Y |
| Shield | 50 | 5 | **5** | **−20 draw** | **+50 pts** | 0.5 cube, grounded+centred | −Y |
| *(Laser, next item)* | — | — | *2* | *−5 draw* | — | *(its own item)* | *(its own item)* |

So **two reactors power one shield** (output 10 × 2 = 20 = shield draw 20),
costing 25 mass (2×10 + 5) for a +50 pool — a deliberately heavy commitment
so "shield or no shield" is a real build decision.

### HUD (`FlyShieldIndicator` on `FlyHud.Instance.Root`)

A new `FlyShieldIndicator` script (modelled on `FlyHpIndicator` /
`FlyBoostBar`), built under `FlyHud.Instance.Root`:

- **Shield bar** — bottom-left, stacked above the HP label; cyan fill
  showing `ShieldPoints / ShieldMax`. Hidden when `!HasShieldCubes`. Shown
  greyed/empty when the field is collapsed (`!ShieldActive`).
- **Power readout** — small text `Power: +N` (green when `NetPower ≥ 0`) /
  `−N` (red) in the same bottom-left stack. Hidden when `!HasPowerCubes`.

Reads the construct's `ConstructEnergySystem` (auto-wired via
`FindAnyObjectByType` in `Start`, like the other Fly HUD scripts wire
`FlyController`).

### Cube death recompute

`FlyController.OnCubeDied` already runs
`CascadeDestroyDisconnectedCubes()` then `ResolveRigidbody()`. Add a third
call: `energySystem.RecomputePower()` — *after* the cascade so orphaned
reactor/shield deaths are reflected. `FlyController` resolves the
`ConstructEnergySystem` reference in `Start` (sibling `GetComponent`). The
energy system does **not** subscribe to `CubeDeath.CubeDied` itself — the
orphan cascade kills cubes via `BeginDeath` without re-raising `CubeDied`,
so a single FlyController-orchestrated recompute after the cascade is the
correct, race-free trigger.

## Files touched

**Create:**
- `Assets/Scripts/Fly/ReactorBehavior.cs` (+ `.cs.meta`)
- `Assets/Scripts/Fly/ShieldBehavior.cs` (+ `.cs.meta`)
- `Assets/Scripts/Fly/ConstructEnergySystem.cs` (+ `.cs.meta`)
- `Assets/Scripts/Fly/FlyShieldIndicator.cs` (+ `.cs.meta`)
- `Assets/Scripts/Core/ReactorMeshAuthor.cs` (+ `.cs.meta`)
- `Assets/Shapes/ShapeUtilityReactor.asset`, `ShapeUtilityShield.asset` (+ `.meta`)
- `Assets/Materials/Defs/ReactorMatDef.asset`, `ShieldMatDef.asset` (+ `.meta`)
- `Assets/Materials/ReactorMat.mat`, `ShieldMat.mat` (+ `.meta`)
- `Assets/Prefabs/PlacedReactor.prefab`, `PlacedShield.prefab` (+ `.meta`)

**Modify:**
- `Assets/Scripts/Core/PrimitiveMeshes.cs` — add `SolidCylinder`.
- `Assets/Scripts/Fly/CubeDamage.cs` — shield interception step.
- `Assets/Scripts/Fly/FlyController.cs` — collect reactor/shield cubes in
  `BuildConstruct`; resolve `ConstructEnergySystem` in `Start` + `RegisterCubes`;
  call `RecomputePower()` in `OnCubeDied`.
- `Assets/Shapes/ShapeRegistry.asset` — append the two new shapes.

`MaterialRegistry.asset` is **not** touched — reactor and shield are
non-armour shapes that carry their stats on a coupled `coupledMaterial`
(the `ReactorMatDef` / `ShieldMatDef` above), not in the A/B/C/D armour
pool.

## Delivery

- **Branch:** `feat/power-and-energy` off `main`.
- **Commits**, in dependency order (each compiles + is independently
  reviewable):
  1. **Power core.** `ReactorBehavior`, `ShieldBehavior`,
     `ConstructEnergySystem`; `FlyController` wiring (`RegisterCubes` +
     `RecomputePower` on death); `CubeDamage` interception. No new shapes
     yet — unit-testable in isolation by temporarily adding the behaviours
     to existing cubes, but primarily verified once the cubes exist.
  2. **Reactor + Shield cubes.** `PrimitiveMeshes.SolidCylinder`,
     `ReactorMeshAuthor`, the two shapes + coupled materials + prefabs,
     `ShapeRegistry` append. Makes the system reachable in-game via the
     Build toolbar.
  3. **HUD.** `FlyShieldIndicator` (shield bar + power readout) on `FlyHud`.
- **One PR** for all three commits, Copilot review, then user play-test.

## Verification

No automated test framework (deferred with F5) — verification is the Unity
compile-check (`refresh_unity` + `read_console` filtered to
`Assets/Scripts`) **after each commit**, plus a manual play-test:

- **Build:** Reactor + Shield appear in the Utilities flyout; placement
  obeys the single −Y mount face (you can mount them on a surface but
  nothing stacks on top); the reactor renders as a solid cylinder, the
  shield as a small grounded+centred cube; mass cap counts them; the
  selected-stats readout shows their stats.
- **Power balance:** a construct with 1 reactor + 1 shield reads
  `Power: −10` and the shield never powers on (pool stays 0). Adding a
  second reactor flips it to `Power: +0` and the shield bar fills to +50.
- **Shield absorption:** with a powered shield, a bullet (projectile) is
  absorbed at ×0.9 and the bar drops; a big hit overflows to HP. Crash
  damage (kinetic ×1.0) also drains the shield. The bar refills 5 s after
  the last hit at 20/s.
- **Cascade:** destroy a reactor mid-flight so net goes negative — the
  shield bar collapses to 0 immediately and damage flows to HP.
- **Backward compat:** a construct with no reactor/shield cubes shows no
  shield bar / power readout and flies exactly as before; an old save loads
  unchanged.
- Compile clean per commit, no `Assets/Scripts` errors / warnings.

## Out of scope (deferred)

- **Laser / energy weapon** — the next roadmap item; this spec only
  records its power draw (5) + mass (2) so the cascade priority accounts
  for it.
- **Visual shield-dome effect** — deferred to the Extended VFX pass; v1 is
  the HUD bar only.
- **Battery / stored-charge power** — net-rate model only.
- **Directional / per-face shields** — one shared omnidirectional pool.
- **Reactor overcharge, repair cubes, per-cube shield pools** — not planned.
