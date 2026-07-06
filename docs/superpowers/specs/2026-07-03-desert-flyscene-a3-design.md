# Milestone A3 — Desert cel look + live toggle — Design Spec

**Date:** 2026-07-03
**Branch:** `explore/desert-flyscene` (experimental — not on `main`)
**Roadmap:** item 4, Milestone A, sub-phase **A3** (`ROADMAP.md §4`).
**Predecessor:** A2 (`docs/superpowers/specs/2026-06-12-desert-flyscene-a2-design.md`) — done, gate = SHIP.
**Status:** Accepted design, pre-implementation.

## 1. Purpose & scope

A1/A1.5/A2 built and populated the desert basin under the project's **current** renderer. A3
adopts the desert's stylized **cel look** for FlyScene — the screen-space **outline** + the
warm color-graded **post-FX** — and makes it a **live runtime toggle** in the pause menu, so
the as-is vs cel look can be A/B-compared and refined *in-game* rather than guessed at, then a
default chosen.

**Reframe from exploration (important):** most of the look is *already* in FlyScene. Its
formations are the same prefabs as `DesertSandbox`, so they already use the cel-shaded rock
materials (`Desert/CelShaded`), and FlyScene already has the desert skybox + warm fog (A1). The
two pieces the toggle switches on are:
1. the screen-space **outline** — `Desert_Renderer` + `OutlineRendererFeature`, **built but
   unused**: it's registered as renderer index 1 in `PC_RPAsset` but no camera selects it (not
   even DesertSandbox's, which renders with the default `PC_Renderer`); and
2. the **`DesertVolumeProfile`** color grade + bloom — FlyScene currently has **no Volume**.

Continues on `explore/desert-flyscene`. Reuses the existing shaders/renderer/profile as-is
(tune params only) — A3 authors no new shaders.

### In scope
- A global `Volume` in FlyScene carrying `DesertVolumeProfile` (grade + bloom).
- A `DesertLookController` that live-switches the **camera renderer** (`PC_Renderer` ↔
  `Desert_Renderer`) **+ the Volume weight**.
- A **"Cel look" toggle in the `PauseMenu`** — default ON, saved to `PlayerPrefs`, active only
  in FlyScene.
- A **refinement pass** driven by the toggle: outline thickness, `ThrusterMat`, grade strength,
  and the "does the Lit ship read against the cel world?" judgment.
- Decision gate (ship / iterate / shelve).

### Out of scope
- **Ship/projectile cel-material conversion** ("full toon" / option C) — a *conditional*
  follow-up only if the Lit ship reads poorly; flagged, not built.
- The A2-review **shadow-pipeline split** (desert-specific pipeline asset for a scene-local
  shadow distance) — **parked for A4**.
- The tilted-**turret cosmetic** — **parked for A4**.
- New cel/outline/skybox **shader authoring** — reuse `CelShaded` / `OutlineEdgeDetect` /
  `GradientSkybox` / `DesertVolumeProfile` as-is.
- `DesertSandbox.unity` — untouched. Menu/Hangar/Build — the toggle is inert there.

## 2. Decisions locked during brainstorming (2026-07-03)

