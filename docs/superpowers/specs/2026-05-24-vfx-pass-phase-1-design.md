# VFX Pass — Phase 1 (Post-Processing + Debug Tab) — Design Spec

**Status:** Approved design, ready for implementation planning
**Date:** 2026-05-24
**Branch:** `feat/vfx-phase-1` off `main`
**ROADMAP item:** Up Next #1 (Extended VFX pass) — this is the first of multiple PRs
**Ideas backlog:** `docs/vfx_pass_ideas.md` (committed as part of this PR)

## Overview

First concrete VFX pass PR. Two paired deliverables:

1. **Phase A — post-processing.** Five URP Volume overrides
   (`Bloom`, `Vignette`, `Tonemapping`, `ColorAdjustments`,
   `ChromaticAberration`) added to the project's URP Volume profile
   with starter tunings. Cheap visible polish; the highest perceived-
   quality lift per minute of work in the entire VFX backlog.
2. **Debug tab + persistence.** The seventh tab in the Settings menu
   (just merged in PR #46) — five runtime toggles, one per effect,
   each with a hover-tooltip description sourced from
   `docs/vfx_pass_ideas.md`. Toggles persist across sessions via
   `PlayerPrefs` (one key per setting). This is the Settings menu's
   first real persistence consumer, the persistence layer we
   deliberately deferred in the Settings scaffold.

A small **reusable `Tooltip` helper** (TooltipHud + TooltipTrigger)
lands in the same PR — the Debug tab is its first consumer, but it
will be reused by future Settings controls and HUD elements.

After this PR, every subsequent VFX PR appends to the same Debug-tab
pattern: register a toggle key, add a toggle UI row, apply effect on
change. Subsequent PRs cover Phase B (engines, weapons, destruction
prefabs), Phase C (shaders + scripted sequences), and Phase D (alpha-
cube cinematic death) per the `vfx_pass_ideas.md` triage.

## Background — current systems

**URP Volume profiles.** Three exist (`Assets/Settings/`):

- `DefaultVolumeProfile.asset` — used by the main game scenes.
- `SampleSceneProfile.asset` — template scene only (`SampleScene`).
- `DesertVolumeProfile.asset` — `DesertSandbox` experimental scene.

All three contain **no override components today** — bloom, vignette,
etc. are Inspector-toggles waiting to fire. Every scene has a `Volume`
GameObject already pointing at the relevant profile (verified by grep
across the five `Assets/Scenes/*.unity` files).

**No existing `TrailRenderer` / `ParticleSystem` usage anywhere in
`Assets/Scripts/`** — the VFX layer is greenfield. Phase 1 doesn't
introduce particles either (those are Phase B); it stays purely in
the URP Volume / `Bloom`-tier surface.

**Settings menu.** Just merged in PR #46 (commit `101a67d`).
`SettingsMenu : MonoBehaviour` in `CubeFly.Core` — DDOL singleton,
six placeholder tabs (`General` / `Display` / `Graphics` / `Audio` /
`Controls` / `Gameplay`), each rendering a centred "Coming soon"
label inside its content panel. `static readonly string[] TabNames`
drives both the sidebar and the content-panel array; appending a
seventh name + a non-"Coming soon" content panel is the integration
shape.

**No persistence in the project today.** `GameData` is a runtime
static (lives in memory through scene loads); `ConstructSave` is
per-slot JSON via `SaveManager` on disk; that's the entirety of
state-persistence. No `PlayerPrefs` is used anywhere.

## Scope

| In | Out |
|---|---|
| 5 post-processing Volume overrides on the main game profile | Phase B effects (engines, weapons, destruction prefabs) |
| 7th "Debug" tab in `SettingsMenu` with 5 toggles | Phase C shaders (laser glow, shield dome, etc.) |
| `VfxSettings` static class wrapping `PlayerPrefs` | Phase D alpha-cube cinematic death |
| `VfxApplier` DDOL singleton applying settings on scene load + change | Per-scene divergent grading (single shared override set for now) |
| Reusable `Tooltip` helper (TooltipHud + TooltipTrigger) | Contextual ChromaticAberration ramp on damage / overheat |
| `UIStyle.BuildToggle` helper | Audio of any kind |
| Commit `docs/vfx_pass_ideas.md` (no longer "scratch draft") | Volume sliders / FOV / rebinding / any non-VFX Settings controls |
| README + ROADMAP touches | Refactoring `SettingsMenu`'s tab list into a registry |

## The five Phase-A effects (starter tunings)

| Override | Value | Notes |
|---|---|---|
| **Bloom** | Intensity 0.6, Threshold 1.0, Scatter 0.7 | Lifts emissive — laser beam, reactor glow, muzzle flash will benefit when those land. ★ deferred laser-beam-glow seed from PR #44. |
| **Vignette** | Intensity 0.25, Smoothness 0.4, Colour ~black | Subtle base; a future damage-flash variant (Phase B / HUD work) will modulate intensity via script. |
| **Tonemapping** | Mode = ACES | Stops bright effects clipping to pure white; gives a cinematic colour response. |
| **ColorAdjustments** | Post Exposure 0, Contrast +5, Saturation +5, Hue Shift 0 | Light cinematic lift baseline; per-scene divergence is a follow-up. |
| **ChromaticAberration** | Intensity 0.08 | Subtle baseline; the contextual ramp on low HP / overheat / shield-collapse is deferred to a later HUD-VFX PR. |

All five default to ON in the Debug tab. Players can disable any they
personally find noisy.

## Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│  SettingsMenu (existing DDOL singleton — PR #46)                 │
│  └── Debug tab (NEW, 7th)                                        │
│      └── 5x { Toggle + Label + TooltipTrigger }                  │
│           │                                                      │
│           └─ OnValueChanged → VfxSettings.<Effect> = value       │
└──────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌──────────────────────────────────────────────────────────────────┐
│  VfxSettings (static, CubeFly.Core)                              │
│  • Five typed bool properties                                    │
│  • Get → PlayerPrefs.GetInt(key, 1) != 0                         │
│  • Set → PlayerPrefs.SetInt(key, …); .Save(); Changed?.Invoke()  │
│  • static event Action Changed                                   │
└──────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌──────────────────────────────────────────────────────────────────┐
│  VfxApplier (DDOL singleton, BeforeSceneLoad bootstrap)          │
│  • Listens to SceneManager.sceneLoaded + VfxSettings.Changed     │
│  • Apply(): finds scene's Volume, toggles each override's        │
│    .active field on its profile based on VfxSettings             │
│  • Idempotent; missing overrides on a profile are silently       │
│    skipped (TryGet pattern)                                      │
└──────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌──────────────────────────────────────────────────────────────────┐
│  URP Volume Profile (DefaultVolumeProfile.asset, modified)       │
│  • Bloom, Vignette, Tonemapping, ColorAdjustments,               │
│    ChromaticAberration overrides added with starter values       │
└──────────────────────────────────────────────────────────────────┘
```

### `VfxSettings` — PlayerPrefs static facade

`Assets/Scripts/Core/VfxSettings.cs`, ~80 lines.

```csharp
public static class VfxSettings
{
    const string KBloom               = "VfxBloom";
    const string KVignette            = "VfxVignette";
    const string KTonemapping         = "VfxTonemapping";
    const string KColorAdjustments    = "VfxColorAdjustments";
    const string KChromaticAberration = "VfxChromaticAberration";

    public static event Action Changed;

    public static bool Bloom               { get => Get(KBloom); set => Set(KBloom, value); }
    public static bool Vignette            { get => Get(KVignette); set => Set(KVignette, value); }
    public static bool Tonemapping         { get => Get(KTonemapping); set => Set(KTonemapping, value); }
    public static bool ColorAdjustments    { get => Get(KColorAdjustments); set => Set(KColorAdjustments, value); }
    public static bool ChromaticAberration { get => Get(KChromaticAberration); set => Set(KChromaticAberration, value); }

    static bool Get(string k) => PlayerPrefs.GetInt(k, 1) != 0;   // default ON
    static void Set(string k, bool v)
    {
        PlayerPrefs.SetInt(k, v ? 1 : 0);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }
}
```

- Default `1` (ON) for every key (first-launch friendly).
- Each `Set` saves immediately and fires `Changed` — no batching, no
  Apply button. The Debug tab's purpose is real-time A/B comparison.
- All five keys cleanly scope under the `Vfx` prefix; future Settings
  consumers append `Audio`, `Display`, `Controls`, `Gameplay` prefixes.

### `VfxApplier` — DDOL singleton

`Assets/Scripts/Core/VfxApplier.cs`, ~120 lines.

Mirrors `PauseMenu` / `GameOverMenu` / `SettingsMenu` pattern:

- `[DefaultExecutionOrder(-1500)]` — between `SettingsMenu` (-2000)
  and `PauseMenu` (-1000); not strictly necessary but keeps ordering
  consistent with the rest of the persistent-UI tier.
- `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` self-bootstrap.
- `static SettingsMenu Instance { get; private set; }` (well, named
  `VfxApplier` — same pattern).
- `Awake`: instance check, `DontDestroyOnLoad`, subscribe to
  `SceneManager.sceneLoaded` and `VfxSettings.Changed`, call `Apply()`
  once for the current scene.
- `OnDestroy`: unsubscribe, clear `Instance`.

```csharp
void Apply()
{
    Volume v = FindFirstObjectByType<Volume>();        // null-safe
    if (v == null || v.profile == null) return;        // no-op
    VolumeProfile p = v.profile;

    if (p.TryGet<Bloom>(out var bloom))             bloom.active             = VfxSettings.Bloom;
    if (p.TryGet<Vignette>(out var vignette))       vignette.active          = VfxSettings.Vignette;
    if (p.TryGet<Tonemapping>(out var tonemapping)) tonemapping.active       = VfxSettings.Tonemapping;
    if (p.TryGet<ColorAdjustments>(out var color))  color.active             = VfxSettings.ColorAdjustments;
    if (p.TryGet<ChromaticAberration>(out var ca))  ca.active                = VfxSettings.ChromaticAberration;
}
```

- **Idempotent.** Safe to call from any event (scene load, change,
  domain reload). Each call re-reads `VfxSettings` and re-applies.
- **Profile-agnostic.** Doesn't care which `VolumeProfile` the
  scene's `Volume` references — it just probes for the five overrides
  via `TryGet`. Scenes that don't have a volume (e.g. `HangarSelect`
  if it has no Volume GameObject) are silently skipped.
- **Order-independent.** The first event (typically `Awake` itself
  before the first scene-load event fires) handles the initial state;
  subsequent events keep it in sync.

`FindFirstObjectByType<Volume>` is Unity 6.x's replacement for the
deprecated `FindObjectOfType` — appropriate for one-shot scene-load
discovery (called once per scene-load, not per frame).

### `SettingsMenu` Debug-tab integration

`Assets/Scripts/Core/SettingsMenu.cs`, modified:

1. Append `"Debug"` to `TabNames` (size 6 → 7). Sidebar layout
   continues to work — the existing for-loop already handles any
   length.
2. In the content-panel-construction loop, **replace the placeholder
   "Coming soon" label for the Debug tab** with a vertical stack of
   5 toggle rows. The other six tabs keep the "Coming soon"
   placeholder.
3. Each row is built by a new helper:
   ```csharp
   void BuildDebugToggle(RectTransform parent, string label,
       string tooltip, int rowIndex,
       Func<bool> getter, Action<bool> setter);
   ```
   - Creates a `Toggle` via the new `UIStyle.BuildToggle` (see below).
   - Anchors at Y = `-rowIndex * 60f` (60 px per row).
   - Attaches a `TooltipTrigger` child to the label with the tooltip
     text.
   - Wires `Toggle.onValueChanged.AddListener(setter)`.
   - Initial `Toggle.isOn = getter()`.

4. The five tooltip texts (sourced from `docs/vfx_pass_ideas.md` §0,
   shortened where the source line is too long for a hover):

| Toggle | Tooltip |
|---|---|
| Bloom | "Globally lifts emissive surfaces (laser beam, reactor glow, muzzle flash). High visual impact for low cost." |
| Vignette | "Subtle dark edge that focuses attention on the centre of the screen." |
| Tonemapping (ACES) | "Cinematic tone curve. Stops bright effects clipping to pure white." |
| Colour grading | "Light contrast and saturation lift for cinematic colour response." |
| Chromatic aberration | "Subtle colour-fringe at screen edges. Some find it muddies the picture — toggle off if so." |

### `UIStyle.BuildToggle` helper

`Assets/Scripts/Core/UIStyle.cs`, new method ~20 lines, mirrors
`BuildLabeledButton`:

```csharp
public static Toggle BuildToggle(Transform parent, string labelText,
    Vector2 size, int fontSize = 22)
{
    // GameObject + RectTransform + Toggle component + Image (background)
    //                                              + Image child (checkmark)
    //                                              + Text child (label, right of toggle)
    // Wires Toggle.targetGraphic and .graphic to the right Image components.
    // Uses BackgroundIdle / TintNormal / LabelColor from the existing palette.
    // Returns the Toggle; caller uses .onValueChanged and .isOn.
}
```

Reusable for future Settings controls (Audio mute toggles, Gameplay
options, etc.).

### `Tooltip` helper

Two files in `Assets/Scripts/Core/`:

**`TooltipHud.cs`** (~60 lines) — DDOL singleton:

- Lazy-creates on first `Show` call (no `BeforeSceneLoad` bootstrap
  needed — no consumers until SettingsMenu's Debug tab is opened).
- Holds a small `Text` label parented to `PersistentHud.Instance.Root`
  with **its own Canvas override at `sortingOrder = 500`** — above
  `SettingsMenu` (350) and `GameOverMenu` (400). Tooltips are always
  on top.
- Background is a small translucent dark `Image` so the label is
  readable against bright UI.
- `Show(string text, Vector2 screenPos)` positions the panel +20px
  to the right and -20px below the cursor (with screen-edge clamping
  so tooltips near the right/bottom edge flip to the other side),
  sets the text, and activates.
- `Hide()` deactivates.
- `Update`: if shown, updates position to the current mouse position
  (so the tooltip moves with the cursor while hovering).

**`TooltipTrigger.cs`** (~30 lines) — small `MonoBehaviour`:

- Implements `IPointerEnterHandler`, `IPointerExitHandler`.
- `public string text;` — set in code at construction time.
- `OnPointerEnter` → `TooltipHud.Instance.Show(text, eventData.position)`.
- `OnPointerExit` → `TooltipHud.Instance.Hide()`.

The new toggle rows in the Debug tab attach a `TooltipTrigger` to
their label `GameObject` (which already has a `Text` raycast target
since UIStyle.BuildToggle's label component is built with
`raycastTarget = true`).

## Volume profile changes

Modify **`Assets/Settings/DefaultVolumeProfile.asset`** to add the
five overrides with the §3 starter values. `.active` is left baked-in
to `true` — `VfxApplier` flips it based on `VfxSettings` on the first
post-bootstrap apply.

`SampleSceneProfile.asset` and `DesertVolumeProfile.asset` left
untouched in this PR — the template scene is unused and the desert
work is a separate ROADMAP item. When/if the Desert experiment lands,
its PR can mirror the same overrides.

(Verification step in implementation: confirm via grep / scene
inspection that the main game scenes' Volume GameObjects actually
reference `DefaultVolumeProfile.asset`. If any reference a different
profile, that profile gets the same overrides instead. The design is
profile-agnostic — `VfxApplier` just probes whichever profile the
active scene's Volume points at.)

## Files

**New:**
- `Assets/Scripts/Core/VfxSettings.cs` (~80 lines).
- `Assets/Scripts/Core/VfxApplier.cs` (~120 lines).
- `Assets/Scripts/Core/TooltipHud.cs` (~60 lines).
- `Assets/Scripts/Core/TooltipTrigger.cs` (~30 lines).

**Modified:**
- `Assets/Scripts/Core/SettingsMenu.cs` — append `"Debug"` to
  `TabNames`; replace Debug tab's "Coming soon" placeholder with the
  5-toggle vertical stack; add `BuildDebugToggle` helper method
  (~50 lines of additions).
- `Assets/Scripts/Core/UIStyle.cs` — add `BuildToggle` helper
  (~20 lines).
- `Assets/Settings/DefaultVolumeProfile.asset` — add 5 overrides.
- `docs/vfx_pass_ideas.md` — currently untracked draft; commit as
  part of this PR (it's the authoritative VFX backlog now and the
  source for Debug-tab tooltip text). No content changes; just
  promotes it from untracked to tracked.
- `README.md` — short note in "What's In Here" about the post-
  processing tier + Debug tab; update the existing Main Menu Settings
  row to mention the Debug tab (currently "six placeholder tabs"
  becomes "six placeholder tabs + a Debug tab for VFX toggles").
- `ROADMAP.md` — annotate Up Next #1 (Extended VFX pass) as
  "in progress — Phase 1 (post-processing + Debug tab) shipped /
  in-flight", with Phase B/C/D listed as the remaining sub-PRs.

**No new prefabs, scriptable objects, asmdefs, or scene file changes.**
No new layers.

## Out of scope (explicit)

- **Phase B effects** (engine plumes, RCS puffs, muzzle flash, bullet
  tracers, bullet/rocket impact, rocket smoke trail + detonation,
  cube death enhancement, etc.) — each gets its own PR. They all use
  the Debug-tab pattern established here: append a toggle key to
  `VfxSettings`, add a toggle row to the Debug tab, route changes
  through whatever effect-application layer they need.
- **Phase C shaders / scripted sequences** (laser beam glow, shield
  dome with hex/fresnel + hit ripple, rocket detonation multi-emitter,
  delete-tool dissolve, reactor glow + stress sparks + Eject sequence).
- **Phase D alpha-cube cinematic death.**
- **Contextual CA ramp on damage / overheat / shield collapse** —
  the static ChromaticAberration toggle lands here; the dynamic
  intensity-ramp script is HUD-VFX work in a later PR.
- **Per-scene divergent colour grading.** Single shared override set
  for now; per-scene mood (cool MainMenu, neutral BuildScene,
  cinematic FlyScene, warm Desert) is a follow-up that just adds
  per-profile overrides without touching the architecture here.
- **Audio.** Deferred entirely to an audio pass.
- **`Tooltip` advanced features.** Delay-before-show, fade-in/out
  animations, multi-line wrap, rich text — all YAGNI for the scaffold.
  Current Tooltip shows instantly on enter, hides instantly on exit,
  single-line text only.
- **Non-VFX Settings controls.** Volume sliders, FOV, mouse
  sensitivity, key rebinding — each is its own future tab-content PR.
  This PR only fills the Debug tab; the other six remain "Coming soon".

## References

- `docs/vfx_pass_ideas.md` — the authoritative VFX backlog, source
  of tooltip text and triage phasing.
- `docs/superpowers/specs/2026-05-24-settings-menu-design.md` — the
  Settings menu scaffold this PR extends with its 7th tab.
- `Assets/Scripts/Core/SettingsMenu.cs` — DDOL singleton, `TabNames`
  array, sidebar + content-panel loop.
- `Assets/Scripts/Core/UIStyle.cs` — `BuildLabel`, `BuildLabeledButton`,
  palette — pattern for the new `BuildToggle` helper.
- `Assets/Scripts/Core/PersistentHud.cs` — shared canvas the tooltip
  hud parents under.
- `Assets/Settings/DefaultVolumeProfile.asset` — the URP Volume
  profile this PR adds the five overrides to.
- `ROADMAP.md` — Up Next #1 (Extended VFX pass).
