# Desert FlyScene A2 — Combat Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Repopulate the A1.5 desert basin with ~20 destructible targets (curated landmark clusters + a thin plain scatter) and 3 turrets, so FlyScene plays as a combat arena instead of an empty flythrough.

**Architecture:** Two small prefab variants of the existing `WorldTargetCube` — `DesertTarget` (adds a `SurfaceSnap` placer) and `Turret` (adds `SurfaceSnap` + the existing `AutoTurret`) — are hand-placed into a `DesertTargets` container in FlyScene. `SurfaceSnap` (a generalisation of the existing `SpawnSurfacePlacer`) raycasts each object onto the dune surface at runtime, so we author XZ only and never hand-tune heights. Geometry is frozen at A1.5; current renderer (cel look = A3).

**Tech Stack:** Unity 6.3 LTS / URP, MonoBehaviour, `Assembly-CSharp`, namespace `CubeFly.Desert`. All edits via UnityMCP against the **main project root** (the `explore/desert-flyscene` branch is checked out there and the live Editor runs against it — **not** a Claude worktree, per `CLAUDE.md`). Verification = `read_console` + manual Play-mode.

**Spec:** `docs/superpowers/specs/2026-06-12-desert-flyscene-a2-design.md`

---

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `Assets/Scripts/Desert/SurfaceSnap.cs` | General Awake raycast-down placer: seats any object on `terrainMask` at `surface + clearance`, with a fallback. | **Rename** from `SpawnSurfacePlacer.cs` (guid preserved) + rename class. |
| `Assets/Scripts/Desert/SpawnSurfacePlacer.cs` | (folded into `SurfaceSnap`) | **Remove** (via the rename). |
| `Assets/Prefabs/Desert/DesertTarget.prefab` | A destructible target that auto-seats on the dunes. | **Create** — variant of `WorldTargetCube` + `SurfaceSnap`. |
| `Assets/Prefabs/Desert/Turret.prefab` | A destructible, surface-seated turret that fires. | **Create** — variant of `WorldTargetCube` + `SurfaceSnap` + `AutoTurret`. |
| `Assets/Scenes/FlyScene.unity` | Adds the `DesertTargets` container (clusters + scatter + turrets). Construct's placer becomes `SurfaceSnap` automatically (guid-stable). | **Modify**. |

**Decoupling note:** `WorldTargetCube.prefab` and `AutoTurret.cs` are reused **untouched** — the new behaviour lives entirely in the two variants + `SurfaceSnap`. Using prefab *variants* (not per-instance components) keeps the `SurfaceSnap` clearance and turret wiring DRY: set once on the variant, drop instances freely.

---

## Task 1: Generalise `SpawnSurfacePlacer` → `SurfaceSnap`

Rename the existing spawn-only placer into a general, reusable surface-snap component. The guid is preserved, so the construct's existing component reference in FlyScene keeps resolving — it just becomes a `SurfaceSnap` with its `clearance = 9` intact.

