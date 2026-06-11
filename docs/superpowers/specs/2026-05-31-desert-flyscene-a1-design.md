# Milestone A1 — Desert terrain into FlyScene — Design Spec

**Date:** 2026-05-31
**Branch:** `explore/desert-flyscene` (experimental — this spec is intentionally NOT on `main`)
**Roadmap:** item 4, Milestone A, sub-phase **A1** (see `ROADMAP.md §4`).
**Status:** Accepted design, pre-implementation.

## 1. Purpose & scope

Swap FlyScene's flat 200×200 ground plane (+ 20 `WorldTargetCube`s + 4 `AutoTurret`s)
for the desert dune basin — the baked dune ground, the perimeter ridge, and the five
hero formations — **under the current renderer**, then play-test navigation and feel.

This is the first of three desert sub-phases. The two risks are deliberately split so
each gets its own play-test:

- **A1 (this spec):** terrain in, current rendering. *Does the basin navigate well?*
- **A2 (later):** scatter destructible targets through the formations.
- **A3 (later):** adopt the cel-shader + screen-space outline renderer, reconcile ship /
  projectiles / VFX / HUD / post-FX.

### In scope
- A new `DesertEnvironment` prefab bundling the desert geometry.
- Dropping one instance into `FlyScene.unity`; removing the old ground + targets + turrets.
- Repositioning the construct spawn to a safe, hero-facing open dune lane.
- Carrying over the desert's warm lighting/ambient/sky + a **subtle** distance fog.

### Out of scope (deferred by decision)
- The cel shader / outline renderer and any post-FX (`DesertVolumeProfile`) reconciliation → **A3**.
- Placing/tuning combat targets on the new terrain → **A2**.
- Any new vertical flight bounds (ridge-only, matching the demonstrator).
- Touching the `MainMenu → HangarSelect → BuildScene → FlyScene` scene flow (FlyScene stays
  the same scene asset; only its contents change).

## 2. Decisions locked during brainstorming (2026-05-31)

| # | Decision | Choice |
|---|---|---|
| Spawn location | Open dune lane facing the Mesa+Arch hero formation | **C** |
| Spawn altitude | Raycast-to-surface + clearance, guaranteed no clip into plane/dunes/formations | (user requirement) |
| Atmosphere | Warm ambient + light directional + gradient sky + **subtle** distance fog; no post-FX swap | **B (subtle fog)** |
| Existing targets/turrets | Remove the 20 `WorldTargetCube`s + 4 `AutoTurret`s in A1 | **Remove** |
| Flight bounds | Ridge-only (perimeter ridge colliders contain the sides; vertical unbounded) | **A** |
| Integration mechanic | Bundle ground + ridge + formations into ONE `DesertEnvironment` prefab, single instance in FlyScene | **B** |

## 3. The `DesertEnvironment` prefab

New asset: `Assets/Prefabs/Desert/DesertEnvironment.prefab` (with its paired `.meta`).

Under one root GameObject, composed from **existing** project assets (no re-authoring of
geometry):

```
DesertEnvironment            (empty root at origin)
├─ DuneGround                MeshFilter = DesertGround.asset + MeshCollider (Sand material)
├─ PerimeterRidge            parent of the 20 Ridge_00..19 pieces, each with its MeshCollider
└─ Formation_MesaArch        prefab instance @ (-50, y, +45)
   Formation_HoodooSpires    prefab instance @ (+45, y, +60)
   Formation_SlotCanyon      prefab instance @ (+55, y, ±0)
   Formation_ButteRing       prefab instance @ (0, y, -50)
   Formation_FinField        prefab instance @ (-50, y, -50)
```

Positions are the demonstrator's (`desert_level_spec.md §4.3`), lifted from the
`DesertSandbox.unity` instances so they sit correctly on the dune ground.

**Why a prefab:** a single instance in FlyScene means (a) a tiny scene diff, (b) the whole
desert toggles on/off as one object — clean A/B during the play-test and a trivial revert
if the experiment is shelved, and (c) reuse in any future scene. The prefab also gives the
implementation a place to author the spawn-surface helper (see §5) independent of FlyScene.

## 4. Layer & collision

The construct is a non-kinematic Rigidbody compound collider; bullets/rockets/laser use
**swept raycasts** masked to `PlacedCube | AlphaCube`. The desert geometry must:

- **collide** with the construct's Rigidbody (so the ship physically bounces off dunes,
  ridge, and formations — the whole point of fly-through), and
- **not** be treated as a damageable target by projectile raycasts.

Plan: place the `DuneGround`, `PerimeterRidge`, and formation colliders on a dedicated
non-target layer (e.g. a `World`/`Terrain` layer; reuse an existing suitable layer if one
exists, else add one in `TagManager.asset` with its paired meta discipline). The default
collision layer collides with the construct, while the projectile masks already exclude
anything outside `PlacedCube | AlphaCube`, so terrain won't run `CubeStats` damage.

**Implementation must verify:** (a) the exact projectile-mask interaction with whatever
layer the terrain lands on, and (b) that the `PreviewCube`-isolation matrix work from AP-13
doesn't conflict. If a new layer is introduced, double-check the physics collision matrix
allows construct-vs-terrain.

## 5. Spawn placement & guaranteed-safe altitude

The `CubeConstruct` GameObject's own transform **is** the spawn point — `FlyController`
instantiates the alpha + placed cubes as children of it in `Start`. So spawn = where we
put `CubeConstruct` (and its rotation).

1. **Location:** reposition `CubeConstruct` into an open dune lane in the `(+20…+30, _, −35…−45)`
   region, **rotated so `forward` points at the Mesa+Arch hero** at `(−50, +45)`. Exact XZ
   and yaw are tuned in-editor during implementation (the lane must be clear of formation
   footprints — confirm against the §3 positions).

2. **Safe altitude — `SpawnSurfacePlacer` component** (new, small, on `CubeConstruct`):
   - Runs in `Awake`, i.e. **before** `FlyController.Start`/`BuildConstruct` so the cubes
     instantiate at the corrected height.
   - Raycasts straight **down** from well above the spawn XZ (e.g. from `y = +200`) against
     the terrain layer, finds the surface hit, and sets
     `transform.position.y = hit.point.y + clearance`.
   - `clearance` ≈ 8–10u above the construct's own half-height, so even a max-height dune
     (~+9u) or a formation under the lane can't clip the ship.
   - **Fallback:** if the ray misses (no terrain under the point), use a safe fixed Y
     (e.g. the current `+10`, or higher) and log a warning — never spawn at an unknown height.
   - Gravity is off on the construct Rigidbody, so the ship simply **hovers** at the safe
     height until the player thrusts; it will not settle onto a dune.

This satisfies the hard requirement: the construct can never glitch into the plane, a dune,
or a formation at spawn, regardless of the lane's terrain height.

## 6. Atmosphere (current renderer, subtle fog)

Carry the desert's *atmosphere* without the cel shader:

- **Lighting / ambient:** adopt the desert scene's warm directional light orientation/color
  and its ambient (`m_AmbientMode: 0` trilight — sky `(0.212,0.227,0.259)`, equator
  `(0.114,0.125,0.133)`, ground `(0.047,0.043,0.035)`, intensity 1). Applied via FlyScene's
  `RenderSettings` + the Directional Light.
- **Sky:** carry over the **gradient skybox** material (`DesertSky.mat`, guid
  `77d63470f0b284cb98fccb940932f661`) so the horizon reads as a warm desert rather than
  Unity's default blue.
