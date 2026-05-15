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
        public static readonly Color BackgroundIdle = new Color(0.13f, 0.13f, 0.18f, 0.9f);
        public static readonly Color TintNormal     = Color.white;
        public static readonly Color TintHighlight  = new Color(0.85f, 0.85f, 1f, 1f);
        public static readonly Color TintPressed    = new Color(0.55f, 0.55f, 0.7f, 1f);
        public static readonly Color TintDisabled   = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        public static readonly Color LabelColor     = Color.white;

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

        // Builds a Button + label as a child of `parent`. Caller positions the
        // resulting RectTransform (anchors / anchoredPosition).
        public static (Button button, Text label) BuildLabeledButton(
            Transform parent, string labelText, Vector2 size, int fontSize = 28)
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
            text.font = BuiltinFont;
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = fontSize;
            text.color = LabelColor;
            text.text = labelText;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return (button, text);
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
            itemCheckGO.GetComponent<Image>().color = TintHighlight;

            Text itemText = MakeText(itemGO.transform, "Item Label", fontSize, uiLayer);
            RectTransform itemTextRT = (RectTransform)itemText.transform;
            itemTextRT.anchorMin = Vector2.zero;
            itemTextRT.anchorMax = Vector2.one;
            itemTextRT.offsetMin = new Vector2(26f, 1f);
            itemTextRT.offsetMax = new Vector2(-10f, -1f);

            Toggle itemToggle = itemGO.GetComponent<Toggle>();
            itemToggle.targetGraphic = itemBgGO.GetComponent<Image>();
            itemToggle.graphic = itemCheckGO.GetComponent<Image>();
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
            t.font = BuiltinFont;
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
            Transform parent, string text, int fontSize, FontStyle style = FontStyle.Normal)
        {
            GameObject labelGO = new GameObject(text + "Label",
                typeof(RectTransform),
                typeof(CanvasRenderer));
            labelGO.transform.SetParent(parent, false);
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) labelGO.layer = uiLayer;

            Text t = labelGO.AddComponent<Text>();
            t.font = BuiltinFont;
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
