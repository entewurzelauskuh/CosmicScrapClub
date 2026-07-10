# UI Rebrand — B3-polish (Play-check Batch) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (inline — live-Unity-Editor work, single instance, sequential compile order). Steps use checkbox (`- [ ]`) syntax.

**Goal:** Land 11 grouped play-check fixes — all-caps display roles, ochre/danger button variants, pointer-down press-stamp, letter-spacing, brighter base color, rounded cards, AA hazard band, delete hover-red, confirm restructure, toolbar margin, FLY! resize+orange, rotate-hint reposition.

**Architecture:** Shared-layer changes in `CscPalette`/`UIStyle` + two Core components (`LetterSpacing`, reworked `UIClickBounce`) ripple to every scene; each scene file is then touched once for its scene-specific bits. Legacy `UnityEngine.UI` (no TMP); colors from `CscPalette`/`CscTheme`.

**Tech Stack:** Unity 6.3 LTS, C# (`Assembly-CSharp`), legacy `UnityEngine.UI`, UnityMCP for compile verification.

**Verification model:** No automated tests. After each task: `refresh_unity` (force/scripts; force/all when a new file lands) → poll `mcpforunity://editor/state` until `ready_for_tools` → `read_console` (error) clean (MCP-infra noise ignored). Final visual match is the maintainer's Play-check (headless MCP Play frozen at frame 1).

**Source of truth:** spec `docs/superpowers/specs/2026-07-09-ui-rebrand-b3-polish-design.md`. Bundles with the held B3c push.

**Note on edits:** anchors are verbatim from the current files, but prior work shifts line numbers — **Read the method first, then Edit** against the exact current text. All Core/Build/Menu scripts already `using CubeFly.Core;` / `using UnityEngine.UI;`.

---

### Task 0: Pre-flight

- [ ] Confirm branch `explore/ui-rebrand`; one live instance; `read_console` (error) clean (MCP-infra only).

---

### Task 1: `CscPalette` — brighter base color

**Files:** Modify `Assets/Scripts/Core/CscPalette.cs`

- [ ] **Step 1: Point 6.** Replace the `BackgroundIdle` line:
```csharp
        public static readonly Color BackgroundIdle     = new Color(0.16f, 0.16f, 0.22f, 0.9f);  // button/control idle fill (lightened ~20% for hover contrast)
```
  with:
```csharp
        public static readonly Color BackgroundIdle     = new Color(0.165f, 0.137f, 0.173f, 0.9f);  // #2a232c ghost button/control idle fill
```

- [ ] **Step 2: Compile.** `refresh_unity` (force/scripts) → poll → `read_console`. No C# errors.
- [ ] **Step 3: Commit.**
```bash
git -C "/Users/anon/My project" add "Assets/Scripts/Core/CscPalette.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): brighter #2a232c button base (B3-polish)" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Core components — `LetterSpacing` (new) + `UIClickBounce` → press-stamp

**Files:**
- Create: `Assets/Scripts/Core/LetterSpacing.cs`
- Modify: `Assets/Scripts/Core/UIClickBounce.cs` (full rework)

- [ ] **Step 1: Create `LetterSpacing.cs`.** (Point 5 — legacy `Text` has no native spacing.)
```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CubeFly.Core
{
    // Per-character horizontal spacing for legacy uGUI Text (which, unlike
    // TextMeshPro, has no letter-spacing property). A BaseMeshEffect that shifts
    // each glyph quad right by an accumulating offset, reset per line and
    // recentred so the Text's own alignment is preserved. Spacing is pixels
    // added per character gap. This is the Unity-typical approach.
    [RequireComponent(typeof(Text))]
    public class LetterSpacing : BaseMeshEffect
    {
        [SerializeField] float _spacing;
        static readonly List<UIVertex> _verts = new List<UIVertex>();

        public float Spacing
        {
            get => _spacing;
            set { _spacing = value; if (graphic != null) graphic.SetVerticesDirty(); }
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || _spacing == 0f) return;
            Text text = GetComponent<Text>();
            if (text == null) return;

            vh.GetUIVertexStream(_verts);
            int glyphs = _verts.Count / 6;   // 6 verts (two triangles) per glyph
            if (glyphs <= 1) return;

            // Split glyphs into lines by their top-vertex y.
            List<int> lineCounts = new List<int>();
            float prevTop = float.NaN;
            int run = 0;
            for (int g = 0; g < glyphs; g++)
            {
                float top = _verts[g * 6].position.y;
                if (!float.IsNaN(prevTop) && Mathf.Abs(top - prevTop) > 0.1f)
                {
                    lineCounts.Add(run);
                    run = 0;
                }
                prevTop = top;
                run++;
            }
            lineCounts.Add(run);

            TextAnchor a = text.alignment;
            bool centre = a == TextAnchor.LowerCenter || a == TextAnchor.MiddleCenter || a == TextAnchor.UpperCenter;
            bool right  = a == TextAnchor.LowerRight  || a == TextAnchor.MiddleRight  || a == TextAnchor.UpperRight;

            int gi = 0;
            for (int line = 0; line < lineCounts.Count; line++)
            {
                int n = lineCounts[line];
                float total = (n - 1) * _spacing;
                float align = centre ? -total * 0.5f : right ? -total : 0f;
                for (int k = 0; k < n; k++, gi++)
                {
                    float dx = align + k * _spacing;
                    int b = gi * 6;
                    for (int v = 0; v < 6; v++)
                    {
                        UIVertex vert = _verts[b + v];
                        vert.position.x += dx;
                        _verts[b + v] = vert;
                    }
                }
            }

            vh.Clear();
            vh.AddUIVertexTriangleStream(_verts);
        }
    }
}
```

- [ ] **Step 2: Rework `UIClickBounce.cs`** into a pointer-down press-stamp (Point 4). Replace the entire file body (keep the class name — `UIStyle.BuildLabeledButton`'s `bounce` param still opts in):
```csharp
using UnityEngine;
using UnityEngine.EventSystems;