- **Distance fog — ON but subtle:** linear mode, warm tan color (`~0.91,0.81,0.62`), but
  **`LinearFogStart` pulled out to ~80–100u** (vs. the demonstrator's 45) with
  `LinearFogEnd ~280` — so near and mid geometry is crystal-clear and only distant
  formations gently haze into the horizon. Final start value tuned in-editor; the intent is
  "barely-there dusty depth cue," not a visible wall.
- **No post-FX change:** FlyScene keeps its current Volume / post-processing. The
  `DesertVolumeProfile` (warm grade + bloom) is an A3 concern.

## 7. Bounds & camera

- **Bounds:** ridge-only. The `PerimeterRidge` MeshColliders physically contain flight on
  the sides; vertical flight is unbounded (matches the demonstrator). No new bounds code in A1.
- **Camera:** no change. `FlyCamera` uses `GameData.GetConstructBounds()` only to size its
  follow *distance* from the construct, never the world — so terrain has no effect on it.
  Recorded here so implementation doesn't go looking.

## 8. Removed in A1

From `FlyScene.unity`: the `Ground` prefab instance, all 20 `WorldTargetCube` instances,
and all 4 `AutoTurret` instances. (A2 re-introduces targets placed on the dune surface.)
Removing the turrets also removes the only `AutoTurret` users; the script stays in the
project for A2.

## 9. Verification

No automated tests (project has none by design). Manual, in the Unity Editor on the **main
project root** (the live Editor runs there, not in the Claude worktree — so the branch's
edited files must reach the root checkout to Play-mode-verify; see `CLAUDE.md`):

1. Compile clean — `read_console` shows no errors after the scene/script changes.
2. Enter Play in FlyScene: construct spawns **clear and hovering** in the hero-facing lane,
   nothing clipped, no missing-script / null-ref errors.
3. Fly the basin: navigation reads well at the ~3–6u ship scale; the canyons/arches/fins
   are flyable; formations read at distance through the subtle fog; the ridge contains the
   sides; the ship bounces off terrain rather than passing through.
4. Confirm the old flat arena / targets / turrets are gone and nothing references them.

**Decision gate (end of A1):** ship / iterate / shelve — recorded before moving to A2.

### A1 outcome (2026-06-11) — **ITERATE**

Play-tested. Core integration is a success and the play *feel* is liked:
- Navigation at ship scale: **partial** — flyable, but the basin reads as a tight arena.
- Formations read at distance through the subtle fog: **yes**.
- Ridge contains the sides: **yes**.
- Ship bounces off terrain (dunes + rock) rather than passing through: **yes**.
- Feels like the desert the spec promised: **yes**.
- Automated checks: spawn placer seated the construct at y=8.88 on `DuneGround` (raycast
  working); zero errors in Play; Valley-of-Fire palette + subtle (non-wall) fog render correctly.

**Iterate reason — scale.** Constructs can be built arbitrarily large, so a tight arena will
break down (e.g. a construct wider than the Mesa+Arch opening can't fly through). The desert
needs to feel **vast** — huge mesas, deep canyons, an open central plain.

**Agreed scale-up (→ new sub-phase A1.5, see ROADMAP §4):**
- Ground extent **200 → 500** (≈ the `DuneGroundGenerator.size`/resolution + the baked mesh).
- Perimeter ridge: **do not stretch** the existing 20 pieces (unnatural); **add more ridge
  pieces** as needed to re-close the larger 500×500 perimeter naturally.
- **Keep the 5 existing formations** (no new archetypes) — scale **×1.2** (XZ + base) plus an
  **additional ×1.1 on Y** (taller mesas/walls), and re-space them across the larger basin so
  lanes/canyons/arches are wide enough for big constructs. Accept a sparser, vast-empty-plain
  feel in the middle by design (per maintainer call 2026-06-11).
- Re-verify spawn lane clearance + arch/canyon widths at the new scale.

This is a scene-geometry restructuring of its own; tracked as **A1.5** and given its own
spec → plan cycle (brainstorm starting now, 2026-06-11) rather than folded into A1's commits.

## 10. Risks & notes

- **Terrain collider cost:** the dune `MeshCollider` + 20 ridge `MeshCollider`s + 5
  formations are concave static colliders. Fine for a static world vs. one Rigidbody, but
  watch the Play-mode frame cost; flag if it regresses.
- **Spawn lane vs. formation footprints:** the chosen lane XZ must not overlap a formation
  footprint — verify against §3 positions when tuning.
- **Layer/matrix interaction:** §4 must be verified against the existing projectile masks
  and the AP-13 `PreviewCube` matrix change, not assumed.
- **Reversibility:** the whole experiment is one prefab instance + RenderSettings on a
  throwaway branch; shelving = delete the instance / abandon the branch.

## 11. File manifest

**New**
- `Assets/Prefabs/Desert/DesertEnvironment.prefab` (+ `.meta`)
- `Assets/Scripts/Desert/SpawnSurfacePlacer.cs` (+ canonical `.meta`)
- possibly a new layer entry in `ProjectSettings/TagManager.asset` (if no suitable layer exists)

**Edited**
- `Assets/Scenes/FlyScene.unity` — remove Ground/targets/turrets; add one `DesertEnvironment`
  instance; reposition `CubeConstruct`; update `RenderSettings` (ambient/fog/skybox) + the
  Directional Light.

**Unchanged**
- All Fly gameplay scripts (FlyController, FlyCamera, shooting, etc.), the other three
  scenes, the desert source assets (`DesertSandbox.unity` stays as the standalone reference).
