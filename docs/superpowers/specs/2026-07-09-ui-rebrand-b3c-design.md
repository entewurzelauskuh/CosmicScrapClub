# B3c — BuildScene Restyle (Cosmic Scrap Club) — Design Spec

- **Milestone:** B (UI rebrand) · **Sub-phase:** B3c (third of four B3 surfaces)
- **Branch:** `explore/ui-rebrand` (@ `ec3456d` at spec time) · **PR:** #55 (draft)
- **Date:** 2026-07-09
- **Status:** design approved (brainstorm gate passed); spec under review
- **References:** `unity_handoff/reference/screens/hangar-build.png` (target mockup),
  `unity_handoff/reference/BRAND.md` §"The cel-shaded look" (66–82), §"Build screen"
  (113–117), §"Iconography" (119–127).

## 1. Goal & context

Rebrand the **BuildScene 2D UI chrome** to the Cosmic Scrap Club look, matching the
mockup. This is the third of four B3 surfaces (after B3a MainMenu, B3b Slot Picker;
B3d Fly HUD follows). **Presentation-only** — no gameplay, physics, save, or
scene-flow edits; legacy `UnityEngine.UI` (no TMP); every colour from
`CscPalette`/`CscTheme`; `new Color(r,g,b,a)` sRGB value/255.

The Build UI is code-built by three real scene `MonoBehaviour`s on the `BuildHUD`
GameObject (`BuildScene.unity`): `BuildToolbarController`, `BuildShipClassController`,
`BuildHud` (a bare `Canvas` host). `CategoryFlyout` is a plain C# object `new`-ed by
`BuildToolbarController` (`BuildToolbarController.cs:383`) with builder delegates
injected (`CategoryFlyout.cs:44-45,87-88`).

## 2. Scope

**In scope (BuildScene-owned chrome):**
1. Sprite pipeline (foundational — makes the 12 glyph PNGs loadable).
2. Top bar: cool panel + `CLASS` label + branded class **dropdown**.
3. Shape toolbar: the real **5 slots** (`Cube · Slope · Weapons▸ · Utilities▸ · Delete`)
   restyled to glyph + number badge + label; category slots show the armed
   weapon/utility glyph; flyout entries get per-shape glyphs; hazard-stripe floor trim.
4. Material flyout: A–D entries restyled.
5. MASS / HP / Power readout (bottom-left).

**Out of scope (explicit):**
- The **3D construct / build grid** rendering. The mockup's flat grid is artistic
  licence; the game renders 3D cube meshes (`CubePreview`/`BuildCamera`). Untouched.
- The **FLY! / HANGAR** button — it is the shared persistent `UIManager` corner
  button (`UIManager.cs:95,104`), also shown in FlyScene. Deferred to **B4** (which
  owns the CTA recolour and can reconcile Build's orange FLY! vs. the HUD's sand
  HANGAR). It keeps B2's Condensed font + ink outline in the interim.
- The **"×1.0 speed"** readout — does not exist today (class multiplier is data-only,
  `ShipClass.cs:50-55`); **skipped** (would be net-new UI).
- The class **segmented-tabs** look — kept as a restyled **dropdown** (below).

## 3. Decisions locked in brainstorm

| # | Decision | Choice |
|---|---|---|
| 1 | Sprite loading | **Resources + cached loader** (move PNGs to `Assets/Resources/`, `Resources.Load<Sprite>`), mirroring the fonts precedent (`CscThemeBootstrap.cs:17-20`). |
| 2 | Class selector | **Restyle the uGUI `Dropdown` in place** (not replace with tabs). |
| 3 | Speed readout | **Skip.** |
| 4 | FLY!/HANGAR | **Defer to B4.** |
| 5 | Cube slot glyph | **Swaps `matA–D` with the armed material.** |
| 6 | Hazard trim | **`tile_hazard_stripe` sprite, tiled** (not procedural). |
| 7 | HP/Power green | **`CscPalette.PowerPositive`** (existing token). |

## 4. Design detail

### 4.1 Sprite pipeline (foundational)

