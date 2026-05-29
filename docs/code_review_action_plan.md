# Code Review — Joined Action Plan (2026-05-29)

Synthesis of two independent audits of the Cube Fly / Cosmic Scrap Club codebase,
de-duplicated, **re-verified against the actual source**, prioritized, and turned
into actionable work items. This file supersedes and replaces the two raw review
files (`code_review_1.md`, `code_review_2.md`), which have been dropped.

**Sources**
- **R1** — Claude static code+logic review (whole-`Assets/Scripts/` read, partitioned into 5 passes; findings `CR-01…CR-21`).
- **R2** — Unity logic review (runtime paths + Unity-MCP scene/physics validation; findings `P1/P2/P3`).

**Verification status:** every item below was re-read in the current source before
inclusion. ✅ = confirmed by direct code read this pass. Items both reviews raised
are merged. Items one review judged "intended" and the other flagged were
reconciled (see *Reconciliations*).

**Decisions:** four items are design/behavior choices (not pure bug fixes). They were
agreed with the maintainer on 2026-05-29 and the chosen direction is recorded inline
(🟦 **Decision**). Everything else is a straight bug fix or robustness/cleanup.

---

## Reconciliations between the two reviews

| Topic | R1 said | R2 said | Resolution |
|---|---|---|---|
| Stale `ActiveSlot` → autosave into wrong slot | `CR-01`: specific *load-failure* path leaves slot armed | `P1`: **systemic** — `ActiveSlot` is never disarmed on *any* path | Merged into **AP-1**. R2's P1 is the superset; R1's CR-01 is one concrete trigger. Both fixes needed. |
| `NetPower` excludes active laser draw | `CR-07`: flagged as possible inconsistency | Non-finding: **intentional** (build/HUD readout = output − shield draw; weapon power is resolved at firing time) | **Not a bug.** Demoted to a one-line comment clarification (**AP-14**). |
| Laser tick loop | `CR-08`: multi-tick on the same hit on a long frame | Non-finding: "guarded by a `maxSteps` cap" | R2's `maxSteps` claim is **incorrect** — no such cap exists (`LaserWeapon.Fire:115-120`). But the loop is bounded by `tickInterval > 0f` (clamped in `OnValidate`), so **no infinite loop**. The multi-tick-on-corpse edge is real → kept as **AP-8c** (Low). |
| Shield damage model | Noted all-or-nothing collapse as *intended* | `P3`: type-modifier overflow is *surprising* (amplifies energy past raw at low shield) | Different points. Collapse stays intended; the overflow amplification is a real defect → **AP-4** (decision taken). |

---

## Work order (priority)

| # | Item | Source | Type | Priority |
|---|---|---|---|---|
| AP-1 | Save-slot lifecycle: disarm `ActiveSlot` | R2 P1 + R1 CR-01 | Bug fix | **P0** |
| AP-2 | Autosave debounce uses scaled time | R2 P2a | Bug fix | **P0** |
| AP-3 | Save metadata excludes alpha cube | R2 P2b | 🟦 Decided change | **P1** |
| AP-4 | Shield overflow amplifies damage at low shield | R2 P3 | 🟦 Decided change | **P1** |
| AP-5 | Tooltip cursor-follow coord mismatch | R1 CR-03 | Bug fix | **P1** |
| AP-6 | Dead thrusters still boost + emit | R1 CR-02 | Bug fix | **P1** |
| AP-7 | Unguarded `SettingsMenu.Instance` (×2) | R1 CR-04 | Bug fix | **P2** |
| AP-8 | Laser robustness bundle (3 sub-fixes) | R1 CR-05/06/08 | Bug fix | **P2** |
| AP-9 | `BuildToolbarController` NRE after missing-manager | R1 CR-11 | Bug fix | **P2** |
| AP-10 | Material-flyout suppression check | R1 CR-12 | Bug fix | **P2** |
| AP-11 | Singleton/lookup robustness bundle | R1 CR-09/13 | Bug fix | **P2** |
| AP-12 | `.inputactions` asset missing Fire/Boost | R2 P3 | 🟦 Decided change | **P3** |
| AP-13 | Physics layer collision matrix all-collide | R2 P3 | 🟦 Decided change | **P3** |
| AP-14 | Low-risk cleanups + comment clarifications | R1 CR-07/10/14/15/16 | Cleanup | **P3** |
| — | Deferred / out of scope | see below | — | — |

---

## P0 — Data integrity (do first)

