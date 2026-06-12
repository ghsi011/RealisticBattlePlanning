using System.Collections.Generic;
using System.Linq;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// Scripted-timeline tests: fake snapshots driven through the monitor,
    /// asserting on the emitted events (testing architecture, Layer 1).
    /// Attack direction is (0,1) (north) unless stated, so "forward 50"
    /// anchors resolve to +50 on Y and "right 10" to +10 on X.
    /// </summary>
    public class PlanMonitorTests
    {
        [Fact]
        public void NothingHappensBeforeBattleStart()
        {
            var monitor = new PlanMonitor(HoldThenTimerCharge(30f));

            Assert.Empty(monitor.Tick(Snap(0f, started: false, InfantryAt(0, 0))));
            Assert.Empty(monitor.Tick(Snap(60f, started: false, InfantryAt(0, 0))));
            Assert.False(monitor.Started);
        }

        [Fact]
        public void BattleStartActivatesFirstStageExactlyOnce()
        {
            var monitor = new PlanMonitor(HoldThenTimerCharge(30f));

            var first = monitor.Tick(Snap(1f, started: true, InfantryAt(0, 0)));
            var activated = Assert.Single(first.OfType<StageActivated>());
            Assert.Equal(0, activated.StageIndex);
            Assert.Equal(DirectiveType.Hold, activated.Directive.Spec.Type);

            Assert.Empty(monitor.Tick(Snap(2f, started: true, InfantryAt(0, 0))));
        }

        [Fact]
        public void TimerFiresAfterIntervalAndExactlyOnce()
        {
            var monitor = new PlanMonitor(HoldThenTimerCharge(30f));
            monitor.Tick(Snap(5f, started: true, InfantryAt(0, 0))); // battle start at t=5

            Assert.Empty(monitor.Tick(Snap(34.9f, started: true, InfantryAt(0, 0))));

            var fired = monitor.Tick(Snap(35.1f, started: true, InfantryAt(0, 0)));
            var activated = Assert.Single(fired.OfType<StageActivated>());
            Assert.Equal(DirectiveType.Charge, activated.Directive.Spec.Type);

            Assert.Empty(monitor.Tick(Snap(40f, started: true, InfantryAt(0, 0))));
            Assert.Empty(monitor.Tick(Snap(400f, started: true, InfantryAt(0, 0))));
        }

        [Fact]
        public void TimerBaselineIsPreviousStageActivationNotBattleStart()
        {
            // Stage 1 fires at t=20 (timer 20 from battle start at t=0);
            // stage 2's 30s timer must count from t=20, not t=0.
            var plan = Plan(Formation(PlannedFormationClass.Infantry,
                StageOf(null, Hold()),
                StageOf(Timer(20f), Hold()),
                StageOf(Timer(30f), Charge())));
            var monitor = new PlanMonitor(plan);

            monitor.Tick(Snap(0f, started: true, InfantryAt(0, 0)));
            Assert.Single(monitor.Tick(Snap(20.5f, started: true, InfantryAt(0, 0))).OfType<StageActivated>());

            Assert.Empty(monitor.Tick(Snap(35f, started: true, InfantryAt(0, 0))));
            Assert.Single(monitor.Tick(Snap(51f, started: true, InfantryAt(0, 0))).OfType<StageActivated>());
        }

        [Fact]
        public void PositionReachedFiresInsideToleranceNotDuringApproach()
        {
            // Anchor: own start (0,0) + forward 50 => (0,50). Default tolerance 10m.
            var plan = Plan(
                Formation(PlannedFormationClass.Infantry,
                    StageOf(null, MoveToAnchor("goal")),
                    StageOf(new[] { new TriggerSpec { Type = TriggerType.PositionReached, Anchor = "goal" } }, Charge())),
                AnchorOwnStart("goal", forward: 50f));
            var monitor = new PlanMonitor(plan);

            monitor.Tick(Snap(0f, started: true, InfantryAt(0, 0)));
            Assert.Empty(monitor.Tick(Snap(5f, started: true, InfantryAt(0, 20))));
            Assert.Empty(monitor.Tick(Snap(10f, started: true, InfantryAt(0, 35))));

            var fired = monitor.Tick(Snap(15f, started: true, InfantryAt(0, 45)));
            var activated = Assert.Single(fired.OfType<StageActivated>());
            Assert.Equal(DirectiveType.Charge, activated.Directive.Spec.Type);
        }

        [Fact]
        public void ThreeStageTimelineEmitsHoldMoveChargeInOrder()
        {
            var plan = Plan(
                Formation(PlannedFormationClass.Infantry,
                    StageOf(null, Hold()),
                    StageOf(Timer(10f), MoveToAnchor("goal")),
                    StageOf(new[] { new TriggerSpec { Type = TriggerType.PositionReached, Anchor = "goal" } }, Charge())),
                AnchorOwnStart("goal", forward: 50f));
            var monitor = new PlanMonitor(plan);

            var seen = new List<DirectiveType>();
            void Collect(IReadOnlyList<PlanEvent> events)
                => seen.AddRange(events.OfType<StageActivated>().Select(e => e.Directive.Spec.Type));

            Collect(monitor.Tick(Snap(0f, started: true, InfantryAt(0, 0))));
            Collect(monitor.Tick(Snap(5f, started: true, InfantryAt(0, 0))));
            Collect(monitor.Tick(Snap(11f, started: true, InfantryAt(0, 0))));
            Collect(monitor.Tick(Snap(20f, started: true, InfantryAt(0, 30))));
            Collect(monitor.Tick(Snap(30f, started: true, InfantryAt(0, 44))));

            Assert.Equal(new[] { DirectiveType.Hold, DirectiveType.MoveTo, DirectiveType.Charge }, seen);
        }

        [Fact]
        public void MoveToTargetIsResolvedAgainstBattleStartGeometry()
        {
            var plan = Plan(
                Formation(PlannedFormationClass.Infantry,
                    StageOf(null, MoveToAnchor("goal"))),
                AnchorOwnStart("goal", forward: 50f, right: 10f));
            var monitor = new PlanMonitor(plan);

            var events = monitor.Tick(Snap(0f, started: true, InfantryAt(100, 200)));
            var activated = Assert.Single(events.OfType<StageActivated>());

            // Start (100,200), attack dir (0,1): forward 50 => +Y, right 10 => +X.
            Assert.Equal(new MapVec(110f, 250f), activated.Directive.FirstMoveTarget);
        }

        [Fact]
        public void WaypointPathProgressesAsPositionsAreReached()
        {
            var plan = Plan(
                Formation(PlannedFormationClass.Infantry,
                    StageOf(null, MoveToPath("wp1", "wp2", "wp3"))),
                AnchorOwnStart("wp1", forward: 20f),
                AnchorOwnStart("wp2", forward: 40f),
                AnchorOwnStart("wp3", forward: 60f));
            var monitor = new PlanMonitor(plan);

            monitor.Tick(Snap(0f, started: true, InfantryAt(0, 0)));

            Assert.Empty(monitor.Tick(Snap(1f, started: true, InfantryAt(0, 5))));

            var advance1 = monitor.Tick(Snap(2f, started: true, InfantryAt(0, 13)));
            var moved1 = Assert.Single(advance1.OfType<MoveTargetChanged>());
            Assert.Equal(new MapVec(0f, 40f), moved1.Target);

            var advance2 = monitor.Tick(Snap(3f, started: true, InfantryAt(0, 33)));
            var moved2 = Assert.Single(advance2.OfType<MoveTargetChanged>());
            Assert.Equal(new MapVec(0f, 60f), moved2.Target);

            // Final waypoint reached: no further progression events.
            Assert.Empty(monitor.Tick(Snap(4f, started: true, InfantryAt(0, 55))));
        }

        [Fact]
        public void SignalsAreEmittedOnStageActivation()
        {
            var stage = StageOf(null, Hold());
            stage.Emit.Add("advancing");
            var monitor = new PlanMonitor(Plan(Formation(PlannedFormationClass.Infantry, stage)));

            var events = monitor.Tick(Snap(0f, started: true, InfantryAt(0, 0)));

            var signal = Assert.Single(events.OfType<SignalEmitted>());
            Assert.Equal("advancing", signal.Signal);
        }

        [Fact]
        public void FormationsAdvanceIndependently()
        {
            var plan = Plan(
                Formation(PlannedFormationClass.Infantry,
                    StageOf(null, Hold()),
                    StageOf(Timer(10f), Charge())),
                Formation(PlannedFormationClass.Ranged,
                    StageOf(null, Hold()),
                    StageOf(Timer(50f), Charge())));
            var monitor = new PlanMonitor(plan);

            monitor.Tick(Snap(0f, started: true, InfantryAt(0, 0), RangedAt(10, 0)));

            var atEleven = monitor.Tick(Snap(11f, started: true, InfantryAt(0, 0), RangedAt(10, 0)));
            var infantryCharge = Assert.Single(atEleven.OfType<StageActivated>());
            Assert.Equal(PlannedFormationClass.Infantry, infantryCharge.Formation);

            var atFiftyOne = monitor.Tick(Snap(51f, started: true, InfantryAt(0, 0), RangedAt(10, 0)));
            var rangedCharge = Assert.Single(atFiftyOne.OfType<StageActivated>());
            Assert.Equal(PlannedFormationClass.Ranged, rangedCharge.Formation);
        }

        [Fact]
        public void ALateArrivingFormationStartsItsPlanWhereItAppears()
        {
            // Reinforcement-wave slot: no units at battle start, so stage 1
            // waits; on arrival the OwnStart basis is the arrival point.
            var plan = Plan(
                Formation(PlannedFormationClass.Cavalry,
                    StageOf(null, MoveToAnchor("goal"))),
                AnchorOwnStart("goal", forward: 50f));
            var monitor = new PlanMonitor(plan);

            Assert.Empty(monitor.Tick(Snap(0f, started: true, InfantryAt(0, 0))));
            Assert.Empty(monitor.Tick(Snap(5f, started: true, InfantryAt(0, 0))));

            var arrival = monitor.Tick(new FakeBattlefield(10f, started: true)
                .WithOwn(PlannedFormationClass.Infantry, 0, 0)
                .WithOwn(PlannedFormationClass.Cavalry, 30, 40));

            var activated = Assert.Single(arrival.OfType<StageActivated>());
            Assert.Equal(PlannedFormationClass.Cavalry, activated.Formation);
            Assert.Equal(new MapVec(30f, 90f), activated.Directive.FirstMoveTarget);
        }

        // ---- builders ----

        /// <summary>Infantry: Hold on battle start, Charge after a timer.</summary>
        private static BattlePlan HoldThenTimerCharge(float seconds)
            => Plan(Formation(PlannedFormationClass.Infantry,
                StageOf(null, Hold()),
                StageOf(Timer(seconds), Charge())));

        private static BattlePlan Plan(params object[] parts)
        {
            var plan = new BattlePlan();
            foreach (var part in parts)
            {
                if (part is FormationPlan formation) plan.Formations.Add(formation);
                if (part is MapAnchor anchor) plan.Anchors.Add(anchor);
            }
            return plan;
        }

        private static FormationPlan Formation(PlannedFormationClass cls, params Stage[] stages)
        {
            var plan = new FormationPlan { Formation = cls };
            plan.Stages.AddRange(stages);
            return plan;
        }

        private static Stage StageOf(IEnumerable<TriggerSpec> when, DirectiveSpec directive)
        {
            var stage = new Stage { Do = directive };
            if (when != null) stage.When.AddRange(when);
            return stage;
        }

        private static IEnumerable<TriggerSpec> Timer(float seconds)
            => new[] { new TriggerSpec { Type = TriggerType.TimerElapsed, Seconds = seconds } };

        private static DirectiveSpec Hold() => new() { Type = DirectiveType.Hold };
        private static DirectiveSpec Charge() => new() { Type = DirectiveType.Charge };
        private static DirectiveSpec MoveToAnchor(string anchor) => new() { Type = DirectiveType.MoveTo, Anchor = anchor };
        private static DirectiveSpec MoveToPath(params string[] waypoints)
            => new() { Type = DirectiveType.MoveTo, Path = waypoints.ToList() };

        private static MapAnchor AnchorOwnStart(string id, float forward, float right = 0f)
            => new() { Id = id, Basis = AnchorBasis.OwnStart, Forward = forward, Right = right };

        private static (PlannedFormationClass, MapVec) InfantryAt(float x, float y)
            => (PlannedFormationClass.Infantry, new MapVec(x, y));

        private static (PlannedFormationClass, MapVec) RangedAt(float x, float y)
            => (PlannedFormationClass.Ranged, new MapVec(x, y));

        private static FakeBattlefield Snap(float time, bool started, params (PlannedFormationClass Class, MapVec Position)[] formations)
        {
            var snapshot = new FakeBattlefield(time, started);
            foreach (var (cls, position) in formations)
                snapshot.WithOwn(cls, position.X, position.Y);
            return snapshot;
        }
    }
}