| Topic | Decision |
|---|---|
| Look target | **Outline + grading** (the roadmap's "cel shader + screen-space outline"): activate the outline renderer + add the volume grade. Ship stays **Lit** (evaluated via the toggle). |
| Live toggle | **Yes** — the point of A3. Camera renderer index + Volume weight, switched at runtime. |
| Toggle UI | A **"Cel look" toggle in the existing `PauseMenu`** (not a throwaway keybind) — leans permanent. |
| Default state | **ON** (cel) — the milestone target; one click to compare. |
| Persistence | **Saved to `PlayerPrefs`** (a real graphics option). |
| Scope of switch | Renderer index (`PC_Renderer` deferred ↔ `Desert_Renderer` forward) **+** Volume weight. Formations already cel; **no material swaps**. |
| Follow-ups | Shadow-pipeline + turret cosmetic → **A4**. Ship-cel (option C) → conditional follow-up. |

## 3. The look (toggle ON)

- **Renderer:** the FlyScene camera renders with `Desert_Renderer` (index 1) — **Forward** +
  `OutlineRendererFeature` (Roberts-cross depth+normal edge detect; black; thickness 1.5;
  injected at `BeforeRenderingPostProcessing`). This drops `PC_Renderer`'s **Deferred + SSAO** —
  acceptable for a ~single-directional-light desert (verify SSAO isn't relied upon).
- **Post-FX:** a new **global `Volume`** GameObject in FlyScene, `sharedProfile =
  DesertVolumeProfile` (`ColorAdjustments` contrast 6 / saturation 6 / warm filter `~(1,0.97,0.9)`;
  `Bloom` intensity 0.55, threshold 1.1). Its `weight` is driven by the controller (1 = cel,
  0 = as-is).
- **Content behaviour:** formations = cel-shaded (unchanged); ship + projectiles = URP Lit →
  gain outlines + the grade but stay Lit-shaded (not cel-banded); VFX = additive transparent →
  render after opaque, unaffected by cel/outline; HUD = Screen-Space-Overlay → unaffected.

## 4. The switch — `DesertLookController` + PauseMenu toggle

- **`DesertLookController`** (new, `Assets/Scripts/Desert/`): a MonoBehaviour placed in FlyScene.
  Holds `bool celOn`. On `Start`: read `PlayerPrefs` (default `true`), resolve the main camera's
  `UniversalAdditionalCameraData` + the FlyScene `DesertVolumeProfile` `Volume`, apply the state.
  Public `Apply(bool)` / `Toggle()`: `cameraData.SetRenderer(celIndex : asIsIndex)` + set
  `volume.weight` (1 : 0) + write `PlayerPrefs`. Exposes a discoverable handle (static
  `Instance` or a registration event) so the persistent PauseMenu can find it.
- **Renderer indices:** the `PC_RPAsset` list is `[0 = PC_Renderer, 1 = Desert_Renderer]`, so
  `celIndex = 1`, `asIsIndex = 0` (i.e. the pipeline default). Resolve/verify at implementation;
  avoid hard-coding a bare `1` without a comment tying it to `Desert_Renderer`.
- **PauseMenu toggle:** add a UI `Toggle` ("Cel look") to the `PauseMenu` (`Scripts/Core/`). On
  menu open: if a `DesertLookController` exists (i.e. we're in FlyScene), **show + enable** the
  toggle synced to the current state; otherwise **hide/disable** it. Flipping the toggle calls
  the controller. Match the project's legacy `UnityEngine.UI` + `LegacyRuntime.ttf` convention.
- **Persistence:** `PlayerPrefs` key e.g. `"desert.celLook"` (int 0/1); default 1.

## 5. Refinement (the payoff of the toggle)

Once the toggle works, A/B in Play and tune — in-editor, no code churn unless needed — and
record findings for the gate:
- **Outline thickness** (1.5) at the gameplay camera distance — thicker / thinner?
- **`ThrusterMat`** (metallic 0.4 / smoothness 0.6): specular flattens under the grade + outline;
  may want lower metallic or a small tweak so it doesn't read oddly.
- **Grade strength** (contrast 6 / saturation 6) at FlyScene's ~2.5× longer sightlines — too much?
- **The judgment call:** does the Lit-but-outlined ship read coherently against the cel world,
  or does it want ship-cel (the option-C follow-up)?

## 6. Verification

No automated tests. Manual, in the Unity Editor on the **main project root** (per `CLAUDE.md`):

1. Compile/console clean after the new script + scene/PauseMenu edits.
2. **Spike first:** confirm `SetRenderer` **live-switches** Deferred↔Forward cleanly (see §7).
3. Enter Play in FlyScene: default **ON** (cel) — outline + grade visible; the PauseMenu "Cel
   look" toggle flips as-is ↔ cel live with **no errors / no crash / no hang**, look appears +
   disappears correctly.
4. Choice **persists** across Play sessions (`PlayerPrefs`); toggle is **inert/hidden** in
   Menu/Hangar/Build.
5. Frame cost acceptable in cel mode.
6. `DesertSandbox.unity` still opens unchanged.
7. **Human re-fly** to judge the look + run the §5 refinement.

**Decision gate (end of A3):** ship / iterate / shelve — recorded before A4.

### A3 outcome (2026-07-04) — **SHIP**

Re-flown by the maintainer — cel look adopted, verdict SHIP. FlyScene now toggles between the
as-is renderer and the desert cel look (screen-space outline + warm grade + bloom) **live** from
the Settings menu. Built + verified through the per-task checks:

- **Renderer + volume:** the camera switches `PC_Renderer` ↔ `Desert_Renderer` (index 1) at
  runtime; a global `DesertLook` Volume carries `DesertVolumeProfile`. **Blocker found + fixed:**
  `Desert_Renderer` had `postProcessData = null`, so the grade/bloom silently didn't render under
  the cel renderer — proved with a `saturation = -100` grayscale test (no effect until fixed),
  then assigned it the same `PostProcessData` `PC_Renderer` uses (DesertSandbox renders with
  `PC_Renderer`, so it's unaffected).
- **Toggle:** `CelLookSettings` (Core, PlayerPrefs `desert.celLook`, default on) + `OnChanged`;
  `DesertLookController` (FlyScene) applies it (`SetRenderer` + volume weight) on `Start` + on
  change; a "Cel look (desert)" toggle in the `SettingsMenu` debug panel. Default-on, live switch,
  and persistence all confirmed by the maintainer; the runtime Deferred↔Forward switch does not
  throw (volume weight tracks 1↔0 headlessly).
- **Refinement — outline distance blobbing:** the uniform-width outline merged into black blobs
  on distant crowded geometry. Fixed by scaling the sample offset with the center pixel's
  eye-depth — full within `thicknessFalloffStart` (35 u), tapering ~inversely with distance,
  clamped to `minThicknessScale` (tuned to **0.2**). Verified via edit-time screenshots (21 u
  close-up full; distance thin + un-blobbed). Both params exposed on the outline feature.
- **Ship-vs-cel:** the Lit-but-outlined ship reads fine against the cel world (maintainer call)
  — **option C (ship-cel materials) NOT needed.**

**Files touched beyond the §8 manifest** (all warranted by the above): `Assets/Settings/Desert_Renderer.asset`
(postProcessData + the two outline params), `Assets/Shaders/OutlineEdgeDetect.shader`, and
`Assets/Scripts/Desert/OutlineRendererFeature.cs`.

**Next:** A4 (decide land/shelve + docs; also the parked shadow-pipeline split + turret cosmetic).
Milestone A's build work (A1–A3) is complete.

## 7. Risks & notes

- **Live renderer switch (Deferred↔Forward) — the key risk.** Spike `SetRenderer` live-switching
  **first**, before building the UI. **Fallback** if janky: use a single **Forward** renderer and
  toggle only the outline feature + Volume (the "as-is" side then loses SSAO/Deferred, a slightly
  less faithful baseline but a robust toggle).
- **PauseMenu is a persistent cross-scene singleton** — the toggle must gracefully handle scenes
  with no `DesertLookController` (hide/disable), and re-sync each time the menu opens.
- **Forward drops SSAO** — verify the desert doesn't visibly rely on it.
- **One Volume only** — FlyScene has no post-FX today; don't introduce a second global volume.
- **Reversibility:** all additive (one Volume + one controller + one menu toggle); the look flips
  off at runtime, and the default can be set to as-is if the gate says so.

## 8. File manifest

**New**
- `Assets/Scripts/Desert/DesertLookController.cs` (+ `.meta`) — the runtime look switch.
- A `Volume` GameObject in FlyScene (scene edit) carrying `DesertVolumeProfile`.

**Edited**
- `Assets/Scenes/FlyScene.unity` — add the `Volume` + `DesertLookController` (the camera's
  renderer index is set at runtime by the controller, not serialized on the camera).
- The `PauseMenu` (`Scripts/Core/`) + its persistent-HUD UI — add the "Cel look" toggle.

**Unchanged**
- `Desert_Renderer`, `PC_Renderer`, `PC_RPAsset`; `CelShaded` / `OutlineEdgeDetect` /
  `GradientSkybox` shaders; `DesertVolumeProfile`; all desert materials; `DesertSandbox.unity`.