### AP-1 — `GameData.ActiveSlot` can stay armed and let BuildScene autosave into a real slot ✅
**Source:** R2 P1 (systemic) + R1 CR-01 (load-failure path) · **Type:** Bug fix
**Files:** `Assets/Scripts/Core/GameData.cs:45,383` · `Assets/Scripts/HangarSelect/HangarSelectController.cs:260,281` · `Assets/Scripts/Core/PauseMenu.cs` (menu button) · `Assets/Scripts/Core/GameOverMenu.cs` (return) · `Assets/Scripts/MainMenu/MainMenuController.cs`

**Verified problem:** `SetActiveSlot` is only ever called from `HangarSelectController.ActivateSlot:260` with a real slot; **no code path ever resets it to `-1`** (`Clear()` deliberately leaves it). Two concrete consequences:
1. *(CR-01)* In `ActivateSlot`, the slot is armed at line 260 **before** the load; if `TryLoad` then fails, the recovery branch (line 281) calls `GameData.Clear()` but leaves the slot armed → BuildScene autosave overwrites the merely-unreadable save with an empty construct.
2. *(P1)* Once any slot has been selected in a play session, returning to MainMenu / Game Over / pause-quit and then reaching BuildScene by any non-HangarSelect route (dev direct-play, future shortcuts) autosaves into the last-selected slot. (Bounded to one Play session because domain reload is enabled, but still a live data-loss hazard.)

**Fix:**
- Add an explicit `GameData.DisarmAutosave()` (or document `SetActiveSlot(-1)`) call site.
- Call it on **load failure** in `ActivateSlot` (before/instead of the bare `Clear()`), so a transient read error preserves the file instead of clobbering it.
- Call it when **leaving a slot-scoped session**: `PauseMenu` "Menu" button, `GameOverMenu` return-to-menu, and MainMenu bootstrap.
- Do **not** fold the disarm into `GameData.Clear()` — HangarSelect calls `SetActiveSlot(slot)` then `Clear()` for the empty-slot path, so that would disarm the slot it just armed.

**Acceptance / smoke test:**
- Pick slot 1 in HangarSelect, edit, verify autosave still targets slot 1.
- Pick slot 1, return to MainMenu, direct-load BuildScene in the editor, place/delete a cube → no slot file is written (log shows "autosave disabled").
- Simulate a load failure on activation (e.g. corrupt the file between list build and click) → the slot file is **not** overwritten on entering BuildScene.

> Optional/future (not in this pass): a stronger `ArmAutosaveSlot(slot)` + `HasAutosaveContext` session-token model. Flagged for discussion if direct-play save safety becomes a recurring need.

### AP-2 — Pending autosave stalls while pause/settings freeze time ✅
**Source:** R2 P2a · **Type:** Bug fix
**Files:** `Assets/Scripts/Build/BuildManager.cs:656-661` (`AutosaveAfterDelay`), `:648-654` (`ScheduleAutosave`), `:663-672` (`FlushSaveNow`)

**Verified problem:** `AutosaveAfterDelay` does `yield return new WaitForSeconds(autosaveDebounceSeconds);` (line 658). `WaitForSeconds` is **scaled** by `Time.timeScale`, which pause and settings set to `0`. Edit the construct/class and open pause/settings inside the 0.25 s window → the debounce never elapses. `OnDestroy` flushes on scene unload (so normal scene exits are safe), but an editor stop / hard quit / crash while paused loses the newest edit.

**Fix:**
- Use `WaitForSecondsRealtime(autosaveDebounceSeconds)` in `AutosaveAfterDelay`.
- Add a dirty-flag flush in `OnApplicationPause(true)` and `OnApplicationQuit()` guarded by `GameData.ActiveSlot >= 0`.
- Keep `OnDestroy` as the final safety flush.

**Acceptance:** change ship class, immediately open Settings from pause, wait ~1 s real time, stop play, reopen slot → class persisted.

---

## P1 — Player-facing correctness & decided changes

### AP-3 — Saved slot-card Mass/HP exclude the alpha cube; HUDs include it ✅ 🟦
**Source:** R2 P2b · **Type:** Decided change
**Files:** `Assets/Scripts/Core/GameData.cs:348-378` (`SumPlaced*`), `:499-500` (`ToSave`) · `Assets/Scripts/Build/BuildManager.cs:295-313` (`SumStat` includes alpha) · `Assets/Scripts/Fly/FlyController.cs:412-422` (`ComputeTotalMass` adds alpha) · `Assets/Scripts/Core/ShipClass.cs`

