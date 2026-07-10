# Milestone B1 — UI Rebrand Drop-in Groundwork Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to
> implement this plan task-by-task (inline — this is live-Unity-Editor work: one
> Editor instance, sequential compile order, so subagent parallelism does not
> apply). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land every Cosmic Scrap Club brand asset (palette, theme, fonts, sprites) into the project, compiling, with `CscPalette` established as the color source of truth — changing zero pixels.

**Architecture:** Copy the two drop-in scripts + a font bootstrap into `Assets/Scripts/Core/`; fetch the one missing font and import the 12 sprites; repoint `UIStyle`'s six color constants at `CscPalette` (byte-identical values). No existing consumer's behaviour changes; `UIStyle.BuiltinFont` is left alone so the font does not change (that is B2).

**Tech Stack:** Unity 6.3 LTS, URP, C# (`Assembly-CSharp`), legacy `UnityEngine.UI`, UnityMCP for editor ops (refresh/import/console/texture-settings).

**Verification model:** No automated tests (project invariant). Each task verifies via UnityMCP `read_console` (clean compile) + import checks. The final visual "nothing changed" confirmation is the maintainer's Play-mode check (headless MCP Play is frozen at frame 1).

**Source of truth:** spec `docs/superpowers/specs/2026-07-06-ui-rebrand-b1-design.md`; drop `unity_handoff/`.

---

### Task 0: Pre-flight (branch, Unity MCP, LFS)

**Files:** none (inspection only).

- [ ] **Step 1: Confirm branch + drop present**

Run:
```bash
ROOT="/Users/anon/My project"
git -C "$ROOT" branch --show-current           # expect: explore/ui-rebrand
ls "$ROOT/unity_handoff/Scripts/Core/"          # expect: CscPalette.cs CscTheme.cs
ls "$ROOT/Assets/Resources/Fonts/"              # expect the 5 staged TTFs + 3 OFLs
```
Expected: on `explore/ui-rebrand`; drop scripts + staged fonts present.

- [ ] **Step 2: Check LFS attributes (informational)**

Run:
```bash
ROOT="/Users/anon/My project"
grep -E '\.ttf|\.png|\.otf' "$ROOT/.gitattributes" 2>/dev/null || echo "no ttf/png LFS rule"
```
Expected: note whether `*.ttf`/`*.png` are LFS-tracked. Either way the commits proceed (LFS is installed & healthy); this is just to know where the binaries land.

- [ ] **Step 3: Confirm Unity MCP is connected**

Read the resource `mcpforunity://instances` (or call `set_active_instance`) to get the live `Name@hash`. If multiple instances, pin with `set_active_instance`.
Expected: exactly one live Editor on the main project root. If none, STOP and ask the maintainer to open the project in Unity (the drop-in file ops can proceed, but compile/import/texture verification needs the Editor).

---

### Task 1: Add CscPalette + CscTheme

**Files:**
- Create: `Assets/Scripts/Core/CscPalette.cs` (copy of `unity_handoff/Scripts/Core/CscPalette.cs`)
- Create: `Assets/Scripts/Core/CscTheme.cs` (copy of `unity_handoff/Scripts/Core/CscTheme.cs`)

- [ ] **Step 1: Copy both scripts verbatim**

Run:
```bash
ROOT="/Users/anon/My project"
cp "$ROOT/unity_handoff/Scripts/Core/CscPalette.cs" "$ROOT/Assets/Scripts/Core/CscPalette.cs"
cp "$ROOT/unity_handoff/Scripts/Core/CscTheme.cs"   "$ROOT/Assets/Scripts/Core/CscTheme.cs"
```
(They are already `namespace CubeFly.Core` — no edits needed.)

- [ ] **Step 2: Import + compile**

UnityMCP `refresh_unity`, then `read_console` (types: error/warning).
Expected: `.meta` files generated for both; **no compile errors**. `CscPalette`/`CscTheme` compile standalone; nothing references them yet.

- [ ] **Step 3: Commit**

