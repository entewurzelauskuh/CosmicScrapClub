# Milestone B1 — UI rebrand: drop-in groundwork (design)

- **Date:** 2026-07-06
- **Branch:** `explore/ui-rebrand` (off synced `main`)
- **Milestone:** B (UI rebrand) — sub-phase **B1** of B1 → B2 → B3
- **Status:** approved at the brainstorm gate (2026-07-06); slice = "B1 alone", boundary = "stay invisible", sprite import = "(A) direct settings via UnityMCP".

## Context

Milestone A (desert → FlyScene) is landed on `main`. Milestone B consumes the
`unity_handoff/` design drop to rebrand the C#-built UI across all four surfaces
(MainMenu / HangarSelect / BuildScene / FlyScene HUD) into the **Cosmic Scrap
Club** brand. The drop, HANDOFF.md, and the brand reference are the source of
truth; see [[project_milestone_b_ui_rebrand]].

The UI is built entirely in code. `Assets/Scripts/Core/UIStyle.cs` (413 lines,
`internal static`) is the shared builder consumed by **20 scripts** across all
four scenes; its `BuiltinFont` private property is the single font chokepoint,
and its six color constants are byte-identical to `CscPalette`'s `src:`-tagged
values.

## Goal

Land every brand asset (palette, theme, fonts, sprites) into the project,
compiling, with `CscPalette` established as the color source of truth — while
changing **zero pixels**. This de-risks the visible work (B2/B3) by getting all
the mechanical integration and its compile/import surface out of the way first.

**Pass criterion:** the game looks identical to before, still compiles clean,
and the font bootstrap loads all four families with no null warnings.

## Non-goals (explicitly deferred to B2+)

- Swapping `UIStyle.BuiltinFont` to the brand font (the first *visible* change) → **B2**
- Toon outlines (`CscTheme.AddToonOutline`), ochre primary buttons, font-role
  application (Anton titles / Saira body) → **B2**
- Pointing the Fly HUD literals (`FlyBoostBar`/`FlyHeatBar`/`FlyShieldIndicator`/
  `FlyCrosshair`) at `CscPalette` → **B2** (optional)
- Any layout change; using the sprites in the build toolbar / HUD → **B3**

## Scope — what lands in B1

### 1. New scripts → `Assets/Scripts/Core/`

- **`CscPalette.cs`** — copied verbatim from `unity_handoff/Scripts/Core/`
  (already `namespace CubeFly.Core`). ~50 brand colors; compiles standalone.
- **`CscTheme.cs`** — copied verbatim. Semantic roles + font slots
  (`Display`/`Stencil`/`Body`/`Cond`) + `BuiltinFallback` + `*Or` accessors +
  `InteractiveColors()` + `AddToonOutline()` + `HeatColor()`. Compiles
  standalone; nothing references it yet.
- **`CscThemeBootstrap.cs`** — NEW, ~15 lines. Assigns the font slots once at
  startup (matches the project's existing `[RuntimeInitializeOnLoadMethod]`
  self-bootstrap pattern):

  ```csharp
  using UnityEngine;
  namespace CubeFly.Core
  {
      public static class CscThemeBootstrap
      {
          [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
          static void Init()
          {
              CscTheme.Display = Resources.Load<Font>("Fonts/Anton-Regular");
              CscTheme.Body    = Resources.Load<Font>("Fonts/Saira-SemiBold");
              CscTheme.Cond    = Resources.Load<Font>("Fonts/SairaCondensed-Bold");
              CscTheme.Stencil = Resources.Load<Font>("Fonts/SairaStencilOne-Regular");
          }
      }
  }
  ```

### 2. Fonts → `Assets/Resources/Fonts/`

Already staged (untracked): `Anton-Regular.ttf`, `Saira-Regular.ttf`,
`Saira-Bold.ttf`, `Saira-SemiBold.ttf`, `SairaStencilOne-Regular.ttf` +
`OFL-Anton/Saira/SairaStencilOne.txt`.

**Fetch the one gap:** `SairaCondensed-Bold.ttf` (the `Cond` slot = workhorse UI
face) + `OFL-SairaCondensed.txt`, from
`https://github.com/google/fonts/raw/main/ofl/sairacondensed/SairaCondensed-Bold.ttf`
(SIL OFL 1.1 — pre-authorized). Let Unity generate the `.meta` on import.