**Verified problem:** `ToSave` writes `totalMass`/`totalHealthPoints` from `SumPlacedMasses`/`SumPlacedHealthPoints`, which iterate `_placedCubes` only (the alpha anchor is not in that list). But `BuildManager.SumStat` includes `_alphaCubeInstance` and `FlyController.ComputeTotalMass` adds alpha mass. Result: a saved Allrounder slot card under-reports mass by the alpha's 10 and HP by the class alpha-HP (Allrounder 100 / Tank 200 / Scout 60). A fresh empty construct shows HUD HP 100 but slot-card HP 0.

🟦 **Decision (2026-05-29):** **Include the alpha in saved totals** so cards match the Build/Fly HUDs.

**Fix:** in `GameData.ToSave`, add alpha mass (from `AlphaCube.prefab` `CubeStats.mass`, currently 10) and `ShipClasses.StatsFor(ActiveShipClass).AlphaHealthPoints` to the two denormalized totals. Keep `SumPlaced*` as-is (they have other callers) — add the alpha contribution at the `ToSave` site, or introduce `SumTotalMass/HealthPoints` helpers that include the alpha and use those for the save.

**Acceptance:** empty Allrounder save → card shows Mass 10 / HP 100, matching the Build HUD. Switch Tank/Scout → card HP reflects 200 / 60.

### AP-4 — Shield type-modifier amplifies HP overflow at low shield ✅ 🟦
**Source:** R2 P3 · **Type:** Decided change (balance)
**Files:** `Assets/Scripts/Fly/ConstructEnergySystem.cs:167-187` (`ApplyToShield`) · `Assets/Scripts/Fly/CubeDamage.cs` · `docs/power_and_energy_spec.md`

**Verified problem:** `ApplyToShield` scales the **entire** incoming amount by the type modifier, then returns the scaled overflow:
```csharp
float scaled   = amount * TypeModifier(type);     // projectile 0.9 / energy 1.1
float absorbed = Mathf.Min(scaled, _shieldPoints);
_shieldPoints -= absorbed;
return scaled - absorbed;                          // scaled overflow → HP
```
With 1 shield point: a 100 **energy** hit → `scaled 110`, `absorbed 1`, returns **109 to HP** (more than the raw 100); a 100 **projectile** hit → returns 89 (11 blocked for ~free). There's also a discontinuity: at exactly 0 points the method early-returns `amount` (100), so holding 1 point makes you take *more* energy damage than holding 0.

🟦 **Decision (2026-05-29):** **Apply the type modifier to the absorbed portion only; pass the unabsorbed remainder through as raw.**

**Fix (illustrative):**
```csharp
// modifier expresses how efficiently the pool soaks this damage type.
float effectiveAbsorb = Mathf.Min(amount, _shieldPoints / TypeModifier(type)); // raw units the pool can cover
_shieldPoints -= effectiveAbsorb * TypeModifier(type);                          // drain in pool units
return amount - effectiveAbsorb;                                                // raw remainder to HP
```
Validate the exact formula against `power_and_energy_spec.md` intent (projectile-resistant / energy-vulnerable) and update the spec + a worked example comment. Net effect: overflow never exceeds raw `amount`; a near-empty shield no longer amplifies energy damage.

**Acceptance:** 100 energy hit vs 1-point shield → HP overflow ≤ 100 (no amplification); 100 projectile vs full shield still resists per spec; kinetic still bypasses pool & regen timer untouched.

### AP-5 — Tooltip cursor-follow mixes physical pixels with the scaled canvas's units ✅
**Source:** R1 CR-03 · **Type:** Bug fix
**Files:** `Assets/Scripts/Core/TooltipHud.cs:183-197` (depends on `Assets/Scripts/Core/PersistentHud.cs:53-55`)

**Verified problem:** the tooltip panel is parented under `PersistentHud.Root`, whose `CanvasScaler` is `ScaleWithScreenSize` @ 1920×1080, so `anchoredPosition` is in **scaled reference units**. `UpdatePosition` sets it directly from raw cursor pixels and clamps edge-flips against `Screen.width/height` (physical pixels). On any non-1080p display (most hardware) the tooltip detaches from the cursor and flips at the wrong edge.

**Fix:** convert the cursor screen point with `RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPos, null, out var local)`, set `anchoredPosition` from `local`, and clamp against the canvas rect size in the same local space (not `Screen.width/height`).

**Acceptance:** tooltip sits under the cursor and edge-flips correctly at 1080p, 1440p, and 4K.

