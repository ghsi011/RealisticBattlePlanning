using System.Collections.Generic;
using System.Linq;
using ModDebugKit.Commands;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace ModDebugKit.Battles
{
    /// <summary>
    /// Programmatic formation assignment (M1.4): move the player's units into
    /// specific numbered formations (1-8) by live class, so a battle reproduces
    /// an exact layout regardless of the deployment auto-sort. This is the
    /// controllable side of the "formation slot != class != contents" lesson —
    /// run during deployment, then dbg.ready.
    /// </summary>
    public static class FormationCommands
    {
        public static void RegisterAll(CommandDispatcher dispatcher)
        {
            dispatcher.Register("dbg.assign",
                "dbg.assign <all|inf|ranged|cav|ha> <formation 1-8> - move the player's matching units into that formation",
                Assign);
            dispatcher.Register("dbg.layout",
                "dbg.layout <sel=N> [sel=N ...] - assign several classes at once, e.g. dbg.layout inf=1 ranged=3 cav=5 ha=7",
                Layout);
        }

        private static DbgOutcome Assign(DbgCommand command)
        {
            var team = Mission.Current?.PlayerTeam;
            if (team == null)
                return DbgOutcome.Failure("no active mission / player team");
            if (command.Args.Count < 2)
                return DbgOutcome.Failure("usage: dbg.assign <all|inf|ranged|cav|ha> <formation 1-8>");
            if (!AgentSelectors.TryParse(command.Arg(0), out var selector))
                return DbgOutcome.Failure($"unknown selector '{command.Arg(0)}' (all|inf|ranged|cav|ha)");
            if (!TryParseFormation(command.Arg(1), out var number))
                return DbgOutcome.Failure($"formation must be 1-8, got '{command.Arg(1)}'");

            var moved = MoveToFormation(team, selector, number);
            return DbgOutcome.Success(
                $"assigned {moved} {selector} unit(s) to formation {number} ({(FormationClass)(number - 1)})",
                new { moved, formation = number, selector = selector.ToString() });
        }

        private static DbgOutcome Layout(DbgCommand command)
        {
            var team = Mission.Current?.PlayerTeam;
            if (team == null)
                return DbgOutcome.Failure("no active mission / player team");
            if (command.Args.Count == 0)
                return DbgOutcome.Failure("usage: dbg.layout <sel=N> [sel=N ...]  e.g. dbg.layout inf=1 ranged=3 cav=5 ha=7");

            var applied = new List<object>();
            foreach (var pair in command.Args)
            {
                var eq = pair.IndexOf('=');
                if (eq <= 0 || eq >= pair.Length - 1)
                    return DbgOutcome.Failure($"bad pair '{pair}'; expected sel=N (e.g. inf=1)");
                if (!AgentSelectors.TryParse(pair.Substring(0, eq), out var selector))
                    return DbgOutcome.Failure($"unknown selector in '{pair}'");
                if (!TryParseFormation(pair.Substring(eq + 1), out var number))
                    return DbgOutcome.Failure($"formation must be 1-8 in '{pair}'");

                var moved = MoveToFormation(team, selector, number);
                applied.Add(new { selector = selector.ToString(), formation = number, moved });
            }

            return DbgOutcome.Success($"applied layout: {applied.Count} assignment(s)", new { assignments = applied });
        }

        private static int MoveToFormation(Team team, AgentSelector selector, int number)
        {
            var target = team.GetFormation((FormationClass)(number - 1));
            if (target == null)
                return 0;

            var moved = 0;
            // Snapshot the agent list first — setting Formation mutates team membership/detachments.
            foreach (var agent in team.ActiveAgents.ToList())
            {
                if (agent == null || !agent.IsHuman || !agent.IsActive())
                    continue;
                if (!Matches(agent, selector))
                    continue;
                if (agent.Formation == target)
                    continue;
                agent.Formation = target;
                moved++;
            }

            return moved;
        }

        private static bool Matches(Agent agent, AgentSelector selector)
        {
            if (selector == AgentSelector.All)
                return true;
            var mounted = agent.HasMount;
            var shoots = agent.IsRangedCached;
            switch (selector)
            {
                case AgentSelector.Infantry: return !mounted && !shoots;
                case AgentSelector.Ranged: return !mounted && shoots;
                case AgentSelector.Cavalry: return mounted && !shoots;
                case AgentSelector.HorseArcher: return mounted && shoots;
                default: return false;
            }
        }

        private static bool TryParseFormation(string text, out int number)
            => int.TryParse(text, out number) && number >= 1 && number <= 8;
    }
}
