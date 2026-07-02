using System;
using System.Linq;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Fidelity;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// P1: the fidelity seam adds a reaction delay between a stage's trigger
    /// firing and its activation (D3). Pass-through (default) is unchanged
    /// from the pre-fidelity monitor; tiered models lag by their band.
    /// </summary>
    public class FidelityReactionTests
    {
        [Fact]
        public void PassThroughActivatesInstantly()
        {
            // Default monitor = pass-through: the timer stage fires at 5s and
            // activates the same tick — the pre-fidelity behaviour.
            var monitor = new PlanMonitor(HoldThenTimer(5f));

            monitor.Tick(Field(0f));
            Assert.Empty(monitor.Tick(Field(4.9f)).OfType<StageActivated>());

            var fired = monitor.Tick(Field(5.1f));
            Assert.Single(fired.OfType<StageActivated>());
            Assert.Empty(fired.OfType<ReactionDelayed>());
        }

        [Fact]
        public void UntrainedDelaysActivationByItsBand()
        {
            var monitor = new PlanMonitor(HoldThenTimer(5f), new FixedTierFidelityModel(FidelityTier.Untrained), seed: 1);

            monitor.Tick(Field(0f));

            // The 5s timer fires, but a green commander reacts 6–10s later.
            var atFire = monitor.Tick(Field(5.1f));
            Assert.Empty(atFire.OfType<StageActivated>());
            var delayed = Assert.Single(atFire.OfType<ReactionDelayed>());
            Assert.InRange(delayed.DelaySeconds, 6f, 10f);
            Assert.Equal(FidelityTier.Untrained, delayed.Tier);

            // Not yet — still inside the reaction window.
            Assert.Empty(monitor.Tick(Field(5.1f + delayed.DelaySeconds - 0.5f)).OfType<StageActivated>());

            // Activates once the delay elapses.
            var activated = monitor.Tick(Field(5.1f + delayed.DelaySeconds + 0.3f));
            Assert.Single(activated.OfType<StageActivated>());
        }

        [Fact]
        public void MasterReactsAlmostInstantly()
        {
            var monitor = new PlanMonitor(HoldThenTimer(5f), new FixedTierFidelityModel(FidelityTier.Master), seed: 1);
            monitor.Tick(Field(0f));

            var atFire = monitor.Tick(Field(5.1f));
            // Master band is 0–1s; if the roll is ~0 it may even activate at once.
            var delayed = atFire.OfType<ReactionDelayed>().SingleOrDefault();
            if (delayed != null)
                Assert.InRange(delayed.DelaySeconds, 0f, 1f);
            else
                Assert.Single(atFire.OfType<StageActivated>());
        }

        [Fact]
        public void ReactionDelayIsDeterministicPerSeed()
        {
            float Roll()
            {
                var m = new PlanMonitor(HoldThenTimer(5f), new FixedTierFidelityModel(FidelityTier.Untrained), seed: 42);
                m.Tick(Field(0f));
                return m.Tick(Field(5.1f)).OfType<ReactionDelayed>().Single().DelaySeconds;
            }

            Assert.Equal(Roll(), Roll());
        }

        [Fact]
        public void DelaysShortenAsTierImproves()
        {
            // Average reaction delay over many seeds must fall monotonically
            // from Untrained to Master (the progression must be *felt*, D3).
            var tiers = new[] { FidelityTier.Untrained, FidelityTier.Drilled, FidelityTier.Proficient, FidelityTier.Veteran, FidelityTier.Master };
            var averages = tiers.Select(AverageDelay).ToList();

            for (var i = 1; i < averages.Count; i++)
                Assert.True(averages[i] < averages[i - 1], $"{tiers[i]} ({averages[i]:0.##}) should be quicker than {tiers[i - 1]} ({averages[i - 1]:0.##})");
        }

        [Fact]
        public void AnExplicitBattleStartOpenerActivatesInstantlyLikeTheImplicitForm()
        {
            // The opening posture is deployment, not a reaction (D3) — spelling
            // it out as an explicit BattleStart trigger must not roll a
            // reaction delay or drift, exactly like the implicit empty-When form.
            var plan = new BattlePlan
            {
                Formations =
                {
                    new FormationPlan
                    {
                        Formation = PlannedFormationClass.Infantry,
                        Stages =
                        {
                            new Stage
                            {
                                When = { new TriggerSpec { Type = TriggerType.BattleStart } },
                                Do = new DirectiveSpec { Type = DirectiveType.Hold },
                            },
                        },
                    },
                },
            };
            var monitor = new PlanMonitor(plan, new FixedTierFidelityModel(FidelityTier.Untrained), seed: 1);

            var first = monitor.Tick(Field(0f));
            Assert.Single(first.OfType<StageActivated>());
            Assert.Empty(first.OfType<ReactionDelayed>());
        }

        [Fact]
        public void CurrentDirectiveKeepsRunningDuringTheReactionDelay()
        {
            // While the commander is slow to react to stage 2, stage 1's
            // waypoint path must keep progressing (he's still doing the old job).
            var plan = new BattlePlan
            {
                Anchors =
                {
                    new MapAnchor { Id = "wp1", Basis = AnchorBasis.OwnStart, Forward = 20f },
                    new MapAnchor { Id = "wp2", Basis = AnchorBasis.OwnStart, Forward = 40f },
                },
                Formations =
                {
                    new FormationPlan
                    {
                        Formation = PlannedFormationClass.Infantry,
                        Stages =
                        {
                            new Stage { Do = new DirectiveSpec { Type = DirectiveType.MoveTo, Path = new System.Collections.Generic.List<string> { "wp1", "wp2" } } },
                            new Stage { When = { new TriggerSpec { Type = TriggerType.TimerElapsed, Seconds = 3f } }, Do = new DirectiveSpec { Type = DirectiveType.Charge } },
                        },
                    },
                },
            };
            var monitor = new PlanMonitor(plan, new FixedTierFidelityModel(FidelityTier.Untrained), seed: 5);

            monitor.Tick(At(0f, 0f, 0f));      // stage 1 (move) activates
            monitor.Tick(At(3.1f, 0f, 5f));    // timer fires -> reaction pending; infantry still moving

            // Reached wp1 while the charge is still pending: waypoint advances.
            var advanced = monitor.Tick(At(3.5f, 0f, 18f));
            Assert.Single(advanced.OfType<MoveTargetChanged>());
        }

        // ---- builders ----

        private static BattlePlan HoldThenTimer(float seconds) => new()
        {
            Formations =
            {
                new FormationPlan
                {
                    Formation = PlannedFormationClass.Infantry,
                    Stages =
                    {
                        new Stage { Do = new DirectiveSpec { Type = DirectiveType.Hold } },
                        new Stage { When = { new TriggerSpec { Type = TriggerType.TimerElapsed, Seconds = seconds } }, Do = new DirectiveSpec { Type = DirectiveType.Charge } },
                    },
                },
            },
        };

        private static float AverageDelay(FidelityTier tier)
        {
            var total = 0f;
            const int n = 60;
            for (var seed = 0; seed < n; seed++)
            {
                var m = new PlanMonitor(HoldThenTimer(5f), new FixedTierFidelityModel(tier), seed);
                m.Tick(Field(0f));
                var delayed = m.Tick(Field(5.1f)).OfType<ReactionDelayed>().SingleOrDefault();
                total += delayed?.DelaySeconds ?? 0f;
            }
            return total / n;
        }

        private static FakeBattlefield Field(float time)
            => new FakeBattlefield(time).WithOwn(PlannedFormationClass.Infantry, 0f, 0f);

        private static FakeBattlefield At(float time, float x, float y)
            => new FakeBattlefield(time).WithOwn(PlannedFormationClass.Infantry, x, y);
    }
}