namespace CubeFly.Core
{
    // Press feedback for menu / hangar buttons: on pointer-DOWN the button
    // translates into its toon shadow (the BRAND "stamp"), and restores on
    // pointer-up / exit. Reads the sibling Shadow's offset (added by the caller
    // after this component) lazily on first press so it lands exactly on the
    // shadow; falls back to (6,-6). Additive alongside the Button's own onClick.
    [RequireComponent(typeof(RectTransform))]
    public class UIClickBounce : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        RectTransform _rt;
        Vector2 _rest;
        Vector2 _stamp = new Vector2(6f, -6f);
        bool _stampResolved;
        bool _pressed;

        void Awake() => _rt = (RectTransform)transform;

        void OnDisable()
        {
            // Only restore if we're mid-press — otherwise _rest is unset and
            // we'd yank the button to (0,0).
            if (_pressed) { _rt.anchoredPosition = _rest; _pressed = false; }
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || _pressed) return;
            if (!_stampResolved)
            {
                Shadow sh = GetComponent<Shadow>();
                if (sh != null) _stamp = sh.effectDistance;
                _stampResolved = true;
            }
            _rest = _rt.anchoredPosition;
            _rt.anchoredPosition = _rest + _stamp;
            _pressed = true;
        }

        public void OnPointerUp(PointerEventData e) => Release();
        public void OnPointerExit(PointerEventData e) => Release();

        void Release()
        {
            if (!_pressed) return;
            _pressed = false;
            _rt.anchoredPosition = _rest;
        }
    }
}
```

- [ ] **Step 3: Compile.** `refresh_unity` (force/all — new `LetterSpacing.cs` needs its `.meta`) → poll → `read_console`. No C# errors; confirm `Assets/Scripts/Core/LetterSpacing.cs.meta` exists.
- [ ] **Step 4: Commit.**
```bash
git -C "/Users/anon/My project" add "Assets/Scripts/Core/LetterSpacing.cs" "Assets/Scripts/Core/LetterSpacing.cs.meta" "Assets/Scripts/Core/UIClickBounce.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): LetterSpacing mesh effect + press-stamp on pointer-down (B3-polish)" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: `UIStyle` — uppercase, ButtonKind, letter-spacing helper, rounded + hazard sprites

**Files:** Modify `Assets/Scripts/Core/UIStyle.cs`

