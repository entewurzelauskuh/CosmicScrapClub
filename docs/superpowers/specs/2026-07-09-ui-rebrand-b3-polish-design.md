# UI Rebrand — B3-polish (Play-check Batch) — Design Spec

- **Milestone:** B (UI rebrand) · **Sub-phase:** B3-polish (post-B3c play-check feedback)
- **Branch:** `explore/ui-rebrand` (@ `159628a` at spec time) · **PR:** #55 (draft)
- **Date:** 2026-07-09
- **Status:** design approved (brainstorm gate passed); spec under review
- **Bundles with:** the held B3c push — this batch fixes issues surfaced by the B3c
  Play-check, so the whole chain pushes together after this batch's gate.

## 1. Goal & context

Fold in 11 grouped play-check fixes across the shared UI layer + MainMenu +
HangarSelect + BuildScene. **Presentation-only**; legacy `UnityEngine.UI` (no TMP);
colours from `CscPalette`/`CscTheme`; `new Color` sRGB value/255. Organised **by file**
so each is touched once — the shared-layer changes (`UIStyle`/`CscPalette`) ripple to
every scene.

## 2. Decisions locked in brainstorm

| Topic | Decision |
|---|---|
| All-caps scope | **Buttons + titles/headers/tab-labels** (Anton/Condensed display roles). Body text, stat values, help/hint text stay normal case. |
| Letter-spacing | Legacy `Text` has no native spacing → a small custom **`LetterSpacing : BaseMeshEffect`** (the Unity-typical approach). ~+5% of font size per gap. |
| Signal-red | `CscPalette.Critical` (#F24033) = `CscTheme.DangerFill`. |
| Ochre / orange-red | `#d9a441` = `Ochre300` = `PrimaryFill`; `#c8521e` = `Orange600`. No new tokens. |
| FLY! button | The shared persistent `UIManager` corner button, but **shown only in BuildScene** (FlyScene uses the pause-menu Hangar) → restyle is Build-scoped in effect; pulls B4's CTA work forward. |
| Rounded card corners | Procedural rounded-rect feathered sprite (uGUI needs a sprite; extends the B3a `MakePlateSprite` technique). |

## 3. Design detail (by file)

### 3.1 `Assets/Scripts/Core/CscPalette.cs`
- **(6)** `BackgroundIdle` → `#2a232c` = `new Color(0.165f, 0.137f, 0.173f, 0.9f)`. Ripples
  to all ghost buttons + the dropdown control. (The hover-contrast tuning still holds:
  hover = base × `TintHighlight`.)

### 3.2 `Assets/Scripts/Core/UIStyle.cs` (shared workhorse)
- **(1) All-caps.** `BuildLabeledButton` uppercases its label (`ToUpperInvariant`) — covers
  every button. `BuildLabel` gains `bool upper = false`; title/header call sites pass
  `upper: true`. Body/stats/help keep the default (normal case).
- **(2a/2b/7c) Button variants.** Add `ButtonKind kind = ButtonKind.Ghost` (enum: `Ghost` /
  `Primary` / `Danger`) to `BuildLabeledButton`:
  - `Ghost` = current (`BackgroundIdle` fill, `LabelColor` text).
  - `Primary` = `CscTheme.PrimaryFill` (Ochre300) fill, `CscTheme.TextOnLight` (Scorch) text.
  - `Danger` = `CscTheme.DangerFill` (Critical) fill, white text.
  - All three keep the shared `InteractiveColors()` ColorBlock (`TintHighlight`/`Pressed`) —
    consistent hover/press feedback ("same as the dark buttons").
- **(4) Press-stamp.** Rework `UIClickBounce` (in place; keep the class name + the `bounce`
  param) into a **press-stamp**: on **pointer-DOWN** (`IPointerDownHandler`) translate the
  `RectTransform` by its `Shadow.effectDistance` (into the toon shadow — the BRAND
  "stamp"); on **pointer-UP/exit** restore. Reads the sibling `Shadow` for the offset,
  falls back to `(6,-6)`. Fixes the current after-click scale-bounce timing. Uses unscaled
  time-free instant translate (or a ≤0.06s ease).
- **(5) Letter-spacing.** New `Assets/Scripts/Core/LetterSpacing.cs` (`BaseMeshEffect`,
  `[RequireComponent(typeof(Text))]`): `ModifyMesh(VertexHelper)` adds a per-character
  accumulating x-offset (≈ `fontSize * 0.05`), redistributed per line for center/right
  alignment. Applied to every `BuildLabeledButton` label (menu + slot buttons — point 5a)
  and to slot-card titles (point 5b) via a `UIStyle.ApplyLetterSpacing(Text, float)` helper.
- **(3b/8) Procedural sprites.** Generalise the plate generator:
  - `MakeRoundedPlate(w, h, radius, border, fill, ink)` — feathered rounded-rect (corner
    radius + AA), for slot cards.
  - `MakeHazardStripe(size, stripePx, a, b)` — anti-aliased diagonal stripes (feathered
    edges, `Bilinear`), replacing the point-filtered `tile_hazard_stripe` tile so the build
    band's diagonals read smooth (the same AA treatment as the MainMenu title plate).

### 3.3 `Assets/Scripts/Core/CscTheme.cs`
- No change — reuse `PrimaryFill` / `DangerFill` / `TextOnLight` / `InteractiveColors`.

### 3.4 `Assets/Scripts/MainMenu/MainMenuController.cs`
- **(2a)** Hangar button → `ButtonKind.Primary` (ochre + dark text).
- **(3a)** Bump the menu-button `AddToonShadow` 6f → **8f** to match the wordmark's toon
  shadow "sign-post" look. (Buttons already carry a shadow; this strengthens it.)
- Uppercase, press-stamp, new base colour, letter-spacing all inherited from `UIStyle`.

### 3.5 `Assets/Scripts/HangarSelect/HangarSelectController.cs`
- **(2b)** Continue / "Start new construct" (`PrimaryButton`) → `ButtonKind.Primary`.
- **(2c)** Delete button **hover → red**: a small pointer enter/exit handler
  (`UIHoverRecolor` component or an `EventTrigger`) that sets the button's `Outline`
  effectColor + label colour to `CscPalette.Critical` on enter, restores on exit.
- **(3b)** Slot cards → `MakeRoundedPlate` sprite fill (rounded corners) + keep the existing
  `AddToonShadow(6f)`.
- **(5b)** Slot titles ("Slot n") → `ApplyLetterSpacing`; and `upper: true` (→ "SLOT N").
- **(7a)** Delete button → **full width** (= Continue width) in the normal state.
- **(7b)** On confirm, Delete → "Yes, delete" **left half** + "Cancel" **right half** with a
  **gap** between them (resize on `EnterDeleteConfirm`, restore full-width on
  `CancelDeleteConfirm`).
- **(7c)** "Yes, delete" → `ButtonKind.Danger` (Critical fill + white text) while confirming.

### 3.6 `Assets/Scripts/Build/BuildToolbarController.cs`
- **(8)** Hazard band → the AA `MakeHazardStripe` sprite (still tiled).
- **(9)** Add a margin between the toolbar slots and the hazard band (raise `bottomMargin`,
  and/or drop the hazard band height/position) so the 72px slots clear the band.
- **(11)** "Rotate: R/T" hint → below the 64px top bar: keep `hintAnchoredPosition.x`,
  lower `.y` (≈ `-20` → `-80`) so it clears the bar + the in-bar dropdown.

### 3.7 `Assets/Scripts/Core/UIManager.cs`
- **(10a)** FLY! button → shorter + margined in the top bar: size `220×64` → ≈ `200×44`,
  `anchoredPosition` adjusted so a 44px button sits centred in the 64px bar with ~10px
  top/bottom margins (top-right anchor kept).
- **(10b)** Base → `Orange600` (#c8521e) + white text — via `ButtonKind` (a new `Cta`/reuse
  `Danger`-like path) **or** an explicit post-build recolor. Prefer an explicit recolor here
  (`Orange600` isn't a semantic `CscTheme` role) to keep the enum to Ghost/Primary/Danger.

## 4. Files

**New:**
- `Assets/Scripts/Core/LetterSpacing.cs` (`BaseMeshEffect`)

**Changed:**
- `Assets/Scripts/Core/CscPalette.cs` (base colour)
- `Assets/Scripts/Core/UIStyle.cs` (uppercase, `ButtonKind`, press-stamp helper wiring, letter-spacing helper, rounded + hazard sprites)
- `Assets/Scripts/Core/UIClickBounce.cs` (rework → press-stamp)
- `Assets/Scripts/MainMenu/MainMenuController.cs` (Hangar primary, shadow 8f)
- `Assets/Scripts/HangarSelect/HangarSelectController.cs` (primary buttons, delete hover-red, rounded cards, delete width, confirm gap + danger, title case + spacing)
- `Assets/Scripts/Build/BuildToolbarController.cs` (AA hazard band, toolbar margin, rotate-hint reposition)
- `Assets/Scripts/Core/UIManager.cs` (FLY! size + orange-red)

## 5. Acceptance criteria

- Buttons + titles/headers render **UPPERCASE**; body/stats/help unchanged.
- Menu Hangar + slot Continue/Start-new are **ochre** with dark text and the shared hover/press feedback.
- Slot Delete button turns **red** (outline + text) on hover; "Yes, delete" is **red + white**; the confirm pair has a visible gap; Delete is full-width until confirm.
- Menu buttons **sink into their shadow on press** (pointer-down), restore on release; shadows read like the wordmark.
- Menu/slot button labels + "SLOT n" titles have visibly **relaxed letter-spacing**.
- Dark buttons/dropdown use the **#2a232c** base.
- Slot cards have **rounded corners** + shadow.
- BuildScene: hazard-band diagonals are **smooth** (AA); the toolbar has a **margin** above the band; the Rotate hint sits **below** the top bar; **FLY!** fits inside the bar (with margins) and is **orange-red**.
- Compiles clean; no gameplay/save/scene-flow change; maintainer Play-verifies each surface.

## 6. Risks / open implementation questions (resolve in the plan)

- **Letter-spacing tuning:** `fontSize * 0.05` is an estimate of "105%"; the maintainer
  tunes the multiplier at the gate. Alignment redistribution must handle the button labels'
  `MiddleCenter` alignment.
- **Press-stamp vs. Button ColorBlock:** the stamp translate is independent of the Button's
  own ColorTint press feedback — confirm they compose (translate + tint) without fighting;
  restore must be reliable on pointer-exit-while-held.
- **Delete hover-red:** the Delete button has both the ink `Outline` and (from B3b) a
  `Shadow`; the hover handler must recolor the **Outline** (not the Shadow) + the label,
  and restore exactly.
- **Rounded-card sprite:** sliced vs. simple — use a fixed radius sized for the 300px card;
  if `Image.Type.Sliced` is needed for crisp corners at size, set sprite borders.
- **Confirm-button resize:** Delete full→half on `EnterDeleteConfirm` and half→full on
  cancel/commit must not desync with the show/hide of Cancel.
- **FLY! recolor:** `Orange600` is applied as an explicit post-build color (not a semantic
  role); the white text + ink outline stay. Verify it still reads only in BuildScene.
- **All-caps title sites:** grep `BuildLabel(..., font: CscTheme.DisplayOr` to catch every
  title/header (MainMenu wordmark already uppercase; PauseMenu "Paused" → "PAUSED";
  HangarSelect "Choose a Slot" + "Slot n"; SettingsMenu title if present).
