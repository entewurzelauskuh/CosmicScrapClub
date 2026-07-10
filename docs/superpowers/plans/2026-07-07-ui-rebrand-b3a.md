# Milestone B3a — MainMenu Full-Match Restyle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (inline — live-Unity-Editor work, single instance, sequential compile order). Steps use checkbox (`- [ ]`) syntax.

**Goal:** Restyle MainMenu to `hangar-main-menu.png` — warm procedural gradient background, the composite Cosmic Scrap Club wordmark, and ink-outlined + toon-shadowed buttons — landing the reusable `AddToonShadow` + `BuildBrandBackground` primitives.

**Architecture:** Add `CscTheme.AddToonShadow` (hard Ink `Shadow`) and harden `AddToonOutline`; add `UIStyle.BuildBrandBackground` that draws a **procedurally-generated** radial gradient + faint diagonal overlay via `RawImage` (no asset, no Resources — the static builder needs nothing external); rewrite `MainMenuController.BuildUI` to lay down the background, a rotated wordmark plate with three brand-font lines, and toon-shadowed buttons.

**Tech Stack:** Unity 6.3 LTS, C# (`Assembly-CSharp`), legacy `UnityEngine.UI` (`RawImage`/`Image`/`Text`/`Outline`/`Shadow`), UnityMCP for compile verification.

**Verification model:** No automated tests. Each task verifies via `read_console` (clean compile). All *visual* values (gradient colors, plate size/rotation, font sizes, positions, overlay alpha) are a best-effort **first take** — the maintainer's Play-check is where we see the render and tune. Headless MCP Play is frozen at frame 1.

**Source of truth:** spec `docs/superpowers/specs/2026-07-07-ui-rebrand-b3a-design.md`; mockup `unity_handoff/reference/screens/hangar-main-menu.png`.

---

### Task 0: Pre-flight

- [ ] **Step 1:** `git -C "/Users/anon/My project" branch --show-current` → `explore/ui-rebrand`. Read `mcpforunity://instances` → one live instance. `read_console` (error) → no C# errors (MCP-infra lines are noise).

---

### Task 1: `CscTheme` — AddToonShadow + harden AddToonOutline

**Files:** Modify `Assets/Scripts/Core/CscTheme.cs`

- [ ] **Step 1: Harden `AddToonOutline`'s `??`-on-Unity-Object**

Replace this line inside `AddToonOutline`:
```csharp
            Outline o = go.GetComponent<Outline>() ?? go.AddComponent<Outline>();
```
with:
```csharp
            Outline o = go.GetComponent<Outline>();
            if (o == null) o = go.AddComponent<Outline>();
```

- [ ] **Step 2: Add `AddToonShadow`** immediately after the `AddToonOutline` method (before `HeatColor`):

```csharp
        // ---- Add a hard toon drop-shadow (single offset, no blur) ----
        // The world/menu "stamp": Ink, offset down-right. Composes with an
        // Outline (border + shadow together) to match the mockups' plates/buttons.
        public static Shadow AddToonShadow(GameObject go, float offset = 6f)
        {
            Shadow s = go.GetComponent<Shadow>();
            if (s == null) s = go.AddComponent<Shadow>();
            s.effectColor = OutlineColor;
            s.effectDistance = new Vector2(offset, -offset);
            s.useGraphicAlpha = true;
            return s;
        }
```
(`CscTheme.cs` already has `using UnityEngine.UI;` — `Shadow` resolves.)

- [ ] **Step 3: Compile.** `refresh_unity` (force/scripts/request) → poll `mcpforunity://editor/state` until `is_compiling:false` → `read_console` (error). Expected: no C# errors.

