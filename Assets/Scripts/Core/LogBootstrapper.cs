using System;
using System.IO;
using UnityEngine;

namespace CubeFly.Core
{
    // Owns the FileLogHandler for the lifetime of a play session. Lives on the
    // UICanvas prefab next to UIManager, so the existing UIBootstrap mechanism
    // also bootstraps logging — no separate scene wiring needed.
    public class LogBootstrapper : MonoBehaviour
    {
        public static LogBootstrapper Instance { get; private set; }

        const string TAG = "LogBootstrapper";

        FileLogHandler _fileLogHandler;

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
