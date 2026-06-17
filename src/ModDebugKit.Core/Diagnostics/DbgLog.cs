using System;
using System.IO;

namespace ModDebugKit.Diagnostics
{
    /// <summary>
    /// Session log for the kit itself, written to <c>moddebugkit.log</c> under
    /// the Debug output root. Lives in Core (engine-free); the engine assembly
    /// plugs its debug channel into <see cref="MirrorSink"/> so warnings/errors
    /// also reach the game's rgl log. Must never throw into the game.
    /// </summary>
    public static class DbgLog
    {
        private static readonly object Sync = new();
        private static string _path;

        /// <summary>Optional secondary channel for WRN/ERR lines (engine sets this to Debug.Print).</summary>
        public static Action<string> MirrorSink { get; set; }

        public static void Init(string logFilePath)
        {
            try
            {
                var dir = Path.GetDirectoryName(logFilePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                _path = logFilePath;
                File.WriteAllText(_path, $"== ModDebugKit session {DateTime.Now:yyyy-MM-dd HH:mm:ss} =={Environment.NewLine}");
            }
            catch (Exception)
            {
                _path = null;
            }
        }

        public static void Info(string message) => Write("INF", message);

        public static void Warn(string message)
        {
            Write("WRN", message);
            Mirror($"[MDK][WRN] {message}");
        }

        public static void Error(string message, Exception e = null)
        {
            Write("ERR", e == null ? message : $"{message}{Environment.NewLine}{e}");
            Mirror($"[MDK][ERR] {message}");
        }

        private static void Write(string level, string message)
        {
            if (_path == null) return;
            try
            {
                lock (Sync)
                {
                    File.AppendAllText(_path, $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}");
                }
            }
            catch (Exception)
            {
                // Logging must never take the game down.
            }
        }

        private static void Mirror(string message)
        {
            try
            {
                MirrorSink?.Invoke(message);
            }
            catch (Exception)
            {
            }
        }
    }
}
