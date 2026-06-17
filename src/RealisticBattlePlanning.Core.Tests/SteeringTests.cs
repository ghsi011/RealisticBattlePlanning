using System.Collections.Generic;
using System.Linq;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// Steering-directive geometry over scripted timelines (I6): the monitor
    /// recomputes move goals for Skirmish / FlankArc / Screen / Follow as the
    /// battlefield moves, and re-issues them only past the update threshold.
    /// Attack direction is (0,1), so right-of-axis is +X.
    /// </summary>
    public class SteeringTests
    {
        [Fact]
        public void SkirmishStandsAtStandoffOnTheLineTowardOwnPosition()
        {
            var monitor = new PlanMonitor(Plan(Formation(PlannedFormationClass.HorseArcher,
                StageOf(null, new DirectiveSpec { Type = DirectiveType.Skirmish, StandoffMeters = 50f }))));

            var events = monitor.Tick(Snap(0f).WithOwn(PlannedFormationClass.HorseArcher, 0, 0).WithEnemy(1, 0, 100));

            var steering = Assert.Single(events.OfType<SteeringTargetChanged>());
            Assert.Equal(new MapVec(0f, 50f), steering.Target);
        }

        [Fact]
        public void SkirmishGivesGroundAsTheEnemyCloses()
        {
            var monitor = new PlanMonitor(Plan(Formation(PlannedFormationClass.HorseArcher,
                StageOf(null, new DirectiveSpec { Type = DirectiveType.Skirmish, StandoffMeters = 50f }))));

            monitor.Tick(Snap(0f).WithOwn(PlannedFormationClass.HorseArcher, 0, 0).WithEnemy(1, 0, 100));
            var events = monitor.Tick(Snap(1f).WithOwn(PlannedFormationClass.HorseArcher, 0, 10).WithEnemy(1, 0, 70));

            var steering = Assert.Single(events.OfType<SteeringTargetChanged>());
            Assert.Equal(new MapVec(0f, 20f), steering.Target);
        }

        [Fact]
        public void SmallShiftsDoNotReissueTheOrder()
        {
            var monitor = new PlanMonitor(Plan(Formation(PlannedFormationClass.HorseArcher,
                StageOf(null, new DirectiveSpec { Type = DirectiveType.Skirmish, StandoffMeters = 50f }))));

            monitor.Tick(Snap(0f).WithOwn(PlannedFormationClass.HorseArcher, 0, 0).WithEnemy(1, 0, 100));
            // Enemy creeps 5 m: under the 8 m threshold, no new order.
            var events = monitor.Tick(Snap(1f).WithOwn(PlannedFormationClass.HorseArcher, 0, 0).WithEnemy(1, 0, 95));

            Assert.Empty(events.OfType<SteeringTargetChanged>());
        }

        [Fact]
        public void FlankArcSidesMirrorAcrossTheBattleAxis()
        {
            MapVec TargetFor(FlankSide side, PlannedFormationClass cls)
            {
                var monitor = new PlanMonitor(Plan(Formation(cls,
                    StageOf(null, new DirectiveSpec { Type = DirectiveType.FlankArc, Side = side, StandoffMeters = 50f, MissileOnly = true }))));
                var events = monitor.Tick(Snap(0f).WithOwn(cls, 0, 0).WithEnemy(1, 0, 100));
                return Assert.Single(events.OfType<SteeringTargetChanged>()).Target;
            }

            Assert.Equal(new MapVec(-50f, 100f), TargetFor(FlankSide.Left, PlannedFormationClass.HorseArcher));
            Assert.Equal(new MapVec(50f, 100f), TargetFor(FlankSide.Right, PlannedFormationClass.LightCavalry));
        }

        [Fact]
        public void FlankArcChargesHomeOnceItReachesItsStationWhenChargeIsAllowed()
        {
            // MissileOnly unset => the charge is allowed (A5).
            var monitor = new PlanMonitor(Plan(Formation(PlannedFormationClass.Cavalry,
                StageOf(null, new DirectiveSpec { Type = DirectiveType.FlankArc, Side = FlankSide.Left, StandoffMeters = 50f }))));

            // Far from the abeam station (-50,100): still steering toward it, not charging.
            var approach = monitor.Tick(Snap(0f).WithOwn(PlannedFormationClass.Cavalry, 0, 0).WithEnemy(1, 0, 100));
            Assert.Single(approach.OfType<SteeringTargetChanged>());
            Assert.Empty(approach.OfType<ChargeOrdered>());

            // Arrived on the flank station: commit to the charge instead of circling.
            var arrived = monitor.Tick(Snap(1f).WithOwn(PlannedFormationClass.Cavalry, -48, 98).WithEnemy(1, 0, 100));
            Assert.Single(arrived.OfType<ChargeOrdered>());
            Assert.Empty(arrived.OfType<SteeringTargetChanged>());

            // Committed: the enemy relocating no longer drags it back to a standoff.
            var after = monitor.Tick(Snap(2f).WithOwn(PlannedFormationClass.Cavalry, -48, 98).WithEnemy(1, 80, 40));
            Assert.Empty(after.OfType<SteeringTargetChanged>());
            Assert.Empty(after.OfType<ChargeOrdered>());
        }

        [Fact]
        public void MissileOnlyFlankArcNeverCharges()
        {
            var monitor = new PlanMonitor(Plan(Formation(PlannedFormationClass.HorseArcher,
                StageOf(null, new DirectiveSpec { Type = DirectiveType.FlankArc, Side = FlankSide.Left, StandoffMeters = 50f, MissileOnly = true }))));

            monitor.Tick(Snap(0f).WithOwn(PlannedFormationClass.HorseArcher, 0, 0).WithEnemy(1, 0, 100));
            // Sitting right on the abeam station: a charge-allowed arc would commit; this stays a kiter.
            var onStation = monitor.Tick(Snap(1f).WithOwn(PlannedFormationClass.HorseArcher, -50, 100).WithEnemy(1, 0, 100));
            Assert.Empty(onStation.OfType<ChargeOrdered>());
        }

        [Fact]
        public void ScreenStandsBetweenTheProtectedFormationAndTheThreat()
        {
            var monitor = new PlanMonitor(Plan(Formation(PlannedFormationClass.Cavalry,
                StageOf(null, new DirectiveSpec { Type = DirectiveType.Screen, Target = "Infantry", GapMeters = 30f }))));

            var events = monitor.Tick(Snap(0f)
                .WithOwn(PlannedFormationClass.Cavalry, 50, 0)
                .WithOwn(PlannedFormationClass.Infantry, 0, 50)
                .WithEnemy(1, 0, 150));

            var steering = Assert.Single(events.OfType<SteeringTargetChanged>());
            Assert.Equal(new MapVec(0f, 80f), steering.Target);
        }

        [Fact]
        public void FollowTracksTheFormationWithTheConfiguredOffset()
        {
            var monitor = new PlanMonitor(Plan(Formation(PlannedFormationClass.Ranged,
                StageOf(null, new DirectiveSpec { Type = DirectiveType.Follow, Target = "Infantry", OffsetForwardMeters = -20f, OffsetRightMeters = 5f }))));

            var first = monitor.Tick(Snap(0f)
                .WithOwn(PlannedFormationClass.Ranged, 0, 0)
                .WithOwn(PlannedFormationClass.Infantry, 10, 40));
            Assert.Equal(new MapVec(15f, 20f), Assert.Single(first.OfType<SteeringTargetChanged>()).Target);

            // The followed formation advances; the station moves with it.
            var second = monitor.Tick(Snap(1f)
                .WithOwn(PlannedFormationClass.Ranged, 15, 20)
                .WithOwn(PlannedFormationClass.Infantry, 10, 60));
            Assert.Equal(new MapVec(15f, 40f), Assert.Single(second.OfType<SteeringTargetChanged>()).Target);
        }

        [Fact]
        public void FlankArcSideIsStableWhenTheSnapshotAxisDriftsMidBattle()
        {
            // The per-tick AttackDirection follows army centroids and can
            // swing or flip as the trap closes; flank sides must stay pinned
            // to the battle-start axis (the one anchors use).
            var monitor = new PlanMonitor(Plan(Formation(PlannedFormationClass.HorseArcher,
                StageOf(null, new DirectiveSpec { Type = DirectiveType.FlankArc, Side = FlankSide.Left, StandoffMeters = 50f, MissileOnly = true }))));

            var first = monitor.Tick(Snap(0f).WithOwn(PlannedFormationClass.HorseArcher, 0, 0).WithEnemy(1, 0, 100));
            Assert.Equal(new MapVec(-50f, 100f), Assert.Single(first.OfType<SteeringTargetChanged>()).Target);

            var flipped = Snap(1f).WithOwn(PlannedFormationClass.HorseArcher, -30, 60).WithEnemy(1, 0, 80);
            flipped.AttackDirection = new MapVec(0f, -1f);
            var second = monitor.Tick(flipped);

            var steering = Assert.Single(second.OfType<SteeringTargetChanged>());
            Assert.Equal(new MapVec(-50f, 80f), steering.Target);
        }

        [Fact]
        public void NoEnemiesMeansNoSteeringForEnemyRelativeDirectives()
        {
            var monitor = new PlanMonitor(Plan(Formation(PlannedFormationClass.HorseArcher,
                StageOf(null, new DirectiveSpec { Type = DirectiveType.Skirmish }))));

            var events = monitor.Tick(Snap(0f).WithOwn(PlannedFormationClass.HorseArcher, 0, 0));

            Assert.Empty(events.OfType<SteeringTargetChanged>());
        }

        [Fact]
        public void SteeringStopsWhenTheNextStageActivates()
        {
            var monitor = new PlanMonitor(Plan(Formation(PlannedFormationClass.HorseArcher,
                StageOf(null, new DirectiveSpec { Type = DirectiveType.Skirmish, StandoffMeters = 50f }),
                StageOf(new[] { new TriggerSpec { Type = TriggerType.TimerElapsed, Seconds = 10f } },
                    new DirectiveSpec { Type = DirectiveType.Hold }))));

            monitor.Tick(Snap(0f).WithOwn(PlannedFormationClass.HorseArcher, 0, 0).WithEnemy(1, 0, 100));
            var holdTick = monitor.Tick(Snap(11f).WithOwn(PlannedFormationClass.HorseArcher, 0, 50).WithEnemy(1, 0, 100));
            Assert.Single(holdTick.OfType<StageActivated>());

            // Enemy relocates far: a still-skirmishing formation would re-steer.
            var after = monitor.Tick(Snap(12f).WithOwn(PlannedFormationClass.HorseArcher, 0, 50).WithEnemy(1, 80, 40));
            Assert.Empty(after.OfType<SteeringTargetChanged>());
        }

        [Fact]
        public void SkirmishHonorsAnEnemyClassSelector()
        {
            var monitor = new PlanMonitor(Plan(Formation(PlannedFormationClass.HorseArcher,
                StageOf(null, new DirectiveSpec { Type = DirectiveType.Skirmish, Target = "Ranged", StandoffMeters = 50f }))));

            // The infantry blob is nearer, but the selector says Ranged.
            var events = monitor.Tick(Snap(0f)
                .WithOwn(PlannedFormationClass.HorseArcher, 0, 0)
                .WithEnemy(1, 0, 60, cls: PlannedFormationClass.Infantry)
                .WithEnemy(2, 0, 150, cls: PlannedFormationClass.Ranged));

            var steering = Assert.Single(events.OfType<SteeringTargetChanged>());
            Assert.Equal(new MapVec(0f, 100f), steering.Target);
        }

        // ---- builders ----

        private static BattlePlan Plan(params FormationPlan[] formations)
        {
            var plan = new BattlePlan();
            plan.Formations.AddRange(formations);
            return plan;
        }

        private static FormationPlan Formation(PlannedFormationClass cls, params Stage[] stages)
        {
            var formation = new FormationPlan { Formation = cls };
            formation.Stages.AddRange(stages);
            return formation;
        }

        private static Stage StageOf(IEnumerable<TriggerSpec> when, DirectiveSpec directive)
        {
            var stage = new Stage { Do = directive };
            if (when != null) stage.When.AddRange(when);
            return stage;
        }

        private static FakeBattlefield Snap(float time) => new(time, started: true);
    }
}
