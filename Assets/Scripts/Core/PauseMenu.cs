using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CubeFly.Core
{
    // ESC pause overlay for BuildScene and FlyScene. Self-bootstraps
    // via a [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)] hook so
    // every play session has exactly one DDOL singleton — no scene
    // wiring required.
    //
    // Behaviour:
    //   • ESC opens (only in BuildScene / FlyScene) and closes the
    //     overlay. Closed elsewhere (MainMenu, HangarSelect) ESC is
    //     left to the existing scene controllers.
    //   • While open, Time.timeScale = 0 freezes physics + FixedUpdate
    //     so the construct stops drifting in flight.
    //   • Build / Fly scripts check IsOpen at the top of their Update
    //     and short-circuit, so keyboard / mouse can't drive the world
    //     while the overlay is up.
    //   • The overlay's full-screen panel sits at sortingOrder 300 so
    //     it covers the corner UI button (sortingOrder 100) and the
    //     build toolbar (sortingOrder 90). Its GraphicRaycaster
    //     intercepts mouse clicks so nothing under it is reachable.
    //   • Two buttons: "Menu" loads MainMenu; "Back to Desktop" quits.
    //     ESC also closes (no Resume button — keeps the surface to
    //     what the spec asked for).
    //
    // Execution order is forced negative so this script's Update runs
    // before any other unmodified script. Other scripts that respond
    // to ESC (BuildToolbarController, HangarSelectController) check
    // EscConsumedThisFrame to skip their own ESC handler when we
    // already handled it.
    [DefaultExecutionOrder(-1000)]
    public class PauseMenu : MonoBehaviour
    {
        public static PauseMenu Instance { get; private set; }

        public bool IsOpen { get; private set; }

        // True for the rest of the frame after PauseMenu opens or
        // closes itself in response to ESC. Lets other ESC handlers
        // (e.g. BuildToolbarController's flyout-close) skip their own
        // logic for that one frame so a single ESC press doesn't
        // unintentionally trigger two actions.
        public bool EscConsumedThisFrame { get; private set; }

        public static event Action OnOpened;
        public static event Action OnClosed;

        const string TAG = "PauseMenu";
        const string MainMenuSceneName     = "MainMenu";
        const string BuildSceneName        = "BuildScene";
        const string FlySceneName          = "FlyScene";

        GameObject _root;
        Button _hangarButton;       // visible only in FlyScene
        float _previousTimeScale = 1f;

        // Self-bootstrap: spawn the singleton before any scene loads.
        // BeforeSceneLoad runs once per play session in both Editor
        // and standalone, so there's no risk of duplicates.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            GameObject go = new GameObject("PauseMenu");
            go.AddComponent<PauseMenu>();
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

            SceneManager.sceneLoaded += OnSceneLoaded;
            Debug.unityLogger.Log(TAG, "Pause menu ready.");
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                Instance = null;
            }
        }

        void Update()
        {
            EscConsumedThisFrame = false;

            // If SettingsMenu owns ESC this frame (it's open, or just
            // closed itself on this same ESC press), do nothing here.
            // SettingsMenu runs at [DefaultExecutionOrder(-2000)], so
            // its Update has already run by the time we reach this.
            if (SettingsMenu.Instance != null &&
                (SettingsMenu.Instance.IsOpen || SettingsMenu.Instance.EscConsumedThisFrame))
                return;

            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (!kb.escapeKey.wasPressedThisFrame) return;

            if (IsOpen)
            {
                Close();
                EscConsumedThisFrame = true;
            }
            else if (CanOpenInActiveScene())
            {
                Open();
                EscConsumedThisFrame = true;
            }
        }

        // The menu is only meaningful during gameplay. Opening it on
        // the MainMenu / HangarSelect scenes would let ESC steal focus
        // from the existing scene controllers' ESC handlers.
        static bool CanOpenInActiveScene()
        {
            string s = SceneManager.GetActiveScene().name;
            return s == BuildSceneName || s == FlySceneName;
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            ShowUI();
            Debug.unityLogger.Log(TAG, "Opened.");
            OnOpened?.Invoke();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            // Restore the saved time scale rather than blindly setting
            // 1 — a future feature might intentionally run at half speed.
            Time.timeScale = _previousTimeScale;
            HideUI();
            Debug.unityLogger.Log(TAG, "Closed.");
            OnClosed?.Invoke();
        }

        void OnHangarClicked()
        {
            // Restore timeScale BEFORE the scene toggle so the next
            // scene doesn't wake up frozen. SceneSwitcher.Toggle does
            // the GameData snapshot/restore for the FlyScene → BuildScene
            // transition before loading.
            Time.timeScale = 1f;
            _previousTimeScale = 1f;
            Close();
            Debug.unityLogger.Log(TAG, "Hangar button — toggling to BuildScene.");
            SceneSwitcher.Toggle();
        }

        void OnMenuClicked()
        {
            // Restore timeScale BEFORE the scene load so the next
            // scene doesn't wake up frozen.
            Time.timeScale = 1f;
            _previousTimeScale = 1f;
            Close();
            Debug.unityLogger.Log(TAG, "Menu button — loading MainMenu.");
            SceneManager.LoadScene(MainMenuSceneName);
        }

        void OnSettingsClicked()
        {
            // Navigate-to flow. Hide the pause panel WITHOUT closing
            // PauseMenu (IsOpen stays true, Time.timeScale stays 0).
            // SettingsMenu.Show() takes over; on its close it calls
            // PauseMenu.Instance.ShowUI() to restore the pause panel.
            if (SettingsMenu.Instance == null)
            {
                // SettingsMenu self-bootstraps, so this should never happen —
                // but if it did, calling HideUI() below would strand the
                // player on a frozen, button-less screen (IsOpen stays true,
                // timeScale stays 0). Stay on the pause menu instead. (AP-7)
                Debug.unityLogger.LogWarning(TAG, "Settings unavailable — staying on the pause menu.");
                return;
            }
            HideUI();
            Debug.unityLogger.Log(TAG, "Settings button — opening SettingsMenu.");
            SettingsMenu.Instance.Show();
        }

        void OnExitClicked()
        {
            Time.timeScale = 1f;
            Debug.unityLogger.Log(TAG, "Back to Desktop — quitting.");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // Defensive: if the player navigates away from BuildScene / FlyScene
        // while the menu is open (e.g. the corner button's scene-switch
        // race-conditioned through), the menu should close cleanly.
        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Hangar button only makes sense in FlyScene — it routes
            // back to BuildScene via SceneSwitcher.Toggle. Hide it
            // elsewhere so BuildScene's pause menu just shows
            // Menu + Back to Desktop.
            if (_hangarButton != null)
                _hangarButton.gameObject.SetActive(scene.name == FlySceneName);

            if (!IsOpen) return;
            if (scene.name == BuildSceneName || scene.name == FlySceneName) return;
            // Mid-flight reset. Avoid Close()'s "save previousTimeScale"
            // path because the scene we're now in has its own pace.
            IsOpen = false;
            Time.timeScale = 1f;
            HideUI();
            OnClosed?.Invoke();
        }

        // ---------- UI construction ----------

        void BuildUI()
        {
            // Build the pause panel directly under the shared persistent
            // canvas. The full-screen dim is the panel root; it inherits
            // PersistentHud's DontDestroyOnLoad and sortingOrder (200)
            // for free — no canvas, no reparenting trick needed.
            GameObject panelGO = new GameObject("PauseMenuPanel",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panelGO.transform.SetParent(PersistentHud.Instance.Root, false);
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) panelGO.layer = uiLayer;
            _root = panelGO;

            RectTransform root = (RectTransform)panelGO.transform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            // The panel's own Image is the full-screen dim — also catches
            // clicks so nothing under the overlay can be reached.
            Image bgImage = panelGO.GetComponent<Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.70f);
            bgImage.raycastTarget = true;

            // Title.
            Text title = UIStyle.BuildLabel(root, "Paused", fontSize: 96, style: FontStyle.Bold);
            RectTransform trt = (RectTransform)title.transform;
            trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(800f, 160f);
            trt.anchoredPosition = new Vector2(0f, 140f);

            // Buttons stacked below the title. Same dimensions as the
            // MainMenu buttons to keep the visual language consistent.
            // Hangar is created first (top of the stack) but is only
            // visible in FlyScene — see OnSceneLoaded. Four-button
            // restack: 100 px gap between each button, centred around 0.
            _hangarButton = CreateButton(root, "Hangar",          new Vector2(0f,  150f), OnHangarClicked);
                            CreateButton(root, "Menu",            new Vector2(0f,   50f), OnMenuClicked);
                            CreateButton(root, "Settings",        new Vector2(0f,  -50f), OnSettingsClicked);
                            CreateButton(root, "Back to Desktop", new Vector2(0f, -150f), OnExitClicked);
        }

        static Button CreateButton(RectTransform parent, string text,
            Vector2 anchoredPos, UnityEngine.Events.UnityAction onClick)
        {
            (Button button, Text _) = UIStyle.BuildLabeledButton(
                parent, text, new Vector2(360f, 80f), fontSize: 36);
            RectTransform rt = (RectTransform)button.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            button.onClick.AddListener(onClick);
            return button;
        }

        internal void ShowUI()
        {
            if (_root != null) _root.SetActive(true);
        }

        internal void HideUI()
        {
            if (_root != null) _root.SetActive(false);
        }
    }
}
