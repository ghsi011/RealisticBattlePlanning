using System.Collections.Generic;
using TaleWorlds.Library;

namespace ModDebugKit.Commands
{
    /// <summary>
    /// Console front-end (Alt+grave). Each command just forwards to the shared
    /// dispatcher, so the console and the file channel run the exact same
    /// handler — console parity for free. Results show in the console; the file
    /// channel additionally records them to out.jsonl.
    /// </summary>
    public static class ConsoleCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("ping", "dbg")]
        public static string Ping(List<string> args) => Run("dbg.ping", args);

        [CommandLineFunctionality.CommandLineArgumentFunction("help", "dbg")]
        public static string Help(List<string> args) => Run("dbg.help", args);

        [CommandLineFunctionality.CommandLineArgumentFunction("snapshot", "dbg")]
        public static string Snapshot(List<string> args) => Run("dbg.snapshot", args);

        [CommandLineFunctionality.CommandLineArgumentFunction("battle", "dbg")]
        public static string Battle(List<string> args) => Run("dbg.battle", args);

        [CommandLineFunctionality.CommandLineArgumentFunction("ready", "dbg")]
        public static string Ready(List<string> args) => Run("dbg.ready", args);

        [CommandLineFunctionality.CommandLineArgumentFunction("leave", "dbg")]
        public static string Leave(List<string> args) => Run("dbg.leave", args);

        [CommandLineFunctionality.CommandLineArgumentFunction("restart", "dbg")]
        public static string Restart(List<string> args) => Run("dbg.restart", args);

        [CommandLineFunctionality.CommandLineArgumentFunction("assign", "dbg")]
        public static string Assign(List<string> args) => Run("dbg.assign", args);

        [CommandLineFunctionality.CommandLineArgumentFunction("layout", "dbg")]
        public static string Layout(List<string> args) => Run("dbg.layout", args);

        [CommandLineFunctionality.CommandLineArgumentFunction("telemetry", "dbg")]
        public static string Telemetry(List<string> args) => Run("dbg.telemetry", args);

        private static string Run(string fullName, List<string> args)
        {
            var dispatcher = ModDebugKitRuntime.Dispatcher;
            if (dispatcher == null)
                return "ModDebugKit is not initialized.";
            return dispatcher.ExecuteRaw(fullName, args).Message;
        }
    }
}
