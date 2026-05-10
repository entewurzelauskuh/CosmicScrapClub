using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CubeFly.Core
{
    [RequireComponent(typeof(Canvas))]
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [SerializeField] Button sceneSwitchButton;
        [SerializeField] Text buttonLabel;

        const string BuildSceneName = "BuildScene";
        const string FlySceneName   = "FlyScene";
        const string TAG = "UIManager";

        Canvas _canvas;

        void Awake()
        {
            if (Instance != null)
            {
                Debug.unityLogger.LogWarning(TAG, "UIManager duplicate destroyed.");
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;

            _canvas = GetComponent<Canvas>();

            UIStyle.EnsureEventSystem();
            if (sceneSwitchButton == null || buttonLabel == null)
                BuildButton();

            sceneSwitchButton.onClick.RemoveListener(SceneSwitcher.Toggle);
            sceneSwitchButton.onClick.AddListener(SceneSwitcher.Toggle);

            Debug.unityLogger.Log(TAG, "UIManager initialised. Canvas live.");
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
                $"Scene loaded: {scene.name}. Button label set to '{(buttonLabel != null ? buttonLabel.text : "<null>")}'");
        }

        // Per-scene visibility + label. The corner button only makes sense
        // during gameplay; on the Main Menu the canvas is hidden so the menu
        // owns the screen.
        void OnSceneStateChanged(Scene scene)
        {
            UpdateLabel(scene);
            bool inGameplay = scene.name == BuildSceneName || scene.name == FlySceneName;
            if (_canvas != null) _canvas.enabled = inGameplay;
        }

        void UpdateLabel(Scene scene)
        {
            if (buttonLabel == null) return;
            buttonLabel.text = scene.name == BuildSceneName ? "Fly!" : "Hangar";
        }

        // Build the corner button using the shared style so it matches the
        // Main Menu buttons exactly.
        void BuildButton()
        {
            (Button button, Text label) = UIStyle.BuildLabeledButton(
                transform, "Fly!", new Vector2(220f, 64f), fontSize: 28);

            RectTransform brt = (RectTransform)button.transform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(1f, 1f);
            brt.anchoredPosition = new Vector2(-20f, -20f);

            sceneSwitchButton = button;
            buttonLabel = label;
        }
    }
}