```bash
ROOT="/Users/anon/My project"
git -C "$ROOT" add "Assets/Scripts/Core/CscPalette.cs" "Assets/Scripts/Core/CscPalette.cs.meta" \
                   "Assets/Scripts/Core/CscTheme.cs"   "Assets/Scripts/Core/CscTheme.cs.meta"
git -C "$ROOT" commit -m "feat(ui): add CscPalette + CscTheme brand palette/theme (B1)" \
  -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Fonts (fetch the gap) + CscThemeBootstrap

**Files:**
- Create: `Assets/Resources/Fonts/SairaCondensed-Bold.ttf` (fetched)
- Create: `Assets/Resources/Fonts/OFL-SairaCondensed.txt` (fetched)
- Create: `Assets/Scripts/Core/CscThemeBootstrap.cs`

- [ ] **Step 1: Fetch the missing Condensed-Bold font + its license**

Run:
```bash
ROOT="/Users/anon/My project"
BASE="https://github.com/google/fonts/raw/main/ofl/sairacondensed"
curl -fsSL "$BASE/SairaCondensed-Bold.ttf" -o "$ROOT/Assets/Resources/Fonts/SairaCondensed-Bold.ttf"
curl -fsSL "$BASE/OFL.txt"                  -o "$ROOT/Assets/Resources/Fonts/OFL-SairaCondensed.txt"
ls -la "$ROOT/Assets/Resources/Fonts/SairaCondensed-Bold.ttf"   # expect a ~100-300KB TTF
```
Expected: a real TTF (tens–hundreds of KB). If it 404s or is tiny/HTML, fall back to `SairaCondensed-ExtraBold.ttf` (and update the bootstrap name in Step 2 accordingly), or download from `fonts.google.com/specimen/Saira+Condensed`.

- [ ] **Step 2: Create the font bootstrap**

Create `Assets/Scripts/Core/CscThemeBootstrap.cs`:
```csharp
using UnityEngine;