### AP-6 — Dead thrusters keep granting their boost axis and emitting their plume (~2 s) ✅
**Source:** R1 CR-02 · **Type:** Bug fix
**Files:** `Assets/Scripts/Fly/FlyController.cs:510` (list add), `:250-309` (`OnCubeDied`/cascade — never prune), `:718-735` (`EvaluateBoostAxes`), `:744-772` (`DriveThrusterVfxState`) · `Assets/Scripts/Fly/ThrusterBehavior.cs`

**Verified problem:** `_spawnedThrusters` is populated in `BuildConstruct` and **never pruned** on death. Both boost loops only do `if (thruster == null) continue;` — but a dead thruster's GameObject isn't destroyed until its ~2 s `CubeDeath` drift completes, so for those 2 s its `LocalThrustAxis` still sets the boost axis and its detached plume keeps emitting. (The cascade path already zeroes orphan HP "so any liveness poll sees the orphan as dead immediately" — but these loops poll no liveness.)

**Fix:** gate both loops on liveness — add an `IsAlive` to `ThrusterBehavior` (mirror `ReactorBehavior`/`ShieldBehavior`, backed by the sibling `CubeStats.healthPoints > 0`) and `continue` when dead, and/or remove dead entries from `_spawnedThrusters`/`_spawnedWeapons` in `OnCubeDied`.

**Acceptance:** destroy the only thruster on an axis mid-boost → boost bonus on that axis ends immediately; no plume drifts off the dead cube.

---

## P2 — Runtime correctness & robustness (bug fixes)

### AP-7 — Unguarded `SettingsMenu.Instance.Show()` (two sites) ✅
**Source:** R1 CR-04 · **Files:** `Assets/Scripts/Core/PauseMenu.cs:191`, `Assets/Scripts/MainMenu/MainMenuController.cs:64`
**Problem:** both dereference `SettingsMenu.Instance` (a plain non-lazy property) with no null check. In `PauseMenu.OnSettingsClicked`, `HideUI()` has already run, so an NRE leaves a frozen, button-less pause screen (`IsOpen` true, `timeScale` 0).
**Fix:** guard both; in PauseMenu fall back to `ShowUI()` if `SettingsMenu.Instance == null`.