- **Move** all 12 `Assets/Art/UI/Sprites/*.png` → `Assets/Resources/UI/Sprites/` via
  `manage_asset` (so `.meta` guids follow; the sprites are currently unreferenced, so
  this is low-risk). They are already imported as Sprites (`textureType: 8`,
  `spriteMode: 1`, Point filter, alpha-is-transparency). `tile_hazard_stripe` must
  have **wrap = Repeat** for tiling — verify/set its `.meta`.
- **New `Assets/Scripts/Core/CscSprites.cs`** (static, `CubeFly.Core`): a cached
  `Resources.Load<Sprite>("UI/Sprites/<name>")` accessor with a `Dictionary` cache,
  modelled on how `CscThemeBootstrap` loads fonts. Typed helpers:
  - `Sprite Shape(string key)` — generic by sprite base-name.
  - `Sprite CubeMaterial(int index)` — `matA–D` (clamped/fallback to `matA`).
  - `Sprite Hazard()` — `tile_hazard_stripe`.
- **Shape→glyph mapping** is code-side (the SO types carry no sprite field:
  `ShapeDefinition` has only `displayName`/`category`/`coupledMaterial`,
  `MaterialDefinition` only `SwatchColor` `MaterialDefinition.cs:52`). A small table
  in `CscSprites` (or the toolbar) keyed on the shape's stable identity
  (displayName / asset name) → sprite key. **Fallback:** unmapped shape → keep the
  text label, no glyph (never crash on a missing sprite).
- Glyphs are **pre-coloured and pre-ink-outlined** → rendered on a white `Image`
  with **no** `AddToonOutline` (that would double the border).

### 4.2 Top bar

`BuildShipClassController` builds `CLASS` label (`:82`) + dropdown (`:89` via
`UIStyle.BuildDropdown`, template `UIStyle.cs:316-438`).
- Add a **cool near-black bar panel** (`CscTheme.PanelFill`) spanning the top, with a
  **2px ink bottom edge** (thin `Image` strip or an `Outline`-style bottom border).
  (New element — the bar has no backing panel today.)
- `CLASS` label → Condensed font, Steel/Sand.
- **Brand the dropdown** (keep it a dropdown): ink outline on the control, `HudCard`
  fill, Condensed font, **`Ochre300` highlighted/selected option row**, Sand text.
  Prefer branding inside `UIStyle.BuildDropdown` so it is reusable — but first
  confirm no other dropdown (e.g. SettingsMenu) is unintentionally restyled; if it is,
  parameterise or restyle at the call site.

### 4.3 Shape toolbar (largest change)

The live toolbar is **5 slots**, not the mockup's flat 9: 2 armour (Cube, Slope —
individual, `BuildToolbar` :347) + 2 category (Weapons▸, Utilities▸ — each collapses a
flyout, :375) + Delete (:405). We restyle this real structure in place (user decision,
2026-07-09) — flattening to 9 slots would dismantle the category-flyout + digit-shortcut
system (interaction change, out of "presentation-only"). Today each slot is a
`UIStyle.BuildLabeledButton` (text = shape name) plus a solid-colour corner swatch
(`BuildCornerSwatch :717`, recoloured in `RefreshSwatchFor :839`); selection is a
background recolour (`SelectedTypeColor` :124/:806). Restyle each slot to the anatomy:
- Dark slot button (`CscTheme.CardFill`/`HudCard`, ~72×72, 3px radius, **2px ink**).
- **Number badge** top-left (small Condensed, muted `Steel`).
- **Centered glyph sprite** (`Image`, sprite from `CscSprites`, white tint).
- **Label** bottom (Condensed, UPPERCASE, small, Sand) — e.g. `CUBE`, `SLOPE`, `THR`.
- **Selected slot = 3px `Ochre300` outline** (BRAND state).
- **Cube/armour slot** glyph = `CscSprites.CubeMaterial(armedIndex)` — swapped in the
  existing `RefreshSwatchFor` path that already fires on material change.
- **Delete slot** = red ✕ (Unicode `✕` in the label/glyph) + `DELETE`.
- **Category (weapons) slot** (`CategoryFlyout` button `:143`): show the currently-armed
  weapon's glyph (parallel to the cube slot tracking the armed material); each fly-out
  entry shows its own weapon glyph.
