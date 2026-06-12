# Milestone A2 — Desert combat layout — Design Spec

**Date:** 2026-06-12
**Branch:** `explore/desert-flyscene` (experimental — not on `main`)
**Roadmap:** item 4, Milestone A, sub-phase **A2** (`ROADMAP.md §4`).
**Predecessor:** A1.5 (`docs/superpowers/specs/2026-06-11-desert-flyscene-a1.5-design.md`) — done, gate = SHIP.
**Status:** Accepted design, pre-implementation.

## 1. Purpose & scope

A1/A1.5 built and scaled the desert basin (a vast 500×500 plain ringed by a perimeter
ridge, with 5 formations pushed to the rim) but it currently plays as an **empty
flythrough** — A1 deliberately stripped the old flat-arena content (20 `WorldTargetCube`s
+ 4 `AutoTurret`s). A2 repopulates the basin with destructible targets and a little return
fire so it plays as a **combat arena**, using the geography A1.5 built: each formation
becomes a set-piece, the open plain a sparsely-contested crossing.

Continues on `explore/desert-flyscene` under the **current renderer** (cel look = A3).
Geometry is **frozen** (A1.5 shipped); A2 only adds gameplay content + two small scripts.

### In scope
- ≈20 destructible targets in curated clusters at the 5 formations + a thin plain scatter.
- 3 turrets at strongpoints (butte ring ×2, slot-canyon mouth ×1).
- A `Turret` prefab (`WorldTargetCube` + `AutoTurret`).
- A general `SurfaceSnap` component (generalises `SpawnSurfacePlacer`) for runtime surface-seating.
- A `DesertTargets` container in FlyScene holding the authored layout.

### Out of scope
- Cel renderer / outline / post-FX → **A3**.
- New target/turret archetypes or behaviours — reuse `WorldTargetCube` + `AutoTurret`
  **as-is** (no tracking AI, no new stats).
- Score / objectives / respawn / waves — A2 is **static placement only**.
- Geometry changes (frozen at A1.5).
- `DesertSandbox.unity` — untouched.

## 2. Decisions locked during brainstorming (2026-06-12)

| Topic | Decision |
|---|---|
| Placement philosophy | **Hybrid** — curated clusters at the 5 formations **+** a light plain scatter (not pure scatter, not landmarks-only). |
| Density | **Sparse:** ≈20 destructibles total (≈17 clustered, 3–4 scattered). Tunable in-editor. |
| Return fire | **3 turrets** at strongpoints: butte ring ×2 (defended arena) + slot-canyon mouth ×1. |
| Authoring | **Hand-authored** instances (curated set-pieces) — not a procedural scatter algorithm. |
| Surface seating | A reusable **`SurfaceSnap`** component raycasts each object onto the dune surface at runtime. |
| Placer consolidation | **Generalise** `SpawnSurfacePlacer` → `SurfaceSnap` (one component): construct uses it (clearance 9), targets/turrets use it (rest on surface). *Veto path → keep both, accept the duplication.* |
| Home | A **`DesertTargets` container in FlyScene** — **not** the `DesertEnvironment` prefab (keeps the prefab pure "world"; no leak into other instancers). |
| Layer | Targets/turrets on **`PlacedCube` (6)** — existing projectile masks already hit them; damage via `CubeStats.TakeDamage`. |

## 3. Building blocks

### 3.1 Destructible target — reuse `WorldTargetCube.prefab`
guid `ca09b41abaeee4ef688f331982eb3d05`. `CubeStats` HP 30 / AV 0 / mass 1, on the
`PlacedCube` layer. The projectile pipeline already routes damage through
`CubeStats.TakeDamage` (`effective = max(0, raw − AV)`; AV 0 = full damage), and the build
raycast masks already include `PlacedCube`, so **no edits** — reused exactly as the old
arena used it. *(Confirm at implementation: its component set, and whether it carries a
non-kinematic `Rigidbody` that `SurfaceSnap` must account for — see §6.)*

