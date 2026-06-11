# Milestone A1.5 — Scale up the desert basin — Design Spec

**Date:** 2026-06-11
**Branch:** `explore/desert-flyscene` (experimental — not on `main`)
**Roadmap:** item 4, Milestone A, sub-phase **A1.5** (`ROADMAP.md §4`).
**Predecessor:** A1 (`docs/superpowers/specs/2026-05-31-desert-flyscene-a1-design.md`) — done, gate = ITERATE.
**Status:** Accepted design, pre-implementation.

## 1. Purpose & scope

A1 integrated the desert into FlyScene successfully but the 200×200 basin plays as a tight
arena — it won't hold large player constructs (a construct wider than the Mesa+Arch opening
can't fly through). A1.5 grows the basin to a **vast 500×500** desert: huge mesas and deep
canyons around a **vast open central plain**, with lanes/arches/canyons wide enough that an
oversized construct can still navigate.

Continues on `explore/desert-flyscene`, editing the existing `DesertEnvironment` prefab
(`Assets/Prefabs/Desert/DesertEnvironment.prefab`) and its children via UnityMCP. A2 (scatter
targets) and A3 (cel-shader/outline renderer) still follow after — A1.5 is geometry/scale only,
still under the current renderer.

### In scope
- Regenerate the dune ground at 500×500 (new baked mesh asset).
- Scale the 5 formations ×1.2 (XZ/base) + ×1.1 additional on Y, and re-space them outward.
- Expand the perimeter ridge to close the 500×500 boundary.
- Move the spawn into the open plain; re-verify clearances at the new scale.

### Out of scope (unchanged from A1)
- Cel renderer / outline / post-FX reconciliation → **A3**.
- Target placement → **A2**.
- New formation archetypes (keep the 5 — maintainer call 2026-06-11).
- Vertical flight bounds (still ridge-only).
- DesertSandbox.unity — stays the standalone **200×200 reference**; must not break.

## 2. Decisions locked during brainstorming (2026-06-11)

| Topic | Decision |
|---|---|
| Ground mesh | **Regenerate** at `size=500`, `resolution=600` (higher detail), bake to a **NEW** asset `DesertGround_500.asset`; repoint the prefab. Do NOT scale the existing mesh; do NOT overwrite the shared `DesertGround.asset`. |
| Formations | **Keep the 5** (no new archetypes). Scale each to `(1.2, 1.32, 1.2)` (×1.2 XZ, ×1.32 Y = 1.2 × 1.1). |
| Formation layout | **Push outward toward the ridge** (~±150 from centre) so the centre is a vast open plain — layout "A". |
| Perimeter ridge | **Duplicate** the existing 20 ridge pieces out to **~50** around a larger ~258-radius ring, varying rotation (face inward) + Y so it doesn't read tiled. Do NOT stretch the original 20. |
| Spawn | Move to the open central plain, facing the Mesa+Arch hero; `SpawnSurfacePlacer` re-seats altitude on the new ground. |

## 3. Ground — regenerate at 500×500, higher detail

`DuneGroundGenerator.BuildMesh()` builds a `size × size` grid of `resolution × resolution`
cells (step = `size/resolution`), displaced by layered Perlin noise (swell/dune/ripple). At
the A1 values (200/200) step = 1u. For A1.5:

- Set the generator on the prefab's `DuneGround` to `size = 500`, `resolution = 600`
  (step ≈ 0.83u — slightly finer dunes; `601² ≈ 361k` verts, fine with the existing
  `IndexFormat.UInt32`). Keep the noise amplitudes/frequencies/seed as-is (the same dune
  character, just more of it across the bigger extent).
- Bake to a **new asset** `Assets/Models/DesertGround_500.asset`. **Critical:** the existing
  `DesertGround.asset` (guid `9237d5cf8ca344d9990b461d3407590d`) is referenced by BOTH the
  prefab AND `DesertSandbox.unity`, and the generator's editor "Generate" button
  `CopySerialized`s into the existing asset **in place** — so regenerating onto it would
  clobber the 200 mesh DesertSandbox still uses. A1.5 must write a NEW asset and leave the
  original alone. (Implementation: call `BuildMesh()` and `AssetDatabase.CreateAsset` to the
  new path directly via `execute_code`, rather than the in-place editor button.)
- Repoint the prefab's `DuneGround` `MeshFilter.sharedMesh` AND `MeshCollider.sharedMesh` to
  `DesertGround_500.asset`.

Result: a true 500×500 dune field at ~1u density; DesertSandbox keeps its original mesh.

## 4. Formations — scale ×1.2 / +×1.1 Y, push to the rim

Scale each `Formation_*` transform to `localScale = (1.2, 1.32, 1.2)`. Re-space the five
outward to ~±150, preserving each formation's quadrant/role (so the composition the player
liked is retained, just bigger and spread). Current vs. target centers (XZ; from measured
bounds, base at ground level):

