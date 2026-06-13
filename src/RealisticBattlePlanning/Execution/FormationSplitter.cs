using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace RealisticBattlePlanning.Execution
{
    /// <summary>
    /// Test-harness affordance: redistributes a team's troops across a set of
    /// formation slots so each one is populated, removing the manual
    /// Order-of-Battle setup a multi-formation scenario would otherwise need
    /// (e.g. the canonical A6 needs Infantry / HorseArcher / HeavyInfantry /
    /// LightCavalry filled). Round-robin, roughly even; unit type is
    /// irrelevant to plan timing, so any bodies in a slot suffice. Never runs
    /// in normal play — only on an armed harness scenario or the explicit
    /// rbp.harness_split command.
    /// </summary>
    internal static class FormationSplitter
    {
        /// <summary>Reassigns the team's human troops round-robin across <paramref name="targets"/>. Returns the count moved through.</summary>
        public static int SpreadAcross(Team team, IReadOnlyList<FormationClass> targets, Agent ignore = null)
        {
            if (team == null || targets == null || targets.Count == 0)
                return 0;

            // Collect first: reassigning Formation mutates the per-formation
            // unit lists we'd be iterating.
            var agents = new List<Agent>();
            foreach (var formation in team.FormationsIncludingEmpty)
                formation.ApplyActionOnEachUnit(agent =>
                {
                    if (agent.IsHuman && agent != ignore)
                        agents.Add(agent);
                });

            if (agents.Count == 0)
                return 0;

            var slots = new Formation[targets.Count];
            for (var i = 0; i < targets.Count; i++)
                slots[i] = team.GetFormation(targets[i]);

            for (var i = 0; i < agents.Count; i++)
            {
                var target = slots[i % slots.Length];
                if (target != null && agents[i].Formation != target)
                    agents[i].Formation = target;
            }

            return agents.Count;
        }
    }
}
