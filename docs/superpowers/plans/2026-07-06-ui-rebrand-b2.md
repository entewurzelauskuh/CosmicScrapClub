# Milestone B2 — Typography + Toon Outlines Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (inline — live-Unity-Editor work, single instance, sequential compile order). Steps use checkbox (`- [ ]`) syntax.

**Goal:** Route `UIStyle` text through the four `CscTheme` font roles and add the signature ink outline to buttons, making the brand typographically visible across all four surfaces with no RectTransform changes.

**Architecture:** Add an optional `Font` param to the two text builders (default `CondOr` for buttons, `BodyOr` for labels); override the ~9 title/warning sites explicitly; add `CscTheme.AddToonOutline` inside the button builder and recolor the Toggle outline. Remove the now-superseded private `BuiltinFont`.

**Tech Stack:** Unity 6.3 LTS, C# (`Assembly-CSharp`), legacy `UnityEngine.UI`, UnityMCP for compile verification.

**Verification model:** No automated tests. Each task verifies via UnityMCP `read_console` (clean compile). Final visual confirmation (Anton titles / condensed buttons / Saira stats / stencil warnings / ink outlines, layouts intact) is the maintainer's Play check.

**Source of truth:** spec `docs/superpowers/specs/2026-07-06-ui-rebrand-b2-design.md`. `CscTheme`/`CscPalette` are `public`/`internal` in `CubeFly.Core` (same `Assembly-CSharp`), so every file that already calls `UIStyle.*` can call `CscTheme.*` with the same `using CubeFly.Core;`.

---

### Task 0: Pre-flight

- [ ] **Step 1: Confirm branch + Unity + clean baseline**

Run: `git -C "/Users/anon/My project" branch --show-current` → expect `explore/ui-rebrand`.
Read resource `mcpforunity://instances` → expect one live instance (`My project@…`). If Unity disconnected, STOP and ask the maintainer to focus the Editor.
UnityMCP `read_console` (types error/warning) → expect only MCP-infra lines (`McpLog.cs`), no C# errors.

---

### Task 1: Route UIStyle fonts through CscTheme roles

**Files:** Modify `Assets/Scripts/Core/UIStyle.cs`

- [ ] **Step 1: `BuildLabeledButton` — add font param, default `CondOr`**

Edit the signature:
```csharp
        public static (Button button, Text label) BuildLabeledButton(
            Transform parent, string labelText, Vector2 size, int fontSize = 28, Font font = null)
```
Edit the label font assignment (in `BuildLabeledButton`, currently `text.font = BuiltinFont;`):
```csharp
            text.font = font ?? CscTheme.CondOr;
```

- [ ] **Step 2: `BuildLabel` — add font param, default `BodyOr`**

Edit the signature:
```csharp
        public static Text BuildLabel(
            Transform parent, string text, int fontSize, FontStyle style = FontStyle.Normal, Font font = null)
```
Edit its font assignment — disambiguate by the `labelGO` receiver (this is the pair inside `BuildLabel`):
```csharp
            Text t = labelGO.AddComponent<Text>();
            t.font = font ?? CscTheme.BodyOr;
```

- [ ] **Step 3: `MakeText` (dropdown internals) → `CondOr`**

Disambiguate by the `go` receiver (this pair is inside `MakeText`):
```csharp
            Text t = go.AddComponent<Text>();
            t.font = CscTheme.CondOr;
```

- [ ] **Step 4: `BuildToggle` label → `CondOr`**

Change `label.font = BuiltinFont;` (unique — only `label.font` in the file):
```csharp
            label.font = CscTheme.CondOr;
```

- [ ] **Step 5: Remove the now-unused `BuiltinFont` property + field**

Delete this whole block (currently ~lines 25–39):
```csharp
        static Font _builtinFont;
        static Font BuiltinFont
        {
            get
            {
                if (_builtinFont == null)
                {
                    // Unity 6.x ships LegacyRuntime.ttf as the default UI font.
                    // Older versions exposed Arial.ttf; try both for safety.
                    _builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                                   ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                return _builtinFont;
            }
        }
```
(All four readers now use `CscTheme.*Or`, which chain to `CscTheme.BuiltinFallback` — the same LegacyRuntime→Arial logic — so nothing is lost.)

- [ ] **Step 6: Compile**

UnityMCP `refresh_unity` (mode `force`, scope `scripts`, compile `request`), poll `mcpforunity://editor/state` until `is_compiling:false`, then `read_console` (error/warning).
Expected: no C# errors. If `CS0103 BuiltinFont` appears, a reader was missed — fix it.

- [ ] **Step 7: Commit**

```bash
git -C "/Users/anon/My project" add "Assets/Scripts/Core/UIStyle.cs"
git -C "/Users/anon/My project" commit -m "refactor(ui): route UIStyle fonts through CscTheme roles (B2)" \
  -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Toon outlines on buttons + toggle

**Files:** Modify `Assets/Scripts/Core/UIStyle.cs`

- [ ] **Step 1: Outline every button**

In `BuildLabeledButton`, right after `button.targetGraphic = bImage;`, add:
```csharp
            button.targetGraphic = bImage;

            // Signature 2px ink outline on the button fill (render effect — no layout change).
            CscTheme.AddToonOutline(buttonGO);
```

- [ ] **Step 2: Recolor the Toggle box outline to Ink**

In `BuildToggle`, change `bgOutline.effectColor = Color.white;`:
```csharp
            bgOutline.effectColor = CscTheme.OutlineColor;
```

- [ ] **Step 3: Compile**

`refresh_unity` (force/scripts/request) → poll state → `read_console`. Expected: no C# errors.

- [ ] **Step 4: Commit**

```bash
git -C "/Users/anon/My project" add "Assets/Scripts/Core/UIStyle.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): toon outlines on buttons + toggle (B2)" \
  -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: Call-site font overrides (Anton titles + Stencil warnings)

