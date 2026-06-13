using System;
using System.Linq;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Fidelity;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// P2: Command Competence is derived from vanilla Tactics/Leadership plus
    /// the mod's Plan Familiarity layer (D1), maps to a tier (D2), and drives
    /// the competence fidelity model — so a renowned lord executes well with
    /// zero mod XP and a green companion lags.
    /// </summary>
    public class CompetenceModelTests
    {
        [Fact]
        public void TacticsWeighsMoreThanLeadership()
        {
            // Same stat total, weighted toward Tactics — the Tactics-heavy
            // commander scores higher (D1: Tactics primary).
            var tacticsHeavy = CompetenceModel.Score(tactics: 150, leadership: 50, planFamiliarity: 0);
            var leadershipHeavy = CompetenceModel.Score(tactics: 50, leadership: 150, planFamiliarity: 0);

            Assert.True(tacticsHeavy > leadershipHeavy);
        }

        [Fact]
        public void AGreenCompanionIsUntrainedAndAFamousLordIsNot()
        {
            Assert.Equal(FidelityTier.Untrained, CompetenceModel.TierFor(20, 20, 0));
            Assert.True(CompetenceModel.TierFor(220, 180, 0) >= FidelityTier.Veteran,
                "a renowned general should be competent with zero mod XP (D1)");
        }

        [Fact]
        public void PlanFamiliarityRaisesTheTier()
        {
            var without = CompetenceModel.TierFor(60, 40, 0);
            var with = CompetenceModel.TierFor(60, 40, 200);

            Assert.True(with > without, "familiarity with the player's plans should lift competence (D1/D4)");
        }

        [Theory]
        [InlineData(59f, FidelityTier.Untrained)]
        [InlineData(60f, FidelityTier.Drilled)]
        [InlineData(120f, FidelityTier.Proficient)]
        [InlineData(180f, FidelityTier.Veteran)]
        [InlineData(240f, FidelityTier.Master)]
        [InlineData(999f, FidelityTier.Master)]
        public void TierThresholdsAreAtTheBoundaries(float score, FidelityTier expected)
        {
            Assert.Equal(expected, CompetenceModel.TierFor(score));
        }

        [Fact]
        public void NegativeStatsClampToZero()
        {
            Assert.Equal(0f, CompetenceModel.Score(-50, -50, -50));
            Assert.Equal(FidelityTier.Untrained, CompetenceModel.TierFor(-10, -10, 0));
        }

        [Fact]
        public void FromStatsCarriesTheScoreAndTier()
        {
            var profile = CommanderProfile.FromStats(tactics: 200, leadership: 100);

            Assert.Equal(CompetenceModel.Score(200, 100, 0), profile.CompetenceScore);
            Assert.Equal(CompetenceModel.TierFor(200, 100, 0), profile.Competence);
        }

        [Fact]
        public void CompetenceModelMakesABetterCommanderReactFaster()
        {
            // The whole point: drive the monitor with each formation's own
            // commander, and the skilled one acts measurably sooner.
            var green = AverageReaction(CommanderProfile.FromStats(20, 20));
            var veteran = AverageReaction(CommanderProfile.FromStats(220, 180));

            Assert.True(veteran < green, $"veteran ({veteran:0.##}s) should react faster than green ({green:0.##}s)");
        }

        private static float AverageReaction(CommanderProfile commander)
        {
            var total = 0f;
            const int n = 60;
            for (var seed = 0; seed < n; seed++)
            {
                var monitor = new PlanMonitor(HoldThenTimer(), new CompetenceFidelityModel(), seed);
                monitor.SetCommander(PlannedFormationClass.Infantry, commander);
                monitor.Tick(Field(0f));
                var delayed = monitor.Tick(Field(5.1f)).OfType<ReactionDelayed>().SingleOrDefault();
                total += delayed?.DelaySeconds ?? 0f;
            }
            return total / n;
        }

        private static BattlePlan HoldThenTimer() => new()
        {
            Formations =
            {
                new FormationPlan
                {
                    Formation = PlannedFormationClass.Infantry,
                    Stages =
                    {
                        new Stage { Do = new DirectiveSpec { Type = DirectiveType.Hold } },
                        new Stage { When = { new TriggerSpec { Type = TriggerType.TimerElapsed, Seconds = 5f } }, Do = new DirectiveSpec { Type = DirectiveType.Charge } },
                    },
                },
            },
        };

        private static FakeBattlefield Field(float time)
            => new FakeBattlefield(time).WithOwn(PlannedFormationClass.Infantry, 0f, 0f);
    }
}
