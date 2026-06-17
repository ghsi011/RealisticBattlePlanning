using System.Linq;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// I4 trigger vocabulary: signal bus, enemy-commits sustained approach,
    /// distances, casualties, broken — all over scripted snapshot timelines.
    /// </summary>
    public class PlanMonitorTriggerTests
    {
        [Fact]
        public void EnemyCommitsFiresOnSustainedApproachNotOnBriefProbe()
        {
            // Hold, then fall back when the enemy commits (threshold 2 m/s, sustain 4 s).
            var monitor = Monitor(StageOf(null, Hold()), StageOf(
                new[] { new TriggerSpec { Type = TriggerType.EnemyCommits, SpeedThreshold = 2f, SustainSeconds = 4f } },
                Charge()));

            // t0: battle starts, enemy 100 m out.
            monitor.Tick(Field(0).WithEnemy(1, 0, 100));

            // Probe: one second of fast approach, then it pulls back.
            Assert.Empty(monitor.Tick(Field(1).WithEnemy(1, 0, 95)));
            Assert.Empty(monitor.Tick(Field(2).WithEnemy(1, 0, 98)));

            // Real commitment: closing ≥2 m/s from t3 onward. Sustain clock
            // starts at t3 (first satisfied observation); fires once 4 s of
            // continuous approach have accumulated, at t7.
            Assert.Empty(monitor.Tick(Field(3).WithEnemy(1, 0, 95)));
            Assert.Empty(monitor.Tick(Field(4).WithEnemy(1, 0, 90)));
            Assert.Empty(monitor.Tick(Field(5).WithEnemy(1, 0, 85)));
            Assert.Empty(monitor.Tick(Field(6).WithEnemy(1, 0, 80)));

            var fired = monitor.Tick(Field(7).WithEnemy(1, 0, 75));
            Assert.Single(fired.OfType<StageActivated>());
        }

        [Fact]
        public void SignalEmittedByOneFormationIsReceivedByAnotherNextTick()
        {
            var emitter = StageOf(null, Hold());
            emitter.Emit.Add("go");
            var plan = Plan(
                Formation(PlannedFormationClass.Infantry, emitter),
                Formation(PlannedFormationClass.Ranged,
                    StageOf(null, Hold()),
                    StageOf(new[] { new TriggerSpec { Type = TriggerType.SignalReceived, Signal = "go" } }, Charge())));
            var monitor = new PlanMonitor(plan);

            // Tick 0: both stage 1s activate; "go" is emitted but not yet visible.
            var first = monitor.Tick(Field(0).WithOwn(PlannedFormationClass.Infantry, 0, 0).WithOwn(PlannedFormationClass.Ranged, 20, 0));
            Assert.Single(first.OfType<SignalEmitted>());
            Assert.DoesNotContain(first.OfType<StageActivated>(), e => e.Directive.Spec.Type == DirectiveType.Charge);

            // Tick 1: the latched signal is visible; ranged advances.
            var second = monitor.Tick(Field(0.25f).WithOwn(PlannedFormationClass.Infantry, 0, 0).WithOwn(PlannedFormationClass.Ranged, 20, 0));
            var charge = Assert.Single(second.OfType<StageActivated>());
            Assert.Equal(PlannedFormationClass.Ranged, charge.Formation);
        }

        [Fact]
        public void ExternalSignalDrivesPlayerSignalTrigger()
        {
            var monitor = Monitor(StageOf(null, Hold()), StageOf(
                new[] { new TriggerSpec { Type = TriggerType.PlayerSignal, Signal = "hammer" } },
                Charge()));

            monitor.Tick(Field(0));
            Assert.Empty(monitor.Tick(Field(1)));

            monitor.RaiseExternalSignal("hammer");
            var fired = monitor.Tick(Field(2));
            Assert.Single(fired.OfType<StageActivated>());
        }

        [Fact]
        public void AndOfThreeFiresOnlyWhenAllConditionsHold()
        {
            var when = new[]
            {
                new TriggerSpec { Type = TriggerType.TimerElapsed, Seconds = 10f },
                new TriggerSpec { Type = TriggerType.EnemyWithinDistance, Meters = 50f },
                new TriggerSpec { Type = TriggerType.CasualtiesAbove, Percent = 20f },
            };
            var monitor = Monitor(StageOf(null, Hold()), StageOf(when, Charge()));

            monitor.Tick(Field(0).WithEnemy(1, 0, 200));

            // Timer holds, enemy far, no casualties.
            Assert.Empty(monitor.Tick(Field(11).WithEnemy(1, 0, 200)));
            // Timer + enemy close, casualties still low.
            Assert.Empty(monitor.Tick(Field(12, casualties: 5f).WithEnemy(1, 0, 40)));
            // Timer + casualties, enemy far again.
            Assert.Empty(monitor.Tick(Field(13, casualties: 25f).WithEnemy(1, 0, 200)));

            // All three.
            var fired = monitor.Tick(Field(14, casualties: 25f).WithEnemy(1, 0, 40));
            Assert.Single(fired.OfType<StageActivated>());
        }

        [Fact]
        public void EnemyWithinDistanceRespectsClassSelector()
        {
            var monitor = Monitor(StageOf(null, Hold()), StageOf(
                new[] { new TriggerSpec { Type = TriggerType.EnemyWithinDistance, Formation = "Cavalry", Meters = 50f } },
                Charge()));

            monitor.Tick(Field(0));

            // An infantry enemy in range must not satisfy a Cavalry selector.
            Assert.Empty(monitor.Tick(Field(1).WithEnemy(1, 0, 30, cls: PlannedFormationClass.Infantry)));

            var fired = monitor.Tick(Field(2)
                .WithEnemy(1, 0, 30, cls: PlannedFormationClass.Infantry)
                .WithEnemy(2, 0, 40, cls: PlannedFormationClass.Cavalry));
            Assert.Single(fired.OfType<StageActivated>());
        }

        [Fact]
        public void FriendlyWithinDistanceTracksThePlayer()
        {
            // "Charge when I reach the hill" idiom (A3.10): infantry waits for the player.
            var monitor = Monitor(StageOf(null, Hold()), StageOf(
                new[] { new TriggerSpec { Type = TriggerType.FriendlyWithinDistance, Formation = "Player", Meters = 25f } },
                Charge()));

            monitor.Tick(Field(0).WithPlayer(500, 500));
            Assert.Empty(monitor.Tick(Field(1).WithPlayer(200, 200)));

            var fired = monitor.Tick(Field(2).WithPlayer(10, 20));
            Assert.Single(fired.OfType<StageActivated>());
        }

        [Fact]
        public void CasualtiesAboveCountsAVanishedFormationAsTotalLoss()
        {
            // Ranged watches infantry casualties; infantry formation disappears.
            var plan = Plan(Formation(PlannedFormationClass.Ranged,
                StageOf(null, Hold()),
                StageOf(new[] { new TriggerSpec { Type = TriggerType.CasualtiesAbove, Formation = "Infantry", Percent = 50f } }, Charge())));
            var monitor = new PlanMonitor(plan);

            monitor.Tick(new FakeBattlefield(0)
                .WithOwn(PlannedFormationClass.Ranged, 0, 0)
                .WithOwn(PlannedFormationClass.Infantry, 0, 50));
            Assert.Empty(monitor.Tick(new FakeBattlefield(1)
                .WithOwn(PlannedFormationClass.Ranged, 0, 0)
                .WithOwn(PlannedFormationClass.Infantry, 0, 50, casualtiesPercent: 30f)));

            // Infantry gone entirely: counts as 100% casualties.
            var fired = monitor.Tick(new FakeBattlefield(2).WithOwn(PlannedFormationClass.Ranged, 0, 0));
            Assert.Single(fired.OfType<StageActivated>());
        }

        [Fact]
        public void AdvancingForwardEmitsStageCompletedForTheStageLeft()
        {
            var monitor = Monitor(
                StageOf(null, Hold()),                                                                  // 1: battle start
                StageOf(new[] { new TriggerSpec { Type = TriggerType.TimerElapsed, Seconds = 5f } }, Charge()),   // 2
                StageOf(new[] { new TriggerSpec { Type = TriggerType.TimerElapsed, Seconds = 5f } }, Hold()));     // 3

            monitor.Tick(Field(0));                       // stage 1 activates; nothing completed yet
            var toStage2 = monitor.Tick(Field(6));        // 5s after stage 1 -> stage 2; stage 1 completed
            Assert.Contains(toStage2.OfType<StageCompleted>(), e => e.StageIndex == 0);
            var toStage3 = monitor.Tick(Field(12));       // 5s after stage 2 -> stage 3; stage 2 completed
            Assert.Contains(toStage3.OfType<StageCompleted>(), e => e.StageIndex == 1);
        }

        [Fact]
        public void EnemyBrokenFiresOnRouting()
        {
            var monitor = Monitor(StageOf(null, Hold()), StageOf(
                new[] { new TriggerSpec { Type = TriggerType.EnemyBroken } },
                Charge()));

            monitor.Tick(Field(0).WithEnemy(1, 0, 100));
            Assert.Empty(monitor.Tick(Field(1).WithEnemy(1, 0, 100)));

            var routed = monitor.Tick(Field(2).WithEnemy(1, 0, 100, broken: true));
            Assert.Single(routed.OfType<StageActivated>());
        }

        [Fact]
        public void EnemyBrokenDoesNotFireOnAMeleeKill()
        {
            // A formation cut down in melee (vanishes without ever routing) is NOT
            // "broken" (A4 = routs). Only an observed rout latches the trigger.
            var monitor = Monitor(StageOf(null, Hold()), StageOf(
                new[] { new TriggerSpec { Type = TriggerType.EnemyBroken } },
                Charge()));

            monitor.Tick(Field(0).WithEnemy(7, 0, 100));     // alive, unbroken
            Assert.Empty(monitor.Tick(Field(1).WithEnemy(7, 0, 100)));
            var gone = monitor.Tick(Field(2));               // vanished, never broke
            Assert.Empty(gone.OfType<StageActivated>());
        }

        [Fact]
        public void EnemyCommitsIgnoresApproachOutsideEngagementRange()
        {
            // Default max range 150 m: a steady advance from 300 m out is
            // maneuvering, not committing (2026-06-12 playtest finding).
            var monitor = Monitor(StageOf(null, Hold()), StageOf(
                new[] { new TriggerSpec { Type = TriggerType.EnemyCommits, SpeedThreshold = 2f, SustainSeconds = 2f } },
                Charge()));

            monitor.Tick(Field(0).WithEnemy(1, 0, 300));
            // Closing 10 m/s but far outside range: never sustains.
            Assert.Empty(monitor.Tick(Field(1).WithEnemy(1, 0, 290)));
            Assert.Empty(monitor.Tick(Field(2).WithEnemy(1, 0, 280)));
            Assert.Empty(monitor.Tick(Field(3).WithEnemy(1, 0, 270)));
            Assert.Empty(monitor.Tick(Field(4).WithEnemy(1, 0, 260)));

            // Jump cut: now inside range. Sustain starts here, fires 2 s later.
            Assert.Empty(monitor.Tick(Field(5).WithEnemy(1, 0, 140)));
            Assert.Empty(monitor.Tick(Field(6).WithEnemy(1, 0, 130)));
            var fired = monitor.Tick(Field(7).WithEnemy(1, 0, 120));
            Assert.Single(fired.OfType<StageActivated>());
        }

        [Fact]
        public void BrokenEnemiesDoNotSustainEnemyCommits()
        {
            var monitor = Monitor(StageOf(null, Hold()), StageOf(
                new[] { new TriggerSpec { Type = TriggerType.EnemyCommits, SpeedThreshold = 2f, SustainSeconds = 2f } },
                Charge()));

            monitor.Tick(Field(0).WithEnemy(1, 0, 100));
            // Closing fast but already routing (fleeing toward us downhill, say).
            Assert.Empty(monitor.Tick(Field(1).WithEnemy(1, 0, 90, broken: true)));
            Assert.Empty(monitor.Tick(Field(2).WithEnemy(1, 0, 80, broken: true)));
            Assert.Empty(monitor.Tick(Field(3).WithEnemy(1, 0, 70, broken: true)));
            Assert.Empty(monitor.Tick(Field(4).WithEnemy(1, 0, 60, broken: true)));
        }

        [Fact]
        public void EnemyCommitsRebaselinesWhenAnEnemyIdVanishesAndReturns()
        {
            // Engine ids are team/slot based: a reinforcement wave reuses the
            // id of a wiped formation. Stale sustain/approach state must not
            // let the new wave fire EnemyCommits without re-sustaining.
            var monitor = Monitor(StageOf(null, Hold()), StageOf(
                new[] { new TriggerSpec { Type = TriggerType.EnemyCommits, SpeedThreshold = 2f, SustainSeconds = 4f } },
                Charge()));

            // First wave: sustains 2 s of the required 4, then is wiped.
            monitor.Tick(Field(0).WithEnemy(1, 0, 140));
            Assert.Empty(monitor.Tick(Field(1).WithEnemy(1, 0, 135)));
            Assert.Empty(monitor.Tick(Field(2).WithEnemy(1, 0, 130)));
            Assert.Empty(monitor.Tick(Field(3).WithEnemy(1, 0, 125)));
            Assert.Empty(monitor.Tick(Field(4))); // wave gone

            // Second wave reuses id 1, much closer. Without the purge the
            // stale state fires immediately on reappearance.
            Assert.Empty(monitor.Tick(Field(5).WithEnemy(1, 0, 60)));
            Assert.Empty(monitor.Tick(Field(6).WithEnemy(1, 0, 55)));
            Assert.Empty(monitor.Tick(Field(7).WithEnemy(1, 0, 50)));
            Assert.Empty(monitor.Tick(Field(8).WithEnemy(1, 0, 45)));
            Assert.Empty(monitor.Tick(Field(9).WithEnemy(1, 0, 40)));

            // Sustain re-baselined at t=6 (first computable closing speed):
            // four sustained seconds land at t=10.
            var fired = monitor.Tick(Field(10).WithEnemy(1, 0, 35));
            Assert.Single(fired.OfType<StageActivated>());
        }

        // ---- builders ----

        /// <summary>A single-infantry-formation plan from the given stages.</summary>
        private static PlanMonitor Monitor(params Stage[] stages)
            => new(Plan(Formation(PlannedFormationClass.Infantry, stages)));

        /// <summary>Battlefield with infantry at the origin (battle running).</summary>
        private static FakeBattlefield Field(float time, float casualties = 0f)
            => new FakeBattlefield(time).WithOwn(PlannedFormationClass.Infantry, 0, 0, casualties);

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

        private static Stage StageOf(TriggerSpec[] when, DirectiveSpec directive)
        {
            var stage = new Stage { Do = directive };
            if (when != null) stage.When.AddRange(when);
            return stage;
        }

        private static DirectiveSpec Hold() => new() { Type = DirectiveType.Hold };
        private static DirectiveSpec Charge() => new() { Type = DirectiveType.Charge };
    }
}