**Files (modify):** `MainMenu/MainMenuController.cs`, `Core/PauseMenu.cs`, `Core/GameOverMenu.cs`, `Core/SettingsMenu.cs`, `HangarSelect/HangarSelectController.cs`, `Fly/FlyHeatBar.cs`, `Fly/FlyBoostBar.cs` (all under `Assets/Scripts/`).

Each edit appends one named argument to an existing `UIStyle.BuildLabel(...)` call. No other change. Where a file lacks `using CubeFly.Core;`, add it (it already references `UIStyle`, so it will be present).

- [ ] **Step 1: Titles → `CscTheme.DisplayOr` (single-line calls)**

`MainMenuController.cs:41`:
```csharp
            Text title = UIStyle.BuildLabel(root, "Cube Fly", fontSize: 96, style: FontStyle.Bold, font: CscTheme.DisplayOr);
```
`PauseMenu.cs:264`:
```csharp
            Text title = UIStyle.BuildLabel(root, "Paused", fontSize: 96, style: FontStyle.Bold, font: CscTheme.DisplayOr);
```
`HangarSelectController.cs:107`:
```csharp
            Text title = UIStyle.BuildLabel(root, "Choose a Slot", fontSize: 72, style: FontStyle.Bold, font: CscTheme.DisplayOr);
```
`HangarSelectController.cs:151`:
```csharp
            Text title = UIStyle.BuildLabel(rt, $"Slot {slot + 1}", fontSize: 32, style: FontStyle.Bold, font: CscTheme.DisplayOr);
```

- [ ] **Step 2: Titles → `CscTheme.DisplayOr` (multi-line calls)**

For each of these the `BuildLabel(...)` call wraps across ~2 lines; Read the call first, then insert `, font: CscTheme.DisplayOr` immediately before its closing `)`:
- `GameOverMenu.cs:151` — `"Construct Destroyed"` title
- `SettingsMenu.cs:196` — `"Settings"` title
- `SettingsMenu.cs:329` — `"VFX Toggles"` section header

- [ ] **Step 3: Warnings → `CscTheme.StencilOr`**

`FlyHeatBar.cs:182`:
```csharp
            _flashLabel = UIStyle.BuildLabel(canvasRoot, "Overheated!", fontSize: flashFontSize, style: FontStyle.Bold, font: CscTheme.StencilOr);
```
`FlyBoostBar.cs:208`:
```csharp
            _flashLabel = UIStyle.BuildLabel(canvasRoot, "Overboosted!", fontSize: flashFontSize, style: FontStyle.Bold, font: CscTheme.StencilOr);
```

- [ ] **Step 4: Compile**

`refresh_unity` (force/scripts/request) → poll state → `read_console`.
Expected: no C# errors. `CS0103 CscTheme` in a file ⇒ add `using CubeFly.Core;` to that file and recompile.

- [ ] **Step 5: Commit**

```bash
git -C "/Users/anon/My project" add \
  "Assets/Scripts/MainMenu/MainMenuController.cs" "Assets/Scripts/Core/PauseMenu.cs" \
  "Assets/Scripts/Core/GameOverMenu.cs" "Assets/Scripts/Core/SettingsMenu.cs" \
  "Assets/Scripts/HangarSelect/HangarSelectController.cs" \
  "Assets/Scripts/Fly/FlyHeatBar.cs" "Assets/Scripts/Fly/FlyBoostBar.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): Anton titles + stencil warnings (B2)" \
  -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: Final verification + present for the gate

- [ ] **Step 1: Clean console**

`read_console` (error/warning) → only MCP-infra lines; no C# errors from any B2 change.

- [ ] **Step 2: Sanity-check the role wiring at runtime**

UnityMCP `execute_code`: build a throwaway button + label via `UIStyle.BuildLabeledButton`/`BuildLabel` and assert `text.font.name` is `SairaCondensed-Bold` (button) and `Saira-SemiBold` (label default); log both. Confirms the defaults resolve to real brand fonts, not the fallback.

- [ ] **Step 3: Present for the maintainer's Play-mode gate**

Summarise the 3 commits. Ask the maintainer to Play MainMenu / HangarSelect / Build / FlyScene and confirm: Anton titles, condensed buttons+labels, Saira stats, stencil `Overheated!`/`Overboosted!`, 2px ink outline on buttons — layouts recognizably intact (minor over/underfill expected). **Hold the push** until confirmed; **do not merge**. Then push onto `explore/ui-rebrand` (updates PR #55) and re-request Copilot.

---

## Self-review

- **Spec coverage:** font roles via builder defaults + params (T1) ✓; buttons `CondOr` / labels `BodyOr` / dropdown+toggle `CondOr` (T1) ✓; remove `BuiltinFont` (T1 S5) ✓; toon outlines on buttons + Toggle recolor (T2) ✓; 7 title `DisplayOr` + 2 warning `StencilOr` overrides (T3) ✓; verification + gate + push/re-review (T4) ✓. Deferred-to-B4 items correctly absent (no `primary` param, no HUD color edits). All spec scope maps to a task.
- **Placeholders:** none — every edit shows exact code; the 3 multi-line title calls give a precise insert instruction (Read → append `, font: CscTheme.DisplayOr` before `)`), not a vague placeholder.
- **Type/name consistency:** `CscTheme.CondOr`/`BodyOr`/`DisplayOr`/`StencilOr`, `CscTheme.OutlineColor`, `CscTheme.AddToonOutline(GameObject)` — all match the members defined in `CscTheme.cs` (B1). Builder param name `font` used consistently in signatures and `font:` call-site args.
