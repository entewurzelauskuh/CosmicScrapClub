# Boost Overboost Tuning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Apply three play-test-driven tuning tweaks to the Thruster boost mechanic — a red critical-zone HUD throb, a shorter overboost recovery penalty, and a slower "Overboosted!" flash.

**Architecture:** All three changes are localized edits to two existing MonoBehaviours (`FlyController`, `FlyBoostBar`) plus serialized values in `FlyScene.unity`. A new `criticalBoostFraction` field on `FlyController` defines a "bottom 25%" band that drives both the overboost-recovery threshold and — via a new `IsBoostCritical` property — the HUD's red-throb visual. No new files, no new systems.

**Tech Stack:** Unity 6.3 LTS (6000.3.11f1), URP, C# MonoBehaviours, new Input System. Verification is by compile-check (Unity MCP `refresh_unity` + `read_console`) and a final FlyScene play-test — this project does not unit-test gameplay MonoBehaviours, matching the prior Thruster PRs.

**Spec:** `boost_overboost_tuning_spec.md` (repo root).

---

> **Tuning note (post-merge):** the critical band and size pulse were retuned during the Task 5 play-test from the starting defaults to their shipped values — `criticalBoostFraction = 0.25` (critical band = the bottom 25%, not 10%) and `criticalSizePulse = 0.05` (±5%, not ±1%). The Task 1–2 sections below describe and implement the original `0.10` / `0.01` defaults as planned; the Task 5 checklist reflects the shipped values. The retune is commit `b467ddb`.

## Setup

- [ ] **Create the feature branch off `main`**

