using System;
using System.IO;
using ModDebugKit.Commands;
using ModDebugKit.Io;
using ModDebugKit.Scripting;

namespace ModDebugKit.Scripting
{
    /// <summary>
    /// The script-runner front-end: <c>dbg.run &lt;name&gt;</c> loads and runs a
    /// timed scenario, <c>dbg.stop</c> cancels it. This generalizes a manual
    /// sequence of file-channel commands into one repeatable file.
    /// </summary>
    public static class ScriptCommands
    {
        public static void RegisterAll(CommandDispatcher dispatcher)
        {
            dispatcher.Register("dbg.run",
                "dbg.run <name|path> - run a timed script of dbg commands (scripts/<name>.json)",
                Run);
            dispatcher.Register("dbg.stop",
                "dbg.stop - stop the running script",
                Stop);
        }

        private static DbgOutcome Run(DbgCommand command)
        {
            if (command.Arg(0) == null)
                return DbgOutcome.Failure("usage: dbg.run <name|path>");
            if (ScriptRunner.Running)
                return DbgOutcome.Failure("a script is already running (dbg.stop first)");

            if (!TryLoad(command.Arg(0), out var script, out var name, out var error))
                return DbgOutcome.Failure(error);

            return ScriptRunner.Start(script, name);
        }

        private static DbgOutcome Stop(DbgCommand command)
        {
            var wasRunning = ScriptRunner.Running;
            ScriptRunner.Stop();
            return DbgOutcome.Success(wasRunning ? "script stopped" : "no script running");
        }

        private static bool TryLoad(string arg, out DbgScript script, out string name, out string error)
        {
            script = null;
            name = null;
            error = null;

            var paths = ModDebugKitRuntime.Paths;
            var looksLikePath = arg.IndexOf('/') >= 0 || arg.IndexOf('\\') >= 0 ||
                                arg.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            var path = looksLikePath ? paths.Resolve(arg) : Path.Combine(paths.ScriptsDir, arg + ".json");
            name = Path.GetFileNameWithoutExtension(path);

            if (!File.Exists(path))
            {
                error = $"script not found: {path}";
                return false;
            }

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                error = $"could not read script '{path}': {e.Message}";
                return false;
            }

            if (!DbgJson.TryDeserialize<DbgScript>(json, out script, out var parseError))
            {
                error = $"script parse error in '{path}': {parseError}";
                return false;
            }

            return true;
        }
    }
}