- [ ] **Step 1: Add the `ButtonKind` enum.** At the top of the `UIStyle` class body (after the opening `public static class UIStyle` `{` / the first field block — anchor on `public static readonly Color BackgroundIdle` usage is elsewhere; place it just above `BuildLabeledButton`'s summary comment `// Builds a Button + label as a child of parent.`):
```csharp
        public enum ButtonKind { Ghost, Primary, Danger }
```

- [ ] **Step 2: `BuildLabeledButton` — kind + uppercase + letter-spacing.** Change the signature
  `public static (Button button, Text label) BuildLabeledButton(Transform parent, string labelText, Vector2 size, int fontSize = 28, Font font = null, bool bounce = false)`
  to add `, ButtonKind kind = ButtonKind.Ghost`. Then:
  - After `bImage.color = BackgroundIdle;` add:
```csharp
            if (kind == ButtonKind.Primary) bImage.color = CscTheme.PrimaryFill;
            else if (kind == ButtonKind.Danger) bImage.color = CscTheme.DangerFill;
```
  - Change the label text line `text.text = labelText;` to uppercase + kind color + spacing:
```csharp
            text.text = labelText.ToUpperInvariant();
            text.color = kind == ButtonKind.Primary ? CscTheme.TextOnLight
                       : kind == ButtonKind.Danger  ? Color.white
                       : LabelColor;
            ApplyLetterSpacing(text, fontSize * 0.05f);
```
  (Replaces the prior single `text.color = LabelColor;` / `text.text = labelText;` lines — Read the method to confirm the exact current lines and fold these in.)

- [ ] **Step 3: `BuildLabel` — `upper` param.** Change the signature
  `public static Text BuildLabel(Transform parent, string text, int fontSize, FontStyle style = FontStyle.Normal, Font font = null)`
  to add `, bool upper = false`, and change `t.text = text;` to:
```csharp
            t.text = upper ? text.ToUpperInvariant() : text;
```

- [ ] **Step 4: `ApplyLetterSpacing` helper.** Add near the sprite helpers:
```csharp
        // Adds/updates a LetterSpacing mesh effect on a legacy Text (pixels per gap).
        public static void ApplyLetterSpacing(Text t, float spacing)
        {
            if (t == null) return;
            LetterSpacing ls = t.GetComponent<LetterSpacing>();
            if (ls == null) ls = t.gameObject.AddComponent<LetterSpacing>();
            ls.Spacing = spacing;
        }
```

- [ ] **Step 5: `MakeRoundedPlate` sprite.** Add after `MakePlateSprite`:
```csharp
        // A rounded-rect brand plate: fill + ink border, feathered (AA) edges +
        // corners via a signed-distance field. For slot cards. Cache at the call site.
        public static Sprite MakeRoundedPlate(int w, int h, int radius, int border, Color fill, Color ink)
        {
            Color[] px = new Color[w * h];
            const float feather = 1.5f;
            float halfW = w / 2f, halfH = h / 2f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - halfW) - (halfW - radius);
                    float qy = Mathf.Abs(y + 0.5f - halfH) - (halfH - radius);
                    float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
                    float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
                    float edge = radius - (outside + inside);   // inward distance from the rounded boundary
                    Color c = Color.Lerp(ink, fill, Mathf.Clamp01((edge - border) / feather));
                    c.a = Mathf.Clamp01(edge / feather);
                    px[y * w + x] = c;
                }
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            t.SetPixels(px);
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }
```

- [ ] **Step 6: `MakeHazardStripe` sprite.** Add after `MakeRoundedPlate`:
```csharp
        // Anti-aliased diagonal hazard stripes (feathered edges, bilinear) — the
        // same AA treatment as the title plate, replacing the point-filtered tile.
        // Tile it (Image.Type.Tiled) with Repeat wrap.
        public static Sprite MakeHazardStripe(int size, float stripe, Color a, Color b)
        {
            Color[] px = new Color[size * size];
            const float feather = 1.2f;
            Color mid = Color.Lerp(a, b, 0.5f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float m = Mathf.Repeat(x + y, 2f * stripe);
                    float f = Mathf.Repeat(m, stripe);
                    float distEdge = Mathf.Min(f, stripe - f);
                    Color band = m < stripe ? a : b;
                    float k = Mathf.Clamp01(distEdge / feather);   // 0 at a band edge → 1 inside
                    px[y * size + x] = Color.Lerp(mid, band, k);
                }
            Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            t.SetPixels(px);
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
```

- [ ] **Step 7: Compile.** `refresh_unity` (force/scripts) → poll → `read_console`. No C# errors.
- [ ] **Step 8: Commit.**
```bash
git -C "/Users/anon/My project" add "Assets/Scripts/Core/UIStyle.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): uppercase + ButtonKind variants + letter-spacing helper + rounded/hazard sprites (B3-polish)" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: `MainMenuController` — ochre Hangar + stronger shadow

**Files:** Modify `Assets/Scripts/MainMenu/MainMenuController.cs`

- [ ] **Step 1: Hangar primary (2a) + shadow 8f (3a).** `CreateMenuButton` currently builds every button the same and adds `AddToonShadow(button.gameObject, 6f)`. Add a `ButtonKind` param and bump the shadow. Change the method signature
  `static void CreateMenuButton(RectTransform parent, string text, Vector2 anchoredPos, UnityEngine.Events.UnityAction onClick)`
  to add `, UIStyle.ButtonKind kind = UIStyle.ButtonKind.Ghost`; pass `kind` into the `BuildLabeledButton(... bounce: true)` call (add `, kind: kind`); and change `CscTheme.AddToonShadow(button.gameObject, 6f);` to `8f`.
- [ ] **Step 2:** At the Hangar call site (`CreateMenuButton(root, "Hangar", new Vector2(0f, -40f), OnHangar);`) add `, UIStyle.ButtonKind.Primary`.
- [ ] **Step 3: Compile + Commit.** `refresh_unity` (force/scripts) → poll → `read_console` clean.
```bash
git -C "/Users/anon/My project" add "Assets/Scripts/MainMenu/MainMenuController.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): ochre Hangar CTA + wordmark-strength button shadow (B3-polish)" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: `HangarSelectController` — primary/danger buttons, hover-red delete, rounded cards, confirm restructure, title case+spacing

**Files:** Modify `Assets/Scripts/HangarSelect/HangarSelectController.cs`

- [ ] **Step 1: Rounded card (3b).** In `BuildCard`, the card bg is a plain `Image` with `bg.color = CscTheme.CardFill` + `AddToonOutline(rootGO, 3f)`. Replace the fill+outline with a cached rounded-plate sprite (keep the `AddToonShadow`). Add a static field near the other card statics:
```csharp
        static Sprite _cardSprite;
```
  Replace `bg.color = CscTheme.CardFill;` with:
```csharp
            if (_cardSprite == null)
                _cardSprite = UIStyle.MakeRoundedPlate((int)cardSize.x, (int)cardSize.y, 14, 3, CscTheme.CardFill, CscPalette.Ink);
            bg.sprite = _cardSprite;
            bg.color = Color.white;
            bg.type = Image.Type.Simple;
```
  and **remove** the `CscTheme.AddToonOutline(rootGO, 3f);` line (the sprite bakes the ink border). Keep `CscTheme.AddToonShadow(rootGO, 6f);`.

- [ ] **Step 2: Slot title case + spacing (5b).** At the card title build
  `Text title = UIStyle.BuildLabel(rt, $"Slot {slot + 1}", fontSize: 32, style: FontStyle.Bold, font: CscTheme.DisplayOr);`
  add `, upper: true` (→ "SLOT N"), and after `title.color = CscPalette.Ochre300;` add:
```csharp
            UIStyle.ApplyLetterSpacing(title, 32f * 0.05f);
```

- [ ] **Step 3: Primary Continue/Start-new (2b).** The primary button is built via `UIStyle.BuildLabeledButton(rt, "—", new Vector2(cardSize.x - 80f, 56f), fontSize: 26, bounce: true);`. Add `, kind: UIStyle.ButtonKind.Primary`.

- [ ] **Step 4: Delete → full width (7a).** Change the delete button build
  `(Button del, Text delLabel) = UIStyle.BuildLabeledButton(rt, "Delete", new Vector2((cardSize.x - 80f) / 2f - 4f, 44f), fontSize: 20, bounce: true);`
  to full width: `new Vector2(cardSize.x - 80f, 44f)`, and centre it — change its `delRT.anchoredPosition = new Vector2(-(cardSize.x - 80f) / 4f - 2f, 24f);` to `new Vector2(0f, 24f);`.

- [ ] **Step 5: Delete hover-red (2c).** After the delete button is built (`card.DeleteButton = del;` / `card.DeleteLabel = delLabel;`), add a pointer enter/exit recolor via `EventTrigger` (matching the codebase's toolbar pattern):
```csharp
            Outline delOutline = del.GetComponent<Outline>();
            Color delRestText = delLabel.color;
            EventTrigger delTrig = del.gameObject.AddComponent<EventTrigger>();
            EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => { if (delOutline != null) delOutline.effectColor = CscPalette.Critical; delLabel.color = CscPalette.Critical; });
            EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => { if (delOutline != null) delOutline.effectColor = CscTheme.OutlineColor; delLabel.color = delRestText; });
            delTrig.triggers.Add(enter);
            delTrig.triggers.Add(exit);
```
  (Requires `using UnityEngine.EventSystems;` — add it to the file's usings if absent.)

- [ ] **Step 6: Confirm pair — gap + danger + resize (7b/7c).** Two parts:
  - In `EnterDeleteConfirm(card)`, before showing Cancel, shrink Delete to the left half and recolor it danger; keep a gap. After `card.DeleteConfirming = true;` add:
```csharp
            RectTransform delRT2 = (RectTransform)card.DeleteButton.transform;
            float half = (cardSizeX - 80f) / 2f - 6f;   // 12px gap total
            delRT2.sizeDelta = new Vector2(half, 44f);
            delRT2.anchoredPosition = new Vector2(-(half / 2f + 6f), 24f);
            card.DeleteButton.image.color = CscTheme.DangerFill;
            card.DeleteLabel.color = Color.white;
```
  - In `CancelDeleteConfirm(card)`, restore Delete to full width + ghost. After `card.DeleteConfirming = false;` add:
```csharp
            RectTransform delRTr = (RectTransform)card.DeleteButton.transform;
            delRTr.sizeDelta = new Vector2(cardSizeX - 80f, 44f);
            delRTr.anchoredPosition = new Vector2(0f, 24f);
            card.DeleteButton.image.color = UIStyle.BackgroundIdle;
            card.DeleteLabel.color = CscPalette.Sand100;   // restored ghost label
```
  - The Cancel button (right half) is created in `BuildCard`; set its width to the same `half` and position it right, with the gap. Change the Cancel build size `new Vector2((cardSize.x - 80f) / 2f - 4f, 44f)` to `new Vector2((cardSize.x - 80f) / 2f - 6f, 44f)` and its anchoredPosition `new Vector2((cardSize.x - 80f) / 4f + 2f, 24f)` to `new Vector2((cardSize.x - 80f) / 4f + 3f, 24f)` (nudge to open the gap symmetrically).
  - Add a `cardSizeX` field the confirm methods can read (they don't currently have `cardSize` in scope). At `BuildCard`, after `SlotCard card = ...`/where `cardSize` is defined, store `card.CardWidth = cardSize.x;` (add `public float CardWidth;` to the `SlotCard` class); and in the confirm methods use `card.CardWidth` in place of `cardSizeX` above.

  > Note: read `BuildCard`, `EnterDeleteConfirm`, `CancelDeleteConfirm`, and the `SlotCard` class first; wire `card.CardWidth` and fold the resize blocks in against the exact current lines.

- [ ] **Step 7: Title of the scene (all-caps).** The scene header `Text title = UIStyle.BuildLabel(root, "Choose a Slot", fontSize: 72, style: FontStyle.Bold, font: CscTheme.DisplayOr);` → add `, upper: true`.

- [ ] **Step 8: Compile + Commit.** `refresh_unity` (force/scripts) → poll → `read_console` clean.
```bash
git -C "/Users/anon/My project" add "Assets/Scripts/HangarSelect/HangarSelectController.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): slot picker — ochre CTAs, hover-red delete, rounded cards, confirm gap+danger, caps titles (B3-polish)" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: `BuildToolbarController` — AA hazard band, toolbar margin, rotate-hint reposition

**Files:** Modify `Assets/Scripts/Build/BuildToolbarController.cs`

- [ ] **Step 1: AA hazard band (8).** The `HazardTrim` Image uses `CscSprites.Hazard()` (point-filtered PNG). Replace its sprite with a cached procedural AA stripe. Add a static field near the class top:
```csharp
        static Sprite _hazardSprite;
```
  and change `haz.sprite = CscSprites.Hazard();` to:
```csharp
            if (_hazardSprite == null)
                _hazardSprite = UIStyle.MakeHazardStripe(64, 10f, CscPalette.HazardYellow, CscPalette.HazardStripe);
            haz.sprite = _hazardSprite;
```

- [ ] **Step 2: Toolbar margin above the band (9).** The hazard band is 28px tall at the very bottom; the slots sit at `bottomMargin` (serialized 30). Raise the slots clear of the band: at the top of `BuildToolbar`, where `buttonSize`/`spacing` are overridden, also set:
```csharp
            bottomMargin = 44f;   // lift the toolbar clear of the 28px hazard band
```
  (Add this line right after `spacing = 8f;`.)

- [ ] **Step 3: Rotate hint below the top bar (11).** The `hint` uses the serialized `hintAnchoredPosition` (default `(20,-20)`), overlapping the 64px top bar. After the hint build (`hrt.anchoredPosition = hintAnchoredPosition;`), override the Y:
```csharp
            hrt.anchoredPosition = new Vector2(hintAnchoredPosition.x, -80f);   // below the 64px top bar
```
  (Replace the existing `hrt.anchoredPosition = hintAnchoredPosition;` line.)

- [ ] **Step 4: Compile + Commit.** `refresh_unity` (force/scripts) → poll → `read_console` clean.
```bash
git -C "/Users/anon/My project" add "Assets/Scripts/Build/BuildToolbarController.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): AA hazard band + toolbar margin + rotate-hint below top bar (B3-polish)" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 7: `UIManager` — FLY! button size + orange-red

**Files:** Modify `Assets/Scripts/Core/UIManager.cs`

- [ ] **Step 1: Size + color (10a/10b).** The button is built `(Button button, Text label) = UIStyle.BuildLabeledButton(PersistentHud.Instance.Root, "Fly!", new Vector2(220f, 64f), fontSize: 28);` with `brt.anchoredPosition = new Vector2(-20f, -20f);`. Change the size to fit the 64px bar with margins and recolor:
  - Size `new Vector2(220f, 64f)` → `new Vector2(200f, 44f)`.
  - `anchoredPosition` `new Vector2(-20f, -20f)` → `new Vector2(-20f, -10f)` (10px top margin; a 44px button in the 64px bar leaves ~10px bottom margin too).
  - After `_buttonLabel = label;` (or wherever the button `Image` is reachable) add an explicit orange-red recolor + white text (Orange600 isn't a semantic role, so recolor directly):
```csharp
            button.image.color = CscPalette.Orange600;
            label.color = Color.white;
```

- [ ] **Step 2: Compile + Commit.** `refresh_unity` (force/scripts) → poll → `read_console` clean.
```bash
git -C "/Users/anon/My project" add "Assets/Scripts/Core/UIManager.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): FLY! fits the top bar + orange-red CTA (B3-polish)" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 8: Verify + present for the gate

- [ ] **Step 1:** `read_console` (error) → clean (MCP-infra only).
- [ ] **Step 2:** Present for the maintainer's Play-mode gate (all three surfaces per §5 acceptance). **Hold the push**; dispatch the internal `superpowers:code-reviewer` on the B3-polish diff. On confirm, bundle-push the whole B3c + B3-polish chain onto `explore/ui-rebrand`. Merge to `main` = hard gate (explicit sign-off); PR #56 coordination first.

---

## Self-review

- **Spec coverage:** (1) uppercase → T3S2 (buttons) + `upper` param at title sites T3S3/T5S2/T5S7/T4 ✓; (2a) T4 ✓; (2b) T5S3 ✓; (2c) T5S5 ✓; (3a) T4 ✓; (3b) T5S1 ✓; (4) T2S2 ✓; (5) T2S1 + T3S2/S4 + T5S2 ✓; (6) T1 ✓; (7a) T5S4 ✓; (7b) T5S6 ✓; (7c) T5S6 ✓; (8) T6S1 ✓; (9) T6S2 ✓; (10a) T7 ✓; (10b) T7 ✓; (11) T6S3 ✓. All 11 points mapped.
- **Placeholders:** new files/methods are full code; edits give exact anchors + code. T5S6 (confirm resize) carries a "read first, wire `card.CardWidth`" instruction — the resize/colors are concrete; only the field wiring is read-then-edit, not a vague TODO.
- **Type/name consistency:** `UIStyle.ButtonKind{Ghost,Primary,Danger}`, `ApplyLetterSpacing`, `MakeRoundedPlate`, `MakeHazardStripe`, `LetterSpacing.Spacing`, `CscTheme.PrimaryFill/DangerFill/TextOnLight/OutlineColor`, `CscPalette.Ochre300/Orange600/Critical/Ink/HazardYellow/HazardStripe/Sand100/BackgroundIdle` — all real / defined in this plan. `UIClickBounce` keeps its name + `bounce` param (no call-site churn). MainMenu `CreateMenuButton` gains a `kind` param (default Ghost → other call sites unaffected).
- **Risks flagged in spec §6:** letter-spacing multiplier tuning, press-stamp vs ColorTint compose, delete hover-red restore, rounded-sprite radius, confirm resize desync, FLY! Build-only — all carried into the tasks above.
