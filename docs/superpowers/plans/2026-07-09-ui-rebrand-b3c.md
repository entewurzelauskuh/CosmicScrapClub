# Milestone B3c — BuildScene Restyle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (inline — live-Unity-Editor work, single instance, sequential compile order). Steps use checkbox (`- [ ]`) syntax.

**Goal:** Restyle the BuildScene UI chrome to `hangar-build.png` — sprite glyphs on the toolbar, branded top-bar dropdown, hazard-stripe trim, on-brand MASS/HP/Power — reusing the B1–B3a primitives plus a new `CscSprites` loader. Presentation-only.

**Architecture:** New `CscSprites` static (cached `Resources.Load<Sprite>`, mirrors `CscThemeBootstrap`). New `UIStyle` helpers (`DecorateToolbarSlot`, `AddSelectionOutline`) + `BuildDropdown` branding. The 5 real toolbar slots (`Cube · Slope · Weapons▸ · Utilities▸ · Delete`) get glyph + number badge + bottom caption + ochre selection outline; the category-flyout structure is **kept**, not flattened. `BuildToolbarController` / `BuildShipClassController` / `CategoryFlyout` are the touched scene scripts.

**Tech Stack:** Unity 6.3 LTS, C# (`Assembly-CSharp`), legacy `UnityEngine.UI`, UnityMCP for compile verification.

**Verification model:** No automated tests. After each task: `refresh_unity` (force/scripts, or force/all when assets/new files change) → poll `mcpforunity://editor/state` until `ready_for_tools` → `read_console` (error) clean (MCP-infra noise ignored). Final visual match + working toolbar/dropdown/flyout is the maintainer's Play-check (headless MCP Play is frozen at frame 1).

**Source of truth:** spec `docs/superpowers/specs/2026-07-09-ui-rebrand-b3c-design.md`; mockup `unity_handoff/reference/screens/hangar-build.png`; BRAND.md §"Build screen".

**Note on edits:** anchors below are verbatim from the current files, but prior work shifts line numbers — **Read the method first, then Edit** against the exact current text. `CscPalette`/`CscTheme`/`UIStyle`/`CscSprites` are all in `CubeFly.Core`; the Build scripts already `using CubeFly.Core;`.

---

### Task 0: Pre-flight

- [ ] Confirm branch `explore/ui-rebrand`; `mcpforunity://instances` shows one live instance (`My project@…`); `read_console` (error) clean (MCP-infra only).

---

### Task 1: Sprite pipeline — move to Resources + `CscSprites`

**Files:**
- Move: `Assets/Art/UI/Sprites/*.png` (+ `.meta`) → `Assets/Resources/UI/Sprites/`
- Create: `Assets/Scripts/Core/CscSprites.cs`

- [ ] **Step 1: Move the 12 sprites (meta follows → guid preserved).** The sprites are currently unreferenced, so relocation is safe. Run:
```bash
cd "/Users/anon/My project"
mkdir -p Assets/Resources/UI/Sprites
git mv Assets/Art/UI/Sprites/*.png Assets/Art/UI/Sprites/*.png.meta Assets/Resources/UI/Sprites/ 2>/dev/null || (for f in Assets/Art/UI/Sprites/*.png Assets/Art/UI/Sprites/*.png.meta; do git mv "$f" Assets/Resources/UI/Sprites/; done)
# remove the now-empty source folder + its meta if present
git rm -q Assets/Art/UI/Sprites.meta 2>/dev/null || true
rmdir Assets/Art/UI/Sprites 2>/dev/null || true
ls Assets/Resources/UI/Sprites/
```
Expected: 12 `.png` + 12 `.meta` listed under `Assets/Resources/UI/Sprites/`.

- [ ] **Step 2: Ensure the hazard tile wraps Repeat.** Read `Assets/Resources/UI/Sprites/tile_hazard_stripe.png.meta`; under `textureSettings`, if `wrapU`/`wrapV` are not `0`, set both to `0` (Repeat). (Point filter `filterMode: 0` and `spriteMode: 1` are already correct.)

