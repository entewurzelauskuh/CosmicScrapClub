# Milestone A4 — Land the desert (decide + docs + cleanup + merge) — Design Spec

**Date:** 2026-07-04
**Branch:** `explore/desert-flyscene` (experimental — merging to `main` at the end of this milestone).
**Roadmap:** item 4, Milestone A, sub-phase **A4** (`ROADMAP.md §4`).
**Predecessor:** A3 (`docs/superpowers/specs/2026-07-03-desert-flyscene-a3-design.md`) — done, gate = SHIP.
**Status:** Accepted design, pre-implementation.

## 1. Purpose & scope

A1–A3 built, populated, and styled the desert as an integrated FlyScene level (all shipped +
Copilot-clean on draft PR #54). A4 is the **decision + close-out**: the maintainer's call is
**LAND** — make the desert a permanent part of the game and **merge PR #54 to `main`**. This
milestone does the housekeeping that landing requires: remove the exploration scaffolding, make
the desert's shadow-distance scene-local (the twice-flagged A2/A3 review item), rewrite the docs
from "detached demonstrator" to "integrated level", and merge.

No new gameplay. Continues on `explore/desert-flyscene`; the terminal action is the merge.

### In scope
- **Cleanup:** remove `DesertSandbox.unity`, `FreeFlyCamera.cs`, and the orphaned 200 u
  `DesertGround.asset` (+ `.meta`s).
- **Shadow split:** `Desert_RPAsset` (shadowDistance 300) + global `PC_RPAsset` restored to 50 +
  a `ScenePipelineOverride` component that activates the desert asset in FlyScene only.
- **Docs:** rewrite `docs/desert_level_spec.md`; update `docs/full_architecture.md` + `ROADMAP.md`;
  surface `CLAUDE.md`/`README.md` for the maintainer.
- **Merge:** final Play-mode sanity, mark PR #54 ready, merge to `main`.

### Out of scope
- **Turret tilt** — accepted as-is (maintainer call); no change.
- Milestone **B** (the `unity_handoff/` UI rebrand) — the next, separate milestone.
- Any new desert gameplay / content.
- Reworking the cel shader, combat, or terrain (all shipped in A1–A3).

## 2. Decisions locked during brainstorming (2026-07-04)

| Topic | Decision |
|---|---|
| Fate | **LAND + merge PR #54 to `main`.** The desert becomes a real part of the game. |
| Scaffolding | **Drop** `DesertSandbox.unity` + `FreeFlyCamera.cs` + the orphaned 200 u `DesertGround.asset`. |
| Shadow distance | **Desert-local pipeline:** `Desert_RPAsset` (300) for FlyScene via a `ScenePipelineOverride`; restore global `PC_RPAsset` to 50. |
| Turrets | **Accept the tilt** — no change. |
| Docs | Rewrite the desert spec (demonstrator → integrated level); update the architecture map + roadmap; flag `CLAUDE.md` (untracked) + `README.md`. |
| Merge timing | **Now** — at the end of A4, after a final verify + explicit maintainer go. |

## 3. Cleanup — remove the exploration scaffolding

The standalone sandbox and its free-fly camera were scaffolding for the *detached* exploration;
the integrated FlyScene desert supersedes them.

- **Verify no live references first** (exploration confirmed: only `DesertSandbox.unity` uses
  `FreeFlyCamera`; only `DesertSandbox` uses the 200 u `DesertGround.asset`). Re-grep before
  deleting.
- **Delete** (asset + `.meta`): `Assets/Scenes/DesertSandbox.unity`,
  `Assets/Scripts/Desert/FreeFlyCamera.cs`, `Assets/Models/DesertGround.asset`. Remove
  `DesertSandbox` from Build Settings if listed.
- **Keep** everything FlyScene uses: `DesertGround_500.asset`, all shaders/materials, the
  `DesertEnvironment`/`DesertTarget`/`Turret` prefabs, `Desert_Renderer`, `DesertVolumeProfile`.
- Confirm the other scenes + FlyScene open with **no missing-script / missing-asset** errors after
  the deletions.

## 4. Shadow-distance split — desert-local pipeline

The A2 shadow-pop fix (shadowDistance 50→300) lives on the **shared** `PC_RPAsset`, softening
shadows in every scene (Copilot flagged it in the A2 *and* A3 reviews). Make it scene-local:

- **`Assets/Settings/Desert_RPAsset.asset`** — duplicate `PC_RPAsset` (so it carries the same
  renderer list `[0 = PC_Renderer, 1 = Desert_Renderer]`, keeping the cel toggle's indices valid);
  set its `shadowDistance = 300`.
- **`PC_RPAsset`** — restore `shadowDistance` to **50** (its pre-A2 value).
- **`ScenePipelineOverride`** (new, `Assets/Scripts/Core/`): a small MonoBehaviour that, in
  `OnEnable`, caches `QualitySettings.renderPipeline` and sets it to a serialized override asset;
  in `OnDisable`, restores the cached value. Placed on FlyScene's `DesertLook` GO, assigned
  `Desert_RPAsset`. Kept separate from `DesertLookController` (single responsibility) and generic
  (Core, not desert-specific). The override is **unconditional** — it applies regardless of the
  cel toggle, because the long shadow distance is about the 500 u basin scale, not the cel look.

Result: FlyScene renders with 300 u shadows (far formations cast, no pop); Menu/Hangar/Build get
their crisp 50 u shadows back. Two pipeline assets to keep loosely in sync (documented).

## 5. Turrets

No change — the +Y-aim tilt is accepted (the A3 cel look already restyles the scene). Documented
as a known cosmetic in the desert spec.

## 6. Docs — from demonstrator to integrated level

The content work of the landing. Keep `README`/`docs` as the source of truth (per `CLAUDE.md`).

- **`docs/desert_level_spec.md`** — **rewrite.** It currently opens *"a standalone demonstrator…
  deliberately detached from the game… on the `explore/desert-level` branch."* Rewrite it to
  describe the **integrated FlyScene desert level**: the 500 u basin (terrain + ridge + 5
  formations), the combat layout (targets + turrets), the cel-look toggle, and the scripts /
  prefabs / renderer that implement it — with the A1–A4 arc summarised and pointers to the
  per-sub-phase specs under `docs/superpowers/specs/`.
- **`docs/full_architecture.md`** — add the desert files to the file-by-file map: `Scripts/Desert/`
  (`SurfaceSnap`, `DesertLookController`, `OutlineRendererFeature`, `DuneGroundGenerator`;
  `FreeFlyCamera` removed), `Scripts/Core/` (`CelLookSettings`, `ScenePipelineOverride`), the
  desert prefabs, `Desert_Renderer` / `Desert_RPAsset` / `DesertVolumeProfile`, and the FlyScene
  changes (DesertEnvironment instance, DesertTargets, DesertLook).
- **`ROADMAP.md`** — mark Milestone A **complete** (A1–A4 done; the desert has landed); point to
  Milestone B.
- **`CLAUDE.md`** — its desert note still reads "experimental desert-level work… throwaway
  evaluation tooling," now stale. **But `CLAUDE.md` is untracked** (never committed) — so **surface
  it to the maintainer** with a proposed edit rather than committing it silently.
- **`README.md`** — check the scene list / controls; add a short desert-level line if warranted
  (flag to maintainer if it needs more than a touch).

## 7. Verification

No automated tests. Manual, in the Unity Editor on the **main project root** (per `CLAUDE.md`):

1. Compile/console clean after the deletions + the new component; **no missing-script errors** in
   FlyScene, MainMenu, HangarSelect, BuildScene.
2. **Shadow split:** Play FlyScene → far formations cast shadows (300 u, no pop); Play MainMenu /
   HangarSelect / BuildScene → crisp shadows (50 u); returning from FlyScene to another scene
   **restores** the pipeline (no leaked 300 u). Confirm `PC_RPAsset.shadowDistance == 50` and
   `Desert_RPAsset.shadowDistance == 300` on disk.
3. **Cleanup:** `DesertSandbox` gone, nothing references it; FlyScene unaffected.
4. Docs read accurately against the shipped implementation.
5. **Merge:** PR #54 marked ready, merges to `main` cleanly (merge commit per repo convention).

**Decision gate (end of A4):** this *is* the landing — the gate is "merge to main," taken after
verification + an explicit maintainer go.

### A4 outcome (2026-07-04) — **LANDED**

The desert lands on `main`. Everything built + verified:
- **Cleanup:** deleted `DesertSandbox.unity`, `FreeFlyCamera.cs`, and the orphaned 200 u
  `DesertGround.asset` (grep-verified that only DesertSandbox referenced the latter two);
  project compiles clean, no missing scripts/assets; DesertSandbox wasn't a build scene.
- **Shadow split:** `Desert_RPAsset` (duplicate of `PC_RPAsset`, same renderer list,
  `shadowDistance 300`) + global `PC_RPAsset` restored to `50`; `ScenePipelineOverride` on
  FlyScene's `DesertLook` GO swaps to it on load and restores on exit. Verified headlessly:
  Play FlyScene → active pipeline `Desert_RPAsset`; stop → `PC_RPAsset` (per-scene shadow
  *visuals* confirmed at the maintainer gate).
- **Docs:** `desert_level_spec.md` rewritten (demonstrator → integrated level);
  `full_architecture.md` re-synced (stale DesertSandbox / FlyScene-volume refs fixed,
  `CelLookSettings` + `ScenePipelineOverride` + a Scripts-Desert section added); `ROADMAP.md`
  marks Milestone A complete; `README.md`'s world-map line updated; `CLAUDE.md` updated locally
  (kept untracked, per maintainer).
- **Turrets:** tilt accepted, no change.

**Terminal action:** PR #54 marked ready + merged to `main` (merge commit, repo convention).
Milestone A (A1–A4) complete; **Milestone B** (the `unity_handoff/` UI rebrand) is next.

## 8. Risks & notes

- **Pipeline swap correctness:** `ScenePipelineOverride` must restore on `OnDisable` so other
  scenes aren't left on the desert pipeline. Verify the FlyScene→Menu transition explicitly. A
  runtime pipeline switch causes a one-time rebuild hitch on scene load — acceptable.
- **Two pipeline assets** (`PC_RPAsset`, `Desert_RPAsset`) can drift if URP settings change later —
  documented in the architecture map. `Desert_RPAsset` is cloned from the **PC-quality** `PC_RPAsset`,
  so the override is only exactly right at the PC quality level; on a lower tier (e.g. Mobile) it would
  swap the PC-grade pipeline in for FlyScene (self-restoring, no leak) — acceptable, the project ships
  Standalone-only.
- **Deletions:** re-grep for references before deleting; a stray reference would leave a
  missing-script/asset. (Exploration says only DesertSandbox is affected.)
- **`CLAUDE.md` untracked:** do not commit it as part of the merge without maintainer sign-off.
- **Merge is outward-facing + hard to reverse:** it is the explicit final step, gated on the
  maintainer's go after verification; not done automatically mid-plan.

## 9. File manifest

**New**
- `Assets/Settings/Desert_RPAsset.asset` (+ `.meta`) — desert URP pipeline asset (shadowDistance 300).
- `Assets/Scripts/Core/ScenePipelineOverride.cs` (+ `.meta`) — per-scene pipeline override.

**Edited**
- `Assets/Settings/PC_RPAsset.asset` — `shadowDistance` 300 → 50.
- `Assets/Scenes/FlyScene.unity` — add `ScenePipelineOverride` (on `DesertLook`, → `Desert_RPAsset`).
- `docs/desert_level_spec.md` (rewrite), `docs/full_architecture.md`, `ROADMAP.md`.

**Removed**
- `Assets/Scenes/DesertSandbox.unity` (+ `.meta`), `Assets/Scripts/Desert/FreeFlyCamera.cs`
  (+ `.meta`), `Assets/Models/DesertGround.asset` (+ `.meta`).

**Surfaced, not auto-committed**
- `CLAUDE.md` (untracked — proposed desert-framing edit for maintainer), `README.md` (optional line).

**Terminal action**
- Merge PR #54 → `main`.
