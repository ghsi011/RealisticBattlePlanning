using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Library;

namespace RealisticBattlePlanning.Harness
{
    /// <summary>
    /// Dev-console surface of the Layer-2 harness (open the console via BLSE
    /// / dev mode). Flow: rbp.harness_arm &lt;scenario...&gt;|all, fight the
    /// armed battles (everything after clicking Ready is hands-off), then
    /// rbp.harness_diff / rbp.harness_accept on the written results.
    /// </summary>
    public static class HarnessCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("harness_list", "rbp")]
        public static string List(List<string> args)
        {
            var names = HarnessSession.ListScenarioNames();
            return names.Count == 0
                ? $"no scenarios in {HarnessSession.ScenariosDir}"
                : string.Join("\n", names);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("harness_arm", "rbp")]
        public static string Arm(List<string> args)
        {
            if (args.Count == 0)
                return "usage: rbp.harness_arm <scenario...> | all";

            var names = args.Count == 1 && args[0].Equals("all", StringComparison.OrdinalIgnoreCase)
                ? HarnessSession.ListScenarioNames()
                : (IReadOnlyList<string>)args;

            var error = HarnessSession.Arm(names);
            return error ?? $"armed {names.Count} scenario(s): {string.Join(", ", names)}. " +
                            "Start a field battle you command to run the first one.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("harness_disarm", "rbp")]
        public static string Disarm(List<string> args)
        {
            HarnessSession.Disarm();
            return "harness disarmed";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("harness_status", "rbp")]
        public static string Status(List<string> args) => HarnessSession.Status();

        [CommandLineFunctionality.CommandLineArgumentFunction("harness_diff", "rbp")]
        public static string Diff(List<string> args) => HarnessSession.DiffLastRun();

        [CommandLineFunctionality.CommandLineArgumentFunction("harness_accept", "rbp")]
        public static string Accept(List<string> args) => HarnessSession.AcceptLastRun();

        /// <summary>
        /// Fills the active plan's formation slots by redistributing the
        /// player's troops — no manual Order-of-Battle setup. Armed harness
        /// runs do this automatically at deployment; this is the manual
        /// trigger for debug-plan runs or a re-split.
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("harness_split", "rbp")]
        public static string Split(List<string> args)
            => Execution.PlanMissionLogic.Active?.SplitTroopsForPlan() ?? "no plan is active this battle";
    }
}
