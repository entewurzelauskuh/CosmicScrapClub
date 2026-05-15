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

        const string BuildSceneName        = "BuildScene";
        const string FlySceneName          = "FlyScene";
        const string HangarSelectSceneName = "HangarSelect";
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
        // during gameplay; on the Main Menu and HangarSelect (slot picker)
        // the canvas is hidden so each owns its full screen. The check is
        // an explicit allowlist rather than a deny-list so future scenes
        // (HangarSelect, settings menus) default to "hide me".
        void OnSceneStateChanged(Scene scene)
        {
            UpdateLabel(scene);
            bool inGameplay = scene.name == BuildSceneName || scene.name == FlySceneName;
            // Reference the HangarSelect constant so the intent is greppable
            // from the .cs file even though the boolean already evaluates
            // to false there.
            _ = HangarSelectSceneName;
            if (_canvas != null) _canvas.enabled = inGameplay;

            // Reset the corner button to interactable on every scene
            // change. The FlyScene "Hangar" button is always clickable;
            // the BuildScene "Fly!" button starts clickable and gets
            // gated by BuildManager if the construct is over its mass
            // budget. sceneLoaded fires before BuildManager.Start, so
            // BuildManager re-evaluates the gate right after this reset.
            if (sceneSwitchButton != null) sceneSwitchButton.interactable = true;
        }

        // Enable / disable the corner scene-switch button. BuildManager
        // calls this to gate the "Fly!" button while the construct
        // exceeds the active ship class's mass cap — the player can go
        // over budget by switching to a lower-cap class, and shouldn't
        // be able to fly an over-budget construct.
        public void SetSceneSwitchInteractable(bool interactable)
        {
            if (sceneSwitchButton != null) sceneSwitchButton.interactable = interactable;
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
