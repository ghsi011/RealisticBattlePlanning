using System.Linq;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Fidelity;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// P4: abort composure (D3). A green commander pulls out before the
    /// configured casualty limit; a Master fights to the letter; pass-through
    /// is unchanged. Composure is deterministic (a stable trait, no rng).
    /// </summary>
    public class FidelityAbortTests
    {
        private const float ConfiguredLimit = 50f;

        [Fact]
        public void PassThroughAbortsAtTheConfiguredThreshold()
        {
            // 49% must not abort, 51% must — exactly the configured 50%.
            Assert.False(Aborts(null, casualties: 49f));
            Assert.True(Aborts(null, casualties: 51f));
        }

        [Fact]
        public void UntrainedAbortsEarly()
        {
            // 0.7 composure -> early limit 35%. 40% is fine at the letter but
            // breaks a green commander.
            Assert.True(Aborts(new FixedTierFidelityModel(FidelityTier.Untrained), casualties: 40f));
            Assert.False(Aborts(null, casualties: 40f)); // pass-through holds at 40%
        }

        [Fact]
        public void MasterFightsToTheConfiguredLetter()
        {
            // 1.0 composure -> exact parity with pass-through: 49% holds, 51% aborts.
            Assert.False(Aborts(new FixedTierFidelityModel(FidelityTier.Master), casualties: 49f));
            Assert.True(Aborts(new FixedTierFidelityModel(FidelityTier.Master), casualties: 51f));
        }

        [Fact]
        public void GreenerCommandersAbortAtLowerCasualties()
        {
            // The abort point rises monotonically with tier.
            var tiers = new[] { FidelityTier.Untrained, FidelityTier.Drilled, FidelityTier.Proficient, FidelityTier.Veteran, FidelityTier.Master };
            var points = tiers.Select(AbortPoint).ToList();
            for (var i = 1; i < points.Count; i++)
                Assert.True(points[i] > points[i - 1], $"{tiers[i]} should fight longer than {tiers[i - 1]}");
        }

        [Fact]
        public void CompetenceModelUsesTheCommandersOwnComposure()
        {
            var monitor = new PlanMonitor(HoldPlan(), new CompetenceFidelityModel());
            monitor.SetCommander(PlannedFormationClass.Infantry, CommanderProfile.FromStats(tactics: 20, leadership: 20)); // green
            monitor.Tick(Field(0f, casualties: 0f));

            // Green (Untrained) -> early limit 35%, so 40% aborts.
            Assert.Single(monitor.Tick(Field(1f, casualties: 40f)).OfType<PlanAborted>());
        }

        // ---- helpers ----

        /// <summary>Approx. casualty % at which this tier aborts a 50%-configured plan.</summary>
        private static float AbortPoint(FidelityTier tier)
            => ConfiguredLimit * FidelityDefaults.AbortComposureFactor(tier);

        private static bool Aborts(IFidelityModel model, float casualties)
        {
            var monitor = new PlanMonitor(HoldPlan(), model);
            monitor.Tick(Field(0f, casualties: 0f));      // opening hold activates
            var events = monitor.Tick(Field(1f, casualties: casualties));
            return events.OfType<PlanAborted>().Any();
        }

        private static BattlePlan HoldPlan() => new()
        {
            Formations =
            {
                new FormationPlan
                {
                    Formation = PlannedFormationClass.Infantry,
                    Abort = new AbortConditions { CasualtiesAbovePercent = ConfiguredLimit },
                    Stages = { new Stage { Do = new DirectiveSpec { Type = DirectiveType.Hold } } },
                },
            },
        };

        private static FakeBattlefield Field(float time, float casualties)
            => new FakeBattlefield(time).WithOwn(PlannedFormationClass.Infantry, 0f, 0f, casualtiesPercent: casualties);
    }
}
