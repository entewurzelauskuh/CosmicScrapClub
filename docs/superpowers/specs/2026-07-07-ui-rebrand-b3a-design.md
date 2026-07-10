# Milestone B3a — UI rebrand: MainMenu full-match restyle (design)

- **Date:** 2026-07-07
- **Branch:** `explore/ui-rebrand`
- **Milestone:** B (UI rebrand) — sub-phase **B3a** (of B3a MainMenu → B3b Slot Picker → B3c Build → B3d Fly HUD)
- **Status:** approved at the brainstorm gate (2026-07-07). Decisions: decomposition = **per surface (4)**; fidelity = **full match** to the mockups.

## Context

B1 (assets) + B2 (typography + outlines) are done and pushed on `explore/ui-rebrand`
(PR #55, draft). B3 is the **layout phase** — restyle each surface to its mockup.
Per the brainstorm, B3 is split per surface; **B3a is MainMenu**, and it also
introduces the shared brand primitives B3b–d reuse. Fidelity bar is **full match**
to `unity_handoff/reference/screens/hangar-main-menu.png` (1280×720 reference frame).

Target mockup: warm ochre→rust radial-gradient background with faint diagonal
texture; a hazard-yellow **wordmark** plate (rotated ≈ −2°, ink border + hard toon
shadow) reading `COSMIC` / `SCRAP` / `★ CLUB ★` in the three brand fonts; three
stacked buttons (Hangar/Settings/Exit) with ink outlines + hard toon shadows.

Current `MainMenuController.BuildUI()` builds a plain "Cube Fly" `BuildLabel` title
+ three `BuildLabeledButton`s (Hangar/Settings/Exit) — see `Assets/Scripts/MainMenu/MainMenuController.cs`.

## Goal

Restyle MainMenu to match the mockup, and land two reusable brand primitives
(`AddToonShadow`, `BuildBrandBackground`) plus a generated gradient asset that
B3b–d will consume. **Presentation only** — no gameplay/scene-flow/save changes.

**Pass criterion:** MainMenu reads like `hangar-main-menu.png` — warm gradient bg,
the composite wordmark, outlined + toon-shadowed buttons — verified by the maintainer's
Play-check.

## Non-goals

- The ochre **primary** fill on the HANGAR button → **B4** (stays ghost/dark here).
- Slot Picker / Build / Fly HUD → **B3b/c/d**.
- No gameplay, scene flow, save, or input changes. Legacy `UnityEngine.UI` (no TMP).

## Shared brand primitives (new — reused by B3b–d)

### `CscTheme.AddToonShadow(GameObject go, float offset = 6f)`

Adds the signature hard toon drop-shadow (uGUI `Shadow`, Ink, offset down-right, no blur):

```csharp
public static Shadow AddToonShadow(GameObject go, float offset = 6f)
{
    Shadow s = go.GetComponent<Shadow>();
    if (s == null) s = go.AddComponent<Shadow>();
    s.effectColor = OutlineColor;                    // Ink #0B0906
    s.effectDistance = new Vector2(offset, -offset); // hard, single offset (no blur)
    s.useGraphicAlpha = true;
    return s;
}
```

Uses an explicit null check (not `??` on a Unity `Object`). While in `CscTheme`,
**also harden `AddToonOutline`** the same way (the B2 review's noted `??`-on-Object
nit) — one-line change, same file.

> uGUI `Outline` + `Shadow` compose: applying both to one Graphic yields the ink
> border *and* the offset shadow, matching the mockup's buttons/plate.

### `UIStyle.BuildBrandBackground(RectTransform canvasRoot)`

Full-screen warm background, inserted as the **first / back-most** children so all
UI sits on top:

```csharp
public static void BuildBrandBackground(RectTransform canvasRoot)
{
    // 1. Gradient plate — full-screen Image, brand gradient sprite, stretched.
    // 2. Hazard overlay — full-screen Image, tile_hazard_stripe, Image Type Tiled,
    //    low alpha (~0.06), for the faint diagonal texture.
    // Both raycastTarget = false; both SetAsFirstSibling so menu content draws over.
}
```

### New asset — `Assets/Art/UI/Backgrounds/menu_gradient.png`

A radial gradient, **ochre `#D9A441` center → deep-rust/brown `#2E2219` edges**,
~512². Generated via UnityMCP `manage_texture` `apply_gradient` (radial); fallback:
`execute_code` building a `Texture2D` + `EncodeToPNG` written under `Assets/Art/UI/Backgrounds/`.
Import as Sprite (2D/UI), Bilinear (smooth gradient), Clamp. Committed (LFS: `*.png`).

## The wordmark (inline in `MainMenuController` — MainMenu-only, YAGNI on a helper)

A `BuildWordmark(RectTransform parent)` private method building a container rotated
`localEulerAngles.z = 2f` (≈ −2° visual) holding, back-to-front:

1. **Plate** — `Image` filled `CscPalette.HazardYellow`; `CscTheme.AddToonOutline(plate, 4f)` (thick ink border); `CscTheme.AddToonShadow(plate, 8f)` (big offset).
2. **`COSMIC`** — `UIStyle.BuildLabel(plate, "COSMIC", …, font: CscTheme.CondOr)`, color `CscPalette.Scorch`, near the top.
3. **`SCRAP`** — `UIStyle.BuildLabel(plate, "SCRAP", ~120, font: CscTheme.DisplayOr)`, color `CscPalette.Scorch`, center (the hero).
4. **`★ CLUB ★`** — `UIStyle.BuildLabel(plate, "★ CLUB ★", …, font: CscTheme.StencilOr)`, color `CscPalette.Orange600`, bottom.

All three lines UPPERCASE. uGUI `Text` has no native letter-spacing; tracking is
approximated by the fonts' own character (Anton/Stencil already read on-brand) —
if the mockup's wide `COSMIC` tracking is essential we insert thin spaces, else accept.

## Buttons

The three menu buttons keep their B2 ink outline and gain `CscTheme.AddToonShadow(go, 6f)`.
Fills stay the current ghost (`ButtonFill`) — the ochre HANGAR primary is **B4**.
`fontSize`/`sizeDelta`/`anchoredPosition` may be tuned to match the mockup's
proportions (B3 is the layout phase — RectTransform edits are now in scope).

## `MainMenuController` changes

`BuildUI()` becomes: `EnsureEventSystem()` → `BuildScreenSpaceCanvas` →
`BuildBrandBackground(root)` → `BuildWordmark(root)` (replacing the "Cube Fly"
`BuildLabel`) → the three buttons via `CreateMenuButton`, each `+ AddToonShadow`.
`OnHangar`/`OnSettings`/`OnExit` are untouched (behavior unchanged).

## Verification

- UnityMCP `read_console` (clean compile) after the `CscTheme`/`UIStyle` primitive
  edits and after the controller rewrite.
- Gradient asset: confirm it imports as a Sprite with no importer errors.
- **Maintainer Play-mode gate:** MainMenu matches `hangar-main-menu.png` — warm
  gradient bg, the composite wordmark, outlined + toon-shadowed buttons; the three
  buttons still navigate (Hangar→HangarSelect, Settings→SettingsMenu, Exit→quit).
  Headless MCP Play is frozen at frame 1, so visual confirmation is the maintainer's.

## Commit plan (surgical)

1. `feat(ui): AddToonShadow primitive + harden AddToonOutline (B3a)` — `CscTheme`.
2. `feat(ui): brand gradient asset + BuildBrandBackground (B3a)` — the generated sprite + `UIStyle` helper.
3. `feat(ui): Cosmic Scrap Club wordmark + toon-shadowed menu (B3a)` — `MainMenuController` rewrite.

Hold the push until the maintainer's Play-check; then push onto `explore/ui-rebrand`
(updates PR #55) and dispatch the B3a internal review. Merge to `main` = hard gate.

## Risks / notes

- **Gradient generation** may need a tuning pass to match the mockup's warmth/vignette —
  iterate against the screenshot at the gate.
- **Rotation + layout:** the wordmark container is rotated; size its `RectTransform`
  generously so the rotated plate + shadow aren't clipped by the parent.
- **Wordmark tracking:** uGUI can't letter-space; if the mockup's wide tracking on
  `COSMIC`/`CLUB` reads as essential at the gate, revisit (thin-space hack) — otherwise accept.
- **`FontStyle.Bold` on Anton** (synthesized) — the B2-noted nicety; the wordmark's
  `SCRAP` should pass `FontStyle.Normal` to Anton (it's already a display weight).
- New asset folder `Assets/Art/UI/Backgrounds/` needs its `.meta` — let Unity generate it.
