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

            // Text label fills the panel with a small padding.
            _label = UIStyle.BuildLabel(_panelRT, "", fontSize: 16);
            _label.alignment = TextAnchor.MiddleLeft;
            _label.raycastTarget = false;
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

        void UpdatePosition(Vector2 screenPos)
        {
            if (_panelRT == null) return;

            // Size the panel to its content for this frame.
            float textWidth  = _label.preferredWidth  + 16f;
            float textHeight = _label.preferredHeight + 8f;
            _panelRT.sizeDelta = new Vector2(
                Mathf.Min(textWidth, 400f),
                Mathf.Max(textHeight, 24f));

            // Default offset: +20 right, -20 below cursor.
            // anchoredPosition is in (x, -y) from top-left.
            float x = screenPos.x + 20f;
            float y = -(Screen.height - screenPos.y) - 20f;

            // Right-edge clamp: flip to the left if it would extend
            // past the right edge.
            if (x + _panelRT.sizeDelta.x > Screen.width)
                x = screenPos.x - _panelRT.sizeDelta.x - 20f;

            // Bottom-edge clamp: flip above the cursor if it would
            // extend past the bottom edge.
            if (-(y - _panelRT.sizeDelta.y) > Screen.height)
                y = -(Screen.height - screenPos.y) + _panelRT.sizeDelta.y + 20f;

            _panelRT.anchoredPosition = new Vector2(x, y);
        }
    }
}
