using CubeFly.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CubeFly.MainMenu
{
    // Builds the Main Menu UI on Awake: title + three buttons (Hangar /
    // Settings / Exit). Lives in its own scene as the first thing the player
    // sees. The persistent UICanvas (corner button) hides itself when this
    // scene is active — see UIManager.OnSceneStateChanged.
    public class MainMenuController : MonoBehaviour
    {
        const string TAG = "MainMenu";
        const string BuildSceneName = "BuildScene";

        void Awake()
        {
            BuildUI();
            Debug.unityLogger.Log(TAG, "Main Menu initialised.");
        }

        void BuildUI()
        {
            UIStyle.EnsureEventSystem();
            // Sit above any persistent UICanvas (sortingOrder 100); the latter
            // should be hidden anyway, but this is defensive.
            Canvas canvas = UIStyle.BuildScreenSpaceCanvas("MainMenuCanvas", sortingOrder: 200);
            RectTransform root = (RectTransform)canvas.transform;

            // Title
            Text title = UIStyle.BuildLabel(root, "Cube Fly", fontSize: 96, style: FontStyle.Bold);
            RectTransform trt = (RectTransform)title.transform;
            trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(800f, 160f);
            trt.anchoredPosition = new Vector2(0f, 220f);

            // Buttons stacked vertically.
            CreateMenuButton(root, "Hangar",   new Vector2(0f, 40f),    OnHangar);
            CreateMenuButton(root, "Settings", new Vector2(0f, -60f),   OnSettings);
            CreateMenuButton(root, "Exit",     new Vector2(0f, -160f),  OnExit);
        }

        static void CreateMenuButton(RectTransform parent, string text,
            Vector2 anchoredPos, UnityEngine.Events.UnityAction onClick)
        {
            (Button button, Text _) = UIStyle.BuildLabeledButton(
                parent, text, new Vector2(360f, 80f), fontSize: 36);
            RectTransform rt = (RectTransform)button.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            button.onClick.AddListener(onClick);
        }

        void OnHangar()
        {
            Debug.unityLogger.Log(TAG, "Hangar selected — loading BuildScene.");
            SceneManager.LoadScene(BuildSceneName);
        }

        void OnSettings()
        {
            // Placeholder: Settings panel is a future feature.
            Debug.unityLogger.Log(TAG, "Settings selected — not implemented yet.");
        }

        void OnExit()
        {
            Debug.unityLogger.Log(TAG, "Exit selected — quitting.");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