| Formation | Now (XZ) | A1.5 center (XZ) | size now (XZ) | size ×1.2 |
|---|---|---|---|---|
| MesaArch (hero) | (−50, 45) | **(−150, 135)** | 69×36 | ~83×43 |
| HoodooSpires | (45, 60) | **(150, 165)** | 39×32 | ~47×38 |
| SlotCanyon | (55, 0) | **(170, −20)** | 57×95 | ~68×114 |
| ButteRing | (0, −50) | **(−30, −155)** | 109×108 | ~131×130 |
| FinField | (−50, −50) | **(−165, −150)** | 38×49 | ~46×59 |

**Base re-seat (Y):** the regenerated dunes have new heights, so after repositioning each
formation in XZ, raycast its center down onto the new `DesertGround_500` surface (World layer)
and set the formation's Y so its scaled base (`bounds.min.y`) sits ≈ on the surface (the A1
formations had base at the dune surface ≈ y −6..−7). Verify in-editor; nudge any that
float/sink. This mirrors the `SpawnSurfacePlacer` surface-snap logic.

**Exact centers are tunable in-editor:** the table is the starting layout; nudge during the
play-test if a lane is too tight or a formation overlaps the ridge.

## 5. Perimeter ridge — expand to ~50 pieces

The 20 ridge pieces ring the 200 basin at radius ~108 (each at |X| or |Z| ≈ 108, with
per-piece Y 14–23 and its own rotation). For the 500 basin (half-extent 250), build a ring at
radius ~258:

- Duplicate the existing 20 ridge-piece instances (clone their ProBuilder meshes — no new
  authoring) and distribute **~50 total** around the larger ring.
- Place each on the ring at its angle, **rotated to face inward**, with small per-piece Y and
  yaw jitter (reuse the natural variation already in the 20) so the wall reads as natural rock,
  not a tiled fence.
- Done via `execute_code` (instantiate clones of the existing Ridge_NN GameObjects, set
  ring positions/rotations). All on the `World` layer, all keep their MeshColliders.
- **Verify full closure:** after placement, confirm there are no gaps wide enough for the
  construct to fly out the side (visual + a coarse perimeter check).

## 6. Spawn + re-verify at new scale

- Move `CubeConstruct` to the open central plain — start point **(0, _, 60)** (Y provisional;
  `SpawnSurfacePlacer` re-seats it on the new ground at runtime) — rotated to face the
  Mesa+Arch hero at (−150, 135). Confirm (0,60) is clear of all repositioned footprints.
- Re-verify at the new scale (the A1.5 acceptance criteria):
  - **Arch opening width** — measure the Mesa+Arch's actual fly-through gap at ×1.2; confirm
    it's wide enough for a deliberately-large construct.
  - **Slot-canyon width** — same, the narrowest navigable lane.
  - **Spawn lane** clear; **ridge** fully closes the perimeter.

## 7. Unchanged

`World` layer, `SpawnSurfacePlacer` (component + wiring), the prefab-instance structure in
FlyScene, atmosphere/RenderSettings, and all gameplay objects. **Atmosphere note:** sightlines
are ~2.5× longer now, so the subtle fog (start 90) may want a small bump — flag for the re-fly,
not changing pre-emptively. DesertSandbox.unity untouched.

## 8. Verification

No automated tests. Manual, in the Unity Editor on the main project root (per `CLAUDE.md`):

1. Compile/console clean after the regenerate + scene edits.
2. Enter Play: construct spawns clear and hovering on the new central plain; no errors.
3. **Re-fly (human-in-the-loop):** is there room for a large construct — arch + canyons wide
   enough? Does the centre read as a vast open plain with huge mesas around it? Ridge contains?
   Terrain still collides (no fall-through on the new mesh)?
4. Confirm DesertSandbox still opens with its original 200 mesh (reference intact).

**Decision gate (end of A1.5):** ship / iterate / shelve — recorded before A2.

## 9. Risks & notes

- **Collider cost:** a 500×500 @ res 600 `MeshCollider` + ~50 ridge `MeshCollider`s + 5 larger
  formation colliders is materially heavier than A1. Watch Play-mode frame cost; if it
  regresses, options are lowering ground resolution or giving the ground a simpler collision
  mesh (flag, don't pre-optimise).
- **Shared-asset trap:** §3 — must bake to a NEW mesh asset; clobbering `DesertGround.asset`
  breaks DesertSandbox. This is the single most important correctness point.
- **Formation re-seat** on new dune heights is fiddly — raycast-snap + in-editor eyeball.
- **Ridge gaps:** confirm the duplicated ring fully closes the 500 perimeter.
- **Reversibility:** still one prefab on a throwaway branch; the new mesh asset is additive.

## 10. File manifest

**New**
- `Assets/Models/DesertGround_500.asset` (+ `.meta`) — the regenerated 500×500 dune mesh.

**Edited**
- `Assets/Prefabs/Desert/DesertEnvironment.prefab` — `DuneGround` generator params + mesh/collider
  repoint; 5 formations scaled + repositioned + Y-reseated; ridge expanded to ~50 pieces.
- `Assets/Scenes/FlyScene.unity` — `CubeConstruct` spawn position/rotation (+ possibly a fog
  start tweak after the re-fly).

**Unchanged**
- `DesertGround.asset` (original 200 mesh), `DesertSandbox.unity`, `SpawnSurfacePlacer.cs`,
  `DuneGroundGenerator.cs`, `World` layer, all gameplay scripts.