Commit the whole `Assets/Resources/Fonts/` folder. Fonts are loaded into
`CscTheme` slots by the bootstrap but **not consumed** by any UI builder yet →
zero visual change.

### 3. Sprites → `Assets/Art/UI/Sprites/` (new folder)

Copy the 12 PNGs from `unity_handoff/Sprites/`: `shape_cube_matA…D`,
`shape_slope`, `shape_pyramid_mg`, `shape_cylinder_rocket`, `shape_laser`,
`shape_thruster`, `shape_reactor`, `shape_shield`, `tile_hazard_stripe`.

**Import settings — approach (A): direct, via UnityMCP `manage_texture`** (no
lingering `AssetPostprocessor` for a fixed one-time set):
- Texture Type: `Sprite (2D and UI)`
- Filter Mode: `Point (no filter)`
- Alpha Is Transparency: on
- Compression: None / high quality
- `tile_hazard_stripe` only: Wrap Mode `Repeat`

Imported but unreferenced until B2/B3 → zero visual change.

### 4. `UIStyle` color forward (the only edit to existing code)

Replace the six local constants with forwards to `CscPalette`. Values verified
**byte-identical**, so this is provably zero-visual:

| `UIStyle` constant | current literal | → forward |
|---|---|---|
| `BackgroundIdle` | `(0.13, 0.13, 0.18, 0.9)` | `CscPalette.BackgroundIdle` |
| `TintNormal` | `Color.white` = `(1,1,1,1)` | `CscPalette.TintNormal` |
| `TintHighlight` | `(0.85, 0.85, 1, 1)` | `CscPalette.TintHighlight` |
| `TintPressed` | `(0.55, 0.55, 0.7, 1)` | `CscPalette.TintPressed` |
| `TintDisabled` | `(0.5, 0.5, 0.5, 0.5)` | `CscPalette.TintDisabled` |
| `LabelColor` | `Color.white` = `(1,1,1,1)` | `CscPalette.Label` |

**`BuiltinFont` is deliberately NOT touched** — the font stays `LegacyRuntime.ttf`.
That swap is the first visible change and belongs to B2.

## Verification

- After each script add/edit: UnityMCP `read_console` (or poll
  `editor_state.isCompiling`) → confirm a clean compile before proceeding.
  Order matters: `CscPalette` → `CscTheme` → `CscThemeBootstrap` → `UIStyle`
  forward (each depends on the prior).
- Font/sprite import: `refresh_unity` / import, then confirm no importer errors
  and that `Resources.Load<Font>("Fonts/SairaCondensed-Bold")` resolves.
- **Maintainer Play-mode check (the gate):** MainMenu + one other surface look
  identical to before; the bootstrap logs no font-null. "Looks unchanged" IS the
  pass. Headless MCP Play is frozen at frame 1, so visual confirmation is the
  maintainer's.

## Commit plan (surgical)

1. `feat(ui): add CscPalette + CscTheme brand palette/theme (B1)`
2. `feat(ui): stage brand fonts + CscThemeBootstrap (B1)`
3. `feat(ui): import 12 brand UI sprites (B1)`
4. `refactor(ui): forward UIStyle colors to CscPalette (B1, zero-visual)`

Hold the push until the maintainer's Play-check passes (bundle, per the desert
rhythm). Merge to `main` is a separate hard gate needing explicit sign-off.

## Risks / pre-flight checks

- **LFS:** check `.gitattributes` before committing — if `*.ttf`/`*.png` are
  LFS-tracked, the fonts/sprites go to LFS (fine, just be aware); if not, they
  commit as normal small binaries (fine).
- **Font fetch URL:** if the raw URL 404s (Google Fonts occasionally reorganises),
  fall back to the `fonts.google.com/specimen/Saira+Condensed` listing or
  `-ExtraBold`.
- **New asset folders** (`Assets/Art/`, `Assets/Art/UI/`, `Assets/Art/UI/Sprites/`)
  each need a `.meta` — let Unity generate them on import; never hand-author.
- **`.meta` discipline:** every asset moves with its meta (project invariant).
- **Editor sees the branch:** the live Editor runs against the main project root,
  and we're on a branch *in that root* (not a worktree), so it picks up these
  changes directly.
