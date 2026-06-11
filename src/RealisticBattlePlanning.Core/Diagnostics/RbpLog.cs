using System;
using System.IO;

namespace RealisticBattlePlanning.Diagnostics
{
    /// <summary>
    /// Plan-event log, written to Logs\rbp.log under the deployed module.
    /// Spec R2: this log is where intended fidelity noise gets distinguished
    /// from genuine execution faults, so every plan event goes through here.
    /// Must never throw into the game.
    ///
    /// Lives in Core (engine-free); the engine assembly plugs its debug
    /// channel into <see cref="MirrorSink"/> so warnings/errors also reach
    /// the game's rgl log.
    /// </summary>
    public static class RbpLog
    {
        private static readonly object Sync = new();
        private static string _path;

        /// <summary>Optional secondary channel for WRN/ERR lines (engine sets this to Debug.Print).</summary>
        public static Action<string> MirrorSink { get; set; }

        public static void Init(string moduleRoot)
        {
            try
            {
                var dir = Path.Combine(moduleRoot, "Logs");
                Directory.CreateDirectory(dir);
                _path = Path.Combine(dir, "rbp.log");
                File.WriteAllText(_path, $"== RealisticBattlePlanning session {DateTime.Now:yyyy-MM-dd HH:mm:ss} =={Environment.NewLine}");
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
            Mirror($"[RBP][WRN] {message}");
        }

        public static void Error(string message, Exception e = null)
        {
            Write("ERR", e == null ? message : $"{message}{Environment.NewLine}{e}");
            Mirror($"[RBP][ERR] {message}");
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
