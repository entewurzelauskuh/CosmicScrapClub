# Milestone B2 — UI rebrand: typography + toon outlines (design)

- **Date:** 2026-07-06
- **Branch:** `explore/ui-rebrand`
- **Milestone:** B (UI rebrand) — sub-phase **B2** of B1 → B2 → B3 (→ B4 polish)
- **Status:** approved at the brainstorm gate (2026-07-06). Decisions: typography = **all four roles**; ochre primary buttons + Fly-HUD color migration **deferred to B4**.

## Context

B1 landed the brand assets (palette, theme, fonts, sprites) with zero visual
change; `CscPalette` is the color source of truth and `CscTheme` exposes the
font slots (`Display`/`Body`/`Cond`/`Stencil`, populated by `CscThemeBootstrap`).
See [[project_milestone_b_ui_rebrand]].

`UIStyle.cs` builds all UI in code and is consumed by 20 scripts across the four
surfaces. Its `BuiltinFont` chokepoint currently fonts everything with
`LegacyRuntime.ttf`. Call-site survey (grep): `BuildLabel` ×40 (titles 48–96px,
body/stat 16–32px, warnings), `BuildLabeledButton` ×16, `BuildToggle` ×1,
`BuildDropdown` ×1.

## Goal

Make the Cosmic Scrap Club typography and the signature ink outline **visible**
across all four surfaces, with **no layout (RectTransform) changes**. This is the
"the brand turns on" step; matching the mockup *layouts* is B3.

**Pass criterion:** Anton titles, Saira-Condensed buttons/labels, Saira
body/stat text, Saira-Stencil warning flashes, and a 2px ink outline on every
button — across MainMenu / HangarSelect / Build / Fly HUD — with every element
still in its current position.

## Non-goals

- **Deferred to B3:** sprite usage in toolbars/HUD; matching mockup layouts,
  sizes, spacing, panel/card outlines & toon shadows; retiring `unity_handoff/`.
- **Deferred to B4 (brand polish):** the ochre **primary button** variant + its
  3 CTA call sites; the Fly-HUD color-literal → `CscPalette` migration. (See the
  dedicated section below so it is not lost.)
- No RectTransform / anchor / size edits. No gameplay/physics/save/scene-flow
  changes. Legacy `UnityEngine.UI` (no TMP).

## Approach — font routing mechanism

Add an optional `Font font = null` parameter to the text builders; resolve to a
**role default** when omitted, and pass an explicit role only where the default
is wrong. Rejected alternatives: a fontSize→role heuristic (fragile — 32px is
both a small title and a big stat) and separate `BuildTitle`/`BuildBody` methods
(more API surface + more call-site churn). The optional-param approach is
backward-compatible (existing positional calls keep working) and confines edits
to the ~9 sites that need a non-default role.

Role defaults chosen from the call-site survey (the majority case wins the
default):
- **Buttons** are always the workhorse face → `BuildLabeledButton` default `CondOr`.
- **`BuildLabel`** is dominated by stat readouts + body/help text → default `BodyOr` (Saira).
- **Dropdown / toggle labels** are UI chrome → `CondOr`.

## Scope — what changes

### 1. `UIStyle.cs` (the builder)

- **Remove** the private `BuiltinFont` property + `_builtinFont` field — now
  superseded by `CscTheme`'s `BuiltinFallback` (reached via the `*Or`
  accessors). All four internal uses are replaced below.
- **`BuildLabeledButton(Transform parent, string labelText, Vector2 size, int fontSize = 28, Font font = null)`**
  - `text.font = font ?? CscTheme.CondOr;`
  - after the button Image/Button are set up: `CscTheme.AddToonOutline(buttonGO);`
    (2px Ink edge on the button fill — a render effect, no layout shift).
- **`BuildLabel(Transform parent, string text, int fontSize, FontStyle style = FontStyle.Normal, Font font = null)`**
  - `t.font = font ?? CscTheme.BodyOr;`
- **`BuildToggle`**: label `font = CscTheme.CondOr`; recolor the existing white
  box `Outline.effectColor = CscTheme.OutlineColor` (Ink).
- **`MakeText`** (dropdown caption/items): `t.font = CscTheme.CondOr;`

### 2. Call-site font overrides (~9 edits)

Pass `font:` only where the default is wrong. Everything else inherits the role
default with no edit.

