using System;

namespace RealisticBattlePlanning.Fidelity
{
    /// <summary>
    /// Derives Command Competence from vanilla stats plus the mod's Plan
    /// Familiarity layer, and maps it to a tier (spec D1/D2). Vanilla-first:
    /// the base comes from existing Tactics (primary) and Leadership
    /// (secondary), so a renowned lord is competent with zero mod XP and "a
    /// great general acts like a recruit" never happens. The Plan Familiarity
    /// XP (0–300, earned per D4) is the only mod-owned layer and adds on top.
    /// All weights/thresholds are defaults for the Area-F config table.
    /// </summary>
    public static class CompetenceModel
    {
        public const float TacticsWeight = 0.7f;
        public const float LeadershipWeight = 0.3f;

        // Tier thresholds on the effective competence score (D2; configurable defaults).
        public const float DrilledThreshold = 60f;
        public const float ProficientThreshold = 120f;
        public const float VeteranThreshold = 180f;
        public const float MasterThreshold = 240f;

        /// <summary>Effective Command Competence: vanilla-derived base + familiarity layer (D1).</summary>
        public static float Score(int tactics, int leadership, int planFamiliarity)
        {
            var t = Math.Max(0, tactics);
            var l = Math.Max(0, leadership);
            var f = Math.Max(0, planFamiliarity);
            return t * TacticsWeight + l * LeadershipWeight + f;
        }

        /// <summary>
        /// Effective competence when familiarity is split into battle- and
        /// drill-earned (D1 + C5): battle familiarity counts in full, but drill
        /// familiarity lifts competence only up to the Proficient ceiling —
        /// drills teach the choreography, only battle teaches the judgment for
        /// Veteran/Master.
        /// </summary>
        public static float EffectiveScore(int tactics, int leadership, float battleFamiliarity, float drillFamiliarity)
        {
            var baseAndBattle = Score(tactics, leadership, Round(battleFamiliarity));
            var roomToProficient = Math.Max(0f, ProficientThreshold - baseAndBattle);
            var drillContribution = Math.Min(Math.Max(0f, drillFamiliarity), roomToProficient);
            return baseAndBattle + drillContribution;
        }

        public static FidelityTier TierFor(float score)
        {
            if (score >= MasterThreshold) return FidelityTier.Master;
            if (score >= VeteranThreshold) return FidelityTier.Veteran;
            if (score >= ProficientThreshold) return FidelityTier.Proficient;
            if (score >= DrilledThreshold) return FidelityTier.Drilled;
            return FidelityTier.Untrained;
        }

        // Round, not truncate, so float drift near a threshold (e.g. 119.97)
        // doesn't silently cost a tier step.
        private static int Round(float xp) => (int)Math.Round(xp, MidpointRounding.AwayFromZero);

        public static FidelityTier TierFor(int tactics, int leadership, int planFamiliarity)
            => TierFor(Score(tactics, leadership, planFamiliarity));
    }
}
