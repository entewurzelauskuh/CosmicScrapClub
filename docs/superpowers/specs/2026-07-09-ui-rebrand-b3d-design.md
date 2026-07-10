# B3d — Fly HUD Restyle (Cosmic Scrap Club) — Design Spec

- **Milestone:** B (UI rebrand) · **Sub-phase:** B3d (fourth & LAST B3 surface)
- **Branch:** `explore/ui-rebrand` (@ `2231fcc` at spec time) · **PR:** #55 (draft)
- **Date:** 2026-07-09
- **Status:** design approved (brainstorm gate passed); spec under review
- **References:** `unity_handoff/reference/screens/flight-hud-default.png` +
  `flight-hud-laser-heat.png`; `unity_handoff/reference/BRAND.md` "Flight HUD layout
  map" table (lines ~84–101) + "The cel-shaded look" (66–82).

## 1. Goal & context

Restyle the FlyScene HUD to the brand's **cool "esports overlay"** — the deliberate
opposite of the warm world/menu surfaces: translucent near-black panels, energy-blue /
heat-orange accents, crisp ink borders. Also completes **B4's Fly-HUD colour-literal →
`CscPalette` migration**. **Presentation-only**; legacy `UnityEngine.UI` (no TMP);
colours from `CscPalette`/`CscTheme`. After B3d: **retire `unity_handoff/`** →
Milestone B complete.

**Host:** one FlyScene-local `FlyHud` canvas (`FlyHud.cs`, ScreenSpaceOverlay,
sortingOrder 100). Eight HUD `MonoBehaviour`s live on the scene object `FlyHUD`; all UI
is code-built in each component's `BuildUI`. **Colours are `[SerializeField]` → also
serialized in `FlyScene.unity`**, so recolouring in C# alone won't take effect unless we
drop `[SerializeField]` (decision below).

## 2. Decisions locked in brainstorm

| Topic | Decision |
|---|---|
| Missing multiplayer elements (objective bar, kill feed, damage vignette) | **Skip** — no gameplay drives them; building them = non-functional chrome. |
| Weapon toolbar | **Full BRAND match** — 64×64, glyph, key badge, swatch, ochre-outline selection, reload-along-bottom, dead = 40% α. |
| Colour migration mechanism | **Drop `[SerializeField]`, initialise the colour fields from `CscPalette`** — centralises to one source, ignores the scene-serialized dupes, no scene editing. |
| Emissive glow | **Out (stretch)** — overlay UI has no bloom; a faked halo isn't worth it for a lean B3d. Core cool-overlay = dark panel + ink border + energy fill. |
| Crosshair hit-confirm scale / shield-absorb tint | **Skip** — need gameplay hit-event hooks, beyond presentation-only. |

## 3. Colour migration (foundational) — `CscPalette` mapping

