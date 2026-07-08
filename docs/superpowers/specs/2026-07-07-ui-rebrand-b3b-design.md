# Milestone B3b — UI rebrand: Slot Picker full-match restyle (design)

- **Date:** 2026-07-07
- **Branch:** `explore/ui-rebrand`
- **Milestone:** B (UI rebrand) — sub-phase **B3b** (of B3a MainMenu → **B3b Slot Picker** → B3c Build → B3d Fly HUD)
- **Status:** approved at the brainstorm gate (2026-07-07). Fidelity = **full match**; decomposition = **per surface**.

## Context

B3a shipped the shared brand primitives — `UIStyle.BuildBrandBackground` (procedural
warm gradient + faint diagonal overlay, session-cached) and `CscTheme.AddToonShadow`
(hard Ink drop-shadow) — plus the anti-aliased plate helper `UIStyle.MakePlateSprite`.
B3b **reuses** those; it introduces no new primitives. Target:
`unity_handoff/reference/screens/hangar-slot-picker.png` — the same warm world as
MainMenu, with dark slot cards (ink border + toon shadow), ochre `SLOT n` titles,
sand body stats, ochre primary buttons, ghost Delete/Cancel.

All work is in `Assets/Scripts/HangarSelect/HangarSelectController.cs`
(`BuildUI` / `BuildCard` / `RefreshAllCards`). "Choose a Slot" is already Anton (B2).

## Goal

Restyle the Slot Picker to match the mockup, reusing B3a's primitives.
**Presentation only** — no save/scene-flow/logic changes; the two-click delete
confirm, slot activation, and cancel all keep working.

**Pass criterion:** Slot Picker reads like `hangar-slot-picker.png` — warm gradient
bg, dark ink-bordered + toon-shadowed cards, ochre slot titles, sand stats — verified
by the maintainer's Play-check.

## Non-goals

- The **ochre fill** on the primary CONTINUE / START-NEW button → **B4** (stays ghost).
- B3c / B3d surfaces. No new primitives. Legacy `UnityEngine.UI`.

## Scope — changes in `HangarSelectController`

### `BuildUI`
- Add `UIStyle.BuildBrandBackground(root)` right after the canvas is built, **before**
  the title/cards (so it sits behind them — matching how MainMenu calls it first).
- Title "Choose a Slot": set `.color = CscPalette.Sand100` (light cream; it is already Anton).
- Bottom **Cancel** button: `CscTheme.AddToonShadow(cancelButton.gameObject, 6f)`.

### `BuildCard`
- Card background `Image`: fill → `CscTheme.CardFill` (currently `new Color(0.10,0.10,0.14,0.92)`);
  add `CscTheme.AddToonOutline(rootGO, 3f)` + `CscTheme.AddToonShadow(rootGO, 6f)`
  (ink border + hard shadow — cards currently have neither).
- Slot title (`card.TitleLabel`): `.color = CscPalette.Ochre300`.
- Body (`card.BodyLabel`): `.color = CscPalette.Sand100`.
- The three card buttons (primary / delete / delete-cancel): `AddToonShadow(…, 6f)` each.

### `RefreshAllCards`
- Where the filled-slot body string is composed, wrap the trailing **"Last edited …"**
  line in a `<color=#7E776C>…</color>` rich-text tag (Steel300 grey) so it reads muted
  vs. the sand stats. uGUI `Text.supportRichText` is on by default. *(First-take fallback:
  if the string assembly makes this awkward, accept a single `Sand100` body for now and
  revisit — it's a minor deviation.)*
- The `<empty>` hint keeps its existing muted styling; recolor to `Steel300` if it reads too bright.

### Layout
- Nudge card size / spacing / button offsets only if they visibly diverge from the
  mockup (B3 is the layout phase). The existing card layout is already close.

## Verification

- UnityMCP `read_console` (clean compile) after the edits.
- **Maintainer Play-mode gate:** Slot Picker matches `hangar-slot-picker.png`; the
  Continue/Start action loads the slot, Delete shows the two-click confirm, Cancel
  returns to menu — all still work. Visual confirmation is the maintainer's.

## Commit plan (surgical)

1. `feat(ui): brand background + card restyle on the slot picker (B3b)` — `BuildUI` + `BuildCard`.
2. `feat(ui): muted last-edited line + button shadows on slot picker (B3b)` — `RefreshAllCards` + button shadows (fold into #1 if small).

Hold the push until the maintainer's Play-check; then push onto `explore/ui-rebrand`
(updates PR #55) and dispatch the B3b internal review. Merge to `main` = hard gate.

## Risks / notes

- **Card outline + rotation:** cards are axis-aligned (no rotation), so the B3a plate-AA
  concern does not apply — the `AddToonOutline` ink border renders crisp here.
- **Rich-text color** on the body assumes `supportRichText` (default on); if the body
  label had it disabled, re-enable it or accept single-color (see fallback above).
- Reuses the **session-cached** background textures from B3a — no extra allocation.