### 3.2 Turret — new `Turret.prefab`
A small prefab = a `WorldTargetCube` (so the turret is **itself destructible**, HP 30) with
`AutoTurret` added. `AutoTurret` fires a `Bullet` straight along its facing every 1 s for
40 dmg, **no tracking** (existing behaviour, unchanged). This replaces the old
AddComponent-onto-a-cube hack with a real, reusable asset. Each instance is **oriented** to
fire across its set-piece toward the likely player approach. *(Confirm `AutoTurret`'s
serialized fields — interval, damage, `Bullet` prefab reference — at implementation and wire
the `Bullet` prefab into the asset.)*

### 3.3 `SurfaceSnap` — new component (generalises `SpawnSurfacePlacer`)
`[DefaultExecutionOrder(-2000)]` MonoBehaviour. `Awake`: raycast straight down from
`(x, rayStartHeight, z)` against `terrainMask` (default = `World` layer), set
`transform.y = hit.point.y + clearance`; on a miss, fall back + warn. This is the **same
raycast** `SpawnSurfacePlacer` already does, lifted into a general, per-instance placer:

- **Construct:** `clearance ≈ 9` (hover spawn) — the A1.5 behaviour, unchanged.
- **Targets / turrets:** `clearance` ≈ half the object's height so its base rests on the
  sand (exact value read from the prefab bounds at implementation; optionally a `bottomAlign`
  mode that derives the offset from the collider so it is size-agnostic).