### AP-8 — Laser robustness bundle ✅
**Source:** R1 CR-05/06/08 · **Files:** `Assets/Scripts/Fly/LaserWeapon.cs:65-70,115-120`, `Assets/Scripts/Fly/FlyShootingController.cs:217`
- **8a (CR-05):** `OnValidate` clamps `tickInterval`/`range`/`beamWidth` but **not** `powerDraw`; `FlyShootingController:217` sets `budget = int.MaxValue` when `drawPer <= 0`, so a `powerDraw == 0` laser bypasses the power gate. → add `powerDraw = Mathf.Max(0.01f, powerDraw);`.
- **8b (CR-06):** `range = Mathf.Max(0f, range)` allows `range == 0` → `TrySweep` returns false; the beam consumes heat/power but can't hit. → clamp to `Mathf.Max(0.01f, range)`.
- **8c (CR-08):** the `while (_tickTimer >= tickInterval)` loop re-applies damage to the **same** captured `hit` on a long frame (no `maxSteps` cap, contrary to R2's note); if the cube dies on tick 1, later ticks still hit the corpse (idempotent death guards prevent double-destroy, but the shield regen timer is reset and ticks are wasted). → break the loop once `hit`'s `CubeStats.healthPoints <= 0`, or cap to one application per frame and clamp `_tickTimer = Min(_tickTimer, tickInterval)`.

### AP-9 — `BuildToolbarController.Update` NREs after a missing-`BuildManager` start ✅
**Source:** R1 CR-11 · **Files:** `Assets/Scripts/Build/BuildToolbarController.cs:135` (Start error path)
**Problem:** when `buildManager == null`, `Start` logs and returns but doesn't disable the component, so `Update` keeps running and the first digit/`M`/Esc press dereferences `buildManager`.
**Fix:** set `enabled = false;` in that error branch.

### AP-10 — Material-flyout suppression checks `IsWeapon` instead of `UsesCoupledMaterial` ✅
**Source:** R1 CR-12 · **Files:** `Assets/Scripts/Build/BuildToolbarController.cs:556`
**Problem:** the armour-material flyout should be suppressed for *all* coupled-material shapes (Weapon **and** Utility); the guard only checks `IsWeapon`. Currently masked by the `_shapeButtons[shapeIndex] == null` check (Utilities have no armour button), and the Shift+digit path already uses `UsesCoupledMaterial`. Latent inconsistency.
**Fix:** `if (def == null || def.UsesCoupledMaterial) return;`.

### AP-11 — Singleton/lookup robustness bundle ✅
**Source:** R1 CR-09/13
- **11a (CR-09):** `FlyShootingController:107` resolves `_energy` via scene-wide `FindAnyObjectByType<ConstructEnergySystem>()` instead of the construct's own. Fine in the single-construct FlyScene; fragile if a second ever exists. → have `FlyController` pass its `_energySystem` into `RegisterWeapons`.
- **11b (CR-13):** `VfxApplier:106` uses `FindFirstObjectByType<Volume>()` (arbitrary when a scene has multiple Volumes). → prefer the global, highest-priority Volume.

---

## P3 — Decided maintenance changes & cleanups

### AP-12 — `.inputactions` asset is missing `Fire`/`Boost` ✅ 🟦
**Source:** R2 P3 · **Type:** Decided change
**Files:** `Assets/Input/CubeFlyInputActions.inputactions`, `Assets/Input/CubeFlyInputActions.cs`
**Verified problem:** the asset's Fly map has `Thrust/Pitch/Yaw/Roll/Look/LookHeld` but **no** `Fire`/`Boost` (grep: 0 `Fire` entries). The hand-written wrapper (authoritative at runtime) defines `Fire ← <Mouse>/leftButton` and `Boost ← <Keyboard>/leftCtrl`. Stale asset = a trap for anyone regenerating from it.
🟦 **Decision:** **update the asset to mirror the wrapper** (add Fire/Boost with matching bindings); keep the wrapper authoritative; add a comment/doc line noting the asset is a manually-synced mirror.
**Acceptance:** asset's Fly map lists Fire + Boost; optional edit-time check asserts both exist.

### AP-13 — Physics layer collision matrix collides every layer with every other ✅ 🟦
**Source:** R2 P3 · **Type:** Decided change
**Files:** `ProjectSettings/DynamicsManager.asset:20` (all-`f` `m_LayerCollisionMatrix`), `ProjectSettings/TagManager.asset` (layers `PlacedCube`/`AlphaCube`/`PreviewCube`)
**Verified problem:** the matrix is fully enabled. Preview ghosts are kept safe today only by raycast layer **masks**, not the matrix — no margin for future preview/UI/VFX/trigger colliders.
🟦 **Decision:** **conservative** — disable `PreviewCube` vs all gameplay/world layers; leave the rest of the matrix intact. Document the chosen rows in a short table; optionally add a lightweight editor validation asserting the `PreviewCube` rows stay disabled.
**Acceptance:** preview placement, firing, crash damage, and world-target impacts all still behave; `PreviewCube` no longer physically collides.

### AP-14 — Low-risk cleanups & comment clarifications ✅
**Source:** R1 CR-07/10/14/15/16 · **Type:** Cleanup (low risk; can be one small PR)
- **CR-07 (NetPower):** add a one-line comment that `NetPower` is deliberately the build/HUD readout (`output − shield draw`); weapon power is resolved at firing time. *(R2 confirms intended — comment only, no behavior change.)*
- **CR-10:** zero `_boostHeld` in the `PauseMenu.IsOpen` branch of `FlyController.Update` for symmetry with the other inputs (harmless today due to Update-before-FixedUpdate ordering).
- **CR-14:** `FlyCamera` and `FlyController` each instantiate + enable their own `CubeFlyInputActions`; consider sharing one instance.
- **CR-15:** `FlyShootingController.SelectedChanged` has no subscribers (the toolbar polls `SelectedTypeIndex`); either drive the highlight off the event or remove it.
- **CR-16:** `ShipClasses.Parse` allocates via `Enum.GetValues` + per-element `ToString` on each hangar refresh; switch to `Enum.TryParse`/a static switch.

---

## Deferred / out of scope

- **Desert & Editor tooling (R1 CR-17…CR-21):** `FreeFlyCamera` normalized-move coupling, `OutlineRendererFeature.Dispose` not nulling refs, `DuneGroundGeneratorEditor` undo not tracking the asset write, `VfxAssetsInstaller` possibly-null texture on fresh clone, `RegistryValidator` root-vs-children check inconsistency. These are throwaway/experimental (Desert) or Editor-only (stripped from player builds). **Fix only if the desert level or those tools are promoted to real features.**
- **MCP console noise (R2 P3):** the `MCP-FOR-UNITY: Client handler …` error/exception entries come from `Packages/com.coplaydev.unity-mcp/…` — **third-party tooling code under `Packages/`, not our `Assets/` game code** (it's a checked-in editor/tooling dependency, separate from the game source we own). No source change; just be aware it's tooling noise when reading the console during validation.
- **Automated tests (R2 recommends EditMode/PlayMode tests throughout):** the project currently **has no test harness** — observable in-repo: no test assemblies (`*.asmdef`), no `Assets/Tests`, no EditMode/PlayMode tests. Treat R2's "regression test" suggestions as **manual smoke-test steps** (captured under each item's *Acceptance*) unless we decide to introduce a test assembly — that is a **separate scope decision**, not assumed here. If we do, the highest-value targets are the save lifecycle (AP-1/2), save metadata totals (AP-3), and shield overflow (AP-4).

---

## Appendix A — Behaviors confirmed INTENTIONAL (do not "fix")

Both reviews (and re-verification) agree these are by-design:
- **Shield all-or-nothing power collapse** at the power-parity boundary (`ConstructEnergySystem.RecomputePower`) — documented. *(Distinct from AP-4, which is the overflow-scaling defect.)*
- **Kinetic/crash damage bypasses the shield pool** entirely — documented.
- **`NetPower` = output − shield draw** as the build/HUD readout; weapon power is resolved at firing time (see AP-14 comment).
- **`AutoTurret` deliberately can hit the player construct** (passes its own transform as the self-filter); 4 instances are wired in `FlyScene`.
- **`CubeDeath` drift uses `Time.deltaTime`** and freezes during pause / Game Over; scene unload cleans up.
- **`BuildIndicatorController` only considers `cell.z > 0`** cubes (alpha fallback otherwise) — documented front-marker rule.
- **`RcsPuffVfx` per-emitter cooldown** can suppress one axis when pitching+yawing simultaneously — documented limitation.
- **`Rocket` locks its target once at launch** — by design.
- **`MaterialIndex = -1`** sentinel for coupled-material shapes — consumed correctly by `ShapeDefinition.ResolveMaterial`.

## Appendix B — Verified NOT issues (skip; already investigated)

- **Static state across Play sessions:** domain reload is **enabled** (`ProjectSettings/EditorSettings.asset: m_EnterPlayModeOptionsEnabled: 0`), so statics reset each Play — no stale-static bug.
- **Event-subscription leaks:** DDOL singletons unsubscribe in `OnDestroy` (guarded by `Instance == this`); Fly subscribers pair `+=`/`-=` in `OnEnable`/`OnDisable` (or `Start`/`OnDestroy`). Balanced.
- **Save layer:** atomic `File.Replace` + `AtomicReplaceFallback` preserves the prior save on partial failure; future-version refused; corrupt ticks clamped; unknown names skipped (logged). R2 also validated all enabled scenes/prefabs as clean via Unity-MCP.
- **Build flood-fill delete, self-hit filtering, alpha-fatal-hit handling, double-`AddComponent<CubeDeath>` guard, `FirstAliveInstance`, `Rocket` exit phase, reload/heat cooldowns frozen on pause, `DuneGroundGenerator` mesh math** — all verified correct.
- **`ClearDeleteHover` `SetPropertyBlock(null)`** is safe (cubes use shared materials, no pre-existing per-renderer block) and **`ProjectileHit` static buffer** is safe (consumed fully before return, main-thread sequential).

## Appendix C — Cross-reference (action item ↔ source findings)

| AP | R1 | R2 |
|---|---|---|
| AP-1 | CR-01 | P1 |
| AP-2 | — | P2a |
| AP-3 | — | P2b |
| AP-4 | (shield note) | P3 (shield) |
| AP-5 | CR-03 | — |
| AP-6 | CR-02 | — |
| AP-7 | CR-04 | — |
| AP-8 | CR-05, CR-06, CR-08 | (P3 laser non-finding, corrected) |
| AP-9 | CR-11 | — |
| AP-10 | CR-12 | — |
| AP-11 | CR-09, CR-13 | — |
| AP-12 | — | P3 (inputactions) |
| AP-13 | — | P3 (layer matrix) |
| AP-14 | CR-07, CR-10, CR-14, CR-15, CR-16 | (P3 NetPower non-finding) |
| Deferred | CR-17…CR-21 | P3 (MCP console), test recommendations |