For every field below: **remove `[SerializeField]`** and set the initialiser to the
token. RGB-exact matches are visual no-ops; near-misses snap to the true token.
**Alpha caveat:** where a field carries a non-1 alpha (`shieldFillColor` .95,
`shieldDownColor` .6, `shieldFrameColor` .85, `reloadBarBackground` .6), preserve it —
`new Color(Token.r, Token.g, Token.b, α)` — unless the `CscPalette` token already carries
that alpha (verify each token's definition when implementing).

| Script | Field (`file:line`) | → Token |
|---|---|---|
| `FlyBoostBar.cs` | `fillColor :40` / `criticalColor :57` / `flashColor :47` / `frameColor :41` | `Boost` / `Critical` / `WarnFlash` / `HudPanel` |
| `FlyHeatBar.cs` | `coolColor :29` / `hotColor :30` / `flashColor :36` / `frameColor :31` | `HeatCool` / `HeatHot` / `WarnFlash` (fixes near-miss) / `HudPanel` |
| `FlyShieldIndicator.cs` | `shieldFillColor :26` / `shieldDownColor :28` / `powerPositiveColor :29` / `powerNegativeColor :30` / `ejectHintColor :35` / `shieldFrameColor :27` | `Shield` / `ShieldDown` / `PowerPositive` / `PowerNegative` / `Eject` / `HudPanel` |
| `FlyCrosshair.cs` | `crosshairColor :32` | `Label` (white) |
| `FlyWeaponToolbarController.cs` | `deathMarkColor :41` / `deadColor :39` / `reloadBarBackground :32` / `SelectedTypeColor :62` (private `static` const) | `Critical` / `Steel500` / `HudPanel` (α .6) / `Ochre300` |

`FlyHpIndicator` / `FlySpeedIndicator` have no colour literals (inherit white `BuildLabel`).

## 4. Restyle detail (by element)

### 4.1 Meters — cool-overlay chip look (`FlyBoostBar`, `FlyHeatBar`, `FlyShieldIndicator`)
- Bar **frame/track** `Image` → `HudPanel` (translucent near-black) + a **2px ink border**
  via `CscTheme.AddToonOutline` on the frame GameObject (the esports-chip signature).
- **Fill** keeps its energy token (`Boost` / `HeatCool→HeatHot` lerp / `Shield`).
- Heat meter's laser-only visibility (`FlyHeatBar.cs:81-83`) unchanged.

### 4.2 Weapon toolbar — full match (`FlyWeaponToolbarController`)
Restructure each slot from the current 160×60 text button to a **64×64** brand slot,
reusing B3c's shared primitives:
- Centred **`CscSprites` glyph** for the weapon (`ForShape(shape.displayName, 0)` →
  laser / cylinder-rocket / pyramid-mg) via `UIStyle.DecorateToolbarSlot` (which also
  lays the **number key badge** top-left). **Caption suppressed** (pass empty) so the
  glyph identifies the weapon and the bottom edge is free for the reload bar.
- **Swatch** top-right (12px) = `shape.coupledMaterial.SwatchColor` (keep, reposition).
- **Selection** = 3px **ochre outline** via `UIStyle.AddSelectionOutline` (toggle
  in `RefreshWeaponStates`), replacing the blue `SelectedTypeColor` fill.
- **Reload bar** moved to run **along the bottom** of the slot (from above), bg `HudPanel`.
- **Dead** state = **40% alpha** on the slot (from the grey `deadColor` fill) +
  non-interactable; the pulsing red `✕` partial-loss mark (`Critical`) stays.
- Slot fill stays `UIStyle.BackgroundIdle`; ink border from `BuildLabeledButton` (B2).

### 4.3 Readouts (`FlyHpIndicator`, `FlySpeedIndicator`, power in `FlyShieldIndicator`)
- Recolour to `CscPalette` tokens.
- Emphasise the **value** with a bigger `<size>` span (as B3c's MASS/HP), Condensed font,
  `supportRichText = true`.
- **HP turns `CscPalette.Critical`** (red) when `hp < 25%` of max; normal otherwise.
- Speed / Power keep their positions; Power already flips `PowerPositive/Negative` by sign.

### 4.4 Crosshair (`FlyCrosshair`)
- Recolour to `CscPalette.Label`. No dynamic scale/tint (skipped, §2).

## 5. Files

**Changed (all `Assets/Scripts/Fly/`):** `FlyBoostBar.cs`, `FlyHeatBar.cs`,
`FlyShieldIndicator.cs`, `FlyCrosshair.cs`, `FlyHpIndicator.cs`, `FlySpeedIndicator.cs`,
`FlyWeaponToolbarController.cs`.
**Reused (no change):** `UIStyle` (`DecorateToolbarSlot`, `AddSelectionOutline`),
`CscSprites`, `CscPalette`, `CscTheme`.
**Post-B3d:** delete `unity_handoff/` (fully consumed) → Milestone B complete.

## 6. Acceptance criteria

- Every Fly-HUD brand colour comes from `CscPalette` (no `new Color` literals for brand
  colours; `[SerializeField]` dropped on those fields). Visual result unchanged for the
  12 exact-match tokens; heat flash + weapon death-mark shift to the true token.
- Boost / heat / shield meters read as dark translucent chips with a 2px ink border +
  energy fill.
- Weapon slots are 64×64 with a weapon glyph, number key badge, swatch, **ochre-outline**
  selection, bottom reload bar, and a 40%-alpha dead state.
- HP value goes red below 25%; readout values are size-emphasised.
- No objective bar / kill feed / damage vignette added; no gameplay/save/scene-flow change.
- Compiles clean; maintainer Play-verifies the HUD (default + laser-heat states).

## 7. Risks / open implementation questions (resolve in the plan)

- **Alpha on migrated tokens:** confirm each `CscPalette` token's alpha (some HUD fields
  need α<1); preserve the field alpha where the token is opaque.
- **Dropping `[SerializeField]`:** the scene's serialized colour values become orphaned
  (harmless — Unity ignores data for non-serialized fields); confirm no other code path
  writes these fields from the Inspector at runtime.
- **`DecorateToolbarSlot` fit:** it was built for the build toolbar (caption at bottom); on
  a 64×64 weapon slot with a swatch + bottom reload bar, verify the glyph/badge don't
  collide with the swatch/reload — adjust the reload bar to sit *below* the caption strip
  or suppress the caption.
- **Weapon glyph vs. name:** the mockup shows an abbreviation (MG/RKT/LAS); we use the
  `CscSprites` glyph instead (cleaner at 64×64, consistent with the build toolbar). If the
  maintainer prefers the abbreviation text, that's a caption swap.
- **Reload-bar reposition** must not break the per-frame `ReadyFraction` width update
  (`FlyWeaponToolbarController.cs:100-104`).
