# Power & Energy — System Spec

**Status:** Current behaviour (shipped). Reflects the merged Power & Energy
(PR #43) and Laser (PR #44) work.

The power/shield/energy-weapon foundation. Three cube types — **reactor**,
**shield**, **laser** — plus a construct-wide `ConstructEnergySystem` that
balances power, owns the shared shield pool, and gates the laser. This is
where the `DamageType` split (`Projectile` / `Energy` / `Kinetic`) finally
changes outcomes: shields react differently to each.

Quick mental model: **reactors make power, shields and lasers spend it.**
Two reactors power one shield. A laser needs spare power after the shield's
claim, or it can't fire. Lose all your reactors and your shields + lasers
become dead weight you can **Eject**.

---

## The cubes

All three are non-armour shapes with a coupled `MaterialDefinition` (their
stats), mounted on their single local **−Y** face (barrel / mount face),
like the cylinder weapon and thruster. Stats are tunable; starter values:

| Cube | Category | HP | AV | Mass | Power | Geometry |
|---|---|---|---|---|---|---|
| **Reactor** (`ShapeUtilityReactor`) | Utility | 60 | 5 | 10 | **+10 output** | solid cylinder (`PrimitiveMeshes.SolidCylinder`) |
| **Shield** (`ShapeUtilityShield`) | Utility | 50 | 5 | 5 | **−20 draw**, **+50 pool** | half-size cube (built-in cube on a `(0.5,0.5,0.5)` child, offset to sit flush against −Y) |
| **Laser** (`ShapeWeaponLaser`) | Weapon | 40 | 0 | 2 | **−5 draw** (while firing) | thin barrel (`SolidCylinder` scaled `(0.3,1,0.3)`) |

Reactor + Laser share `SolidCylinderMeshAuthor` (assigns the runtime
`SolidCylinder` mesh). The reactor and shield behaviours are passive
descriptors — `ReactorBehavior` (`Output`) and `ShieldBehavior` (`Draw`,
`Contribution`) — the `ThrusterBehavior` pattern. The laser is different:
`LaserWeapon` is an active `WeaponBehavior` (it draws the beam and applies
ticked damage) that also exposes a `PowerDraw` the energy system reads.

Backward compatible: a construct with none of these has no power system and
flies/builds exactly as before; old saves load unchanged (shapes are stored
by name, so appending them to `ShapeRegistry` doesn't bump the schema).

---

## `ConstructEnergySystem`

One per construct, on `CubeConstruct` (sibling to `FlyController`).
`FlyController.BuildConstruct` collects the reactor / shield / laser cubes
and hands them over via `RegisterCubes(reactors, shields, lasers)` in
`Start`. Recomputed on `Start` and after every cube death
(`FlyController.OnCubeDied` → `RecomputePower()` after the disconnect
cascade settles).

### Power model — instantaneous net rate

No battery, no stored charge. Each recompute sums the **alive** cubes:

- `totalOutput = Σ alive reactor Output`
- `shieldDraw = Σ alive shield Draw`, `shieldMax = Σ alive shield Contribution`

The shield is a **single all-or-nothing consumer** that claims power first:
`shieldPowered = totalOutput ≥ shieldDraw`. So **two reactors (10 + 10)
power one shield (draw 20)**; under-build reactors and the shield can't come
up at all.

- **`NetPower`** (the player-facing readout) = `totalOutput − shieldDraw` —
  the demand balance. Negative = under-powered.
- **`AvailableForWeapons`** = `max(0, totalOutput − (shieldPowered ?
  shieldDraw : 0))` — the spare power for the weapon tier after the shield's
  higher-priority claim. A shield that's offline (unaffordable) draws
  nothing, so its budget frees up for the laser.

This is the **consumer-priority cascade**: the shield has first claim, but
all-or-nothing. When output covers the shield draw, the shield comes up and
the laser runs on the remainder — so the laser is cut first if that remainder
is too small. When output *can't* cover the shield, the shield stays offline
(its pool collapses) and its whole budget frees up, so the laser can still
fire on the available output.

### Shield mechanics

One shared pool covering the whole construct. `ShieldMax = Σ alive shield
Contribution` (+50 each). Damage is intercepted in `CubeDamage.ApplyAndLog`:
it resolves the struck cube's `ConstructEnergySystem` (via
`GetComponentInParent`) and calls `ApplyToShield(amount, type)` before HP.

- **Type modifiers** (the pool **cost per unit of raw damage absorbed**, not
  a scale on the damage): `Projectile ×0.9` (shields resist projectiles —
  cheap to soak, the pool lasts longer), `Energy ×1.1` (shields are weak to
  energy — each absorbed point of energy drains 1.1 of the pool). The pool
  can therefore cover `ShieldPoints / modifier` raw damage of that type.
- **Overflow to HP:** a hit beyond what the pool can cover spills the
  **raw** remainder (never the type-scaled value) through to the cube's HP
  via the normal armour-aware path — so a near-empty shield can never
  amplify a hit past its raw amount, and there is no 0-vs-1-point
  discontinuity in damage taken.
- **Kinetic bypasses entirely:** crash / kinetic damage never touches the
  pool or the regen timer — it always goes straight to HP. A shield stops a
  beam or a bullet, not a physical ram.
- **Collapse on unpowered:** the moment the construct goes power-negative
  (reactor lost, or shields over-built), the pool drops to 0 immediately.
- **Regen:** toward `ShieldMax` at **20 pts/sec**, starting **5 s after the
  last projectile/energy hit** to any construct cube. No regen while
  unpowered.

So the shield is resistant against projectiles, vulnerable to the laser,
and useless against crashes — a clear counter/weakness profile.

### Eject (P)

When the construct has lost **all reactors** but still carries **power-
drawing cubes** (shields and/or lasers), those cubes can never function
again — they only carry mass. `CanEject` (`aliveReactorCount == 0 && (alive
shields > 0 || alive lasers > 0)`) lights a top-left **"Eject: P"** hint;
pressing **P** runs `Eject()`, which self-destructs every alive shield + laser
(drop from `GameData`, zero HP, death-drift) and raises `CubeDied` so
`FlyController` recomputes mass + power and cascades any orphans.

---

## Laser weapon

The first **energy**-type weapon and the second power consumer.
`LaserWeapon : WeaponBehavior`, so it rides the existing
`FlyShootingController` select-and-dispatch loop — but `reloadSeconds = 0`
(fires every frame LMB is held) and it has **no projectile**: it's a
continuous **hitscan beam**.

- **Beam:** each fire, a raycast from the barrel (`transform.position` along
  `transform.up` — fixed barrel axis, **not** crosshair-tracked), self-
  construct filtered via `ProjectileHit.TrySweep`, up to `range` (100). A
  runtime `LineRenderer` draws barrel→hit (or →max range). Single-target
  (first hit). The beam turns off (`LateUpdate`) on any frame it isn't
  dispatched.
- **Damage:** applied in **fixed ticks** (every 0.1 s, raw 6 ≈ 60 raw DPS)
  rather than per-frame, so each tick is a meaningful chunk against the
  subtractive-armour formula. Routed as `DamageType.Energy` through
  `CubeDamage.ApplyAndLog` (shield ×1.1). Like every weapon, per-tick raw
  must exceed a cube's AV to penetrate — so the laser excels vs shields and
  light / AV-0 targets, and is weak vs heavy armour.
- **Heat (shared per laser type):** owned by `FlyShootingController`, stored
  on the laser's `WeaponTypeGroup`. Rises **50/s** while any laser of the
  selected type fires; cools **30/s** when released early; at 100 the type
  **locks out** (no fire) + the "Overheated!" flash fires, then cools at the
  slower **15/s** until back to 0, when it unlocks. Short bursts sustain;
  held beams overheat. The toolbar bar reuses `1 − heat` so it drains as the
  laser heats.
- **Power:** the laser is per-cube power-gated. `FlyShootingController`
  reads `AvailableForWeapons` and powers `floor(available / 5)` of the alive
  lasers wanting to fire (5 each); the rest are cut. **A laser needs a
  reactor to fire** — with no spare power, no beam.

---

## HUD

All elements live under `FlyHud.Instance.Root` (FlyScene) — except the
build-scene power readout, which lives on the build toolbar.

| Element | Where | Source |
|---|---|---|
| **Shield bar** (`FlyShieldIndicator`) | bottom-left, above HP | cyan fill = `ShieldPoints / ShieldMax`; greyed when collapsed; hidden when no shield cubes |
| **`POWER:` readout** (`FlyShieldIndicator` / `BuildToolbarController`) | bottom-left | `POWER: ±N` (green ≥ 0, red < 0); shown in **both** FlyScene and BuildScene; the hangar readout uses a big value + red-**pulses** on a deficit (`UIPulse`); hidden when no power cubes |
| **Heat bar** (`FlyHeatBar`) | right of crosshair (mirrors the boost bar) | fill + opacity = `Heat / 100` — invisible when cold, fades in with use, fades out on regen; red throb + "Overheated!" flash at lockout; shown only while a laser is the selected type |
| **"EJECT: P" hint** (`FlyShieldIndicator`) | top-left | shown only while `CanEject`; red + pulsing |

The build-scene `POWER:` readout is computed by
`BuildManager.ComputeCurrentNetPower(out hasPowerCubes)` — summing reactor
`Output` − (shield `Draw` + laser `PowerDraw`) across the placed cubes — so power
balance is visible while building, not just in flight. Unlike flight's steady
`NetPower` (which excludes weapon draw, since it's contended at fire time), the
hangar folds laser draw in as a worst-case "can everything run at once?" budget,
so a laser without enough reactor reads negative and the readout pulses red.

---

## Files

**Core systems:**
- `Scripts/Fly/ConstructEnergySystem.cs` — power balance, shield pool, regen,
  cascade, Eject.
- `Scripts/Fly/ReactorBehavior.cs`, `ShieldBehavior.cs` — passive descriptors.
- `Scripts/Fly/LaserWeapon.cs` — the beam weapon.

**Touched:**
- `Scripts/Fly/CubeDamage.cs` — shield interception step before HP.
- `Scripts/Fly/ProjectileHit.cs` — `ApplyAndLog` takes an optional
  `DamageType` (so the laser routes `Energy`).
- `Scripts/Fly/FlyShootingController.cs` — laser dispatch (power-gated) +
  shared per-type heat; `WeaponTypeGroup` gains `Heat` / `Overheated` /
  `IsLaser` / heat-based `ReadyFraction`.
- `Scripts/Fly/FlyController.cs` — collects reactor / shield / laser cubes,
  registers the energy system, recomputes power on cube death.
- `Scripts/Core/SolidCylinderMeshAuthor.cs` — renamed from
  `ReactorMeshAuthor` (shared by reactor + laser barrel).
- `Scripts/Build/BuildManager.cs`, `BuildToolbarController.cs` — build-scene
  power readout.

**HUD:** `Scripts/Fly/FlyShieldIndicator.cs`, `FlyHeatBar.cs`.

**Assets:** `Shapes/ShapeUtilityReactor`, `ShapeUtilityShield`,
`ShapeWeaponLaser`; `Materials/Defs/ReactorMatDef`, `ShieldMatDef`,
`LaserMatDef` (+ their `.mat`s); `Prefabs/PlacedReactor`, `PlacedShield`,
`PlacedLaser`; all appended to `ShapeRegistry`.

## Out of scope (future)

Beam / glow VFX (Extended VFX pass), the visual shield dome, battery /
stored-charge power, directional / per-face shields, more energy weapons,
reactor overcharge, repair cubes.