- [ ] **Step 4: Commit.**
```bash
git -C "/Users/anon/My project" add "Assets/Scripts/Core/CscTheme.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): AddToonShadow primitive + harden AddToonOutline (B3a)" \
  -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: `UIStyle` — BuildBrandBackground (procedural)

**Files:** Modify `Assets/Scripts/Core/UIStyle.cs`

- [ ] **Step 1: Add the background builder + texture helpers** (place after `BuildScreenSpaceCanvas`):

```csharp
        // Full-screen warm brand background: a procedural radial ochre→rust
        // gradient + a faint diagonal overlay, inserted behind all other UI.
        // Textures are generated in code (no asset / no Resources) so this static
        // builder needs nothing external. Reused across menu surfaces (B3a–c).
        public static void BuildBrandBackground(RectTransform canvasRoot)
        {
            AddFullScreenRaw(canvasRoot, "BrandGradient",
                MakeRadialGradient(256, CscPalette.Ochre300, CscPalette.Brown900),
                1f, new Vector2(1f, 1f));
            AddFullScreenRaw(canvasRoot, "BrandHazardOverlay",
                MakeDiagonalStripes(32, CscPalette.Scorch),
                0.06f, new Vector2(24f, 14f));
        }

        static void AddFullScreenRaw(RectTransform parent, string name, Texture2D tex,
            float alpha, Vector2 tiling)
        {
            int uiLayer = LayerMask.NameToLayer("UI");
            GameObject go = new GameObject(name,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            go.transform.SetParent(parent, false);
            if (uiLayer >= 0) go.layer = uiLayer;
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            RawImage img = go.GetComponent<RawImage>();
            img.texture = tex;
            img.color = new Color(1f, 1f, 1f, alpha);
            img.uvRect = new Rect(0f, 0f, tiling.x, tiling.y);
            img.raycastTarget = false;
            go.transform.SetAsFirstSibling();   // draw behind everything
        }

        static Texture2D MakeRadialGradient(int size, Color center, Color edge)
        {
            Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp };
            float c = (size - 1) / 2f, maxR = Mathf.Sqrt(2f) * c;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Clamp01(Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / maxR);
                    t.SetPixel(x, y, Color.Lerp(center, edge, d * d));
                }
            t.Apply();
            return t;
        }

        static Texture2D MakeDiagonalStripes(int size, Color line)
        {
            Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Repeat };
            Color clear = new Color(line.r, line.g, line.b, 0f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    t.SetPixel(x, y, ((x + y) % 16 < 2) ? line : clear);
            t.Apply();
            return t;
        }
```
(`UIStyle.cs` already has `using UnityEngine.UI;` for `RawImage`.)

- [ ] **Step 2: Compile.** `refresh_unity` (force/scripts/request) → poll state → `read_console`. Expected: no C# errors.

- [ ] **Step 3: Commit.**
```bash
git -C "/Users/anon/My project" add "Assets/Scripts/Core/UIStyle.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): procedural brand background (gradient + hazard overlay) (B3a)" \
  -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: `MainMenuController` — wordmark + toon-shadowed menu

**Files:** Modify `Assets/Scripts/MainMenu/MainMenuController.cs`

- [ ] **Step 1: Replace `BuildUI()`** (currently builds the "Cube Fly" title + 3 buttons) with:

```csharp
        void BuildUI()
        {
            UIStyle.EnsureEventSystem();
            Canvas canvas = UIStyle.BuildScreenSpaceCanvas("MainMenuCanvas", sortingOrder: 200);
            RectTransform root = (RectTransform)canvas.transform;

            UIStyle.BuildBrandBackground(root);
            BuildWordmark(root);

            // Buttons stacked below the wordmark.
            CreateMenuButton(root, "Hangar",   new Vector2(0f, -40f),  OnHangar);
            CreateMenuButton(root, "Settings", new Vector2(0f, -140f), OnSettings);
            CreateMenuButton(root, "Exit",     new Vector2(0f, -240f), OnExit);
        }

        // The Cosmic Scrap Club wordmark: a slightly-tilted hazard-yellow plate
        // (ink border + toon shadow) with COSMIC / SCRAP / ★ CLUB ★ in the three
        // brand fonts. Built inline — it is MainMenu-only.
        static void BuildWordmark(RectTransform parent)
        {
            int uiLayer = LayerMask.NameToLayer("UI");
            GameObject plateGO = new GameObject("Wordmark",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            plateGO.transform.SetParent(parent, false);
            if (uiLayer >= 0) plateGO.layer = uiLayer;
            RectTransform prt = (RectTransform)plateGO.transform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(620f, 300f);
            prt.anchoredPosition = new Vector2(0f, 200f);
            prt.localEulerAngles = new Vector3(0f, 0f, 2f);   // ≈ −2° visual tilt
            Image plate = plateGO.GetComponent<Image>();
            plate.color = CscPalette.HazardYellow;
            CscTheme.AddToonOutline(plateGO, 4f);
            CscTheme.AddToonShadow(plateGO, 8f);

            WordmarkLine(prt, "COSMIC",   40,  CscTheme.CondOr,    CscPalette.Scorch,   95f);
            WordmarkLine(prt, "SCRAP",    130, CscTheme.DisplayOr, CscPalette.Scorch,   0f);
            WordmarkLine(prt, "★ CLUB ★", 44, CscTheme.StencilOr, CscPalette.Orange600, -95f);
        }

        static void WordmarkLine(RectTransform plate, string text, int size,
            Font font, Color color, float y)
        {
            Text t = UIStyle.BuildLabel(plate, text, size, FontStyle.Normal, font);
            t.color = color;
            RectTransform rt = (RectTransform)t.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(600f, size + 20f);
            rt.anchoredPosition = new Vector2(0f, y);
        }
```

