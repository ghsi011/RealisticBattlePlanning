using System;
using System.IO;
using System.Threading;
using ModDebugKit.Commands;
using ModDebugKit.Diagnostics;
using ModDebugKit.Io;
using ModDebugKit.Observability;
using TaleWorlds.MountAndBlade;

namespace ModDebugKit.Diagnostics
{
    /// <summary>
    /// Captures faults to <c>errors.jsonl</c>: the kit's own caught errors
    /// (routed from <see cref="DbgLog.ErrorSink"/>) and any unhandled AppDomain
    /// exception, each with its stack. On the first fault it also writes an
    /// <c>error_snapshot.json</c> of the live battle, so a crash leaves a trail.
    /// Reentrancy-guarded and self-contained — it never calls back into
    /// <see cref="DbgLog"/> (which would recurse).
    /// </summary>
    public static class ErrorLog
    {
        private static readonly object Sync = new();
        private static long _seq;
        private static bool _capturing;
        private static bool _snapshotTaken;

        public static bool Enabled { get; set; } = true;

        /// <summary>Adapter for <see cref="DbgLog.ErrorSink"/>.</summary>
        public static void FromLog(string message, Exception exception) =>
            Capture("modkit", message, exception, terminating: null);

        public static void Capture(string source, string message, Exception exception, bool? terminating)
        {
            if (!Enabled)
                return;

            lock (Sync)
            {
                if (_capturing)
                    return; // a fault while capturing a fault — drop it rather than recurse
                _capturing = true;
            }

            try
            {
                var record = new ErrorRecord
                {
                    Seq = Interlocked.Increment(ref _seq),
                    TimestampUtc = DateTime.UtcNow.ToString("o"),
                    Source = source,
                    Message = message,
                    ExceptionType = exception?.GetType().FullName,
                    Stack = exception?.ToString(),
                    Terminating = terminating,
                };

                try
                {
                    var mission = Mission.Current;
                    if (mission != null)
                        record.MissionTime = mission.CurrentTime;
                }
                catch (Exception)
                {
                    // best-effort mission time
                }

                if (!_snapshotTaken)
                {
                    _snapshotTaken = true;
                    record.Snapshot = TryAutoSnapshot();
                }

                var line = DbgJson.Line(record);
                File.AppendAllText(ModDebugKitRuntime.Paths.Errors, line + Environment.NewLine);
            }
            catch (Exception)
            {
                // Capturing a fault must never throw (or recurse into DbgLog).
            }
            finally
            {
                lock (Sync)
                    _capturing = false;
            }
        }

        public static void Clear()
        {
            try
            {
                lock (Sync)
                {
                    File.WriteAllText(ModDebugKitRuntime.Paths.Errors, string.Empty);
                    Interlocked.Exchange(ref _seq, 0);
                    _snapshotTaken = false;
                }
            }
            catch (Exception)
            {
            }
        }

        public static void RegisterCommands(CommandDispatcher dispatcher)
        {
            dispatcher.Register("dbg.errors",
                "dbg.errors [on|off|clear|status] - the captured fault stream (errors.jsonl)",
                Errors);
        }

        private static string TryAutoSnapshot()
        {
            try
            {
                var mission = Mission.Current;
                if (mission == null)
                    return null;
                var dto = BattleSnapshotReader.Capture(mission);
                var path = Path.Combine(ModDebugKitRuntime.Paths.Root, "error_snapshot.json");
                File.WriteAllText(path, DbgJson.Pretty(dto));
                return path;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static DbgOutcome Errors(DbgCommand command)
        {
            switch (command.Arg(0)?.Trim().ToLowerInvariant())
            {
                case "on":
                    Enabled = true;
                    return DbgOutcome.Success("error capture on");
                case "off":
                    Enabled = false;
                    return DbgOutcome.Success("error capture off");
                case "clear":
                    Clear();
                    return DbgOutcome.Success("errors cleared");
                case null:
                case "status":
                    return DbgOutcome.Success(
                        $"error capture {(Enabled ? "on" : "off")} -> {ModDebugKitRuntime.Paths.Errors}",
                        new { enabled = Enabled, path = ModDebugKitRuntime.Paths.Errors });
                default:
                    return DbgOutcome.Failure("usage: dbg.errors [on|off|clear|status]");
            }
        }
    }
}
