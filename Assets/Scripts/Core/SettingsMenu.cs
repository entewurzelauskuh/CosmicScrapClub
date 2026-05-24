using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CubeFly.Core
{
    // Tabbed Settings UI reachable from both the Main Menu's Settings
    // button and the ESC pause overlay. Six placeholder tabs (General /
    // Display / Graphics / Audio / Controls / Gameplay) — actual
    // controls fill in tab by tab in later PRs. A seventh Debug tab
    // is added during the VFX pass to surface per-effect toggles.
    //
    // DDOL self-bootstrapping singleton, same shape as PauseMenu and
    // GameOverMenu.
    //
    // Behaviour:
    //   • Show() opens the modal and freezes time. Called from
    //     MainMenuController.OnSettings or PauseMenu.OnSettingsClicked.
    //   • ESC closes (matches PauseMenu's no-Resume-button minimalism).
    //     A small × button top-right of the modal is also available
    //     for mouse-only discoverability.
    //   • When Hide() runs, if PauseMenu was the caller (its IsOpen is
    //     still true), it re-shows PauseMenu's panel — the navigate-to
    //     drill-down is Settings → Pause → game.
    //   • Execution order is -2000 (below PauseMenu's -1000) so this
    //     script's ESC handler runs first; PauseMenu checks
    //     SettingsMenu.IsOpen / EscConsumedThisFrame and short-circuits
    //     its own ESC handling for that frame.
    //   • The modal panel adds its own Canvas override with
    //     overrideSorting=true, sortingOrder=350 so it draws above
    //     MainMenu's own scene canvas (also sortingOrder 200) and any
    //     PersistentHud sibling (UIManager corner button, PauseMenu),
    //     while staying below GameOverMenu (~400).
    //
    // Persistence: there is none. The scaffold has nothing to save;
    // the first real setting brings its own persistence design with it.
    [DefaultExecutionOrder(-2000)]
    public class SettingsMenu : MonoBehaviour
    {
        public static SettingsMenu Instance { get; private set; }

        public bool IsOpen { get; private set; }
        public bool EscConsumedThisFrame { get; private set; }

        public static event Action OnOpened;
        public static event Action OnClosed;

        const string TAG = "SettingsMenu";

        // Tab names also serve as the display label for the sidebar
        // buttons. Adding a tab later (e.g. Debug during the VFX pass)
        // means appending here + adding a content panel in BuildUI.
        static readonly string[] TabNames = new[]
        {
            "General", "Display", "Graphics", "Audio", "Controls", "Gameplay"
        };

        GameObject _root;
        GameObject[] _tabPanels = Array.Empty<GameObject>();
        Button[] _tabButtons = Array.Empty<Button>();
        int _activeTab;
        float _previousTimeScale = 1f;

        static readonly Color SidebarActiveTint   = new Color(0.85f, 0.85f, 1f, 1f);
        static readonly Color SidebarInactiveTint = new Color(0.30f, 0.30f, 0.40f, 0.9f);

        // Self-bootstrap: spawn the singleton before any scene loads.
        // BeforeSceneLoad runs once per play session in both Editor
        // and standalone, so there's no risk of duplicates.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            GameObject go = new GameObject("SettingsMenu");
            go.AddComponent<SettingsMenu>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            BuildUI();
            HideUI();

            Debug.unityLogger.Log(TAG, "Settings menu ready.");
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            EscConsumedThisFrame = false;
            if (!IsOpen) return;

            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (!kb.escapeKey.wasPressedThisFrame) return;

            Hide();
            EscConsumedThisFrame = true;
        }

        public void Show()
        {
            if (IsOpen) return;
            IsOpen = true;
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            ShowUI();
            Debug.unityLogger.Log(TAG, "Opened.");
            OnOpened?.Invoke();
        }

        public void Hide()
        {
            if (!IsOpen) return;
            IsOpen = false;
            Time.timeScale = _previousTimeScale;
            HideUI();
            Debug.unityLogger.Log(TAG, "Closed.");
            OnClosed?.Invoke();

            // Navigate-to flow: if Settings was opened from PauseMenu,
            // restore the pause panel so the player lands back where
            // they were. PauseMenu's IsOpen / Time.timeScale stay
            // owned by PauseMenu itself.
            if (PauseMenu.Instance != null && PauseMenu.Instance.IsOpen)
            {
                PauseMenu.Instance.ShowUI();
            }
        }

        void ShowUI() { if (_root != null) _root.SetActive(true); }
        void HideUI() { if (_root != null) _root.SetActive(false); }

        // ---------- UI construction ----------

        void BuildUI()
        {
            // Parent under the shared persistent canvas. The panel
            // adds its own Canvas override below so it draws above
            // MainMenu's own scene canvas (also sortingOrder 200).
            GameObject panelGO = new GameObject("SettingsMenuPanel",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(Canvas), typeof(GraphicRaycaster));
            panelGO.transform.SetParent(PersistentHud.Instance.Root, false);
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) panelGO.layer = uiLayer;
            _root = panelGO;

            RectTransform root = (RectTransform)panelGO.transform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            // Canvas override: draw above MainMenu's scene canvas (200)
            // and any PersistentHud sibling, but below GameOverMenu.
            Canvas canvasOverride = panelGO.GetComponent<Canvas>();
            canvasOverride.overrideSorting = true;
            canvasOverride.sortingOrder = 350;

            // Dim backdrop — full-screen, also catches clicks so
            // nothing under the overlay can be reached.
            Image dim = panelGO.GetComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.70f);
            dim.raycastTarget = true;

            // Modal frame — ~1340 x 760, centred. Holds title, close
            // button, sidebar, and the six content panels.
            GameObject frameGO = new GameObject("Frame",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            frameGO.transform.SetParent(root, false);
            if (uiLayer >= 0) frameGO.layer = uiLayer;

            RectTransform frameRT = (RectTransform)frameGO.transform;
            frameRT.anchorMin = frameRT.anchorMax = frameRT.pivot = new Vector2(0.5f, 0.5f);
            frameRT.sizeDelta = new Vector2(1340f, 760f);
            frameRT.anchoredPosition = Vector2.zero;

            Image frameImg = frameGO.GetComponent<Image>();
            frameImg.color = UIStyle.BackgroundIdle;
            frameImg.raycastTarget = true;

            // Title at top of frame.
            Text title = UIStyle.BuildLabel(frameRT, "Settings", fontSize: 48,
                style: FontStyle.Bold);
            RectTransform titleRT = (RectTransform)title.transform;
            titleRT.anchorMin = new Vector2(0f, 1f);
            titleRT.anchorMax = new Vector2(1f, 1f);
            titleRT.pivot = new Vector2(0.5f, 1f);
            titleRT.sizeDelta = new Vector2(0f, 70f);
            titleRT.anchoredPosition = new Vector2(0f, -20f);

            // × close button — top-right of frame, mouse-only convenience.
            (Button closeButton, Text _) = UIStyle.BuildLabeledButton(
                frameRT, "×", new Vector2(40f, 40f), fontSize: 28);
            RectTransform closeRT = (RectTransform)closeButton.transform;
            closeRT.anchorMin = closeRT.anchorMax = new Vector2(1f, 1f);
            closeRT.pivot = new Vector2(1f, 1f);
            closeRT.anchoredPosition = new Vector2(-20f, -20f);
            closeButton.onClick.AddListener(Hide);

            // Sidebar at left, holding tab buttons stacked top-down.
            GameObject sidebarGO = new GameObject("Sidebar", typeof(RectTransform));
            sidebarGO.transform.SetParent(frameRT, false);
            if (uiLayer >= 0) sidebarGO.layer = uiLayer;

            RectTransform sidebarRT = (RectTransform)sidebarGO.transform;
            sidebarRT.anchorMin = new Vector2(0f, 0f);
            sidebarRT.anchorMax = new Vector2(0f, 1f);
            sidebarRT.pivot = new Vector2(0f, 1f);
            sidebarRT.sizeDelta = new Vector2(220f, -120f);          // width / height-minus-title
            sidebarRT.anchoredPosition = new Vector2(20f, -100f);    // 20 px left, below title

            _tabButtons = new Button[TabNames.Length];
            for (int i = 0; i < TabNames.Length; i++)
            {
                int captured = i;
                (Button tabButton, Text _) = UIStyle.BuildLabeledButton(
                    sidebarRT, TabNames[i], new Vector2(200f, 60f), fontSize: 22);
                RectTransform tabRT = (RectTransform)tabButton.transform;
                tabRT.anchorMin = tabRT.anchorMax = tabRT.pivot = new Vector2(0f, 1f);
                tabRT.anchoredPosition = new Vector2(0f, -i * 70f);
                tabButton.onClick.AddListener(() => SelectTab(captured));
                _tabButtons[i] = tabButton;
            }

            // Content area — right of sidebar.
            GameObject contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(frameRT, false);
            if (uiLayer >= 0) contentGO.layer = uiLayer;

            RectTransform contentRT = (RectTransform)contentGO.transform;
            contentRT.anchorMin = new Vector2(0f, 0f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot = new Vector2(0.5f, 0.5f);
            contentRT.offsetMin = new Vector2(260f, 20f);            // 20 left + 220 sidebar + 20 gap
            contentRT.offsetMax = new Vector2(-20f, -100f);          // 20 right, 100 title clearance

            // Six placeholder content panels — one per tab.
            _tabPanels = new GameObject[TabNames.Length];
            for (int i = 0; i < TabNames.Length; i++)
            {
                GameObject panel = new GameObject(TabNames[i] + "Panel",
                    typeof(RectTransform));
                panel.transform.SetParent(contentRT, false);
                if (uiLayer >= 0) panel.layer = uiLayer;

                RectTransform prt = (RectTransform)panel.transform;
                prt.anchorMin = Vector2.zero;
                prt.anchorMax = Vector2.one;
                prt.offsetMin = Vector2.zero;
                prt.offsetMax = Vector2.zero;

                Text label = UIStyle.BuildLabel(prt, "Coming soon", fontSize: 32);
                RectTransform lrt = (RectTransform)label.transform;
                lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0.5f, 0.5f);
                lrt.sizeDelta = new Vector2(400f, 80f);
                lrt.anchoredPosition = Vector2.zero;

                _tabPanels[i] = panel;
            }

            SelectTab(0);
        }

        void SelectTab(int index)
        {
            if (index < 0 || index >= _tabPanels.Length) return;
            _activeTab = index;
            for (int i = 0; i < _tabPanels.Length; i++)
            {
                _tabPanels[i].SetActive(i == index);
                if (_tabButtons[i] != null)
                {
                    Image img = _tabButtons[i].GetComponent<Image>();
                    if (img != null)
                    {
                        img.color = (i == index) ? SidebarActiveTint : SidebarInactiveTint;
                    }
                }
            }
        }
    }
}
