using System;
using System.IO;
using System.Threading;
using ModDebugKit.Diagnostics;
using ModDebugKit.Io;

namespace ModDebugKit.Telemetry
{
    /// <summary>
    /// Appends typed events to <c>telemetry.jsonl</c> — the flight recorder.
    /// Engine-side because callers pass mission time; the event shape and the
    /// JSON live in Core. On by default (a recorder should always be running);
    /// toggle/clear via <c>dbg.telemetry</c>. Never throws into the game.
    /// </summary>
    public static class TelemetryLog
    {
        private static readonly object Sync = new();
        private static long _seq;

        public static bool Enabled { get; set; } = true;

        public static void Write(string kind, float? missionTime, string message = null, object data = null)
        {
            if (!Enabled)
                return;
            try
            {
                var ev = new TelemetryEvent
                {
                    Seq = Interlocked.Increment(ref _seq),
                    TimestampUtc = DateTime.UtcNow.ToString("o"),
                    MissionTime = missionTime,
                    Kind = kind,
                    Message = message,
                    Data = data,
                };
                var line = DbgJson.Line(ev);
                lock (Sync)
                {
                    File.AppendAllText(ModDebugKitRuntime.Paths.Telemetry, line + Environment.NewLine);
                }
            }
            catch (Exception e)
            {
                DbgLog.Error("Telemetry write failed.", e);
            }
        }

        public static void Clear()
        {
            try
            {
                lock (Sync)
                {
                    File.WriteAllText(ModDebugKitRuntime.Paths.Telemetry, string.Empty);
                    Interlocked.Exchange(ref _seq, 0);
                }
            }
            catch (Exception e)
            {
                DbgLog.Error("Telemetry clear failed.", e);
            }
        }
    }
}
