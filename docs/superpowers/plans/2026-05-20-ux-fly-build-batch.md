# Fly + Build UX Batch — Implementation Doc

Bundled quality-of-life pass over the Fly weapon HUD and the Build toolbar
flyouts. Seven items, one PR. Behaviour-preserving where applicable;
mostly small targeted edits.

**Branch:** `chore/ux-fly-build-batch` off `main`.

**Independence:** touches no files shared with the open HitContext PR (#39),
so the two PRs merge in any order.

---

## Item 1 — Fly: reload bar fix

**Symptom:** the reload-bar foreground above the weapon toolbar buttons
doesn't visibly deplete and refill when firing.

**Hypothesis:** the foreground `Image` is set to `Image.Type.Filled` with
`FillMethod.Horizontal`, but no sprite is assigned. Without a sprite,
Unity's filled-mode rendering does not reliably clip the colored quad
against `fillAmount` (behaviour varies across Unity 6.x). The per-frame
`fillAmount = ReadyFraction` assignment runs, but the visual stays full.

**Fix:** rewrite the foreground bar to use **width-based fill** —
`RectTransform.sizeDelta.x = ready * fullWidth` with a left-anchored
pivot. No sprite dependency, no Filled-mode dependency.

**Files:** `Assets/Scripts/Fly/FlyWeaponToolbarController.cs`.

- In `BuildReloadRect`, the `isFill: true` branch becomes:
  - `pivot = (0, 0.5)` (left edge, vertical centre).
  - `anchorMin = anchorMax = (0.5, 0)` (unchanged — anchor at canvas
    bottom-centre).
  - `anchoredPosition = (anchoredPos.x - size.x / 2, anchoredPos.y + size.y / 2)`
    so the foreground's pivot (left-vertical-centre) sits at the
    background's left-vertical-centre.
  - No `Image.Type.Filled` / `fillMethod` / `fillOrigin` / `fillAmount`
    setup. Default `Type.Simple` solid quad.
- In `Update`, replace the `fillAmount` write with a `sizeDelta` write:

  ```csharp
  RectTransform fgRT = (RectTransform)_reloadBars[i].transform;
  fgRT.sizeDelta = new Vector2(
      reloadBarSize.x * shootingController.Types[i].ReadyFraction,
      reloadBarSize.y);
  ```

## Item 2 — Fly: move Hangar button into ESC pause menu

**Symptom:** the only way back to BuildScene from FlyScene is the
persistent top-right "Hangar" button. The user wants it inside the ESC
pause menu instead.

**Fix:** two-part change.

- **Hide the corner button in FlyScene.** `UIManager.OnSceneStateChanged`
  already labels the corner button "Hangar" vs "Fly!" per scene. Add a
  `SetActive(false)` for the button GameObject when the active scene is
  `FlyScene`; re-enable elsewhere (so BuildScene keeps the "Fly!"
  button).
- **Add a "Hangar" button to PauseMenu.** Insert as the FIRST stacked
  button (above "Menu" → "Back to Desktop"). Visible only when the
  active scene is `FlyScene`; hidden in `BuildScene`. On click: close the
  pause menu (restores `Time.timeScale`), then call
  `SceneSwitcher.Toggle()` — the same Fly→Build path the corner button
  used, so the existing `GameData` snapshot/restore handling applies.
  - Hookup detail: PauseMenu currently builds its UI once in
    `Awake` (DDOL singleton). The Hangar button is created once but its
    visibility is toggled per scene-load via `SceneManager.sceneLoaded`.

**Files:** `Assets/Scripts/Core/UIManager.cs`,
`Assets/Scripts/Core/PauseMenu.cs`.

## Item 3 — Fly: mouse-wheel weapon switching reverts

**Symptom:** with a Pyramid + Cylinder loadout, scrolling the wheel
briefly flickers both buttons (the visual selection-indicator) before
jumping back to the original weapon.

**Hypothesis:** in `FlyShootingController.Update`, the
`IsPointerOverGameObject()` early-return runs BEFORE
`HandleSelectionInputs()`. When the cursor is over the weapon toolbar
(the natural place to scroll), selection inputs are blocked entirely.
The visible "flicker" is from auto-switch reverting an aliased event;
the wheel never reaches `CycleSelected`.

**Fix:** move `HandleSelectionInputs()` BEFORE the pointer-over-UI gate.
The gate continues to guard `HandleFireInput()` only (LMB conflicts with
UI clicks). Scroll + digit selection then works regardless of cursor
position.

```csharp
void Update()
{
    if (PauseMenu.Instance != null && PauseMenu.Instance.IsOpen) return;
    if (!HasWeapons) return;
    AutoSwitchOffDeadType();
    HandleSelectionInputs();          // moved up — selection ignores UI hover

    if (EventSystem.current != null
        && EventSystem.current.IsPointerOverGameObject()) return;
    HandleFireInput();
}
```

**Files:** `Assets/Scripts/Fly/FlyShootingController.cs`.

## Item 4 — Build: remove flyout-on-hover; add 3 s close-after-leave

Two changes to the flyout UX.

### 4a — Remove peek-on-hover entirely

Hovering a non-selected category button currently fades a non-interactive
flyout in after `hoverPeekDelay` (0.3 s). Remove that path. The flyout
opens **only** on click (always pinned) or right-click. Hover does nothing.

- `BuildToolbarController.OnShapeButtonHoverEnter` — body removed (no
  more peek coroutine).
- `BuildToolbarController.OnShapeButtonHoverExit` — body removed; the
  related peek-coroutine stop and `IsPointerOverFlyout` close path are
  superseded by the away-timer below.
- `BuildToolbarController.PeekAfterDelay` — method deleted.
- `CategoryFlyout.OnHoverEnter` / `OnHoverExit` / `PeekAfterDelay` —
  removed for the same reason.

### 4b — Auto-close 3 s after the mouse leaves the flyout buttons

Once a flyout is open AND the mouse is no longer over any of its
buttons, start a 3 s timer. If the mouse re-enters the flyout before the
timer expires, reset to 0. If the mouse stays on a flyout button, the
timer does not start.

Per-frame logic (driven from `BuildToolbarController.Update`):

```csharp
const float FlyoutAwayCloseSeconds = 3f;

if (_flyout != null && _flyout.activeSelf)
{
    if (IsPointerOverFlyout()) _flyoutAwayTimer = 0f;
    else                       _flyoutAwayTimer += Time.unscaledDeltaTime;
    if (_flyoutAwayTimer >= FlyoutAwayCloseSeconds) HideFlyout();
}

for (int i = 0; i < _categoryFlyouts.Count; i++)
    _categoryFlyouts[i].TickAwayTimer(Time.unscaledDeltaTime, FlyoutAwayCloseSeconds);
```

- `BuildToolbarController` gains `float _flyoutAwayTimer`. Reset to 0 in
  `OpenFlyoutForShape` and `HideFlyout`. Ticked in `Update` per the
  snippet above.
- `CategoryFlyout` gains `float _awayTimer` + a new public
  `TickAwayTimer(float dt, float closeSeconds)` method that mirrors the
  same logic (uses the existing `IsPointerOverFlyout()` helper). Reset
  to 0 in `Open`.

**Note on existing close paths:** the explicit close triggers (Esc, M,
shape change, tool change, flyout-entry click) all stay — the away-timer
is a stacked auto-close, not a replacement.

**Files:** `Assets/Scripts/Build/BuildToolbarController.cs`,
`Assets/Scripts/Build/CategoryFlyout.cs`.

## Item 5 — Build: flyout entry swatch on the right

**Symptom:** the colored swatch in each flyout entry sits on the LEFT,
overlapping the entry's text label.

**Fix:** in `BuildToolbarController.BuildEntrySwatch`, anchor and pivot
the swatch to the RIGHT (8 px from the right edge instead of the left).
`CategoryFlyout` reuses the same builder via injection, so this single
change covers both flyouts.

```csharp
rt.anchorMin = new Vector2(1f, 0.5f);
rt.anchorMax = new Vector2(1f, 0.5f);
rt.pivot     = new Vector2(1f, 0.5f);
rt.anchoredPosition = new Vector2(-8f, 0f);
```

**Files:** `Assets/Scripts/Build/BuildToolbarController.cs`.

## Item 6 — Build: shrink flyout entry height + lift above category buttons

### 6a — Entry height -20 %

Default `flyoutEntrySize` changes from `(220, 56)` to `(220, 45)`
(56 × 0.8 ≈ 45). Update both the C# default AND the serialized value in
`Assets/Scenes/BuildScene.unity` so Unity's serialized value matches.

### 6b — Lift the flyout fully above its category button

Today the flyout's bottom edge sits at
`bottomMargin + buttonSize.y / 2 + flyoutBottomGap` — inside the
button's vertical extent. Change to
`bottomMargin + buttonSize.y + flyoutBottomGap` so the bottom edge sits
ABOVE the button's top edge.

- `BuildToolbarController.OpenFlyoutForShape` — y-formula updated.
- `CategoryFlyout.Open` — y-formula updated.

**Files:** `Assets/Scripts/Build/BuildToolbarController.cs`,
`Assets/Scripts/Build/CategoryFlyout.cs`,
`Assets/Scenes/BuildScene.unity` (if `flyoutEntrySize` is overridden
there).

## Item 7 — Fly: static X on fully-dead button + faster partial pulse

### 7a — Static X on fully-dead button

A fully-dead weapon type's button currently greys out without an X. Add:
when fully dead, also show the death mark X at full opacity (static, no
pulse). The X stays alongside the greyed button so the player sees a
clear "this slot is dead" signal.

```csharp
if (_deathMarks[i] != null)
{
    bool showMark = fullyDead || partiallyDead;
    _deathMarks[i].enabled = showMark;
    if (showMark)
    {
        Color c = deathMarkColor;
        if (partiallyDead)
        {
            // Sine pulse, deathMarkAlphaMin → 1, period = deathMarkPulseSeconds.
            float period = Mathf.Max(0.01f, deathMarkPulseSeconds);
            float phase = 0.5f + 0.5f * Mathf.Sin(
                Time.unscaledTime * (2f * Mathf.PI / period));
            c.a = Mathf.Lerp(deathMarkAlphaMin, 1f, phase);
        }
        // Fully dead: static, full opacity (c.a stays at deathMarkColor.a).
        _deathMarks[i].color = c;
    }
}
```

### 7b — Speed up partial pulse 10 %

Default `deathMarkPulseSeconds` changes from `1.0` to `0.9` (10 % shorter
period). Update the serialized value in
`Assets/Scenes/FlyScene.unity` (set to `1` in the F10 PR) to `0.9`.

**Files:** `Assets/Scripts/Fly/FlyWeaponToolbarController.cs`
(RefreshWeaponStates branch + default), `Assets/Scenes/FlyScene.unity`.

---

## Files touched (consolidated)

- `Assets/Scripts/Fly/FlyWeaponToolbarController.cs` — items 1, 7
- `Assets/Scripts/Fly/FlyShootingController.cs` — item 3
- `Assets/Scripts/Core/UIManager.cs` — item 2
- `Assets/Scripts/Core/PauseMenu.cs` — item 2
- `Assets/Scripts/Build/BuildToolbarController.cs` — items 4a, 4b, 5, 6
- `Assets/Scripts/Build/CategoryFlyout.cs` — items 4a, 4b, 6b
- `Assets/Scenes/BuildScene.unity` — item 6a (`flyoutEntrySize`)
- `Assets/Scenes/FlyScene.unity` — item 7b (`deathMarkPulseSeconds`)

## Verification

Compile-clean via `refresh_unity` + `read_console` (filtered to
`Assets/Scripts`). Manual play-test by the user covering:

- **Reload bar:** fire a weapon → the foreground bar visibly depletes and
  refills.
- **Mouse-wheel cycle:** scroll while the cursor hovers the weapon
  toolbar → selection cycles between Pyramid and Cylinder; no flicker /
  revert.
- **Hangar relocation:** in FlyScene, the corner button is gone; ESC
  opens the pause menu with **Hangar / Menu / Back to Desktop** stacked;
  clicking Hangar restores to BuildScene with the saved construct.
  BuildScene still shows the "Fly!" corner button.
- **Flyout-on-hover removed:** hovering a non-selected category button
  no longer previews its flyout.
- **3 s away-close:** click a category to pin its flyout → move the
  cursor away from the flyout → flyout closes after ~3 s. Re-enter
  before then → timer resets.
- **Swatch + layout:** flyout entries show the swatch on the RIGHT (no
  text overlap), entries are ~45 px tall, and the flyout sits fully
  above the category buttons (no vertical overlap).
- **Dead-button X:** destroy all of a weapon type → button greys AND
  shows a static red X. Destroy some of a multi-instance type → X
  pulses, slightly faster than before (period ≈ 0.9 s).
- Compile clean, no `Assets/Scripts` errors / warnings.