- [ ] **Step 3: Create `CscSprites.cs`.**
```csharp
using System.Collections.Generic;
using UnityEngine;

namespace CubeFly.Core
{
    // Cached loader for the brand build-shape glyph sprites. The 12 PNGs live
    // under Assets/Resources/UI/Sprites/ so the code-built UI can reach them via
    // Resources.Load — the static UIStyle builders and the toolbar can't hold
    // serialized Inspector refs. Mirrors CscThemeBootstrap's Resources.Load<Font>.
    //
    // Glyphs are pre-coloured with baked ink outlines — render them on a white
    // Image with NO extra AddToonOutline (that would double the border).
    public static class CscSprites
    {
        const string Root = "UI/Sprites/";
        static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        // Load a sprite by file base-name (e.g. "shape_slope"), cached. Returns
        // null if absent — callers keep their text label rather than crash.
        public static Sprite Get(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName)) return null;
            if (_cache.TryGetValue(spriteName, out Sprite s)) return s;
            s = Resources.Load<Sprite>(Root + spriteName);
            _cache[spriteName] = s;   // cache misses too, so a null never re-hits disk
            return s;
        }

        // The four armour-cube variants by MaterialRegistry index (0→A … 3→D).
        // Out-of-range clamps to A so a 5th material can never mis-index.
        public static Sprite CubeMaterial(int index)
        {
            char v = (char)('A' + Mathf.Clamp(index, 0, 3));
            return Get($"shape_cube_mat{v}");
        }

        // The tiled yellow/black hazard stripe (toolbar floor trim).
        public static Sprite Hazard() => Get("tile_hazard_stripe");

        // Maps a build shape to its glyph, keyed on the shape's stable
        // displayName; armour "Cube" additionally varies by armed material.
        // Unmapped → null (caller keeps the text label).
        public static Sprite ForShape(string shapeDisplayName, int armedMaterialIndex)
        {
            switch (shapeDisplayName)
            {
                case "Cube":     return CubeMaterial(armedMaterialIndex);
                case "Slope":    return Get("shape_slope");
                case "Pyramid":  return Get("shape_pyramid_mg");
                case "Cylinder": return Get("shape_cylinder_rocket");
                case "Laser":    return Get("shape_laser");
                case "Thruster": return Get("shape_thruster");
                case "Reactor":  return Get("shape_reactor");
                case "Shield":   return Get("shape_shield");
                default:         return null;
            }
        }
    }
}
```

- [ ] **Step 4: Compile.** `refresh_unity` (mode=force, scope=all — assets moved + new script needs its `.meta`) → poll `editor_state` → `read_console`. No C# errors; confirm `Assets/Scripts/Core/CscSprites.cs.meta` now exists.

