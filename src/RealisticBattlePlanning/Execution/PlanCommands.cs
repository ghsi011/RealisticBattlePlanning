using System.Collections.Generic;
using TaleWorlds.Library;

namespace RealisticBattlePlanning.Execution
{
    /// <summary>
    /// Console surface for plan control during a battle (B5). The order-menu
    /// "Resume plan" entry is deferred to the UI iterations (I9+); these
    /// commands are the working path until then, and stay as the dev/test
    /// path afterwards.
    /// </summary>
    public static class PlanCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("resume", "rbp")]
        public static string Resume(List<string> args)
        {
            if (PlanMissionLogic.Current == null)
                return "no plan is active this battle";
            if (args.Count != 1)
                return "usage: rbp.resume <formation|all>";
            return PlanMissionLogic.Current.RequestResume(args[0]);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("plan_status", "rbp")]
        public static string Status(List<string> args)
            => PlanMissionLogic.Current?.DescribePlanStates() ?? "no plan is active this battle";
    }
}
