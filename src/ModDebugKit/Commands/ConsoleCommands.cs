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

        private static string Run(string fullName, List<string> args)
        {
            var dispatcher = ModDebugKitRuntime.Dispatcher;
            if (dispatcher == null)
                return "ModDebugKit is not initialized.";
            return dispatcher.ExecuteRaw(fullName, args).Message;
        }
    }
}
