using System;
using System.IO;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CubeFly.Core
{
    // Plain C# class (no MonoBehaviour). Replaces Unity's default log handler so
    // every Debug.Log* call also writes to a file, then forwards the call to the
    // original handler so the Unity Console still receives everything.
    public class FileLogHandler : ILogHandler
    {
        readonly StreamWriter _writer;
        readonly ILogHandler _defaultHandler;
        readonly object _lock = new object();
        bool _closed;

        public string FilePath { get; }

        public FileLogHandler(string filePath)
        {
            FilePath = filePath;
            _writer = new StreamWriter(new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read), Encoding.UTF8)
            {
                AutoFlush = true
            };
            _defaultHandler = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = this;
        }

        public void LogFormat(LogType logType, Object context, string format, params object[] args)
        {
            // Always forward first so the Console behaves normally even if our
            // own write throws.
            try
            {
                WriteFormatted(logType, format, args);
            }
            catch
            {
                // Logging must never bring down the game; swallow file errors.
            }
            _defaultHandler.LogFormat(logType, context, format, args);
        }

        public void LogException(Exception exception, Object context)
        {
            try
            {
                WriteException(exception);
            }
            catch
            {
                // Logging must never bring down the game; swallow file errors.
            }
            _defaultHandler.LogException(exception, context);
        }

        void WriteFormatted(LogType logType, string format, object[] args)
        {
            // Unity's tagged Logger.Log(tag, message) reaches us as
            // format = "{0}: {1}", args = [tag, message]. Detect that pattern so
            // the file output gets a proper [TAG] column.
            string tag;
            string message;
            if (format == "{0}: {1}" && args != null && args.Length == 2)
            {
                tag = args[0]?.ToString() ?? "-";
                message = args[1]?.ToString() ?? "";
            }
            else
            {
                tag = "-";
                message = (args != null && args.Length > 0) ? string.Format(format, args) : format;
            }

            string time = DateTime.Now.ToString("HH:mm:ss.fff");
            string line = $"[{time}] [{logType,-9}] [{tag}] {message}";

            lock (_lock)
            {
                if (_closed) return;
                _writer.WriteLine(line);
            }
        }

        void WriteException(Exception exception)
        {
            string time = DateTime.Now.ToString("HH:mm:ss.fff");
            string header = $"[{time}] [EXCEPTION] {exception.GetType().Name}: {exception.Message}";

            lock (_lock)
            {
                if (_closed) return;
                _writer.WriteLine(header);
                if (!string.IsNullOrEmpty(exception.StackTrace))
                {
                    foreach (string raw in exception.StackTrace.Split('\n'))
                    {
                        string trimmed = raw.TrimEnd('\r');
                        if (trimmed.Length > 0) _writer.WriteLine("  " + trimmed);
                    }
                }
            }
        }

        public void WriteRaw(string line)
        {
            lock (_lock)
            {
                if (_closed) return;
                _writer.WriteLine(line);
            }
        }

        public void Close()
        {
            lock (_lock)
            {
                if (_closed) return;
                _closed = true;
                if (Debug.unityLogger.logHandler == this)
                    Debug.unityLogger.logHandler = _defaultHandler;
                try
                {
                    _writer.Flush();
                    _writer.Dispose();
                }
                catch
                {
                    // Best-effort close.
                }
            }
        }
    }
}
