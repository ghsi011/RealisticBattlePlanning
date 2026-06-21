using System;
using System.Linq;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Fidelity;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// P3: positional drift (D3). A green formation ends up well off the
    /// anchor it was sent to; a Master one lands nearly on it. Pass-through is
    /// exact (the pre-fidelity behaviour). One roll drives both reaction delay
    /// and drift, so a battle replays consistently.
    /// </summary>
    public class FidelityPositionTests
    {
        private static readonly MapVec Anchor = new(0f, 50f); // OwnStart (0,0) + forward 50

        [Fact]
        public void PassThroughHitsTheAnchorExactly()
        {
            var target = MoveTarget(new PlanMonitor(MoveToGoal()));
            Assert.Equal(Anchor, target);
        }

        [Fact]
        public void UntrainedDriftsWithinItsBand()
        {
            var target = MoveTarget(new PlanMonitor(MoveToGoal(), new FixedTierFidelityModel(FidelityTier.Untrained), seed: 3));
            var drift = target.DistanceTo(Anchor);
            Assert.InRange(drift, 15f, 25f);
        }

        [Fact]
        public void MasterBarelyDrifts()
        {
            var target = MoveTarget(new PlanMonitor(MoveToGoal(), new FixedTierFidelityModel(FidelityTier.Master), seed: 3));
            var drift = target.DistanceTo(Anchor);
            Assert.InRange(drift, 2f, 3f);
        }

        [Fact]
        public void DriftShrinksAsTierImproves()
        {
            float Average(FidelityTier tier)
            {
                var total = 0f;
                const int n = 60;
                for (var seed = 0; seed < n; seed++)
                    total += MoveTarget(new PlanMonitor(MoveToGoal(), new FixedTierFidelityModel(tier), seed)).DistanceTo(Anchor);
                return total / n;
            }

            var tiers = new[] { FidelityTier.Untrained, FidelityTier.Drilled, FidelityTier.Proficient, FidelityTier.Veteran, FidelityTier.Master };
            var averages = tiers.Select(Average).ToList();
            for (var i = 1; i < averages.Count; i++)
                Assert.True(averages[i] < averages[i - 1], $"{tiers[i]} drift ({averages[i]:0.#}) should be tighter than {tiers[i - 1]} ({averages[i - 1]:0.#})");
        }

        [Fact]
        public void DriftIsDeterministicPerSeed()
        {
            MapVec Roll() => MoveTarget(new PlanMonitor(MoveToGoal(), new FixedTierFidelityModel(FidelityTier.Untrained), seed: 99));
            Assert.Equal(Roll(), Roll());
        }

        [Fact]
        public void WaypointPathDriftsByAConsistentOffset()
        {
            // Every waypoint shifts by the same offset (the commander's drift),
            // so the path keeps its shape but lands off-target.
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
                        Stages = { new Stage { Do = new DirectiveSpec { Type = DirectiveType.MoveTo, Path = new System.Collections.Generic.List<string> { "wp1", "wp2" } } } },
                    },
                },
            };
            // Opening posture is exempt from fidelity, so make it a triggered move.
            plan.Formations[0].Stages.Insert(0, new Stage { Do = new DirectiveSpec { Type = DirectiveType.Hold } });
            plan.Formations[0].Stages[1].When.Add(new TriggerSpec { Type = TriggerType.TimerElapsed, Seconds = 1f });

            var monitor = new PlanMonitor(plan, new FixedTierFidelityModel(FidelityTier.Untrained), seed: 7);
            monitor.Tick(Field(0f));
            // The timer fires at 1s but the move activates after the reaction delay.
            StageActivated activated = null;
            for (var t = 1.1f; t <= 30f && activated == null; t += 0.5f)
                activated = monitor.Tick(Field(t)).OfType<StageActivated>().FirstOrDefault(e => e.Directive.Spec.Type == DirectiveType.MoveTo);
            Assert.NotNull(activated);

            var offset1 = activated.Directive.Path[0] - new MapVec(0f, 20f);
            var offset2 = activated.Directive.Path[1] - new MapVec(0f, 40f);
            Assert.Equal(offset1.X, offset2.X, 3);
            Assert.Equal(offset1.Y, offset2.Y, 3);
            Assert.InRange(offset1.Length, 15f, 25f);
        }

        [Fact]
        public void DriftedArrivalStillChainsToTheNextWaypoint()
        {
            // A click-march chain (MapAuthoring) gates each leg on PositionReached the
            // prior waypoint. Under drift the formation settles its ordered point off by
            // ~20 m (Untrained) — past the 10 m default tolerance — so without widening
            // the arrival tolerance by the drift the chain would stall on the first leg.
            var plan = new BattlePlan
            {
                Anchors =
                {
                    new MapAnchor { Id = "wp1", Basis = AnchorBasis.OwnStart, Forward = 50f },
                    new MapAnchor { Id = "wp2", Basis = AnchorBasis.OwnStart, Forward = 100f },
                },
                Formations =
                {
                    new FormationPlan
                    {
                        Formation = PlannedFormationClass.Infantry,
                        Stages =
                        {
                            new Stage { Do = new DirectiveSpec { Type = DirectiveType.Hold } },
                            new Stage { When = { new TriggerSpec { Type = TriggerType.TimerElapsed, Seconds = 1f } }, Do = new DirectiveSpec { Type = DirectiveType.MoveTo, Anchor = "wp1" } },
                            new Stage { When = { new TriggerSpec { Type = TriggerType.PositionReached, Anchor = "wp1" } }, Do = new DirectiveSpec { Type = DirectiveType.MoveTo, Anchor = "wp2" } },
                        },
                    },
                },
            };

            var monitor = new PlanMonitor(plan, new FixedTierFidelityModel(FidelityTier.Untrained), seed: 3);
            monitor.Tick(Field(0f)); // opening hold

            // First leg (to wp1) activates after the reaction delay; capture where the
            // drifted order actually sends the formation.
            StageActivated leg1 = null;
            for (var t = 1.1f; t <= 30f && leg1 == null; t += 0.5f)
                leg1 = monitor.Tick(Field(t)).OfType<StageActivated>().FirstOrDefault(e => e.StageIndex == 1);
            Assert.NotNull(leg1);

            var drifted = leg1.Directive.FirstMoveTarget.Value;
            var trueWp1 = new MapVec(0f, 50f);
            Assert.True(drifted.DistanceTo(trueWp1) > TriggerDefaults.PositionToleranceMeters,
                "precondition: the drift must exceed the default tolerance, else the test proves nothing");

            // The formation settles at its drifted destination — ~20 m off the true wp1.
            // The next leg must still fire (PositionReached, widened by the drift).
            StageActivated leg2 = null;
            for (var t = 40f; t <= 70f && leg2 == null; t += 0.5f)
                leg2 = monitor.Tick(FieldAt(t, drifted)).OfType<StageActivated>().FirstOrDefault(e => e.StageIndex == 2);
            Assert.NotNull(leg2);
            Assert.Equal(DirectiveType.MoveTo, leg2.Directive.Spec.Type);
        }

        // ---- builders ----

        private static FakeBattlefield FieldAt(float time, MapVec pos)
            => new FakeBattlefield(time).WithOwn(PlannedFormationClass.Infantry, pos.X, pos.Y);

        /// <summary>Drives the monitor until the move stage activates and returns its resolved target.</summary>
        private static MapVec MoveTarget(PlanMonitor monitor)
        {
            monitor.SetCommander(PlannedFormationClass.Infantry, CommanderProfile.Default);
            monitor.Tick(Field(0f)); // opening hold
            // Timer fires at 1s; advance past any reaction delay (<=10s) to the activation.
            StageActivated activated = null;
            for (var t = 1.1f; t <= 30f && activated == null; t += 0.5f)
                activated = monitor.Tick(Field(t)).OfType<StageActivated>().FirstOrDefault(e => e.Directive.Spec.Type == DirectiveType.MoveTo);
            Assert.NotNull(activated);
            return activated.Directive.FirstMoveTarget.Value;
        }

        private static BattlePlan MoveToGoal() => new()
        {
            Anchors = { new MapAnchor { Id = "goal", Basis = AnchorBasis.OwnStart, Forward = 50f } },
            Formations =
            {
                new FormationPlan
                {
                    Formation = PlannedFormationClass.Infantry,
                    Stages =
                    {
                        new Stage { Do = new DirectiveSpec { Type = DirectiveType.Hold } },
                        new Stage { When = { new TriggerSpec { Type = TriggerType.TimerElapsed, Seconds = 1f } }, Do = new DirectiveSpec { Type = DirectiveType.MoveTo, Anchor = "goal" } },
                    },
                },
            },
        };

        private static FakeBattlefield Field(float time)
            => new FakeBattlefield(time).WithOwn(PlannedFormationClass.Infantry, 0f, 0f);
    }
}