`main` already contains the merged Thruster boost mechanic (PR #30). From a clean `main`:

```bash
git checkout main
git pull
git checkout -b feat/boost-overboost-tuning
```

- [ ] **Commit this plan**

```bash
git add docs/superpowers/plans/2026-05-18-boost-overboost-tuning.md
git commit -m "Add boost overboost tuning implementation plan"
```

---

## Task 1: FlyController — regen partition + critical-state API

This adds the shared `criticalBoostFraction` field, the `IsBoostCritical` property the HUD will read, and changes the overboost latch so the penalty clears at the 10% mark instead of at full.

**Files:**
- Modify: `Assets/Scripts/Fly/FlyController.cs`

- [ ] **Step 1: Apply the four edits to `FlyController.cs`**

**Edit 1.1 — add the `criticalBoostFraction` field** (in the `[Header("Boost ...")]` block, after `overCapDecaySpeed`).

Find:
```csharp
        [Tooltip("Speed (u/s per second) at which over-cap velocity eases back down to maxSpeed once boosting ends. Tuned so the drop from maxSpeed*boostMaxSpeedMultiplier to maxSpeed reads as quick but not an instant snap — a fraction of a second.")]
        [SerializeField] float overCapDecaySpeed = 60f;

        [Header("Rotation (Rigidbody.AddTorque)")]
```
Replace with:
```csharp
        [Tooltip("Speed (u/s per second) at which over-cap velocity eases back down to maxSpeed once boosting ends. Tuned so the drop from maxSpeed*boostMaxSpeedMultiplier to maxSpeed reads as quick but not an instant snap — a fraction of a second.")]
        [SerializeField] float overCapDecaySpeed = 60f;
        [Tooltip("Fill fraction (0-1) marking the bottom 'critical' band of the Boost meter. Below this the HUD bar turns red and throbs; an overboosted construct also stays locked out of boosting until the meter refills back up to this mark. 0.10 = the bottom 10%.")]
        [SerializeField] float criticalBoostFraction = 0.10f;

        [Header("Rotation (Rigidbody.AddTorque)")]
```

**Edit 1.2 — correct the `_overboosted` field comment.**

Find:
```csharp
        // --- Boost resource state ---
        // _boost is the 0-100 meter; it starts full at boostMax (set in
        // Start). _overboosted latches true when _boost hits 0 and
        // clears only when _boost regenerates all the way back to
        // boostMax — while latched, boosting is disabled entirely.
        // _boostingThisFrame is set each FixedUpdate by the activation
        // rule (Task 5; provisionally in Task 4) — true iff >=1 thruster
        // is contributing, i.e. the construct is "actively boosting";
        // it drives drain-vs-regen and the boosted speed clamp.
```
Replace with:
```csharp
        // --- Boost resource state ---
        // _boost is the 0-100 meter; it starts full at boostMax (set in
        // Start). _overboosted latches true when _boost hits 0 and
        // clears once _boost refills to the criticalBoostFraction mark
        // — while latched, boosting is disabled entirely.
        // _boostingThisFrame is set each FixedUpdate by the activation
        // rule — true iff >=1 thruster is contributing, i.e. the
        // construct is "actively boosting"; it drives drain-vs-regen
        // and the boosted speed clamp.
```

**Edit 1.3 — correct the `IsOverboosted` comment and add `IsBoostCritical`.**

Find:
```csharp
        // True while the Boost resource is exhausted and recovering —
        // boosting is disabled until the meter refills to boostMax.
        // FlyBoostBar reads this to drive the "Overboosted!" flash.
        public bool IsOverboosted => _overboosted;
```
Replace with:
```csharp
        // True while the Boost resource is exhausted and recovering —
        // boosting is disabled until the meter refills to the
        // criticalBoostFraction mark. FlyBoostBar reads this to drive
        // the "Overboosted!" flash.
        public bool IsOverboosted => _overboosted;

        // True while the Boost meter sits in its bottom criticalBoostFraction
        // band. Drives the FlyBoostBar critical-zone red throb. Independent
        // of _overboosted — it also shows when the player drains low without
        // bottoming out.
        public bool IsBoostCritical => BoostFraction <= criticalBoostFraction;
```

**Edit 1.4 — update `TickBoostResource`'s doc comment and the latch-clear condition.**

Find:
```csharp
        // Ticks the Boost resource each FixedUpdate: drains while
        // actively boosting, regenerates otherwise, and runs the
        // overboosted latch. Overboosted is entered the moment boost
        // hits 0 and cleared only when boost regenerates all the way
        // back to boostMax — a full recovery is required (spec §5).
        // _boostingThisFrame is set by the activation rule just above
        // the call site in FixedUpdate.
```
Replace with:
```csharp
        // Ticks the Boost resource each FixedUpdate: drains while
        // actively boosting, regenerates otherwise, and runs the
        // overboosted latch. Overboosted is entered the moment boost
        // hits 0 and cleared once the meter refills to the
        // criticalBoostFraction mark — the penalty (slow regen + no
        // boosting) spans the bottom band only. _boostingThisFrame is
        // set by the activation rule just above the call site in
        // FixedUpdate.
```

Find:
```csharp
            // Overboosted latch. Enter at 0; exit only at full boostMax.
            if (!_overboosted)
            {
                if (_boost <= 0f) _overboosted = true;
            }
            else
            {
                if (_boost >= boostMax) _overboosted = false;
            }
```
Replace with:
```csharp
            // Overboosted latch. Enter at 0; exit once the meter has
            // refilled to the criticalBoostFraction mark.
            if (!_overboosted)
            {
                if (_boost <= 0f) _overboosted = true;
            }
            else
            {
                if (_boost >= boostMax * criticalBoostFraction) _overboosted = false;
            }
```

- [ ] **Step 2: Compile and check the console**

Run (Unity MCP):
1. `refresh_unity(compile="request", scope="scripts", mode="force")`
2. Poll the `mcpforunity://editor/state` resource until `compilation.is_compiling` is `false`.
3. `read_console(types=["error"], count=20)`

Expected: no compilation errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Fly/FlyController.cs
git commit -m "Add criticalBoostFraction — partition boost regen at the 10% mark"
```

---

## Task 2: FlyBoostBar — critical-zone red throb

When `FlyController.IsBoostCritical` is true, the bar's fill turns red and the whole bar throbs (alpha + size) on one slow sine cycle. Outside the band, behavior is unchanged.

**Files:**
- Modify: `Assets/Scripts/Fly/FlyBoostBar.cs`

- [ ] **Step 1: Apply the four edits to `FlyBoostBar.cs`**

**Edit 2.1 — add the critical-zone serialized fields** (after the `[Header("Overboosted flash")]` block, before the `Canvas _canvas;` field declarations).

Find:
```csharp
        [Tooltip("Seconds the flash label is hidden per cycle.")]
        [SerializeField] float flashOffSeconds = 0.12f;

        Canvas _canvas;
```
Replace with:
```csharp
        [Tooltip("Seconds the flash label is hidden per cycle.")]
        [SerializeField] float flashOffSeconds = 0.12f;

        [Header("Critical zone (bottom of the meter)")]
        [Tooltip("Fill color while the meter is in its critical bottom band (FlyController.IsBoostCritical).")]
        [SerializeField] Color criticalColor = new Color(0.95f, 0.25f, 0.20f, 1f);
        [Tooltip("Seconds for one full throb cycle (alpha + size) while critical. Larger = slower.")]
        [SerializeField] float criticalPulseSeconds = 1.2f;
        [Tooltip("Size throb amplitude while critical — the bar's localScale oscillates by +/- this fraction. 0.01 = +/-1%.")]
        [SerializeField] float criticalSizePulse = 0.01f;
        [Tooltip("Low point of the alpha throb while critical; the high point is 1.")]
        [SerializeField] float criticalAlphaMin = 0.55f;

        Canvas _canvas;
```

**Edit 2.2 — add the `_frame` field** (so `Update` can throb the bar's `localScale`).

Find:
```csharp
        Canvas _canvas;
        RectTransform _fill;     // grows bottom-up with BoostFraction
        Image _fillImage;
```
Replace with:
```csharp
        Canvas _canvas;
        RectTransform _frame;    // bar background; localScale throbs while critical
        RectTransform _fill;     // grows bottom-up with BoostFraction
        Image _fillImage;
```

**Edit 2.3 — capture the frame RectTransform in `BuildUI`.**

Find:
```csharp
            RectTransform frameRT = (RectTransform)frameGO.transform;
            frameRT.anchorMin = frameRT.anchorMax = frameRT.pivot = new Vector2(0.5f, 0.5f);
```
Replace with:
```csharp
            RectTransform frameRT = (RectTransform)frameGO.transform;
            _frame = frameRT;
            frameRT.anchorMin = frameRT.anchorMax = frameRT.pivot = new Vector2(0.5f, 0.5f);
```

**Edit 2.4 — replace the opacity block in `Update` with the critical/normal branch.**

Find:
```csharp
            // Opacity ramps with use: invisible at full boost, opaque
            // when drained.
            float alpha = 1f - fraction;
            SetImageAlpha(_fillImage, fillColor, alpha);
            SetImageAlpha(_frameImage, frameColor, alpha);
```
Replace with:
```csharp
            // Critical zone (bottom band) — red fill, slow alpha + size
            // throb. Otherwise the normal look: blue fill, opacity ramps
            // with use, no throb.
            if (flyController.IsBoostCritical)
            {
                float pulse01 = 0.5f * (1f + Mathf.Sin(
                    Time.unscaledTime * (2f * Mathf.PI / criticalPulseSeconds)));
                float critAlpha = Mathf.Lerp(criticalAlphaMin, 1f, pulse01);
                SetImageAlpha(_fillImage, criticalColor, critAlpha);
                SetImageAlpha(_frameImage, frameColor, critAlpha);
                float scale = 1f + (pulse01 * 2f - 1f) * criticalSizePulse;
                _frame.localScale = new Vector3(scale, scale, 1f);
            }
            else
            {
                float alpha = 1f - fraction;
                SetImageAlpha(_fillImage, fillColor, alpha);
                SetImageAlpha(_frameImage, frameColor, alpha);
                _frame.localScale = Vector3.one;
            }
```

- [ ] **Step 2: Compile and check the console**

Run (Unity MCP):
1. `refresh_unity(compile="request", scope="scripts", mode="force")`
2. Poll `mcpforunity://editor/state` until `compilation.is_compiling` is `false`.
3. `read_console(types=["error"], count=20)`

Expected: no compilation errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Fly/FlyBoostBar.cs
git commit -m "Add critical-zone red throb to the Boost bar"
```

---

## Task 3: FlyBoostBar — slower "Overboosted!" flash

**Files:**
- Modify: `Assets/Scripts/Fly/FlyBoostBar.cs`

- [ ] **Step 1: Change the `flashOnSeconds` default**

Find:
```csharp
        [Tooltip("Seconds the flash label is visible per cycle.")]
        [SerializeField] float flashOnSeconds = 0.18f;
```
Replace with:
```csharp
        [Tooltip("Seconds the flash label is visible per cycle.")]
        [SerializeField] float flashOnSeconds = 0.27f;
```

- [ ] **Step 2: Compile and check the console**

Run (Unity MCP):
1. `refresh_unity(compile="request", scope="scripts", mode="force")`
2. Poll `mcpforunity://editor/state` until `compilation.is_compiling` is `false`.
3. `read_console(types=["error"], count=20)`

Expected: no compilation errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Fly/FlyBoostBar.cs
git commit -m "Slow the Overboosted message flash by 1.5x"
```

---

## Task 4: FlyScene — carry the new serialized values

The new `[SerializeField]` fields from Tasks 1–2 take their code defaults at runtime even if absent from the scene, but for Inspector visibility and consistency with PR #30 they should be written into the scene. `flashOnSeconds` is an *existing* field, so the scene still holds the stale `0.18` — it must be explicitly set to `0.27`.

**Files:**
- Modify: `Assets/Scenes/FlyScene.unity`

- [ ] **Step 1: Set `flashOnSeconds` on the scene's FlyBoostBar component**

In Unity (FlyScene is the active scene):
1. `find_gameobjects(search_term="FlyHUD", search_method="by_name")` to locate the `FlyHUD` GameObject (the `FlyBoostBar` component lives on it).
2. Use `manage_components` to set the `FlyBoostBar` component's `flashOnSeconds` property to `0.27`.

- [ ] **Step 2: Save the scene**

Run `manage_scene(action="save")`. Saving reserializes the `FlyController` and `FlyBoostBar` components, writing the new fields (`criticalBoostFraction`, `criticalColor`, `criticalPulseSeconds`, `criticalSizePulse`, `criticalAlphaMin`) at their code defaults.

- [ ] **Step 3: Verify the scene diff**

```bash
git diff Assets/Scenes/FlyScene.unity
```
Expected: the `FlyController` block gains `criticalBoostFraction: 0.1`; the `FlyBoostBar` block gains `criticalColor`, `criticalPulseSeconds: 1.2`, `criticalSizePulse: 0.01`, `criticalAlphaMin: 0.55`, and its `flashOnSeconds` changes `0.18` → `0.27`. No other components should change. (If unrelated GameObjects appear in the diff, discard those hunks — only the two boost components should change.)

- [ ] **Step 4: Commit**

```bash
git add Assets/Scenes/FlyScene.unity
git commit -m "Carry the new boost-tuning fields into FlyScene"
```

---

## Task 5: FlyScene play-test verification

This project verifies gameplay feel by play-testing, not automated tests. This task is interactive — coordinate with the user to run it.

- [ ] **Step 1: Enter Play mode in FlyScene and verify**

Enter Play mode (`manage_editor` play control, or have the user press Play). Confirm:

- **Regen partition (1b):** Hold Left Ctrl + a thrust key to drain Boost to empty. Confirm the construct becomes overboosted (cannot boost, slow regen). Confirm that once the meter refills to ~25% the overboost lock lifts — boosting works again — and regen visibly speeds up for the climb to full.
- **Critical throb (1a):** Confirm the bar is red and gently throbs (brightness + a slight ~±5% size pulse, ~1.2 s per cycle) whenever the meter is in its bottom ~25% — both while overboosted and while merely drained low without bottoming out. Confirm it returns to the normal blue look above the 25% mark.
- **Flash timing (2):** Confirm the "Overboosted!" message flash is noticeably slower than before (each flash visible longer).

- [ ] **Step 2: Check the console**

`read_console(types=["error", "warning"], count=20)` — expected: no errors from the boost code.

- [ ] **Step 3: Exit Play mode**

---

## Self-review

Run after completing all tasks (handled by the execution skill / final review):

- **Spec coverage:** 1a → Tasks 1 (`IsBoostCritical`) + 2; 1b → Task 1 (latch change); 2 → Task 3; scene serialization → Task 4; verification → Task 5. All spec sections covered.
- The implementation PR branches off `main`; it shares no files with the Weapon Toolbar Death Response work.