- [ ] **Step 2: Update `CreateMenuButton`** to add the toon shadow (and drop the button size to match the mockup's proportions):

Replace the existing `CreateMenuButton` body's button-build + add the shadow line. The full method becomes:
```csharp
        static void CreateMenuButton(RectTransform parent, string text,
            Vector2 anchoredPos, UnityEngine.Events.UnityAction onClick)
        {
            (Button button, Text _) = UIStyle.BuildLabeledButton(
                parent, text, new Vector2(360f, 72f), fontSize: 32);
            RectTransform rt = (RectTransform)button.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            CscTheme.AddToonShadow(button.gameObject, 6f);
            button.onClick.AddListener(onClick);
        }
```

- [ ] **Step 3: Verify `using`s.** `MainMenuController.cs` already has `using CubeFly.Core;` and `using UnityEngine.UI;` (it uses `Button`/`Text`). `Font` is `UnityEngine.Font` (via `using UnityEngine;`). No new imports.

- [ ] **Step 4: Compile.** `refresh_unity` (force/scripts/request) → poll state → `read_console`. Expected: no C# errors. (If `CS` errors reference `WordmarkLine`/`BuildWordmark`, check the method bodies were added inside the class.)

- [ ] **Step 5: Commit.**
```bash
git -C "/Users/anon/My project" add "Assets/Scripts/MainMenu/MainMenuController.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): Cosmic Scrap Club wordmark + toon-shadowed menu (B3a)" \
  -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: Verify + present for the gate

- [ ] **Step 1:** `read_console` (error) → clean (only MCP-infra lines).
- [ ] **Step 2:** Present for the maintainer's Play-mode gate: Play MainMenu, compare to `hangar-main-menu.png` — warm radial gradient bg, the tilted hazard-yellow wordmark (COSMIC/SCRAP/★CLUB★), outlined + toon-shadowed buttons; the 3 buttons still navigate. Collect tuning notes (gradient warmth, plate size/rotation, font sizes, button positions, overlay strength). **Hold the push** until confirmed; **do not merge**. Then push onto `explore/ui-rebrand` (updates PR #55) and dispatch the B3a internal review.

---

## Self-review

- **Spec coverage:** `AddToonShadow` + `AddToonOutline` harden (T1) ✓; `BuildBrandBackground` — realized as procedural gradient + overlay, the noted refinement of the spec's "generate a gradient" intent (T2) ✓; wordmark inline in `MainMenuController`, plate + 3 brand-font lines + rotation + outline + shadow (T3) ✓; buttons keep B2 outline + gain toon shadow, HANGAR stays ghost [B4] (T3) ✓; verify + gate (T4) ✓. **Spec divergence (intentional):** no `menu_gradient.png` asset — the gradient is runtime-procedural; the spec's "New asset" section is superseded (cleaner for a static code builder). Everything else matches.
- **Placeholders:** none — all code is complete. Visual constants (sizes, colors, positions, `d*d` falloff, `%16<2` stripe, `(24,14)` tiling) are concrete first-take values explicitly earmarked for gate-tuning, not placeholders.
- **Type/name consistency:** `CscTheme.AddToonShadow(GameObject, float)`, `AddToonOutline(GameObject, float)`, `UIStyle.BuildBrandBackground(RectTransform)`, `BuildLabel(Transform, string, int, FontStyle, Font)` (the B2 signature), `CscPalette.Ochre300/Brown900/HazardYellow/Scorch/Orange600` (all real `CscPalette` members) — consistent across tasks.
