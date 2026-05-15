using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CubeFly.Core
{
    // Game-over overlay shown when the alpha cube reaches 0 HP. Same
    // DDOL singleton + BeforeSceneLoad bootstrap pattern as PauseMenu
    // so every play session has exactly one instance with no scene
    // wiring required.
    //
    // Behaviour:
    //   • TriggerGameOver() is the public entry point — currently
    //     called from CubeDamage.ApplyAndLog when fatal damage lands
    //     on an alpha-tagged cube. Idempotent: subsequent calls while
    //     the overlay is already open are no-ops.
    //   • While open, Time.timeScale = 0 freezes physics + any
    //     coroutine running on Time.deltaTime (the in-flight CubeDeath
    //     drifts of recently-killed cubes pause mid-animation; the
    //     construct stops translating).
    //   • One button: "Return to main menu" → load MainMenu. ESC
    //     does nothing; the construct is destroyed, there's no
    //     resuming.
    //   • The overlay's full-screen panel sits at sortingOrder 400 so
    //     it covers PauseMenu (300), UIManager (100), and the build /
    //     fly toolbars (90 / 120). Its GraphicRaycaster intercepts
    //     clicks so nothing under it is reachable.
    [DefaultExecutionOrder(-1000)]
    public class GameOverMenu : MonoBehaviour
    {
        public static GameOverMenu Instance { get; private set; }

        public bool IsOpen { get; private set; }

        public static event Action OnTriggered;

        const string TAG = "GameOverMenu";
        const string MainMenuSceneName = "MainMenu";

        GameObject _root;

        // Self-bootstrap: spawn the singleton before any scene loads.
        // BeforeSceneLoad runs once per play session in both Editor
        // and standalone, so there's no risk of duplicates.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            GameObject go = new GameObject("GameOverMenu");
            go.AddComponent<GameOverMenu>();
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

            UIStyle.EnsureEventSystem();
            BuildUI();
            HideUI();

            SceneManager.sceneLoaded += OnSceneLoaded;
            Debug.unityLogger.Log(TAG, "Game-over menu ready.");
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                Instance = null;
            }
        }

        // Idempotent — call from anywhere (CubeDamage when alpha dies,
        // future end-of-run sources). If already open, no-op.
        public void TriggerGameOver()
        {
            if (IsOpen) return;
            IsOpen = true;
            Time.timeScale = 0f;
            ShowUI();
            Debug.unityLogger.Log(TAG, "Triggered. Construct destroyed.");
            OnTriggered?.Invoke();
        }

        void OnMainMenuClicked()
        {
            // Restore timeScale BEFORE the scene load so MainMenu doesn't
            // wake up frozen.
            Time.timeScale = 1f;
            IsOpen = false;
            HideUI();
            Debug.unityLogger.Log(TAG, "Return to main menu — loading MainMenu.");
            SceneManager.LoadScene(MainMenuSceneName);
        }

        // Defensive: if some other path navigates away while the overlay
        // is up (e.g. the corner UI button is somehow clicked, or a
        // future scripted transition fires), close cleanly so the
        // overlay doesn't bleed into the next scene.
        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!IsOpen) return;
            IsOpen = false;
            Time.timeScale = 1f;
            HideUI();
        }

        // ---------- UI construction ----------

        void BuildUI()
        {
            // Sit above PauseMenu (sortingOrder 300) and every other UI.
            Canvas canvas = UIStyle.BuildScreenSpaceCanvas("GameOverMenuCanvas", sortingOrder: 400);
            _root = canvas.gameObject;
            // UIStyle creates the canvas as a free-standing root
            // GameObject. Re-parent it under this DDOL singleton so the
            // canvas inherits DontDestroyOnLoad — otherwise the first
            // scene transition would destroy the UI behind our back.
            // worldPositionStays:false keeps the screen-space-overlay
            // canvas sized to the screen.
            _root.transform.SetParent(transform, worldPositionStays: false);
            RectTransform root = (RectTransform)canvas.transform;

            // Full-screen dim background that also catches clicks so
            // nothing under the overlay can be reached. Slightly
            // darker / redder than PauseMenu's dim to signal the
            // different mood.
            GameObject bgGO = new GameObject("Dim",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bgGO.transform.SetParent(root, false);
            RectTransform bgRT = (RectTransform)bgGO.transform;
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;
            Image bgImage = bgGO.GetComponent<Image>();
            bgImage.color = new Color(0.15f, 0.04f, 0.04f, 0.85f);
            bgImage.raycastTarget = true;

            // Title.
            Text title = UIStyle.BuildLabel(root, "Construct Destroyed",
                fontSize: 84, style: FontStyle.Bold);
            RectTransform trt = (RectTransform)title.transform;
            trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(900f, 160f);
            trt.anchoredPosition = new Vector2(0f, 100f);

            // One button, centred below the title. Same dimensions as
            // the PauseMenu buttons so the two overlays feel like the
            // same family.
            (Button button, Text _) = UIStyle.BuildLabeledButton(
                root, "Return to main menu", new Vector2(420f, 80f), fontSize: 32);
            RectTransform rt = (RectTransform)button.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -60f);
            button.onClick.AddListener(OnMainMenuClicked);
        }

        void ShowUI()
        {
            if (_root != null) _root.SetActive(true);
        }

        void HideUI()
        {
            if (_root != null) _root.SetActive(false);
        }
    }
}
