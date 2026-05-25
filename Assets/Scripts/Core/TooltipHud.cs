using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CubeFly.Core
{
    // Lazy DDOL singleton hosting the floating tooltip text label that
    // TooltipTrigger drives. Parented to PersistentHud.Instance.Root
    // with its OWN Canvas override at sortingOrder=500 — above the
    // SettingsMenu (350), GameOverMenu (400), and everything else.
    // Tooltips are always on top.
    //
    // Behaviour:
    //   • Show(text, screenPos): position the panel near the cursor,
    //     set the text, activate. Updated each frame while shown so
    //     the tooltip moves with the mouse.
    //   • Hide(): deactivate.
    //   • Screen-edge clamping: if a tooltip would extend past the
    //     right or bottom screen edge, it flips to the other side of
    //     the cursor so it's still visible.
    //
    // Lazy-create: the singleton spawns on first Show() call rather
    // than via BeforeSceneLoad bootstrap, because there are no
    // consumers until the user opens Settings → Debug tab.
    public class TooltipHud : MonoBehaviour
    {
        static TooltipHud _instance;
        public static TooltipHud Instance => _instance != null ? _instance : CreateInstance();

        // Side-effect-free existence check. Use this in early-exit paths
        // (e.g. OnPointerExit) so consumers don't accidentally lazy-spawn
        // the hud just to call a no-op Hide() on a never-shown tooltip.
        public static bool HasInstance => _instance != null;

        const string TAG = "TooltipHud";

        GameObject _panel;
        RectTransform _panelRT;
        Text _label;
        bool _showing;

        static TooltipHud CreateInstance()
        {
            GameObject go = new GameObject("TooltipHud");
            _instance = go.AddComponent<TooltipHud>();
            DontDestroyOnLoad(go);
            return _instance;
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            BuildUI();
            HideUI();

            Debug.unityLogger.Log(TAG, "Tooltip hud ready.");
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        void BuildUI()
        {
            // Panel parented under PersistentHud's root. Has its own
            // Canvas override at sortingOrder=500 so tooltips always
            // render above any other UI.
            GameObject panelGO = new GameObject("TooltipPanel",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(Canvas), typeof(GraphicRaycaster));
            panelGO.transform.SetParent(PersistentHud.Instance.Root, false);
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) panelGO.layer = uiLayer;
            _panel = panelGO;
            _panelRT = (RectTransform)panelGO.transform;

            // Canvas override: tooltips render above everything.
            Canvas canvas = panelGO.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 500;

            // The tooltip itself should not block clicks on whatever
            // is being hovered.
            GraphicRaycaster raycaster = panelGO.GetComponent<GraphicRaycaster>();
            raycaster.enabled = false;

            Image bg = panelGO.GetComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.08f, 0.92f);
            bg.raycastTarget = false;

            // Anchor top-left so anchoredPosition values correspond to
            // (x, -y) screen pixels from the top-left of the screen.
            _panelRT.anchorMin = new Vector2(0f, 1f);
            _panelRT.anchorMax = new Vector2(0f, 1f);
            _panelRT.pivot = new Vector2(0f, 1f);

            // Text label fills the panel with a small padding. Wrap
            // mode (not Overflow) so long tooltip strings break onto
            // multiple lines instead of running off the panel's edge —
            // UpdatePosition caps the panel width and reads the wrapped
            // preferredHeight to grow the panel vertically.
            _label = UIStyle.BuildLabel(_panelRT, "", fontSize: 16);
            _label.alignment = TextAnchor.UpperLeft;
            _label.raycastTarget = false;
            _label.horizontalOverflow = HorizontalWrapMode.Wrap;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            RectTransform lrt = (RectTransform)_label.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(8f, 4f);
            lrt.offsetMax = new Vector2(-8f, -4f);
        }

        public void Show(string text, Vector2 screenPos)
        {
            if (_label == null) return;
            _label.text = text;
            _showing = true;
            _panel.SetActive(true);
            UpdatePosition(screenPos);
        }

        public void Hide()
        {
            _showing = false;
            HideUI();
        }

        void HideUI()
        {
            if (_panel != null) _panel.SetActive(false);
        }

        void Update()
        {
            if (!_showing) return;
            Mouse mouse = Mouse.current;
            if (mouse == null) return;
            UpdatePosition(mouse.position.ReadValue());
        }

        // Cap for the tooltip's horizontal width. Long tooltip strings
        // wrap to multiple lines at this width — the panel then grows
        // vertically (preferredHeight after wrap) so the dark backdrop
        // still spans the whole text. Keeps tooltips from being so
        // wide they can't fit beside the cursor without running off
        // the screen.
        const float MaxPanelWidth = 400f;

        void UpdatePosition(Vector2 screenPos)
        {
            if (_panelRT == null) return;

            // Pick the panel width: prefer single-line (no wrap needed),
            // but cap at MaxPanelWidth. The label uses HorizontalWrapMode.Wrap
            // so when the available label width drops below the
            // unwrapped preferredWidth, the text wraps automatically.
            const float HPad = 16f;
            const float VPad = 8f;
            float unwrappedW = _label.preferredWidth + HPad;
            float panelW = Mathf.Min(unwrappedW, MaxPanelWidth);

            // Set the width now (height is provisional). A forced
            // layout pass makes the label honour the new width, so the
            // subsequent preferredHeight read reflects the actual
            // wrapped multi-line height — without that pass, the
            // preferredHeight reads as the single-line value.
            _panelRT.sizeDelta = new Vector2(panelW, 24f);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_panelRT);

            float panelH = Mathf.Max(_label.preferredHeight + VPad, 24f);
            _panelRT.sizeDelta = new Vector2(panelW, panelH);

            // Default offset: +20 right, -20 below cursor.
            // anchoredPosition is in (x, -y) from top-left.
            float x = screenPos.x + 20f;
            float y = -(Screen.height - screenPos.y) - 20f;

            // Right-edge clamp: flip to the left if it would extend
            // past the right edge. With the wrap-capped panel width
            // (<= MaxPanelWidth), the flipped position fits on-screen.
            if (x + panelW > Screen.width)
                x = screenPos.x - panelW - 20f;

            // Bottom-edge clamp: flip above the cursor if it would
            // extend past the bottom edge.
            if (-(y - panelH) > Screen.height)
                y = -(Screen.height - screenPos.y) + panelH + 20f;

            _panelRT.anchoredPosition = new Vector2(x, y);
        }
    }
}
