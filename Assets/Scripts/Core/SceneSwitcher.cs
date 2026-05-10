using UnityEngine;
using UnityEngine.SceneManagement;

namespace CubeFly.Core
{
    public static class SceneSwitcher
    {
        public const string BuildSceneName = "BuildScene";
        public const string FlySceneName = "FlyScene";

        const string TAG = "SceneSwitcher";

        public static void Toggle()
        {
            string current = SceneManager.GetActiveScene().name;
            string next = current == BuildSceneName ? FlySceneName : BuildSceneName;
            Debug.unityLogger.Log(TAG, $"SceneSwitcher: transitioning from '{current}' to '{next}'");
            SceneManager.LoadScene(next);
        }
    }
}