- [ ] **Step 5: Commit.**
```bash
git -C "/Users/anon/My project" add Assets/Resources/UI/Sprites Assets/Scripts/Core/CscSprites.cs Assets/Scripts/Core/CscSprites.cs.meta
git -C "/Users/anon/My project" add -A Assets/Art/UI 2>/dev/null || true
git -C "/Users/anon/My project" commit -m "feat(ui): move build-shape sprites to Resources + CscSprites loader (B3c)" \
  -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: `UIStyle` — slot-decoration + selection helpers + dropdown branding

**Files:** Modify `Assets/Scripts/Core/UIStyle.cs`

- [ ] **Step 1: Add the two helpers.** Insert after `BuildLabeledButton` returns (anchor: its closing `            return (button, text);\n        }`):
```csharp
        // Restyles a BuildLabeledButton result into a brand toolbar slot: the
        // existing label drops to a small bottom caption, a number badge sits
        // top-left, and a centered glyph Image fills the middle. Returns the
        // glyph Image so callers can swap its sprite later (e.g. the cube slot
        // tracking the armed material). glyph == null leaves the image hidden.
        public static Image DecorateToolbarSlot(Button slot, Text label, Sprite glyph, string number, string caption)
        {
            int uiLayer = LayerMask.NameToLayer("UI");
            RectTransform slotRT = (RectTransform)slot.transform;

            // Bottom caption — reuse the label the button already carries.
            label.text = string.IsNullOrEmpty(caption) ? string.Empty : caption.ToUpperInvariant();
            label.font = CscTheme.CondOr;
            label.fontSize = 14;
            label.color = CscPalette.Sand100;
            label.alignment = TextAnchor.LowerCenter;
            RectTransform lrt = (RectTransform)label.transform;
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(1f, 0f);
            lrt.pivot = new Vector2(0.5f, 0f);
            lrt.offsetMin = new Vector2(2f, 4f);
            lrt.offsetMax = new Vector2(-2f, 18f);

            // Centered glyph.
            GameObject glyphGO = new GameObject("Glyph", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            glyphGO.transform.SetParent(slotRT, false);
            if (uiLayer >= 0) glyphGO.layer = uiLayer;
            RectTransform grt = (RectTransform)glyphGO.transform;
            grt.anchorMin = grt.anchorMax = grt.pivot = new Vector2(0.5f, 0.5f);
            grt.anchoredPosition = new Vector2(0f, 6f);   // nudged up to clear the caption
            grt.sizeDelta = new Vector2(40f, 40f);
            Image glyphImg = glyphGO.GetComponent<Image>();
            glyphImg.sprite = glyph;
            glyphImg.color = Color.white;
            glyphImg.preserveAspect = true;
            glyphImg.raycastTarget = false;
            glyphImg.enabled = glyph != null;

            // Number badge, top-left.
            GameObject badgeGO = new GameObject("Badge", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            badgeGO.transform.SetParent(slotRT, false);
            if (uiLayer >= 0) badgeGO.layer = uiLayer;
            RectTransform brt = (RectTransform)badgeGO.transform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0f, 1f);
            brt.anchoredPosition = new Vector2(4f, -4f);
            brt.sizeDelta = new Vector2(18f, 16f);
            Text badge = badgeGO.GetComponent<Text>();
            badge.font = CscTheme.CondOr;
            badge.fontSize = 13;
            badge.color = CscPalette.Steel300;
            badge.alignment = TextAnchor.UpperLeft;
            badge.raycastTarget = false;
            badge.text = number ?? string.Empty;

            return glyphImg;
        }

        // Adds a disabled ochre selection Outline (distinct from the ink toon
        // outline BuildLabeledButton already carries). Toggle .enabled to show
        // the "selected" ring. Returns it so the caller can keep the ref.
        public static Outline AddSelectionOutline(GameObject go)
        {
            Outline o = go.AddComponent<Outline>();
            o.effectColor = CscPalette.Ochre300;
            o.effectDistance = new Vector2(3f, -3f);
            o.enabled = false;
            return o;
        }
```

- [ ] **Step 2: Brand the dropdown root.** In `BuildDropdown`, after `rootImage.color = BackgroundIdle;` add the ink outline:
```csharp
            CscTheme.AddToonOutline(rootGO);
```

- [ ] **Step 3: Brand the dropdown option rows (ochre highlight).** In `BuildDropdown`, the item template sets `itemToggle.targetGraphic` / `.graphic` / `.isOn` (anchor: `            itemToggle.isOn = true;`). Immediately **before** `itemToggle.isOn = true;`, drive the row via a ColorBlock and let the ColorBlock own the tint (set the bg white so the tint reads cleanly):
```csharp
            itemBgGO.GetComponent<Image>().color = Color.white;
            ColorBlock itemColors = itemToggle.colors;
            itemColors.normalColor      = BackgroundIdle;
            itemColors.highlightedColor = CscPalette.Ochre300;   // hover row = ochre
            itemColors.pressedColor     = CscPalette.Ochre500;
            itemColors.selectedColor    = CscPalette.Ochre300;
            itemColors.disabledColor    = TintDisabled;
            itemColors.colorMultiplier  = 1f;
            itemColors.fadeDuration     = 0.1f;
            itemToggle.colors = itemColors;
```
(Note: `itemBgGO`'s color was previously set to `BackgroundIdle` a few lines up — this reassigns it to white so the ColorBlock's `normalColor` supplies the idle shade. `BuildDropdown` is shared with SettingsMenu, so this brands every dropdown consistently — verify SettingsMenu still reads fine at the gate; it should, same brand language.)

- [ ] **Step 4: Compile.** `refresh_unity` (force/scripts) → poll → `read_console`. No C# errors.

- [ ] **Step 5: Commit.**
```bash
git -C "/Users/anon/My project" add "Assets/Scripts/Core/UIStyle.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): toolbar-slot + selection-outline helpers, branded dropdown (B3c)" \
  -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: Top bar — panel + `CLASS` label (`BuildShipClassController`)

**Files:** Modify `Assets/Scripts/Build/BuildShipClassController.cs`

- [ ] **Step 1: Add a top-bar panel behind the class control.** In `BuildUI`, after `RectTransform root = BuildHud.Instance.Root;` insert:
```csharp
            // Cool near-black top bar spanning the screen, 2px ink bottom edge.
            GameObject barGO = new GameObject("TopBarPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            barGO.transform.SetParent(root, false);
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) barGO.layer = uiLayer;
            RectTransform barRT = (RectTransform)barGO.transform;
            barRT.anchorMin = new Vector2(0f, 1f);
            barRT.anchorMax = new Vector2(1f, 1f);
            barRT.pivot = new Vector2(0.5f, 1f);
            barRT.sizeDelta = new Vector2(0f, 64f);
            barRT.anchoredPosition = Vector2.zero;
            barGO.GetComponent<Image>().color = CscTheme.PanelFill;
            barGO.transform.SetAsFirstSibling();   // behind the label + dropdown

            GameObject inkGO = new GameObject("TopBarInk", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            inkGO.transform.SetParent(barGO.transform, false);
            if (uiLayer >= 0) inkGO.layer = uiLayer;
            RectTransform inkRT = (RectTransform)inkGO.transform;
            inkRT.anchorMin = new Vector2(0f, 0f);
            inkRT.anchorMax = new Vector2(1f, 0f);
            inkRT.pivot = new Vector2(0.5f, 1f);
            inkRT.sizeDelta = new Vector2(0f, 2f);
            inkRT.anchoredPosition = Vector2.zero;
            inkGO.GetComponent<Image>().color = CscPalette.Ink;
            inkGO.GetComponent<Image>().raycastTarget = false;
```

- [ ] **Step 2: Brand the `CLASS` label.** Replace the label build line
  `Text label = UIStyle.BuildLabel(root, "Class", fontSize: fontSize, style: FontStyle.Bold);`
  with:
```csharp
            Text label = UIStyle.BuildLabel(root, "CLASS", fontSize: fontSize, style: FontStyle.Bold, font: CscTheme.CondOr);
```
  and after `label.alignment = TextAnchor.MiddleLeft;` add:
```csharp
            label.color = CscPalette.Steel100;
```
  (The dropdown itself is already branded by Task 2 — no change needed here.)

- [ ] **Step 3: Compile.** `refresh_unity` (force/scripts) → poll → `read_console`. No C# errors.

- [ ] **Step 4: Commit.**
```bash
git -C "/Users/anon/My project" add "Assets/Scripts/Build/BuildShipClassController.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): branded build top bar + CLASS label (B3c)" \
  -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: Armour + Delete slots (`BuildToolbarController`)

**Files:** Modify `Assets/Scripts/Build/BuildToolbarController.cs`

- [ ] **Step 1: Add glyph + selection-outline arrays.** After the `Image[] _shapeSwatches;` field (anchor near `int[] _armourShapeIndices;`) add:
```csharp
        Image[] _shapeGlyphs;
        Outline[] _shapeSelectionOutlines;
        Outline _deleteSelectionOutline;
```

- [ ] **Step 2: Allocate them.** After `_shapeSwatches = new Image[totalShapes];` add:
```csharp
            _shapeGlyphs = new Image[totalShapes];
            _shapeSelectionOutlines = new Outline[totalShapes];
```

- [ ] **Step 3: Square up the slots.** At the top of `BuildToolbar`, right after `RectTransform root = BuildHud.Instance.Root;`, force square slots + tighter spacing (overrides the serialized 160×60 — intentional, keeps the change code-only):
```csharp
            buttonSize = new Vector2(72f, 72f);
            spacing = 8f;
```

- [ ] **Step 4: Decorate each armour slot.** In the armour loop, change the button build to capture the label and decorate it. Replace:
```csharp
                (Button btn, Text _) = UIStyle.BuildLabeledButton(root, label, buttonSize, fontSize);
```
  with:
```csharp
                (Button btn, Text lbl) = UIStyle.BuildLabeledButton(root, label, buttonSize, fontSize);
                int armedMat = buildManager != null ? buildManager.GetMaterialForShape(i) : 0;
                Sprite glyph = def != null ? CscSprites.ForShape(def.displayName, armedMat) : null;
                _shapeGlyphs[i] = UIStyle.DecorateToolbarSlot(btn, lbl, glyph, (a + 1).ToString(), label);
                _shapeSelectionOutlines[i] = UIStyle.AddSelectionOutline(btn.gameObject);
```
  Then, still in the loop, the existing corner swatch is now redundant (the glyph shows the material) — hide it. After `Image swatch = BuildCornerSwatch(rt);` add:
```csharp
                swatch.enabled = false;
```

- [ ] **Step 5: Style the Delete slot.** The delete build line already captures its label as `_ignored` (`(Button delBtn, Text _ignored) = UIStyle.BuildLabeledButton(root, deleteButtonLabel, buttonSize, fontSize);`). After `_deleteBackground = delBtn.GetComponent<Image>();` add a `DELETE` caption + a big red ✕ standing in for a glyph (there is no ✕ sprite):
```csharp
            _deleteSelectionOutline = UIStyle.AddSelectionOutline(delBtn.gameObject);
            UIStyle.DecorateToolbarSlot(delBtn, _ignored, null, null, "Delete");
            GameObject xGO = new GameObject("DeleteX", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            xGO.transform.SetParent(delBtn.transform, false);
            RectTransform xRT = (RectTransform)xGO.transform;
            xRT.anchorMin = xRT.anchorMax = xRT.pivot = new Vector2(0.5f, 0.5f);
            xRT.anchoredPosition = new Vector2(0f, 6f);
            xRT.sizeDelta = new Vector2(40f, 40f);
            Text xT = xGO.GetComponent<Text>();
            xT.font = CscTheme.DisplayOr;
            xT.fontSize = 34;
            xT.alignment = TextAnchor.MiddleCenter;
            xT.color = CscPalette.Critical;
            xT.raycastTarget = false;
            xT.text = "✕";
```
  (`✕` U+2715 renders via OS font fallback. The serialized `deleteSelectedColor` field is now unused — leave it to avoid a scene-compat change.)

- [ ] **Step 6: Swap selection from bg-recolour to ochre outline.** In `UpdateButtonStates`, replace the armour-slot loop body
```csharp
                    if (_shapeBackgrounds[i] == null) continue;
                    _shapeBackgrounds[i].color = (!deleteActive && !weaponActive && i == activeIdx)
                        ? SelectedTypeColor
                        : UIStyle.BackgroundIdle;
```
  with:
```csharp
                    if (_shapeBackgrounds[i] != null)
                        _shapeBackgrounds[i].color = CscTheme.CardFill;   // dark slot, always
                    if (_shapeSelectionOutlines != null && _shapeSelectionOutlines[i] != null)
                        _shapeSelectionOutlines[i].enabled = (!deleteActive && !weaponActive && i == activeIdx);
```
  and replace the delete-highlight block
```csharp
            if (_deleteBackground != null)
                _deleteBackground.color = deleteActive ? deleteSelectedColor : UIStyle.BackgroundIdle;
```
  with:
```csharp
            if (_deleteBackground != null)
                _deleteBackground.color = CscTheme.CardFill;
            if (_deleteSelectionOutline != null)
                _deleteSelectionOutline.enabled = deleteActive;
```

- [ ] **Step 7: Make the cube glyph track the armed material.** In `RefreshSwatchFor`, after the existing `_shapeSwatches[shapeIndex].color = …;` line add:
```csharp
            if (_shapeGlyphs != null && shapeIndex < _shapeGlyphs.Length && _shapeGlyphs[shapeIndex] != null)
            {
                ShapeDefinition sdef = buildManager.Shapes != null ? buildManager.Shapes.Get(shapeIndex) : null;
                Sprite g = sdef != null ? CscSprites.ForShape(sdef.displayName, mIdx) : null;
                if (g != null)
                {
                    _shapeGlyphs[shapeIndex].sprite = g;
                    _shapeGlyphs[shapeIndex].enabled = true;
                }
            }
```
  (`mIdx` is the local already computed in `RefreshSwatchFor` from `GetMaterialForShape`.)

- [ ] **Step 8: Compile.** `refresh_unity` (force/scripts) → poll → `read_console`. No C# errors.

- [ ] **Step 9: Commit.**
```bash
git -C "/Users/anon/My project" add "Assets/Scripts/Build/BuildToolbarController.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): brand armour + delete toolbar slots — glyphs, badges, ochre select (B3c)" \
  -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: Category button + flyout entries (`CategoryFlyout`)

**Files:** Modify `Assets/Scripts/Build/CategoryFlyout.cs`

- [ ] **Step 1: Migrate the local selection colours + add owned refs.** Replace the three static colour literals (`SelectedTypeColor`, `FlyoutEntryIdle`, `FlyoutEntryActive`) with brand tokens, and add glyph/outline fields next to `Image _swatch;`:
```csharp
        static readonly Color FlyoutEntryIdle   = CscPalette.HudCard;
        static readonly Color FlyoutEntryActive = CscPalette.Ochre300;
```
  (Delete the `SelectedTypeColor` literal — selection becomes an outline below.) Add fields (next to `Image _swatch;`):
```csharp
        Image _glyph;
        Text _btnLabel;
        Outline _selectionOutline;
```

- [ ] **Step 2: Decorate the category button.** In `BuildButton`, first capture the label — change
  `(Button btn, Text _) = UIStyle.BuildLabeledButton(canvas, _buttonLabel, _buttonSize, _fontSize);` to:
```csharp
            (Button btn, Text lbl) = UIStyle.BuildLabeledButton(canvas, _buttonLabel, _buttonSize, _fontSize);
            _btnLabel = lbl;
```
  Then, after `_background = btn.GetComponent<Image>();` (where `_button`/`_swatch` are already assigned), add — the category button shows the armed shape's glyph; no digit badge:
```csharp
            int armedShape = _lastArmedShapeIndex >= 0 ? _lastArmedShapeIndex : (_shapeIndices.Length > 0 ? _shapeIndices[0] : -1);
            ShapeDefinition armed = armedShape >= 0 && _buildManager.Shapes != null ? _buildManager.Shapes.Get(armedShape) : null;
            int armedMat = armed != null ? _buildManager.GetMaterialForShape(armedShape) : 0;
            Sprite g = armed != null ? CscSprites.ForShape(armed.displayName, armedMat) : null;
            _glyph = UIStyle.DecorateToolbarSlot(_button, _btnLabel, g, null, _buttonLabel);
            _selectionOutline = UIStyle.AddSelectionOutline(_button.gameObject);
            _swatch.enabled = false;   // glyph conveys the armed shape now
```

- [ ] **Step 3: Selection = ochre outline (not bg recolour).** In `RefreshButtonHighlight`, replace
  `_background.color = IsActiveCategory() ? SelectedTypeColor : UIStyle.BackgroundIdle;`
  with:
```csharp
            _background.color = CscTheme.CardFill;
            if (_selectionOutline != null) _selectionOutline.enabled = IsActiveCategory();
```

- [ ] **Step 4: Keep the category glyph current.** In `RefreshSwatch`, after `_swatch.color = wmat != null ? wmat.SwatchColor : Color.gray;` add:
```csharp
            if (_glyph != null && shape != null)
            {
                int mat = _buildManager.GetMaterialForShape(swatchShape);
                Sprite g = CscSprites.ForShape(shape.displayName, mat);
                if (g != null) { _glyph.sprite = g; _glyph.enabled = true; }
            }
```

- [ ] **Step 5: Glyphs on flyout entries.** In `BuildFlyout`, after `_buildEntrySwatch(brt, wmat != null ? wmat.SwatchColor : Color.gray);` add a small glyph on each entry (left of the text; the entry keeps its name + stat line):
```csharp
                Sprite eg = shape != null ? CscSprites.ForShape(shape.displayName, 0) : null;
                if (eg != null)
                {
                    GameObject egGO = new GameObject("EntryGlyph", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    egGO.transform.SetParent(brt, false);
                    RectTransform egRT = (RectTransform)egGO.transform;
                    egRT.anchorMin = egRT.anchorMax = new Vector2(0f, 0.5f);
                    egRT.pivot = new Vector2(0f, 0.5f);
                    egRT.anchoredPosition = new Vector2(6f, 0f);
                    egRT.sizeDelta = new Vector2(28f, 28f);
                    Image egImg = egGO.GetComponent<Image>();
                    egImg.sprite = eg; egImg.color = Color.white;
                    egImg.preserveAspect = true; egImg.raycastTarget = false;
                    RectTransform elrt = (RectTransform)label.transform;
                    elrt.offsetMin = new Vector2(40f, elrt.offsetMin.y);   // inset text to clear the glyph
                }
```

- [ ] **Step 6: Flyout entry idle/active already migrated** in Step 1 (`FlyoutEntryIdle`/`FlyoutEntryActive`) — `RefreshFlyoutHighlights` uses them unchanged.

- [ ] **Step 7: Compile.** `refresh_unity` (force/scripts) → poll → `read_console`. No C# errors.

- [ ] **Step 8: Commit.**
```bash
git -C "/Users/anon/My project" add "Assets/Scripts/Build/CategoryFlyout.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): brand category buttons + flyout entries with glyphs (B3c)" \
  -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: Material flyout + MASS/HP/Power readout (`BuildToolbarController`)

**Files:** Modify `Assets/Scripts/Build/BuildToolbarController.cs`

- [ ] **Step 1: Migrate the Power colour literals.** Replace the two local statics
  `static readonly Color PowerPositive = new Color(0.4f, 1f, 0.5f, 1f);` and
  `static readonly Color PowerNegative = new Color(1f, 0.4f, 0.35f, 1f);`
  with the brand tokens (identical values):
```csharp
        static readonly Color PowerPositive     = CscPalette.PowerPositive;
        static readonly Color PowerNegative     = CscPalette.PowerNegative;
```

- [ ] **Step 2: MASS/HP values emphasised + on-brand colours.** In `RefreshStatLabels`, replace:
```csharp
            if (_massLabel != null)
                _massLabel.text = $"Mass: {mass:F1} / {buildManager.MassLimit:F0}";
            if (_hpLabel != null)
                _hpLabel.text = $"HP: {hp:F0}";
```
  with (a big `<size>` value span — one uGUI Text can't mix fonts, so emphasis is via size):
```csharp
            if (_massLabel != null)
                _massLabel.text = $"MASS <size={statFontSize + 12}>{mass:F0}</size> / {buildManager.MassLimit:F0}";
            if (_hpLabel != null)
            {
                _hpLabel.text = $"HP <size={statFontSize + 12}>{hp:F0}</size>";
                _hpLabel.color = CscPalette.PowerPositive;
            }
```
  Then brand the stat labels at build time. After the `_massLabel = UIStyle.BuildLabel(…)` line add:
```csharp
            _massLabel.font = CscTheme.CondOr;
            _massLabel.supportRichText = true;
            _massLabel.color = CscPalette.Sand100;
```
  after the `_hpLabel = UIStyle.BuildLabel(…)` line add:
```csharp
            _hpLabel.font = CscTheme.CondOr;
            _hpLabel.supportRichText = true;
            _hpLabel.color = CscPalette.PowerPositive;
```
  and after the `_powerLabel = UIStyle.BuildLabel(…)` line add `_powerLabel.font = CscTheme.CondOr;` (its colour is set per-sign in `RefreshStatLabels`).

- [ ] **Step 3: Restyle the material-flyout entries.** In `BuildFlyout` (the armour A–D flyout), the entries are `BuildLabeledButton` + `BuildEntrySwatch`. They already inherit B2's ink outline + Condensed font, and the swatch shows `mdef.SwatchColor`. Add an ink outline is already present; ensure the entry text colour is on-brand — after `label.alignment = TextAnchor.MiddleLeft;` add:
```csharp
                label.color = CscPalette.Sand100;
```

- [ ] **Step 4: Compile.** `refresh_unity` (force/scripts) → poll → `read_console`. No C# errors.

- [ ] **Step 5: Commit.**
```bash
git -C "/Users/anon/My project" add "Assets/Scripts/Build/BuildToolbarController.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): brand MASS/HP/Power readout + material flyout entries (B3c)" \
  -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 7: Hazard-stripe floor trim (`BuildToolbarController`)

**Files:** Modify `Assets/Scripts/Build/BuildToolbarController.cs`

- [ ] **Step 1: Add a tiled hazard strip behind the toolbar.** At the top of `BuildToolbar`, right after the `buttonSize`/`spacing` override from Task 4 Step 3, add:
```csharp
            // Hazard-stripe trim along the floor, behind the toolbar slots.
            GameObject hazGO = new GameObject("HazardTrim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            hazGO.transform.SetParent(root, false);
            int hazLayer = LayerMask.NameToLayer("UI");
            if (hazLayer >= 0) hazGO.layer = hazLayer;
            RectTransform hazRT = (RectTransform)hazGO.transform;
            hazRT.anchorMin = new Vector2(0f, 0f);
            hazRT.anchorMax = new Vector2(1f, 0f);
            hazRT.pivot = new Vector2(0.5f, 0f);
            hazRT.sizeDelta = new Vector2(0f, 28f);
            hazRT.anchoredPosition = Vector2.zero;
            Image haz = hazGO.GetComponent<Image>();
            haz.sprite = CscSprites.Hazard();
            haz.type = Image.Type.Tiled;
            haz.raycastTarget = false;
            haz.enabled = haz.sprite != null;
            hazGO.transform.SetAsFirstSibling();   // behind the slots
```

- [ ] **Step 2: Compile.** `refresh_unity` (force/scripts) → poll → `read_console`. No C# errors.

- [ ] **Step 3: Commit.**
```bash
git -C "/Users/anon/My project" add "Assets/Scripts/Build/BuildToolbarController.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): hazard-stripe floor trim on the build toolbar (B3c)" \
  -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 8: Verify + present for the gate

- [ ] **Step 1:** `read_console` (error) → clean (MCP-infra only).
- [ ] **Step 2:** Present for the maintainer's Play-mode gate against `hangar-build.png`:
  top bar (cool panel, 2px ink, `CLASS` + branded dropdown with ochre option rows);
  toolbar slots (dark, ink border, centered glyph, number badge on Cube/Slope, bottom
  caption, ochre outline on the selected slot); Cube glyph tracks the armed material
  (A–D); Weapons/Utilities category buttons show the armed shape's glyph and open their
  flyouts (entries carry glyphs); Delete shows the red ✕; hazard-stripe trim along the
  floor; MASS/HP (green)/Power on-brand. Confirm the class dropdown still changes class
  and SettingsMenu dropdowns still read fine. **Hold the push**; on confirm, push onto
  `explore/ui-rebrand` + dispatch the B3c internal `superpowers:code-reviewer`. Merge to
  `main` = hard gate (explicit sign-off).

---

## Self-review

- **Spec coverage:** sprite pipeline — Resources move + `CscSprites` + hazard wrap (T1) ✓; top-bar panel + ink + `CLASS` + branded dropdown (T2/T3) ✓; toolbar slots restyled with glyph/badge/caption + ochre selection outline + cube-material glyph swap + Delete ✕ (T4) ✓; category buttons + flyout-entry glyphs, local colour migration (T5) ✓; material flyout restyle + MASS(Anton)/HP/Power(PowerPositive) readout (T6) ✓; hazard trim (T7) ✓; out-of-scope items (FLY!/HANGAR, speed, 3D grid, dropdown-not-tabs) correctly untouched ✓; verify + gate (T8) ✓.
- **Placeholders:** none — new files/helpers are full code; every edit gives the exact anchor + replacement. Two judgement notes are called out inline (Anton `<b>` fallback in T6; `✕` OS fallback in T4) with concrete fallbacks, not TBDs.
- **Type/name consistency:** `CscSprites.ForShape/CubeMaterial/Hazard/Get`, `UIStyle.DecorateToolbarSlot/AddSelectionOutline`, `CscTheme.CardFill/PanelFill/CondOr/AddToonOutline`, `CscPalette.Ochre300/Ochre500/Steel100/Steel300/Sand100/Ink/Critical/PowerPositive/PowerNegative` — all real members (confirmed in the files). Local names (`root`, `btn`, `lbl`, `def`, `label`, `_ignored`, `mIdx`, `swatchShape`, `shape`, `wmat`, `_shapeSwatches`, `_shapeBackgrounds`, `_deleteBackground`, `_button`, `_background`, `_swatch`, `_lastArmedShapeIndex`) match the current files.
- **Risk checks:** material count vs 4 cube sprites → `CubeMaterial` clamps 0–3 ✓; hazard wrap set in T1S2 ✓; new panels use `SetAsFirstSibling` so they sit behind existing widgets without disturbing anchoring ✓; glyph mapping falls back to text label on any unmapped shape / missing sprite (`ForShape`/`Get` return null, `DecorateToolbarSlot` hides the empty glyph) ✓; dropdown branding is global (shared `BuildDropdown`) → flagged for SettingsMenu re-check at the gate ✓.
