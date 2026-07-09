using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CubeFly.Core
{
    // Shared runtime UI builder helpers. UIManager and MainMenuController both
    // construct their hierarchies from these so the visual style stays
    // consistent without each call site duplicating the same setup.
    //
    // Uses legacy UnityEngine.UI.Text (not TextMeshPro) because Unity ships a
    // built-in font usable without any package import step. Switching back to
    // TMP would require importing TMP Essentials (Window > TextMeshPro > Import
    // TMP Essential Resources) so a default TMP_FontAsset exists at runtime.
    internal static class UIStyle
    {
        public static readonly Color BackgroundIdle = CscPalette.BackgroundIdle;
        public static readonly Color TintNormal     = CscPalette.TintNormal;
        public static readonly Color TintHighlight  = CscPalette.TintHighlight;
        public static readonly Color TintPressed    = CscPalette.TintPressed;
        public static readonly Color TintDisabled   = CscPalette.TintDisabled;
        public static readonly Color LabelColor     = CscPalette.Label;

        public static Canvas BuildScreenSpaceCanvas(string name, int sortingOrder = 100)
        {
            GameObject go = new GameObject(name,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) go.layer = uiLayer;

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        // Spawn the EventSystem once and DontDestroyOnLoad it so that when the
        // first scene unloads, scene transitions don't leave the rest of the
        // game without a working EventSystem. Without this, GraphicRaycaster
        // hits never fire and IsPointerOverGameObject always returns false —
        // which means in-game raycasts (BuildManager / CubePreview) treat
        // clicks-on-buttons as clicks-on-the-world.
        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            GameObject es = new GameObject("EventSystem",
                typeof(EventSystem),
                typeof(InputSystemUIInputModule));
            Object.DontDestroyOnLoad(es);
        }

        // Full-screen warm brand background: a procedural radial ochre->rust
        // gradient + a faint diagonal overlay, inserted behind all other UI.
        // Textures are generated in code (no asset / no Resources) so this static
        // builder needs nothing external. Reused across menu surfaces (B3a-c).
        static Texture2D _gradientTex, _hazardTex;

        public static void BuildBrandBackground(RectTransform canvasRoot)
        {
            // Generate the brand textures once per session (shared by every menu
            // surface) instead of regenerating on each menu (re)build.
            if (_gradientTex == null)
                _gradientTex = MakeRadialGradient(256, CscPalette.Ochre300, CscPalette.Brown900);
            if (_hazardTex == null)
                _hazardTex = MakeDiagonalStripes(32, CscPalette.Scorch);
            AddFullScreenRaw(canvasRoot, "BrandGradient", _gradientTex, 1f, new Vector2(1f, 1f));
            AddFullScreenRaw(canvasRoot, "BrandHazardOverlay", _hazardTex, 0.06f, new Vector2(24f, 14f));
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
            // Background layers are added before any menu content, and the
            // gradient before the overlay, so natural sibling order already
            // draws them back-to-front behind the UI. (An earlier SetAsFirstSibling
            // here inverted the two layers and hid the overlay under the gradient.)
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

        // A brand plate sprite: fill + ink border with feathered (anti-aliased)
        // edges, so a *rotated* plate renders without staircase aliasing (uGUI
        // Screen-Space-Overlay has no MSAA). Generate once and cache at the call site.
        public static Sprite MakePlateSprite(int w, int h, int border, Color fill, Color ink)
        {
            Color[] px = new Color[w * h];
            const float feather = 1.5f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float edge = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
                    Color c = Color.Lerp(ink, fill, Mathf.Clamp01((edge - border) / feather));
                    c.a = Mathf.Clamp01(edge / feather);   // feathered outer edge
                    px[y * w + x] = c;
                }
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            t.SetPixels(px);
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        // Builds a Button + label as a child of `parent`. Caller positions the
        // resulting RectTransform (anchors / anchoredPosition).
        public static (Button button, Text label) BuildLabeledButton(
            Transform parent, string labelText, Vector2 size, int fontSize = 28, Font font = null, bool bounce = false)
        {
            GameObject buttonGO = new GameObject(labelText + "Button",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            buttonGO.transform.SetParent(parent, false);
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) buttonGO.layer = uiLayer;

            RectTransform brt = (RectTransform)buttonGO.transform;
            brt.sizeDelta = size;

            Image bImage = buttonGO.GetComponent<Image>();
            bImage.color = BackgroundIdle;

            Button button = buttonGO.GetComponent<Button>();
            ColorBlock cb = button.colors;
            cb.normalColor      = TintNormal;
            cb.highlightedColor = TintHighlight;
            cb.pressedColor     = TintPressed;
            cb.selectedColor    = TintHighlight;
            cb.disabledColor    = TintDisabled;
            cb.colorMultiplier  = 1f;
            cb.fadeDuration     = 0.1f;
            button.colors = cb;
            button.targetGraphic = bImage;

            // Signature 2px ink outline on the button fill (render effect — no layout change).
            CscTheme.AddToonOutline(buttonGO);
            if (bounce) buttonGO.AddComponent<UIClickBounce>();

            GameObject labelGO = new GameObject("Label",
                typeof(RectTransform),
                typeof(CanvasRenderer));
            labelGO.transform.SetParent(buttonGO.transform, false);
            if (uiLayer >= 0) labelGO.layer = uiLayer;

            RectTransform lrt = (RectTransform)labelGO.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;

            Text text = labelGO.AddComponent<Text>();
            text.font = font ?? CscTheme.CondOr;
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = fontSize;
            text.color = LabelColor;
            text.text = labelText;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return (button, text);
        }

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

        // Builds a legacy uGUI Dropdown as a child of `parent`, with the
        // full template hierarchy the control needs (Label + Template →
        // Viewport → Content → Item → Background/Checkmark/Label). The
        // project has no dropdown prefabs and builds all UI in code, so
        // this mirrors what `GameObject > UI > Dropdown` would create.
        //
        // The caller sets `.options`, `.value`, and `.onValueChanged`.
        // No scrollbar — intended for short option lists; the template
        // is sized for ~4 visible rows and clamps beyond that.
        // Builds a labelled checkbox-style Toggle. Container GameObject
        // holds the Toggle component, a Background Image (the visible
        // square), a Checkmark Image (the tick) as a child of the
        // Background, and a Label Text to the right.
        //
        // The caller drives the toggle via `.isOn` and listens to
        // `.onValueChanged`. Hover/press feedback comes from the
        // Toggle's default ColorTint transition on the Background.
        public static Toggle BuildToggle(Transform parent, string labelText,
            Vector2 size, int fontSize = 22)
        {
            GameObject containerGO = new GameObject(labelText + "Toggle",
                typeof(RectTransform), typeof(Toggle));
            containerGO.transform.SetParent(parent, false);
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) containerGO.layer = uiLayer;

            RectTransform crt = (RectTransform)containerGO.transform;
            crt.sizeDelta = size;

            Toggle toggle = containerGO.GetComponent<Toggle>();

            // Match the ColorBlock palette used by BuildLabeledButton /
            // BuildDropdown so hover / press feedback is visually
            // consistent across the Settings UI.
            ColorBlock cb = toggle.colors;
            cb.normalColor      = TintNormal;
            cb.highlightedColor = TintHighlight;
            cb.pressedColor     = TintPressed;
            cb.selectedColor    = TintHighlight;
            cb.disabledColor    = TintDisabled;
            cb.colorMultiplier  = 1f;
            cb.fadeDuration     = 0.1f;
            toggle.colors = cb;

            // Background — the visible square box on the left.
            GameObject bgGO = new GameObject("Background",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bgGO.transform.SetParent(containerGO.transform, false);
            if (uiLayer >= 0) bgGO.layer = uiLayer;
            RectTransform bgRT = (RectTransform)bgGO.transform;
            bgRT.anchorMin = new Vector2(0f, 0.5f);
            bgRT.anchorMax = new Vector2(0f, 0.5f);
            bgRT.pivot = new Vector2(0f, 0.5f);
            bgRT.sizeDelta = new Vector2(28f, 28f);
            bgRT.anchoredPosition = new Vector2(4f, 0f);
            Image bgImage = bgGO.GetComponent<Image>();
            bgImage.color = BackgroundIdle;
            toggle.targetGraphic = bgImage;

            // 1-pixel Ink outline (the brand toon edge) on the checkbox
            // box. Unity's Outline replicates the mesh in four diagonal
            // offsets, producing a uniform outline at the configured
            // effectDistance regardless of the box's tint or the
            // checkmark state.
            Outline bgOutline = bgGO.AddComponent<Outline>();
            bgOutline.effectColor = CscTheme.OutlineColor;
            bgOutline.effectDistance = new Vector2(1f, 1f);

            // Checkmark — child of Background, shown when Toggle.isOn.
            GameObject checkGO = new GameObject("Checkmark",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            checkGO.transform.SetParent(bgGO.transform, false);
            if (uiLayer >= 0) checkGO.layer = uiLayer;
            RectTransform chkRT = (RectTransform)checkGO.transform;
            chkRT.anchorMin = Vector2.zero;
            chkRT.anchorMax = Vector2.one;
            chkRT.offsetMin = new Vector2(6f, 6f);
            chkRT.offsetMax = new Vector2(-6f, -6f);
            Image checkImage = checkGO.GetComponent<Image>();
            checkImage.color = new Color(0.85f, 0.85f, 1f);   // bright checkmark, decoupled from the darkened hover tint
            toggle.graphic = checkImage;

            // Label — text to the right of the box, fills remaining width.
            GameObject labelGO = new GameObject("Label",
                typeof(RectTransform), typeof(CanvasRenderer));
            labelGO.transform.SetParent(containerGO.transform, false);
            if (uiLayer >= 0) labelGO.layer = uiLayer;
            RectTransform lrt = (RectTransform)labelGO.transform;
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.offsetMin = new Vector2(40f, 0f);
            lrt.offsetMax = new Vector2(0f, 0f);
            Text label = labelGO.AddComponent<Text>();
            label.font = CscTheme.CondOr;
            label.alignment = TextAnchor.MiddleLeft;
            label.fontSize = fontSize;
            label.color = LabelColor;
            label.text = labelText;
            label.raycastTarget = true;     // needed for TooltipTrigger to fire on hover

            return toggle;
        }

        public static Dropdown BuildDropdown(Transform parent, Vector2 size, int fontSize = 22)
        {
            int uiLayer = LayerMask.NameToLayer("UI");

            // --- Root (Image + Dropdown) ---
            GameObject rootGO = new GameObject("Dropdown",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Dropdown));
            rootGO.transform.SetParent(parent, false);
            if (uiLayer >= 0) rootGO.layer = uiLayer;
            ((RectTransform)rootGO.transform).sizeDelta = size;
            Image rootImage = rootGO.GetComponent<Image>();
            rootImage.color = BackgroundIdle;
            CscTheme.AddToonOutline(rootGO);   // branded ink border on the dropdown control

            Dropdown dropdown = rootGO.GetComponent<Dropdown>();
            ColorBlock cb = dropdown.colors;
            cb.normalColor = TintNormal;
            cb.highlightedColor = TintHighlight;
            cb.pressedColor = TintPressed;
            cb.selectedColor = TintHighlight;
            cb.disabledColor = TintDisabled;
            dropdown.colors = cb;
            dropdown.targetGraphic = rootImage;

            // --- Caption label (shows the current selection) ---
            Text captionText = MakeText(rootGO.transform, "Label", fontSize, uiLayer);
            RectTransform capRT = (RectTransform)captionText.transform;
            capRT.anchorMin = Vector2.zero;
            capRT.anchorMax = Vector2.one;
            capRT.offsetMin = new Vector2(10f, 2f);
            capRT.offsetMax = new Vector2(-10f, -2f);

            // --- Template (inactive; the control clones it when opened) ---
            GameObject templateGO = new GameObject("Template",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            templateGO.transform.SetParent(rootGO.transform, false);
            if (uiLayer >= 0) templateGO.layer = uiLayer;
            RectTransform templateRT = (RectTransform)templateGO.transform;
            templateRT.anchorMin = new Vector2(0f, 0f);
            templateRT.anchorMax = new Vector2(1f, 0f);
            templateRT.pivot = new Vector2(0.5f, 1f);
            templateRT.anchoredPosition = new Vector2(0f, 2f);
            templateRT.sizeDelta = new Vector2(0f, size.y * 4f);
            templateGO.GetComponent<Image>().color = BackgroundIdle;

            // --- Viewport (masked) ---
            GameObject viewportGO = new GameObject("Viewport",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            viewportGO.transform.SetParent(templateGO.transform, false);
            if (uiLayer >= 0) viewportGO.layer = uiLayer;
            RectTransform viewportRT = (RectTransform)viewportGO.transform;
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.offsetMin = Vector2.zero;
            viewportRT.offsetMax = Vector2.zero;
            viewportRT.pivot = new Vector2(0f, 1f);
            viewportGO.GetComponent<Mask>().showMaskGraphic = false;

            // --- Content ---
            GameObject contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(viewportGO.transform, false);
            if (uiLayer >= 0) contentGO.layer = uiLayer;
            RectTransform contentRT = (RectTransform)contentGO.transform;
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.sizeDelta = new Vector2(0f, size.y);

            // --- Item (the row template the Dropdown clones per option) ---
            GameObject itemGO = new GameObject("Item", typeof(RectTransform), typeof(Toggle));
            itemGO.transform.SetParent(contentGO.transform, false);
            if (uiLayer >= 0) itemGO.layer = uiLayer;
            RectTransform itemRT = (RectTransform)itemGO.transform;
            itemRT.anchorMin = new Vector2(0f, 0.5f);
            itemRT.anchorMax = new Vector2(1f, 0.5f);
            itemRT.sizeDelta = new Vector2(0f, size.y);

            GameObject itemBgGO = new GameObject("Item Background",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            itemBgGO.transform.SetParent(itemGO.transform, false);
            if (uiLayer >= 0) itemBgGO.layer = uiLayer;
            StretchFill((RectTransform)itemBgGO.transform);
            itemBgGO.GetComponent<Image>().color = BackgroundIdle;

            GameObject itemCheckGO = new GameObject("Item Checkmark",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            itemCheckGO.transform.SetParent(itemGO.transform, false);
            if (uiLayer >= 0) itemCheckGO.layer = uiLayer;
            RectTransform checkRT = (RectTransform)itemCheckGO.transform;
            checkRT.anchorMin = new Vector2(0f, 0.5f);
            checkRT.anchorMax = new Vector2(0f, 0.5f);
            checkRT.sizeDelta = new Vector2(14f, 14f);
            checkRT.anchoredPosition = new Vector2(12f, 0f);
            itemCheckGO.GetComponent<Image>().color = new Color(0.85f, 0.85f, 1f);   // bright checkmark, decoupled from the hover tint

            Text itemText = MakeText(itemGO.transform, "Item Label", fontSize, uiLayer);
            RectTransform itemTextRT = (RectTransform)itemText.transform;
            itemTextRT.anchorMin = Vector2.zero;
            itemTextRT.anchorMax = Vector2.one;
            itemTextRT.offsetMin = new Vector2(26f, 1f);
            itemTextRT.offsetMax = new Vector2(-10f, -1f);

            Toggle itemToggle = itemGO.GetComponent<Toggle>();
            itemToggle.targetGraphic = itemBgGO.GetComponent<Image>();
            itemToggle.graphic = itemCheckGO.GetComponent<Image>();
            // Brand the option rows: idle from the ColorBlock, hover/selected ochre.
            itemBgGO.GetComponent<Image>().color = Color.white;
            ColorBlock itemColors = itemToggle.colors;
            itemColors.normalColor      = BackgroundIdle;
            itemColors.highlightedColor = CscPalette.Ochre300;
            itemColors.pressedColor     = CscPalette.Ochre500;
            itemColors.selectedColor    = CscPalette.Ochre300;
            itemColors.disabledColor    = TintDisabled;
            itemColors.colorMultiplier  = 1f;
            itemColors.fadeDuration     = 0.1f;
            itemToggle.colors = itemColors;
            itemToggle.isOn = true;

            ScrollRect scroll = templateGO.GetComponent<ScrollRect>();
            scroll.content = contentRT;
            scroll.viewport = viewportRT;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 20f;

            // Inactive until the Dropdown opens it.
            templateGO.SetActive(false);

            dropdown.template = templateRT;
            dropdown.captionText = captionText;
            dropdown.itemText = itemText;

            return dropdown;
        }

        // Internal helper for BuildDropdown — a bare Text on a fresh
        // GameObject with the builtin font and the shared label colour.
        static Text MakeText(Transform parent, string name, int fontSize, int uiLayer)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            if (uiLayer >= 0) go.layer = uiLayer;
            Text t = go.AddComponent<Text>();
            t.font = CscTheme.CondOr;
            t.fontSize = fontSize;
            t.color = LabelColor;
            t.alignment = TextAnchor.MiddleLeft;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        static void StretchFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static Text BuildLabel(
            Transform parent, string text, int fontSize, FontStyle style = FontStyle.Normal, Font font = null)
        {
            GameObject labelGO = new GameObject(text + "Label",
                typeof(RectTransform),
                typeof(CanvasRenderer));
            labelGO.transform.SetParent(parent, false);
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) labelGO.layer = uiLayer;

            Text t = labelGO.AddComponent<Text>();
            t.font = font ?? CscTheme.BodyOr;
            t.alignment = TextAnchor.MiddleCenter;
            t.fontSize = fontSize;
            t.fontStyle = style;
            t.color = LabelColor;
            t.text = text;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }
    }
}