**Files:**
- Rename: `Assets/Scripts/Desert/SpawnSurfacePlacer.cs` → `Assets/Scripts/Desert/SurfaceSnap.cs`
- Rename: `Assets/Scripts/Desert/SpawnSurfacePlacer.cs.meta` → `Assets/Scripts/Desert/SurfaceSnap.cs.meta` (guid `c0310aadcd51c4e1d84fb1d907ca0263` unchanged)
- Reference (do not edit): `Assets/Scenes/FlyScene.unity` (construct's `m_Script` points at the guid above)

- [ ] **Step 1: Read the current script** so the rename preserves all fields verbatim.

Read `Assets/Scripts/Desert/SpawnSurfacePlacer.cs`. Expected shape (confirm before editing):

```csharp
namespace CubeFly.Desert {
  [DefaultExecutionOrder(-2000)]
  public class SpawnSurfacePlacer : MonoBehaviour {
    [SerializeField] LayerMask terrainMask = ~0;
    [SerializeField] float clearance = 9f;
    [SerializeField] float rayStartHeight = 200f;
    [SerializeField] float fallbackHeight = 20f;
    void Awake() { /* raycast down, set y = hit.point.y + clearance, else fallback + warn */ }
  }
}
```

- [ ] **Step 2: Rename the files (guid preserved).**

Run (on the main project root):
```bash
cd "/Users/anon/My project"
git mv Assets/Scripts/Desert/SpawnSurfacePlacer.cs.meta Assets/Scripts/Desert/SurfaceSnap.cs.meta
git mv Assets/Scripts/Desert/SpawnSurfacePlacer.cs    Assets/Scripts/Desert/SurfaceSnap.cs
```
(Renaming the `.meta` with the same guid is what keeps the FlyScene reference valid.)

- [ ] **Step 3: Rename the class** to match the new filename (Unity requires MonoBehaviour filename == class name). In `SurfaceSnap.cs`, change only the class identifier — keep every `[SerializeField]` field name identical so serialized values (the construct's `clearance = 9`) survive:

```csharp
public class SurfaceSnap : MonoBehaviour {
```

Update the XML/summary comment if one names the old class. Tighten the class summary to reflect the general purpose, e.g. `/// Seats this object on the terrain surface (raycast down) at Awake.`

- [ ] **Step 4: Compile-check.**

Call `refresh_unity`, then `read_console` (filter Error). Expected: **no errors** (ignore any transient "name doesn't match" only if it appears *before* the refresh; the post-refresh console must be clean). Poll `editor_state.isCompiling` until false.

- [ ] **Step 5: Verify the construct reference survived.**

Use `find_gameobjects` / `manage_gameobject` to inspect the `CubeConstruct` in FlyScene: confirm it has a `SurfaceSnap` component (not "missing script"), with `clearance = 9`, `terrainMask = World`. If it shows missing, re-assign the `SurfaceSnap` script to the component and re-set `clearance = 9`, `terrainMask = World`.

- [ ] **Step 6: Play-mode smoke test.**

`manage_editor` enter Play. Confirm the construct still spawns hovering clear above the central plain (the A1.5 behaviour), no console errors. Exit Play.

- [ ] **Step 7: Commit.**

```bash
cd "/Users/anon/My project"
git add Assets/Scripts/Desert/SurfaceSnap.cs Assets/Scripts/Desert/SurfaceSnap.cs.meta
git commit -m "refactor(desert): generalise SpawnSurfacePlacer -> SurfaceSnap (A2)

Same Awake raycast-down placer, renamed for reuse by targets/turrets.
Guid preserved so the FlyScene construct reference resolves unchanged
(clearance 9 retained). Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```
(Git records this as a rename of both files.)

---

## Task 2: `DesertTarget.prefab` — destructible that self-seats

A variant of `WorldTargetCube` that adds `SurfaceSnap` so every instance drops onto the dune surface at runtime.

**Files:**
- Create: `Assets/Prefabs/Desert/DesertTarget.prefab` (+ `.meta`)
- Reference (do not edit): `Assets/Prefabs/Desert/WorldTargetCube.prefab` (guid `ca09b41abaeee4ef688f331982eb3d05`)

- [ ] **Step 1: Read the base cube's size** so `clearance` rests the base on the sand.

Inspect `WorldTargetCube.prefab` (via `manage_prefabs` get_hierarchy / `manage_gameobject`): record the `BoxCollider`/renderer extents. Compute `halfHeight = bounds.extents.y`. Also confirm whether it carries a `Rigidbody` (and if so, whether kinematic) — see Step 5.

- [ ] **Step 2: Create the variant.**

Instantiate `WorldTargetCube` into a prefab stage (or scene), then save a **prefab variant** named `DesertTarget` at `Assets/Prefabs/Desert/DesertTarget.prefab` (keep the link to `WorldTargetCube` so HP/AV/collider/material changes inherit). If MCP cannot create a variant directly, fall back to `manage_prefabs.create_from_gameobject` on a `WorldTargetCube` instance (a flat prefab) and note the loss of the variant link in the commit body.

- [ ] **Step 3: Add `SurfaceSnap`** to the variant root via `manage_components` (the same GameObject that holds `CubeStats`, so the snapped transform is the target itself). Set:
  - `terrainMask` = the `World` layer only. (LayerMask is a struct — set it via `execute_code` `SerializedObject(...).FindProperty("terrainMask").intValue = 1 << LayerMask.NameToLayer("World")`, not a raw int through `manage_components`.)
  - `clearance` = `halfHeight` from Step 1 (so the base sits on the surface).
  - `rayStartHeight` = 200, `fallbackHeight` = 20 (defaults are fine).

- [ ] **Step 4: Confirm layer + stats unchanged.** The variant root stays on the `PlacedCube` layer (6) and keeps `CubeStats` HP 30 / AV 0 / mass 1 (inherited). Verify via `manage_gameobject`.

- [ ] **Step 5: Rigidbody check.** If Step 1 found a non-kinematic `Rigidbody`, set it kinematic (or remove gravity) on the variant so targets stay where `SurfaceSnap` places them instead of toppling. If there is no `Rigidbody` (stationary like the old arena), do nothing.

- [ ] **Step 6: Compile/console check.** `refresh_unity` + `read_console` (Error filter) → clean.

- [ ] **Step 7: Commit.**

```bash
cd "/Users/anon/My project"
git add Assets/Prefabs/Desert/DesertTarget.prefab Assets/Prefabs/Desert/DesertTarget.prefab.meta
git commit -m "feat(desert): DesertTarget prefab (WorldTargetCube + SurfaceSnap) (A2)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: `Turret.prefab` — destructible turret that fires

A variant of `WorldTargetCube` that adds `SurfaceSnap` **and** the existing `AutoTurret`, so it self-seats, returns fire, and is itself destructible.

**Files:**
- Create: `Assets/Prefabs/Desert/Turret.prefab` (+ `.meta`)
- Reference (do not edit): `Assets/Scripts/Fly/AutoTurret.cs`, the `Bullet` prefab used by the weapon system.

- [ ] **Step 1: Read `AutoTurret`'s serialized fields.** Open `Assets/Scripts/Fly/AutoTurret.cs` and record the exact field names for: fire interval (expect ~1 s), damage (expect 40), and the projectile/`Bullet` prefab reference. These drive Step 3's wiring.

- [ ] **Step 2: Create the variant** `Assets/Prefabs/Desert/Turret.prefab` the same way as Task 2 (variant of `WorldTargetCube`, or flat fallback).

- [ ] **Step 3: Add components + wire the bullet.** Via `manage_components`:
  - Add `SurfaceSnap` with the same settings as Task 2 Step 3 (`terrainMask` = World, `clearance` = halfHeight).
  - Add `AutoTurret`. Assign its `Bullet`-prefab field to the project's `Bullet` prefab (find it with `find_gameobjects`/`manage_asset`); leave interval/damage at their defaults (1 s / 40) unless Step 1 shows otherwise.

- [ ] **Step 4: Compile/console check.** `refresh_unity` + `read_console` (Error) → clean. Confirm `AutoTurret` reports no unassigned-reference warning for the bullet.

- [ ] **Step 5: Commit.**

```bash
cd "/Users/anon/My project"
git add Assets/Prefabs/Desert/Turret.prefab Assets/Prefabs/Desert/Turret.prefab.meta
git commit -m "feat(desert): Turret prefab (WorldTargetCube + SurfaceSnap + AutoTurret) (A2)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: `DesertTargets` container + landmark clusters

Create the scene container and place the 17 clustered `DesertTarget` instances. Author **XZ only** — `SurfaceSnap` sets Y at runtime. Positions below are the starting layout (nudge in-editor if any lands inside rock or floats above its formation).

**Files:**
- Modify: `Assets/Scenes/FlyScene.unity`

- [ ] **Step 1: Create the container.** In FlyScene, create an empty GameObject `DesertTargets` at world origin `(0,0,0)`. Add empty child groups: `Cluster_MesaArch`, `Cluster_Hoodoos`, `Cluster_SlotCanyon`, `Cluster_ButteRing`, `Cluster_FinField` (also `Scatter` and `Turrets` for later tasks).

- [ ] **Step 2: Place the clusters.** Instantiate `DesertTarget` under each group at these XZ (Y arbitrary, e.g. 60 — `SurfaceSnap` overrides):

| Group | Instances (X, Z) |
|---|---|
| Cluster_MesaArch (3) | (−150, 110), (−128, 142), (−172, 142) |
| Cluster_Hoodoos (3) | (140, 150), (162, 150), (150, 182) |
| Cluster_SlotCanyon (4) | (170, 15), (168, −15), (172, −45), (170, −72) |
| Cluster_ButteRing (4) | (−30, −155), (−56, −138), (−4, −138), (−30, −182) |
| Cluster_FinField (3) | (−165, −134), (−176, −160), (−154, −166) |

- [ ] **Step 3: Save + compile.** Save the scene (`manage_scene`); `refresh_unity` + `read_console` (Error) → clean.

- [ ] **Step 4: Scene-view sanity (pre-Play).** With a top-down `manage_camera` screenshot (or scene query), confirm the 17 instances sit over their formations and none is obviously stranded in open sand or dead-centre of a mesa wall. Note any to nudge (don't block the task on perfection — the Play gate in Task 7 is the real check).

- [ ] **Step 5: Commit.**

```bash
cd "/Users/anon/My project"
git add Assets/Scenes/FlyScene.unity
git commit -m "feat(desert): DesertTargets container + 17 landmark targets (A2)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: Plain scatter

Add the thin open-plain scatter — 4 `DesertTarget` instances, each ≥40 u from spawn `(0, 60)` (XZ) and ~30 u apart, so the centre is contested without becoming a carpet.

**Files:**
- Modify: `Assets/Scenes/FlyScene.unity`

- [ ] **Step 1: Place 4 scatter instances** under the `Scatter` group at:

| (X, Z) | dist from spawn (0,60) |
|---|---|
| (60, 0) | ~85 u |
| (−60, −10) | ~92 u |
| (40, −70) | ~136 u |
| (−80, 40) | ~82 u |

(All ≥40 u from spawn; nearest pair ~120 u apart — comfortably spaced.)

- [ ] **Step 2: Save + compile.** Save scene; `refresh_unity` + `read_console` (Error) → clean.

- [ ] **Step 3: Commit.**

```bash
cd "/Users/anon/My project"
git add Assets/Scenes/FlyScene.unity
git commit -m "feat(desert): thin plain scatter (4 targets, clear of spawn) (A2)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 6: Turrets at strongpoints

Place 3 `Turret` instances — two holding the butte-ring arena, one covering the slot-canyon mouth — each faced to fire across its set-piece toward the likely player approach (the central plain / spawn).

**Files:**
- Modify: `Assets/Scenes/FlyScene.unity`

- [ ] **Step 1: Place + orient 3 turrets** under the `Turrets` group:

| (X, Z) | Faces (yaw toward) | Role |
|---|---|---|
| (−56, −150) | +Z / centre (toward spawn) | butte-ring left defender |
| (−4, −150) | +Z / centre (toward spawn) | butte-ring right defender |
| (170, 35) | −Z (down the canyon) and/or toward centre | slot-canyon mouth |

Set each turret's Y-rotation so `AutoTurret`'s straight fire crosses the arena/lane where the player flies. (Exact yaw tunable at the Play gate.)

- [ ] **Step 2: Save + compile.** Save scene; `refresh_unity` + `read_console` (Error) → clean.

- [ ] **Step 3: Commit.**

```bash
cd "/Users/anon/My project"
git add Assets/Scenes/FlyScene.unity
git commit -m "feat(desert): 3 turrets at strongpoints (butte ring x2, slot canyon) (A2)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 7: Play-mode verification + decision gate

The real acceptance test — human-in-the-loop fly + shoot — plus the A2 decision gate, then push the branch.

**Files:** none (verification + docs/memory + push).

- [ ] **Step 1: Enter Play** (`manage_editor`). Confirm no console errors on load; the construct spawns clear and hovering.

- [ ] **Step 2: Surface-seat check.** Visually confirm **every** target and turret sits ON the dune surface — none floating above or sunk into the sand (this is the `SurfaceSnap` acceptance). Screenshot a couple of clusters with `manage_camera`.

- [ ] **Step 3: Combat check (maintainer flies).** Fly the basin and verify: targets take damage and are destroyed (`CubeStats` pipeline); turrets return fire and are themselves destructible; the butte ring reads as a defended arena; the plain is contested but sparse; the player is not under fire at spawn.

- [ ] **Step 4: Performance check.** Confirm frame cost is unaffected vs A1.5 (no regression from ~23 extra colliders).

- [ ] **Step 5: Reference-scene check.** Open `DesertSandbox.unity` — confirm it still loads unchanged (no A2 leakage). Reopen FlyScene.

- [ ] **Step 6: Record the decision gate.** In the spec file, append an **"A2 outcome"** section: ship / iterate / shelve, with the verified results and any tuning applied. Update `ROADMAP.md §4` (A2 → DONE + gate) and the `memory/project_desert_level.md` status block (A2 done; A3 next).

- [ ] **Step 7: Commit docs + push the branch.**

```bash
cd "/Users/anon/My project"
git add docs/superpowers/specs/2026-06-12-desert-flyscene-a2-design.md docs/superpowers/plans/2026-06-12-desert-flyscene-a2.md ROADMAP.md
git commit -m "docs(desert): record A2 outcome + roadmap (A2)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
git push
```
(First push of A2 — bundles the spec, this plan, all task commits, and the outcome in one go to the draft PR #54.)

---

## Self-Review

**Spec coverage** (each spec section → task):
- §1/§2 hybrid sparse layout → Tasks 4 (clusters) + 5 (scatter). ✓
- §2/§3.2 3 turrets at strongpoints → Tasks 3 (prefab) + 6 (placement). ✓
- §3.1 reuse `WorldTargetCube` as-is → Tasks 2/3 use it as the variant base, untouched. ✓
- §3.3 `SurfaceSnap` generalisation + construct re-wire → Task 1. ✓
- §2 placer consolidation (with construct ref preserved) → Task 1 (guid-stable rename). ✓
- §2/§4 `DesertTargets` container in FlyScene → Task 4 Step 1. ✓
- §2 `PlacedCube` layer + existing damage pipeline → Task 2 Step 4 (layer unchanged). ✓
- §4.2 scatter ≥40 u from spawn, ~30 u spacing → Task 5 (table with distances). ✓
- §5 verification (snap / damage / return fire / perf / DesertSandbox intact) → Task 7. ✓
- §6 Rigidbody risk → Task 2 Steps 1/5. ✓

**Placeholder scan:** Positions, field-setting method (LayerMask via `execute_code`), commit commands, and verification expectations are all concrete. Remaining "confirm at implementation" items (cube half-height, `AutoTurret` field names, variant-vs-flat-prefab capability) are genuine editor reads with explicit fallback steps, not hand-waving. ✓

**Type/name consistency:** `SurfaceSnap` (class + file + component), `DesertTarget.prefab`, `Turret.prefab`, `DesertTargets` container, group names, and field names (`terrainMask`/`clearance`/`rayStartHeight`/`fallbackHeight`) are used identically across Tasks 1–7. ✓