- **Hazard-stripe trim**: a full-width **Tiled** `Image` using `CscSprites.Hazard()`
  along the floor behind the toolbar strip (BRAND: "hazard-stripe trim along the floor").

### 4.4 Material flyout

`BuildToolbarController` builds the container (`:496`) + entries (`:515`) with a swatch
`Image` coloured `= mdef.SwatchColor` (`:528`); the weapons fly-out mirrors this in
`CategoryFlyout` (`:164/187/198`). Restyle entries: ink outline, `HudCard` fill,
Condensed text; keep the `SwatchColor` swatch (now on-brand). A–D swatches above the
toolbar per BRAND.

### 4.5 MASS / HP / Power readout

Labels live in `BuildToolbarController` (`_massLabel :414/889`, `_hpLabel :421/891`,
`_powerLabel :428/893`). Restyle:
- `MASS n / cap` → **value in Anton (Display)** big number; `MASS` label + `/cap` in
  Condensed; Sand.
- `HP n` → `CscPalette.PowerPositive`.
- `Power` → `PowerPositive` (positive) / `Critical` (negative).
- Migrate any colour literals → `CscPalette` tokens.

### 4.6 Shared / process

Reuse `CscTheme.AddToonOutline` / `AddToonShadow` + `CscTheme`/`CscPalette` tokens.
Rhythm: UnityMCP compile-verify (`refresh_unity` → poll `editor_state` →
`read_console`) → maintainer Play-verify (headless Play frozen at frame 1, so visuals
are the maintainer's) → internal `superpowers:code-reviewer` → decision gate → push.
**Merge to `main` is a hard gate (explicit sign-off).**

## 5. Files

**New:**
- `Assets/Scripts/Core/CscSprites.cs`
- `Assets/Resources/UI/Sprites/*.png` (+ `.meta`) — moved from `Assets/Art/UI/Sprites/`

**Changed:**
- `Assets/Scripts/Build/BuildToolbarController.cs` (slot restructure, glyphs, readout, flyout)
- `Assets/Scripts/Build/BuildShipClassController.cs` (top-bar panel, dropdown branding)
- `Assets/Scripts/Build/CategoryFlyout.cs` (entry glyphs + restyle; glyph source injected)
- `Assets/Scripts/Core/UIStyle.cs` (dropdown branding; possibly a small slot/glyph helper)
- **Not touched:** `Assets/Materials/Defs/*` and the `ShapeDefinition` / `MaterialDefinition`
  SO types (no sprite field added — the glyph mapping is code-side).

## 6. Acceptance criteria

- Compiles clean (no new `CS` errors; MCP-infra console noise ignored).
- Toolbar: each slot shows number badge + glyph sprite + label; selected slot has the
  3px ochre outline; the cube slot's glyph tracks the armed material (`matA–D`);
  Delete shows the red ✕.
- Hazard-stripe trim renders tiled along the floor.
- Class dropdown reads on-brand (ink outline, ochre selected row, Condensed) and still
  changes the ship class; no other dropdown regressed.
- MASS/HP/Power on-brand (Anton value, `PowerPositive` green).
- No gameplay/save/scene-flow behaviour change; construct/grid rendering unchanged.
- Maintainer Play-verifies the four regions against the mockup.

## 7. Risks / open implementation questions (resolve in the plan)

- **Material count vs. 4 `matA–D` sprites:** if `MaterialRegistry` has ≠4 armour
  materials, `CubeMaterial(index)` clamps/falls back to `matA`. Confirm the count.
- **`tile_hazard_stripe` wrap mode:** must be Repeat for a Tiled `Image`; verify/set
  its `.meta` after the move.
- **Top-bar panel + toolbar strip backgrounds** are new elements (none today) — add
  them behind the existing widgets without disturbing layout/anchoring.
- **Delete ✕:** Unicode `✕` glyph in a Text vs. a drawn shape — default Unicode.
- **`UIStyle.BuildDropdown` reuse:** ensure branding it doesn't regress any other
  dropdown; parameterise if shared.
- **Glyph mapping fragility:** code-side displayName/asset-name keys — table + safe
  fallback (text label) if a shape is unmapped.
