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

            if (!JsonFileLibrary.TryLoad<DbgScript>("script", command.Arg(0), ModDebugKitRuntime.Paths.ScriptsDir, out var script, out var path, out var error))
                return DbgOutcome.Failure(error);

            return ScriptRunner.Start(script, System.IO.Path.GetFileNameWithoutExtension(path));
        }

        private static DbgOutcome Stop(DbgCommand command)
        {
            var wasRunning = ScriptRunner.Running;
            ScriptRunner.Stop();
            return DbgOutcome.Success(wasRunning ? "script stopped" : "no script running");
        }
    }
}
