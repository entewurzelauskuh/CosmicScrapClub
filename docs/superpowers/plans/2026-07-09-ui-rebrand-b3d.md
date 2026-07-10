# UI Rebrand — B3d Fly HUD Restyle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (inline — live-Unity-Editor work, single instance, sequential compile order). Steps use checkbox (`- [ ]`) syntax.

**Goal:** Restyle the FlyScene HUD to the cool esports-overlay brand + migrate every Fly-HUD colour literal to `CscPalette` (B4's remainder); full-match the weapon toolbar. Last B3 surface → retire `unity_handoff/`.

**Architecture:** Grouped **by file** — each of the 7 `Assets/Scripts/Fly/` HUD scripts touched once (colour migration + restyle together). Colour migration = **drop `[SerializeField]`, initialise the colour field from a `CscPalette` token** (so the scene-serialized dupe is ignored and colour has one source). Meters get an ink border on their frame; the weapon toolbar is rebuilt to 64×64 brand slots reusing B3c's `DecorateToolbarSlot`/`AddSelectionOutline`/`CscSprites`.

**Tech Stack:** Unity 6.3 LTS, C# (`Assembly-CSharp`), legacy `UnityEngine.UI`, UnityMCP for compile verification.

**Verification model:** No automated tests. After each task: `refresh_unity` (force/scripts) → poll `mcpforunity://editor/state` until `ready_for_tools` → `read_console` (error, filter `.cs(`) clean. Final visual match (default + laser-heat states) is the maintainer's Play-check (headless MCP Play frozen at frame 1).

**Source of truth:** spec `docs/superpowers/specs/2026-07-09-ui-rebrand-b3d-design.md`.

**Note on edits:** anchors are verbatim from the current files, but **Read the region first, then Edit**. All Fly scripts `using CubeFly.Core;`. **Alpha rule:** for a colour field whose alpha is <1, if the token is opaque, write `new Color(CscPalette.X.r, CscPalette.X.g, CscPalette.X.b, α)`; the α-1 fields are drop-in `CscPalette.X`.

---

### Task 0: Pre-flight

- [ ] Confirm branch `explore/ui-rebrand`; one live instance; `read_console` (error, filter `.cs(`) clean.

---

### Task 1: `FlyBoostBar` — colours → tokens + ink frame

**Files:** Modify `Assets/Scripts/Fly/FlyBoostBar.cs`

- [ ] **Step 1: Migrate the colour fields.** Replace:
```csharp
        [SerializeField] Color fillColor = new Color(0.36f, 0.62f, 1f, 1f);
        [SerializeField] Color frameColor = new Color(0.05f, 0.07f, 0.12f, 1f);
```
  with:
```csharp
        Color fillColor = CscPalette.Boost;
        Color frameColor = CscPalette.HudPanel;
```
  Replace `[SerializeField] Color flashColor = new Color(1f, 0.45f, 0.3f, 1f);` with `Color flashColor = CscPalette.WarnFlash;`, and
  `[SerializeField] Color criticalColor = new Color(0.95f, 0.25f, 0.20f, 1f);` with `Color criticalColor = CscPalette.Critical;`.

- [ ] **Step 2: Ink border on the frame.** In `BuildUI`, the frame is `frameGO` (`GameObject frameGO = new GameObject("BoostBarFrame", …)`). Read the frame block; **after** the frame's `Image.color`/`raycastTarget` are set (before the fill is built), add:
```csharp
            CscTheme.AddToonOutline(frameGO);
```

- [ ] **Step 3: Compile.** `refresh_unity` (force/scripts) → poll → `read_console`. No `.cs(` errors.
- [ ] **Step 4: Commit.**
```bash
git -C "/Users/anon/My project" add "Assets/Scripts/Fly/FlyBoostBar.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): boost meter → CscPalette + ink frame (B3d)" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: `FlyHeatBar` — colours → tokens + ink frame

**Files:** Modify `Assets/Scripts/Fly/FlyHeatBar.cs`

- [ ] **Step 1: Migrate the colour fields.** Replace:
```csharp
        [SerializeField] Color coolColor = new Color(1f, 0.6f, 0.2f, 1f);
        [SerializeField] Color hotColor = new Color(1f, 0.2f, 0.1f, 1f);
        [SerializeField] Color frameColor = new Color(0.12f, 0.06f, 0.04f, 1f);
```
  with:
```csharp
        Color coolColor = CscPalette.HeatCool;
        Color hotColor = CscPalette.HeatHot;
        Color frameColor = CscPalette.HudPanel;
```
  Replace `[SerializeField] Color flashColor = new Color(1f, 0.4f, 0.25f, 1f);` with `Color flashColor = CscPalette.WarnFlash;` (fixes the near-miss).

- [ ] **Step 2: Ink border on the frame.** In `BuildUI` (frame `frameGO = new GameObject("HeatBarFrame", …)`), after the frame's `Image.color`/`raycastTarget` are set, add `CscTheme.AddToonOutline(frameGO);` (Read the frame block, then insert).

- [ ] **Step 3: Compile + Commit.** `refresh_unity` → poll → `read_console` clean.
```bash
git -C "/Users/anon/My project" add "Assets/Scripts/Fly/FlyHeatBar.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): heat meter → CscPalette + ink frame (B3d)" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: `FlyShieldIndicator` — colours → tokens + ink frame + POWER caps

**Files:** Modify `Assets/Scripts/Fly/FlyShieldIndicator.cs`

- [ ] **Step 1: Migrate the colour fields.** Replace the `[Header("Colours")]` block:
```csharp
        [SerializeField] Color shieldFillColor = new Color(0.3f, 0.8f, 1f, 0.95f);
        [SerializeField] Color shieldFrameColor = new Color(0.05f, 0.12f, 0.16f, 0.85f);
        [SerializeField] Color shieldDownColor = new Color(0.4f, 0.4f, 0.45f, 0.6f);
        [SerializeField] Color powerPositiveColor = new Color(0.4f, 1f, 0.5f, 1f);
        [SerializeField] Color powerNegativeColor = new Color(1f, 0.4f, 0.35f, 1f);
```
  with (preserve the sub-1 alphas explicitly):
```csharp
        Color shieldFillColor = new Color(CscPalette.Shield.r, CscPalette.Shield.g, CscPalette.Shield.b, 0.95f);
        Color shieldFrameColor = CscPalette.HudPanel;
        Color shieldDownColor = new Color(CscPalette.ShieldDown.r, CscPalette.ShieldDown.g, CscPalette.ShieldDown.b, 0.6f);
        Color powerPositiveColor = CscPalette.PowerPositive;
        Color powerNegativeColor = CscPalette.PowerNegative;
```
  Replace `[SerializeField] Color ejectHintColor = new Color(1f, 0.55f, 0.2f, 1f);` with `Color ejectHintColor = CscPalette.Eject;`.

- [ ] **Step 2: Ink border on the shield frame.** After `frameImg.raycastTarget = false;` (the `ShieldBarFrame` block) add:
```csharp
            CscTheme.AddToonOutline(frameGO);
```

- [ ] **Step 3: POWER label uppercase + Condensed.** The power label builds as `_powerLabel = UIStyle.BuildLabel(root, "Power: +0", fontSize: powerFontSize);`. Change the placeholder to `"POWER: +0"` and after `_powerLabel.alignment = TextAnchor.LowerLeft;` add:
```csharp
            _powerLabel.font = CscTheme.CondOr;
            _powerLabel.supportRichText = true;
```
  Then **Read the power refresh** (where `_powerLabel.text` is set in `Update`/a refresh method) and change the format string from `"Power: …"` to `"POWER: …"` (keep the `+`/sign + the existing `powerPositive/negativeColor` colour flip — now `CscPalette`).

- [ ] **Step 4: Compile + Commit.** `refresh_unity` → poll → `read_console` clean.
```bash
git -C "/Users/anon/My project" add "Assets/Scripts/Fly/FlyShieldIndicator.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): shield/power → CscPalette + ink frame + POWER caps (B3d)" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: `FlyCrosshair` — colour → token

**Files:** Modify `Assets/Scripts/Fly/FlyCrosshair.cs`

- [ ] **Step 1:** Replace `[SerializeField] Color crosshairColor = Color.white;` with `Color crosshairColor = CscPalette.Label;`.
- [ ] **Step 2: Compile + Commit.** `refresh_unity` → poll → `read_console` clean.
```bash
git -C "/Users/anon/My project" add "Assets/Scripts/Fly/FlyCrosshair.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): crosshair colour → CscPalette.Label (B3d)" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: `FlyHpIndicator` + `FlySpeedIndicator` — caps label + big value + HP red-<25%

**Files:** Modify `Assets/Scripts/Fly/FlyHpIndicator.cs`, `Assets/Scripts/Fly/FlySpeedIndicator.cs`

- [ ] **Step 1: HP — brand the label at build.** In `FlyHpIndicator.BuildUI`, after `_label.alignment = TextAnchor.MiddleLeft;` add:
```csharp
            _label.font = CscTheme.CondOr;
            _label.supportRichText = true;
```
- [ ] **Step 2: HP — big value + red-<25% in `Update`.** Replace:
```csharp
            float current = SumCubeHp();
            _label.text = $"HP: {current:F0} / {_maxHp:F0}";
```
  with:
```csharp
            float current = SumCubeHp();
            _label.text = $"HP <size={fontSize + 12}>{current:F0}</size> / {_maxHp:F0}";
            _label.color = (_maxHp > 0f && current < 0.25f * _maxHp) ? CscPalette.Critical : CscPalette.Label;
```
- [ ] **Step 3: SPEED — brand the label at build.** In `FlySpeedIndicator.BuildUI`, after `_label.alignment = TextAnchor.MiddleLeft;` add:
```csharp
            _label.font = CscTheme.CondOr;
            _label.supportRichText = true;
            _label.color = CscPalette.Label;
```
- [ ] **Step 4: SPEED — big value in `Update`.** Replace:
```csharp
            float speed = _constructRb != null ? _constructRb.linearVelocity.magnitude : 0f;
            _label.text = $"Speed: {speed:F1} u/s";
```
  with:
```csharp
            float speed = _constructRb != null ? _constructRb.linearVelocity.magnitude : 0f;
            _label.text = $"SPEED <size={fontSize + 12}>{speed:F0}</size> u/s";
```
- [ ] **Step 5: Compile + Commit.** `refresh_unity` → poll → `read_console` clean.
```bash
git -C "/Users/anon/My project" add "Assets/Scripts/Fly/FlyHpIndicator.cs" "Assets/Scripts/Fly/FlySpeedIndicator.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): HP/Speed readouts — big value + HP red-<25% (B3d)" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: `FlyWeaponToolbarController` — 64×64 brand slots

**Files:** Modify `Assets/Scripts/Fly/FlyWeaponToolbarController.cs`

- [ ] **Step 1: Square slots + brand reload track.** Change `[SerializeField] Vector2 buttonSize = new Vector2(160f, 60f);` to `new Vector2(64f, 64f)`. Change `[SerializeField] Vector2 reloadBarSize = new Vector2(140f, 6f);` to `new Vector2(54f, 4f)`. Migrate `[SerializeField] Color reloadBarBackground = new Color(0f, 0f, 0f, 0.6f);` → `Color reloadBarBackground = CscPalette.HudPanel;`.

- [ ] **Step 2: Migrate death colours; drop the dead-fill + blue-select consts.** Replace:
```csharp
        [Tooltip("Background color of a fully-dead weapon type's button.")]
        [SerializeField] Color deadColor = new Color(0.32f, 0.32f, 0.34f, 0.9f);
        [Tooltip("Color of the partial-death corner mark (the X glyph).")]
        [SerializeField] Color deathMarkColor = new Color(0.95f, 0.2f, 0.2f, 1f);
```
  with (dead is now an alpha-dim, so `deadColor` goes away):
```csharp
        [Tooltip("Color of the partial-death corner mark (the X glyph).")]
        Color deathMarkColor = CscPalette.Critical;
```
  Delete the `static readonly Color SelectedTypeColor = …;` line (selection becomes an ochre outline).

- [ ] **Step 3: New per-slot arrays.** After `Text[] _deathMarks;` add:
```csharp
        Outline[] _selectionOutlines;
        CanvasGroup[] _canvasGroups;
```
  and allocate them alongside the others in `RebuildButtons` (after `_deathMarks = new Text[count];`):
```csharp
            _selectionOutlines = new Outline[count];
            _canvasGroups = new CanvasGroup[count];
```

- [ ] **Step 4: Decorate each slot (glyph + badge + ochre outline + dim group).** Replace the button build:
```csharp
                (Button btn, Text _) = UIStyle.BuildLabeledButton(_container, label, buttonSize, fontSize);
```
  with:
```csharp
                (Button btn, Text lbl) = UIStyle.BuildLabeledButton(_container, label, buttonSize, fontSize);
                Sprite glyph = shape != null ? CscSprites.ForShape(shape.displayName, 0) : null;
                UIStyle.DecorateToolbarSlot(btn, lbl, glyph, (idx + 1).ToString(), string.Empty);   // caption suppressed; glyph identifies the weapon
                _selectionOutlines[i] = UIStyle.AddSelectionOutline(btn.gameObject);
                _canvasGroups[i] = btn.gameObject.AddComponent<CanvasGroup>();
```

- [ ] **Step 5: Reload bar along the bottom.** Replace the reload-bar block:
```csharp
                // ---- Reload bar (background + foreground fill) ----
                float barY = bottomMargin + buttonSize.y + reloadBarGap + reloadBarSize.y / 2f;
                Vector2 barCenter = new Vector2(startX + i * (buttonSize.x + spacing), barY);

                BuildReloadRect(_container, "ReloadBarBg" + i, reloadBarSize, barCenter, reloadBarBackground, isFill: false);
                _reloadBars[i] = BuildReloadRect(_container, "ReloadBarFg" + i, reloadBarSize, barCenter, swatchColor, isFill: true);
```
  with (bar at the slot's bottom edge, rendered over it):
```csharp
                // ---- Reload bar along the slot's bottom edge ----
                float barY = bottomMargin + 3f;
                Vector2 barCenter = new Vector2(startX + i * (buttonSize.x + spacing), barY);

                BuildReloadRect(_container, "ReloadBarBg" + i, reloadBarSize, barCenter, reloadBarBackground, isFill: false);
                _reloadBars[i] = BuildReloadRect(_container, "ReloadBarFg" + i, reloadBarSize, barCenter, swatchColor, isFill: true);
```

- [ ] **Step 6: Selection = ochre outline; dead = 40% alpha.** In `RefreshWeaponStates`, replace:
```csharp
                if (_buttonBackgrounds[i] != null)
                {
                    Color bg;
                    if (fullyDead)          bg = deadColor;
                    else if (i == selected) bg = SelectedTypeColor;
                    else                    bg = UIStyle.BackgroundIdle;
                    _buttonBackgrounds[i].color = bg;
                }
```
  with:
```csharp
                if (_buttonBackgrounds[i] != null)
                    _buttonBackgrounds[i].color = UIStyle.BackgroundIdle;   // dark slot always
                if (_selectionOutlines != null && _selectionOutlines[i] != null)
                    _selectionOutlines[i].enabled = (i == selected && !fullyDead);
                if (_canvasGroups != null && _canvasGroups[i] != null)
                    _canvasGroups[i].alpha = fullyDead ? 0.4f : 1f;
```

- [ ] **Step 7: Compile + Commit.** `refresh_unity` → poll → `read_console` clean.
```bash
git -C "/Users/anon/My project" add "Assets/Scripts/Fly/FlyWeaponToolbarController.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): weapon toolbar — 64x64 glyph slots, ochre select, bottom reload, dead-dim (B3d)" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 7: Verify + retire `unity_handoff/` + gate

- [ ] **Step 1:** `read_console` (error, filter `.cs(`) → clean.
- [ ] **Step 2:** Present for the maintainer's Play-mode gate — default HUD + laser-heat state: meters read as dark ink-bordered chips with energy fills; HP/Speed values size-emphasised, HP red below 25%; weapon slots are 64×64 with glyph + key badge + swatch, ochre-outline selection, bottom reload bar, dead = 40% alpha + red ✕; colours all `CscPalette`. **Hold the push.**
- [ ] **Step 3:** On Play sign-off, dispatch the internal `superpowers:code-reviewer` on `git diff <T0-base>..HEAD -- '*.cs'`; assess + apply.
- [ ] **Step 4:** On confirm, **retire `unity_handoff/`** — `git rm -r unity_handoff/ … ` (it's untracked, so `rm -rf unity_handoff` + note in the commit / ROADMAP) — then bundle-push. **Milestone B content-complete.** Merge to `main` = hard gate (PR #56 coordination first).

---

## Self-review

- **Spec coverage:** colour migration all 7 scripts (T1-T6) ✓; meter ink frames (T1/T2/T3) ✓; weapon toolbar full-match — 64×64 + glyph + badge (DecorateToolbarSlot, T6S4) + ochre outline (T6S4/S6) + bottom reload (T6S5) + dead 40% α (T6S3/S6) ✓; readout big value + HP red-<25% (T5) ✓; POWER caps (T3S3) ✓; crosshair → Label (T4) ✓; skip multiplayer/crosshair-dynamics (not built) ✓; retire unity_handoff (T7) ✓.
- **Placeholders:** none — every field swap gives the exact literal→token; new arrays + refresh logic are concrete. Two Read-then-Edit anchors (Boost/Heat frame `AddToonOutline` insert point; the POWER refresh format string) specify the exact code, only the anchor line is read live.
- **Type/name consistency:** `CscPalette.Boost/HeatCool/HeatHot/Shield/ShieldDown/PowerPositive/PowerNegative/Eject/WarnFlash/Critical/HudPanel/Label`, `CscTheme.AddToonOutline/CondOr`, `CscSprites.ForShape`, `UIStyle.DecorateToolbarSlot/AddSelectionOutline/BackgroundIdle` — all real. New `_selectionOutlines`/`_canvasGroups` declared (S3) before use (S4/S6). `deadColor`/`SelectedTypeColor` removed and their only usages (the dead/selected bg branch) rewritten in S6.
- **Risks:** token alpha handled explicitly for shield fill/down (S1 §Task3); `[SerializeField]` drop leaves scene dupes orphaned (harmless); `DecorateToolbarSlot` caption suppressed so it won't collide with the bottom reload bar; reload-bar reposition keeps `reloadBarSize.x` so the `Update` fill-width math (`:100-104`) still holds.