`SpawnSurfacePlacer.cs` is **renamed / folded into** `SurfaceSnap.cs` (`.meta` guid
**preserved** so the construct's `m_Script` reference still resolves), and the construct is
re-wired to `SurfaceSnap` (clearance 9). *Veto path: leave `SpawnSurfacePlacer` on the
construct and add `SurfaceSnap` only for targets — accepts ≈15 lines of near-duplicate code.*

## 4. Layout

### 4.1 Container
`DesertTargets` (empty GameObject at origin) in `FlyScene`:

```
DesertTargets
├── Cluster_MesaArch   (3 targets — flank the arch fly-through gateway)
├── Cluster_Hoodoos    (3 — perched among the spires)
├── Cluster_SlotCanyon (4 — strung along the lane)
├── Cluster_ButteRing  (4 — inside the arena)
├── Cluster_FinField   (3 — among the fins)
├── Scatter            (3–4 — thin, across the open plain)
└── Turrets            (3 — butte ring ×2, slot-canyon mouth ×1)
```

Every target/turret carries `SurfaceSnap`. **XZ** positions are authored by hand around each
formation's footprint; **Y** is left to `SurfaceSnap` at runtime.

### 4.2 Placement rules
- Clusters **hug** their formation (perched on / around the footprint, flanking lanes and the
  arch gateway) — not buried inside rock, not floating above it.
- **Scatter:** 3–4 across the open plain, ~30 u min-spacing, and **≥40 u clear of the spawn**
  `(0, 30, 60)` so the player is not under fire at spawn.
- **Turrets:** butte ring ×2 (flanking the arena interior), slot-canyon mouth ×1; faced to
  cover their set-piece. Tunable **down** if the re-fly feels heavy.
- Formation centres (XZ) for reference: Mesa+arch `(−150, 135)`; Hoodoos `(150, 165)`;
  SlotCanyon `(170, −20)`; ButteRing `(−30, −155)`; FinField `(−165, −150)`.

**Exact positions are tunable in-editor** — the counts and rules are the starting layout;
nudge during the re-fly.

## 5. Verification

No automated tests. Manual, in the Unity Editor on the **main project root** (per `CLAUDE.md`):

1. Compile/console clean after the 2 new scripts + scene edits.
2. Enter Play: **every target/turret sits ON the sand** (`SurfaceSnap` — none floating or
   buried); the construct still spawns clear and hovering.
3. **Fly + shoot (human-in-the-loop):** targets take damage and are destroyed
   (`CubeStats`); turrets return fire and are themselves destructible; the butte ring reads
   as a defended arena; the open plain is contested but not a carpet.
4. Frame cost unaffected vs A1.5.
5. `DesertSandbox.unity` still opens unchanged.

**Decision gate (end of A2):** ship / iterate / shelve — recorded before A3.

### A2 outcome (2026-06-12) — **SHIP**

Re-flown by the maintainer — **plays great** ("working great, nothing to change"). FlyScene now
reads as a combat arena, not an empty flythrough. Built + verified through the per-task checks:
- `SpawnSurfacePlacer` generalised → `SurfaceSnap` (guid preserved; the construct rebinds with
  `clearance 9` intact; runtime-seats the construct at surface+9 on the dunes).
- `DesertTarget` + `Turret` created as **prefab variants** of `WorldTargetCube` (inherit
  `CubeStats` HP30/AV0, `PlacedCube` layer); both carry `SurfaceSnap` (clearance 0.5 = base
  rests on the sand). `WorldTargetCube` has no `Rigidbody`, so targets stay put once seated.
- 21 destructibles: clusters at the 5 formations (5 that snapped onto rock tops / a perimeter
  ridge were relocated onto the dune apron so all sit at flight level) + a 4-target plain
  scatter (82–136 u from spawn, all ≥40 u clear).
- 3 turrets: butte-ring pair re-aimed to fire **across** the arena interior (60–70 u clear
  traverse) after the first aim splashed on the near wall; slot-canyon turret fires down-lane.
  All enabled (bullet wired), seated on dune.
- Automated smoke test confirmed seating + turrets-enabled + clean console; firing/combat
  confirmed by the maintainer's focused Play session (the headless MCP sim stays frozen at
  frame 1, so firing can't be auto-verified).

**Known rough edge carried to A3:** turret cubes are visibly tilted — `AutoTurret` fires along
local +Y, so aiming = tilting the cube. Cosmetic; the A3 cel/visual pass addresses it.

**Next:** A3 (adopt the cel-shader / `Desert_Renderer` outline look). Geometry + gameplay are
now stable underneath it.

## 6. Risks & notes
- **Placer consolidation** re-touches the A1.5/Copilot-reviewed construct wiring (low risk:
  guid-preserved rename + a clearance field). The veto path keeps `SpawnSurfacePlacer`.
- **`SurfaceSnap` vs `Rigidbody`:** if `WorldTargetCube` has a non-kinematic `Rigidbody`,
  snapping in `Awake` before the first physics step is fine, but confirm targets don't drift
  or sleep oddly after the snap (set kinematic / zero-gravity, or snap the body too, if needed).
- **Turret fairness:** no tracking, fixed 1 s / 40 dmg — at basin scale turrets may be
  trivially easy or quietly annoying; tune count/facing at the re-fly, don't pre-balance.
- **Spawn safety:** enforce the ≥40 u scatter-clear-of-spawn rule so the player isn't fired
  on at spawn.
- **Reversibility:** all additive (one container + two scripts + one prefab) on a throwaway branch.

## 7. File manifest

**New**
- `Assets/Scripts/Desert/SurfaceSnap.cs` (+ `.meta`) — general surface-snap placer (from `SpawnSurfacePlacer`).
- `Assets/Prefabs/Desert/Turret.prefab` (+ `.meta`) — `WorldTargetCube` + `AutoTurret`.

**Edited**
- `Assets/Scenes/FlyScene.unity` — add the `DesertTargets` container (≈20 targets + 3 turrets);
  re-wire the construct to `SurfaceSnap`.

**Removed** *(if consolidation accepted)*
- `Assets/Scripts/Desert/SpawnSurfacePlacer.cs` (+ `.meta`) — folded into `SurfaceSnap`.

**Unchanged**
- `WorldTargetCube.prefab`, `AutoTurret.cs`, `DesertEnvironment.prefab`, `DesertGround_500.asset`,
  `DesertSandbox.unity`, all geometry, the `World` layer.
