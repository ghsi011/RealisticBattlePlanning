using System.Collections.Generic;
using TaleWorlds.Library;
using RealisticBattlePlanning.Planning;

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
            var host = PlanMissionLogic.Active;
            if (host == null)
                return "no plan is active this battle";
            if (args.Count != 1)
                return "usage: rbp.resume <formation|all>";
            return host.RequestResume(args[0]);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("plan_status", "rbp")]
        public static string Status(List<string> args)
            => PlanMissionLogic.Active?.DescribePlanStates() ?? "no plan is active this battle";

        /// <summary>
        /// Reports the session-scoped plan-carry state (Area G): the session key the carry
        /// is bound to ("custom", or "campaign:&lt;id&gt;" in a campaign) and whether a plan is
        /// carried for it. Works from anywhere (menu, battle, campaign map) so the key
        /// derivation and the no-cross-game-leak behaviour are directly observable.
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("session", "rbp")]
        public static string Session(List<string> args)
        {
            var key = PlanMissionLogic.SessionKey();
            var carried = SessionPlanStore.CurrentFor(key);
            var plan = carried == null
                ? "none"
                : $"yes ({carried.Formations.Count} formation(s))";
            return $"session key: {key} | carried plan: {plan}";
        }

        /// <summary>
        /// Signal Palette fallback and the C7 drill-cue mechanism (B9):
        /// always works, regardless of keybinds. Undeclared names are
        /// allowed (drill cues) but called out in the response.
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("signal", "rbp")]
        public static string Signal(List<string> args)
        {
            var host = PlanMissionLogic.Active;
            if (host == null)
                return "no plan is active this battle";
            if (args.Count != 1 || string.IsNullOrWhiteSpace(args[0]))
                return "usage: rbp.signal <name>";
            return host.FirePlayerSignal(args[0]);
        }
    }
}