namespace CubeFly.Core
{
    // Assigns the brand fonts into CscTheme's slots once at startup. Kept as a
    // standalone [RuntimeInitializeOnLoadMethod] to match the project's other
    // self-bootstrapping systems. If a Resources.Load returns null, CscTheme's
    // *Or accessors fall back to the builtin font, so a missing file never
    // breaks the UI. B1 leaves UIStyle using the builtin font, so these slots
    // are populated-but-unused until B2 wires them in.
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

- [ ] **Step 3: Import + compile**

UnityMCP `refresh_unity`, then `read_console`.
Expected: `.meta` generated for the new TTF, the OFL `.txt`, and `CscThemeBootstrap.cs`; **no compile errors** (the `CscTheme.Display/Body/Cond/Stencil` fields exist from Task 1).

- [ ] **Step 4: Commit the whole Fonts folder + bootstrap**

```bash
ROOT="/Users/anon/My project"
git -C "$ROOT" add "Assets/Resources/Fonts" \
                   "Assets/Scripts/Core/CscThemeBootstrap.cs" "Assets/Scripts/Core/CscThemeBootstrap.cs.meta"
git -C "$ROOT" commit -m "feat(ui): stage brand fonts + CscThemeBootstrap (B1)" \
  -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```
(`Assets/Resources/` + `Assets/Resources/Fonts/` folder `.meta`s were untracked — this adds them too. Verify `git status` shows nothing stray left under `Assets/Resources/`.)

---

### Task 3: Import the 12 brand sprites

**Files:**
- Create: `Assets/Art/UI/Sprites/*.png` (12 files copied from `unity_handoff/Sprites/`)

- [ ] **Step 1: Copy the sprites**

Run:
```bash
ROOT="/Users/anon/My project"
mkdir -p "$ROOT/Assets/Art/UI/Sprites"
cp "$ROOT/unity_handoff/Sprites/"*.png "$ROOT/Assets/Art/UI/Sprites/"
ls "$ROOT/Assets/Art/UI/Sprites/" | wc -l   # expect 12
```

- [ ] **Step 2: Import**

UnityMCP `refresh_unity` (imports them with default Texture settings; generates `.meta` for the new `Assets/Art`, `Assets/Art/UI`, `Assets/Art/UI/Sprites` folders + each PNG).

- [ ] **Step 3: Apply Sprite import settings via UnityMCP**

Load the `manage_texture` schema (ToolSearch `select:mcp__UnityMCP__manage_texture`) and set, for **all 12**: `textureType = Sprite (2D and UI)`, `filterMode = Point`, `alphaIsTransparency = true`, `compression = None/Uncompressed`. Then for `tile_hazard_stripe.png` **only**: `wrapMode = Repeat`.
Verify with `read_console` (no importer errors) and re-inspect one sprite's importer to confirm `textureType=Sprite`, `filterMode=Point`.
Expected: all 12 are Sprites, Point-filtered; hazard tile wraps Repeat.

- [ ] **Step 4: Commit**

```bash
ROOT="/Users/anon/My project"
git -C "$ROOT" add "Assets/Art"
git -C "$ROOT" commit -m "feat(ui): import 12 brand UI sprites (B1)" \
  -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: Forward UIStyle colors to CscPalette (zero-visual)

**Files:**
- Modify: `Assets/Scripts/Core/UIStyle.cs:18-23` (the six color constants)

- [ ] **Step 1: Replace the six constants with forwards**

In `Assets/Scripts/Core/UIStyle.cs`, replace the color-constant block (currently
lines 18–23) so each forwards to `CscPalette` (same namespace — no `using`
needed). Leave every other line — including `BuiltinFont` — untouched.

New block:
```csharp
        public static readonly Color BackgroundIdle = CscPalette.BackgroundIdle;
        public static readonly Color TintNormal     = CscPalette.TintNormal;
        public static readonly Color TintHighlight  = CscPalette.TintHighlight;
        public static readonly Color TintPressed    = CscPalette.TintPressed;
        public static readonly Color TintDisabled   = CscPalette.TintDisabled;
        public static readonly Color LabelColor     = CscPalette.Label;
```
(Values are byte-identical to the literals they replace — verified in the spec — so this is provably zero-visual. `CscPalette.Label` is the counterpart of `LabelColor`.)

- [ ] **Step 2: Import + compile**

UnityMCP `refresh_unity`, then `read_console`.
Expected: **no compile errors**; `UIStyle` now sources its colors from `CscPalette`.

- [ ] **Step 3: Commit**

```bash
ROOT="/Users/anon/My project"
git -C "$ROOT" add "Assets/Scripts/Core/UIStyle.cs"
git -C "$ROOT" commit -m "refactor(ui): forward UIStyle colors to CscPalette (B1, zero-visual)" \
  -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: Final verification + present for the gate

**Files:** none.

- [ ] **Step 1: Confirm a fully clean console**

UnityMCP `read_console` (types: error/warning; clear=false).
Expected: no errors from any B1 change.

- [ ] **Step 2: Confirm the fonts actually resolve**

UnityMCP `execute_code`: load each of the four `Resources.Load<Font>("Fonts/…")`
paths used by the bootstrap and log whether each is non-null (Anton-Regular,
Saira-SemiBold, SairaCondensed-Bold, SairaStencilOne-Regular).
Expected: all four non-null → the bootstrap will populate every slot with no
builtin-font fallback.

- [ ] **Step 3: Present for the maintainer's Play-mode gate**

Summarise: 4 commits, zero-visual by construction. Ask the maintainer to Play
MainMenu + one more surface and confirm they look identical and there are no
font-null warnings in the console. **Hold the push** until they confirm; **do
not merge** (separate hard gate). On "iterate", address; on "ship", bundle-push
the branch.

---

## Self-review

- **Spec coverage:** new scripts (T1 + T2 bootstrap) ✓; fonts incl. the gap (T2) ✓; sprites + import settings via approach A (T3) ✓; UIStyle color forward, `BuiltinFont` untouched (T4) ✓; verification + gate (T5) ✓; LFS/font-URL/new-folder-meta risks covered (T0 + inline) ✓. All spec scope items map to a task.
- **Placeholders:** none — every code block is complete (bootstrap + UIStyle block shown in full); the one runtime-schema lookup (`manage_texture`) is explicitly deferred to execution with the exact settings listed.
- **Type consistency:** bootstrap assigns `CscTheme.Display/Body/Cond/Stencil` (the exact public field names in `CscTheme.cs`); forward targets `CscPalette.BackgroundIdle/TintNormal/TintHighlight/TintPressed/TintDisabled/Label` (the exact member names in `CscPalette.cs`, incl. `Label` ↔ `LabelColor`). Consistent.
