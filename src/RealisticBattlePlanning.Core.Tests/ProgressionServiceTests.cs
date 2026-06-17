using System.Collections.Generic;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Fidelity;
using RealisticBattlePlanning.Planning.Model;
using RealisticBattlePlanning.Progression;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// The D4 progression chain, proven end-to-end in Core without the game:
    /// monitor events in -> XP/tier changes -> familiarity-bearing profiles out ->
    /// death-loss forgets. The engine adapter (hero-id lookup, SyncData, the death
    /// event) is the only review-only remainder.
    /// </summary>
    public class ProgressionServiceTests
    {
        private static readonly CommanderKey Hero = new("hero_1");
        private const PlannedFormationClass Inf = PlannedFormationClass.Infantry;

        private static Dictionary<PlannedFormationClass, CommanderKey> Map(CommanderKey key)
            => new() { [Inf] = key };

        private static StageCompleted Completed() => new(Inf, 0, null);
        private static StageSkipped Skipped() => new(Inf, 0, "inevaluable");
        private static PlanAborted Aborted() => new(Inf, "casualties");

        [Fact]
        public void CompletedStagesAwardBattleXpToTheRightCommander()
        {
            var svc = new ProgressionService();
            svc.OnBattleEvents(new PlanEvent[] { Completed(), Completed(), Completed() }, Map(Hero));

            Assert.True(svc.Book.TryGet("hero_1", out var rec));
            Assert.Equal(3 * ProgressionModel.XpPerCompletedStage, rec.PlanFamiliarityXp, 3);
            Assert.Equal(3, rec.StagesExecuted);
            Assert.Equal(0f, rec.DrillFamiliarityXp);
        }

        [Fact]
        public void SkippedAndAbortedStagesGrantOnlyTheTrickle()
        {
            var svc = new ProgressionService();
            svc.OnBattleEvents(new PlanEvent[] { Skipped(), Aborted() }, Map(Hero));

            var rec = svc.Book.GetOrCreate("hero_1");
            Assert.Equal(2 * ProgressionModel.XpPerFailedStage, rec.PlanFamiliarityXp, 3);
            Assert.True(ProgressionModel.XpPerFailedStage < ProgressionModel.XpPerCompletedStage);
            Assert.Equal(2, rec.StagesAbortedOrFailed);
        }

        [Fact]
        public void NonProgressionEventsAreInert()
        {
            var svc = new ProgressionService();
            svc.OnBattleEvents(new PlanEvent[]
            {
                new StageActivated(Inf, 0, new Stage(), null),
                new ReactionDelayed(Inf, 0, 3f, FidelityTier.Untrained),
                new PlanSuspended(Inf),
                new PlanResumed(Inf, 0),
                new SignalEmitted(Inf, "advance"),
            }, Map(Hero));

            Assert.Equal(0, svc.Book.Count);
        }

        [Fact]
        public void BattleFamiliarityRaisesTheTier()
        {
            var svc = new ProgressionService();
            // tactics/leadership 30 -> base score 30 (Untrained, < 60).
            Assert.Equal(FidelityTier.Untrained, svc.ProfileFor(Hero, 30, 30).Competence);

            for (var i = 0; i < 13; i++)                                  // +32.5 XP -> score 62.5
                svc.OnBattleEvents(new PlanEvent[] { Completed() }, Map(Hero));

            Assert.Equal(FidelityTier.Drilled, svc.ProfileFor(Hero, 30, 30).Competence);
        }

        [Fact]
        public void DrillFamiliarityIsCappedAtProficient()
        {
            var svc = new ProgressionService();
            for (var i = 0; i < 500; i++)                                 // maxed drill XP
                svc.OnBattleEvents(new PlanEvent[] { Completed() }, Map(Hero), inDrill: true);

            // Drills teach choreography, not judgment: capped at Proficient (C5).
            Assert.Equal(FidelityTier.Proficient, svc.ProfileFor(Hero, 30, 30).Competence);
        }

        [Fact]
        public void DeathForgetsTheCommander()
        {
            var svc = new ProgressionService();
            for (var i = 0; i < 13; i++)
                svc.OnBattleEvents(new PlanEvent[] { Completed() }, Map(Hero));
            Assert.Equal(FidelityTier.Drilled, svc.ProfileFor(Hero, 30, 30).Competence);

            svc.OnCommanderLost(Hero);

            Assert.False(svc.Book.TryGet("hero_1", out _));
            Assert.Equal(FidelityTier.Untrained, svc.ProfileFor(Hero, 30, 30).Competence); // back to stats-only
        }

        [Fact]
        public void NoneKeyAccruesNothingAndNeverPersists()
        {
            var svc = new ProgressionService();
            svc.OnBattleEvents(new PlanEvent[] { Completed(), Completed() }, Map(CommanderKey.None));

            Assert.Equal(0, svc.Book.Count);
            // A None key reads a blank record, so its profile equals the stats-only base.
            Assert.Equal(CommanderProfile.FromStats(30, 30).Competence, svc.ProfileFor(CommanderKey.None, 30, 30).Competence);
        }

        [Fact]
        public void ProfileForIsAPureReadAndNeverInsertsARecord()
        {
            var svc = new ProgressionService();

            // Querying competence must not mutate the book — records are born only
            // when something is earned. (Regression: GetOrCreate in the read path
            // silently created a blank record for every commander ever queried.)
            svc.ProfileFor(Hero, 30, 30);
            svc.ProfileFor(Hero, 99, 99);
            svc.ProfileFor(CommanderKey.None, 50, 50);

            Assert.Equal(0, svc.Book.Count);
            Assert.False(svc.Book.TryGet("hero_1", out _));

            // ...but a queried-then-awarded commander does get exactly one record.
            svc.OnBattleEvents(new PlanEvent[] { Completed() }, Map(Hero));
            Assert.Equal(1, svc.Book.Count);
        }

        [Fact]
        public void BookRoundTripsThroughSnapshotAndLoad()
        {
            var svc = new ProgressionService();
            for (var i = 0; i < 13; i++)
                svc.OnBattleEvents(new PlanEvent[] { Completed() }, Map(Hero));

            // The serializable surface the engine SyncData moves carries everything.
            var fresh = new ProgressionService();
            foreach (var kv in svc.Book.Snapshot())
                fresh.Book.Load(kv.Key, kv.Value);

            Assert.Equal(svc.ProfileFor(Hero, 30, 30).Competence, fresh.ProfileFor(Hero, 30, 30).Competence);
            Assert.Equal(svc.ProfileFor(Hero, 30, 30).CompetenceScore, fresh.ProfileFor(Hero, 30, 30).CompetenceScore, 3);
        }
    }
}