**Titles → `CscTheme.DisplayOr` (Anton):**
- `MainMenu/MainMenuController.cs:41` — `"Cube Fly"`
- `Core/PauseMenu.cs:264` — `"Paused"`
- `Core/GameOverMenu.cs:151` — `"Construct Destroyed"`
- `Core/SettingsMenu.cs:196` — `"Settings"`
- `Core/SettingsMenu.cs:329` — `"VFX Toggles"` (section header)
- `HangarSelect/HangarSelectController.cs:107` — `"Choose a Slot"`
- `HangarSelect/HangarSelectController.cs:151` — `"Slot {n}"` (card title)

**Warning flashes → `CscTheme.StencilOr`:**
- `Fly/FlyHeatBar.cs:182` — `"Overheated!"`
- `Fly/FlyBoostBar.cs:208` — `"Overboosted!"`

Misc UI labels left at the `BodyOr` default in B2 (e.g. `"Class"`,
`FlyShieldIndicator` `"Eject: P"`) — chasing every label to a perfectly-tuned
role is a B3 refinement; B2 keeps the override list to the unambiguous
title/warning cases.

## Deferred to B4 — brand polish (recorded so it is not lost)

To be done before Milestone B is declared finished (tracked task exists):

1. **Ochre primary button.** Add `bool primary = false` to `BuildLabeledButton`:
   `primary` → `Image.color = CscTheme.PrimaryFill` (Ochre300) + label
   `color = CscTheme.TextOnLight` (Scorch); else the current ghost fill. Apply
   `primary: true` at the three main CTAs:
   - `MainMenu/MainMenuController.cs:56` — **Hangar**
   - `HangarSelect/HangarSelectController.cs:170` — **Build/Continue** (card primary)
   - `Core/UIManager.cs:103` — the persistent scene-switch **FLY!/HANGAR** button
2. **Fly-HUD color migration.** Point the serialized color literals in
   `FlyBoostBar`/`FlyHeatBar`/`FlyShieldIndicator`/`FlyCrosshair` at the matching
   `CscPalette` members (`Boost`, `HeatCool`/`HeatHot` via `CscTheme.HeatColor`,
   `Shield`, `Critical`, `WarnFlash`, `Eject`, `PowerPositive/Negative`,
   `ShieldDown`). Zero-visual (values already identical) — centralises the source
   of truth.

## Zero-layout caveat (honest)

"No layout change" = **no RectTransform edits**. But different fonts have
different metrics, so text will *look* different and may slightly over/underfill
its existing box (the builders use `HorizontalWrapMode.Overflow`, so it renders
at natural width rather than wrapping). Tuning sizes/anchors to the mockups is
B3's job. Also: title call sites pass `FontStyle.Bold`, which Unity will
synthesize on the single-weight Anton — acceptable for B2; dropping the redundant
Bold is a B3 nicety.

## Verification

- UnityMCP `read_console` (clean compile) after each edit; order: `UIStyle`
  builder change first (compiles against `CscTheme`), then the call-site
  overrides.
- **Maintainer Play-mode check (the gate):** all four surfaces show Anton
  titles, condensed buttons/labels, Saira stats, stencil `Overheated!`/
  `Overboosted!`, and ink outlines on buttons — with layouts recognizably intact
  (some text may over/underfill; that's expected). Headless MCP Play is frozen at
  frame 1, so the visual confirmation is the maintainer's.

## Commit plan (surgical)

1. `refactor(ui): route UIStyle fonts through CscTheme roles (B2)` — builder
   font defaults + remove `BuiltinFont`.
2. `feat(ui): toon outlines on buttons + toggle (B2)` — `AddToonOutline` in
   `BuildLabeledButton`, Toggle outline recolor.
3. `feat(ui): Anton titles + stencil warnings (B2)` — the ~9 call-site overrides.

Hold the push until the maintainer's Play-check; bundle onto the branch. Merge to
`main` remains a separate hard gate.

## Risks / notes

- **Text overflow** on some buttons/labels once re-fonted (condensed is narrower,
  Anton is wider). Expected; logged for B3. If anything is egregiously clipped,
  note it at the gate.
- **`CscTheme.CondOr` fallback:** if `SairaCondensed-Bold` failed to load it
  chains to `BodyOr` (Saira) — but B1 verified 4/4 fonts resolve, so this is
  belt-and-suspenders.
- All edits are to `Assets/Scripts/**` only (no assets/metas), so no import step
  beyond compilation.
