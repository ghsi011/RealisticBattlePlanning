using System.Collections.Generic;
using TaleWorlds.Library;
using RealisticBattlePlanning.Planning;
using RealisticBattlePlanning.Planning.Model;

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
        /// Dev/test plan injection: validate a plan JSON file and stage it for
        /// the NEXT battle via the session carry — or apply it to the current
        /// battle immediately when one is running. Turns "try this plan tweak"
        /// into: edit JSON → rbp.plan_load &lt;file&gt; → dbg.restart. With no
        /// argument it loads the classic debug plan
        /// (ModuleData\rbp_debug_plan.json) — the reachable replacement for
        /// DebugPlanLoader.Enabled, which nothing could ever set.
        /// </summary>
        [CommandLineFunctionality.CommandLineArgumentFunction("plan_load", "rbp")]
        public static string PlanLoad(List<string> args)
        {
            BattlePlan plan;
            if (args.Count == 0)
            {
                plan = DebugPlanLoader.TryLoad();
                if (plan == null)
                    return $"no debug plan (ModuleData\\{DebugPlanLoader.FileName}) or it failed to parse — see rbp.log";
            }
            else
            {
                var path = string.Join(" ", args);
                if (!System.IO.File.Exists(path))
                    return $"file not found: {path}";
                string json;
                try { json = System.IO.File.ReadAllText(path); }
                catch (System.Exception e) { return $"read failed: {e.Message}"; }
                if (!PlanSerializer.TryDeserialize(json, out plan, out var error))
                    return $"not a valid plan: {error}";
            }

            var validation = PlanValidator.Validate(plan);
            if (!validation.IsValid)
                return "plan invalid: " + validation.Errors[0];
            var warnings = validation.Warnings.Count > 0 ? $" ({validation.Warnings.Count} warning(s) — see rbp.log)" : "";
            foreach (var warning in validation.Warnings)
                Diagnostics.RbpLog.Warn($"plan_load: {warning}");

            var host = PlanMissionLogic.Active;
            if (host != null)
                return host.ApplyPlan(plan)
                    ? $"plan applied to the CURRENT battle and carried to the next{warnings}"
                    : "apply refused (battle not plannable?) — see rbp.log";

            SessionPlanStore.Set(PlanMissionLogic.SessionKey(), plan);
            return $"plan staged for the next battle (session '{PlanMissionLogic.SessionKey()}', {plan.Formations.Count} formation(s)){warnings}";
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
