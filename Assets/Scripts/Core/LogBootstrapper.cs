using System;
using System.IO;
using UnityEngine;

namespace CubeFly.Core
{
    // Owns the FileLogHandler for the lifetime of a play session.
    // Self-bootstraps via [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]
    // — same pattern as UIManager / PauseMenu / GameOverMenu after the
    // HUD-consolidation refactor (PR #41). Previously it lived on the
    // UICanvas prefab; that prefab is gone, so the script now spawns its
    // own DDOL host before any scene loads.
    public class LogBootstrapper : MonoBehaviour
    {
        public static LogBootstrapper Instance { get; private set; }

        const string TAG = "LogBootstrapper";

        FileLogHandler _fileLogHandler;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            GameObject go = new GameObject("LogBootstrapper");
            go.AddComponent<LogBootstrapper>();
        }

        void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            string logsDir = Path.Combine(Application.persistentDataPath, "Logs");
            Directory.CreateDirectory(logsDir);
            string fileName = $"CubeFly_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log";
            string fullPath = Path.Combine(logsDir, fileName);

            _fileLogHandler = new FileLogHandler(fullPath);

            // Header line bypasses tag formatting so the file starts with the
            // session banner exactly as the spec example shows.
            _fileLogHandler.WriteRaw(
                $"=== CubeFly session started. Unity {Application.unityVersion}, " +
                $"Platform {Application.platform}, persistentDataPath {Application.persistentDataPath} ===");

            Debug.unityLogger.Log(TAG, $"Log file: {fullPath}");

            Application.quitting += OnQuit;
        }

        void OnQuit()
        {
            _fileLogHandler?.Close();
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Application.quitting -= OnQuit;
            _fileLogHandler?.Close();
            Instance = null;
        }
    }
}
