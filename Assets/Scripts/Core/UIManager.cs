using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CubeFly.Core
{
    // The corner scene-switch button + per-scene visibility / labelling.
    // Lives in PersistentHud's canvas so it survives scene transitions
    // without its own canvas / DontDestroyOnLoad bookkeeping.
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        Button _sceneSwitchButton;
        Text _buttonLabel;

        const string BuildSceneName        = "BuildScene";
        const string FlySceneName          = "FlyScene";
        const string HangarSelectSceneName = "HangarSelect";
        const string TAG = "UIManager";

        // Self-bootstrap mirrors PauseMenu / GameOverMenu. UIBootstrap.cs
        // (and the UICanvas.prefab it instantiated) used to handle this;
        // both are deleted in this commit.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            GameObject go = new GameObject("UIManager");
            go.AddComponent<UIManager>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.unityLogger.LogWarning(TAG, "UIManager duplicate destroyed.");
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;

            BuildButton();

            _sceneSwitchButton.onClick.RemoveListener(SceneSwitcher.Toggle);
            _sceneSwitchButton.onClick.AddListener(SceneSwitcher.Toggle);

            Debug.unityLogger.Log(TAG, "UIManager initialised. Corner button live in PersistentHud.");
        }

        void OnDestroy()
        {
            if (Instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void Start() => OnSceneStateChanged(SceneManager.GetActiveScene());

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            OnSceneStateChanged(scene);
            Debug.unityLogger.Log(TAG,
                $"Scene loaded: {scene.name}. Button label set to '{(_buttonLabel != null ? _buttonLabel.text : "<null>")}'");
        }

        // Per-scene visibility + label. The corner button only makes sense
        // on the BuildScene ("Fly!"); FlyScene uses the ESC pause menu's
        // Hangar button instead (UX batch 2026-05-20). MainMenu and
        // HangarSelect own their full screen and don't need it.
        // Reference HangarSelectSceneName so the intent stays greppable.
        void OnSceneStateChanged(Scene scene)
        {
            UpdateLabel(scene);
            _ = HangarSelectSceneName;

            if (_sceneSwitchButton != null)
            {
                _sceneSwitchButton.interactable = true;
                _sceneSwitchButton.gameObject.SetActive(scene.name == BuildSceneName);
            }
        }

        // Enable / disable the corner scene-switch button. BuildManager
        // calls this to gate the "Fly!" button while the construct
        // exceeds the active ship class's mass cap.
        public void SetSceneSwitchInteractable(bool interactable)
        {
            if (_sceneSwitchButton != null) _sceneSwitchButton.interactable = interactable;
        }

        void UpdateLabel(Scene scene)
        {
            if (_buttonLabel == null) return;
            _buttonLabel.text = scene.name == BuildSceneName ? "Fly!" : "Hangar";
        }

        // Build the corner button under PersistentHud's shared canvas.
        // PersistentHud.Instance triggers the canvas's lazy creation if
        // we're the first persistent UI script to Awake.
        void BuildButton()
        {
            (Button button, Text label) = UIStyle.BuildLabeledButton(
                PersistentHud.Instance.Root, "Fly!", new Vector2(220f, 64f), fontSize: 28);

            RectTransform brt = (RectTransform)button.transform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(1f, 1f);
            brt.anchoredPosition = new Vector2(-20f, -20f);

            _sceneSwitchButton = button;
            _buttonLabel = label;
        }
    }
}
