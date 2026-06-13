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

        /// <summary>Effective Command Competence: vanilla-derived base + familiarity layer (D1).</summary>
        public static float Score(int tactics, int leadership, int planFamiliarity)
        {
            var t = Math.Max(0, tactics);
            var l = Math.Max(0, leadership);
            var f = Math.Max(0, planFamiliarity);
            return t * TacticsWeight + l * LeadershipWeight + f;
        }

        /// <summary>Tier thresholds on the effective competence score (D2; configurable defaults).</summary>
        public static FidelityTier TierFor(float score)
        {
            if (score >= 240f) return FidelityTier.Master;
            if (score >= 180f) return FidelityTier.Veteran;
            if (score >= 120f) return FidelityTier.Proficient;
            if (score >= 60f) return FidelityTier.Drilled;
            return FidelityTier.Untrained;
        }

        public static FidelityTier TierFor(int tactics, int leadership, int planFamiliarity)
            => TierFor(Score(tactics, leadership, planFamiliarity));
    }
}
