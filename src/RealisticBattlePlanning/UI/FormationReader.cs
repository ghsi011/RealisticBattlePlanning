using System;
using System.Collections.Generic;
using RealisticBattlePlanning.Diagnostics;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Planning;
using RealisticBattlePlanning.Planning.Model;
using TaleWorlds.MountAndBlade;

namespace RealisticBattlePlanning.UI
{
    /// <summary>
    /// Reads the live troop composition of the player's formations so the editor
    /// can label each numbered formation (1-8) by what it actually holds — an
    /// "Infantry" slot full of archers should read "Ranged", not "Infantry".
    /// Engine-side; the labelling rule itself is Core's unit-tested
    /// <see cref="FormationComposition"/>. Every unit is classed by whether it is
    /// mounted (HasMount) and/or shoots (HasRangedWeapon).
    /// </summary>
    internal static class FormationReader
    {
        /// <summary>
        /// Composition label per planned slot for the formations that actually
        /// have troops, e.g. { Infantry: "Ranged-Infantry" }. Empty / absent
        /// slots are omitted (the VM falls back to the slot's class name).
        /// </summary>
        public static Dictionary<PlannedFormationClass, string> CompositionLabels(Team team)
        {
            var labels = new Dictionary<PlannedFormationClass, string>();
            if (team == null)
                return labels;

            // Reading agents during deployment must never throw the editor down —
            // on any fault, return what we have and let the VM fall back to slot
            // class names for the rest.
            try
            {
                foreach (var (planned, engine) in FormationClassMap.All)
                {
                    var formation = team.GetFormation(engine);
                    if (formation == null || formation.CountOfUnits == 0)
                        continue;

                    int infantry = 0, ranged = 0, cavalry = 0, horseArcher = 0;
                    formation.ApplyActionOnEachUnit(agent =>
                    {
                        if (agent == null)
                            return;
                        var mounted = agent.HasMount;
                        // IsRangedCached counts only a non-consumable ranged weapon with
                        // ammo (bow / crossbow), so javelin- and throwing-axe infantry
                        // stay "Infantry" rather than being miscounted as "Ranged".
                        var shoots = agent.IsRangedCached;
                        if (mounted && shoots) horseArcher++;
                        else if (mounted) cavalry++;
                        else if (shoots) ranged++;
                        else infantry++;
                    });

                    if (infantry + ranged + cavalry + horseArcher > 0)
                        labels[planned] = FormationComposition.Label(infantry, ranged, cavalry, horseArcher);
                }
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] Reading formation composition failed; falling back to class names.", e);
            }

            return labels;
        }
    }
}
