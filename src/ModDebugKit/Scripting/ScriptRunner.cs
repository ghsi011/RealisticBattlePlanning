using System;
using System.Linq;
using ModDebugKit.Commands;
using ModDebugKit.Diagnostics;
using ModDebugKit.Io;
using ModDebugKit.Scripting;

namespace ModDebugKit.Scripting
{
    /// <summary>
    /// Runs a <see cref="DbgScript"/> against wall-clock elapsed time (pumped
    /// from OnApplicationTick, so a script spans menu → battle → result). Each
    /// due step is dispatched like any command and its result journaled to
    /// out.jsonl. Real time, not mission time, so steps fire across loads and at
    /// the menu (e.g. dbg.battle then wait then dbg.ready).
    /// </summary>
    public static class ScriptRunner
    {
        private static DbgScriptScheduler _scheduler;
        private static float _elapsed;
        private static string _name;

        public static bool Running => _scheduler != null && !_scheduler.Done;

        public static DbgOutcome Start(DbgScript script, string name)
        {
            var scheduler = new DbgScriptScheduler(script);
            if (scheduler.Count == 0)
                return DbgOutcome.Failure("script has no runnable steps");

            _scheduler = scheduler;
            _elapsed = 0f;
            _name = name;
            DbgLog.Info($"Script '{name}': started ({scheduler.Count} step(s)).");
            return DbgOutcome.Success($"running script '{name}' ({scheduler.Count} step(s))", new { name, steps = scheduler.Count });
        }

        public static void Stop()
        {
            if (_scheduler == null)
                return;
            DbgLog.Info($"Script '{_name}': stopped.");
            _scheduler = null;
        }

        public static void Tick(float dt)
        {
            if (_scheduler == null)
                return;

            _elapsed += dt;
            foreach (var step in _scheduler.Due(_elapsed).ToList())
            {
                try
                {
                    if (!DbgCommandParser.TryParse(step.Do, out var command, out _))
                        continue;

                    // A script invoking dbg.run/dbg.stop would fight the runner — ignore.
                    if (command.Full.Equals("dbg.run", StringComparison.OrdinalIgnoreCase) ||
                        command.Full.Equals("dbg.stop", StringComparison.OrdinalIgnoreCase))
                    {
                        DbgLog.Warn($"Script '{_name}': nested '{command.Full}' ignored.");
                        continue;
                    }

                    var result = ModDebugKitRuntime.Dispatcher.Execute(command);
                    CommandJournal.Append(result);
                    DbgLog.Info($"Script '{_name}': @{_elapsed:0.0}s {step.Do} => {(result.Ok ? "ok" : "ERR")}");
                }
                catch (Exception e)
                {
                    DbgLog.Error($"Script '{_name}': step '{step.Do}' threw.", e);
                }
            }

            if (_scheduler.Done)
            {
                DbgLog.Info($"Script '{_name}': finished.");
                _scheduler = null;
            }
        }
    }
}
