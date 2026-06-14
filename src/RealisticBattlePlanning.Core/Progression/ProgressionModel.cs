using System;
using RealisticBattlePlanning.Fidelity;

namespace RealisticBattlePlanning.Progression
{
    /// <summary>
    /// Plan Familiarity XP awards and tier-up (spec D4). Battle execution
    /// raises familiarity per completed stage; an aborted/failed stage grants
    /// a reduced "lesson learned" trickle. No decay in v1 (a configurable hook
    /// is reserved); commander death loses everything (CommanderRecordBook).
    /// All rates are defaults for the Area-F config table; pacing is tuned so
    /// a fresh companion reaches Drilled in a handful of battles, not 200
    /// drills (D4 pacing targets).
    /// </summary>
    public static class ProgressionModel
    {
        public const float MaxFamiliarityXp = 300f;     // D1 0–300 scale
        public const float XpPerCompletedStage = 2.5f;  // ~4 stages/battle ≈ 10 XP, so ~3 battles to +30
        public const float XpPerFailedStage = 0.5f;     // lesson-learned trickle (D4)

        /// <summary>Drill familiarity accrues at an accelerated rate vs. battle (C4 default 2×); the Proficient cap (C5) is enforced in CompetenceModel.EffectiveScore, so drill XP can never reach Veteran/Master.</summary>
        public const float DrillXpMultiplier = 2f;

        /// <summary>An approximate battle's worth of completed stages — the shared assumption behind the D4 pacing targets (used by the pacing tests so a rate change can't drift it silently).</summary>
        public const int TypicalStagesPerBattle = 4;

        public static void OnStageCompleted(CommanderRecord record, bool inDrill = false)
        {
            record.StagesExecuted++;
            if (inDrill)
                record.DrillFamiliarityXp = Clamp(record.DrillFamiliarityXp + XpPerCompletedStage * DrillXpMultiplier);
            else
                record.PlanFamiliarityXp = Clamp(record.PlanFamiliarityXp + XpPerCompletedStage);
        }

        public static void OnStageFailed(CommanderRecord record)
        {
            record.StagesAbortedOrFailed++;
            record.PlanFamiliarityXp = Clamp(record.PlanFamiliarityXp + XpPerFailedStage);
        }

        public static void OnBattleUnderCommand(CommanderRecord record)
            => record.BattlesUnderCommand++;

        /// <summary>The commander's current effective competence tier from vanilla stats + battle/drill familiarity (D1/D2/C5).</summary>
        public static FidelityTier TierFor(CommanderRecord record, int tactics, int leadership)
            => CompetenceModel.TierFor(CompetenceModel.EffectiveScore(tactics, leadership, record.PlanFamiliarityXp, record.DrillFamiliarityXp));

        public static CommanderProfile ProfileFor(CommanderRecord record, int tactics, int leadership)
        {
            var score = CompetenceModel.EffectiveScore(tactics, leadership, record.PlanFamiliarityXp, record.DrillFamiliarityXp);
            return new CommanderProfile(CompetenceModel.TierFor(score), score);
        }

        private static float Clamp(float xp)
            => Math.Min(MaxFamiliarityXp, Math.Max(0f, xp));
    }
}
