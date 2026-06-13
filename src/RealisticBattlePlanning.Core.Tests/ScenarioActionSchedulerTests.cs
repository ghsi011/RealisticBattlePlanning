using System.Collections.Generic;
using System.Linq;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Harness;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// The scheduler fires scripted harness inputs against a real PlanMonitor;
    /// these drive both together and assert the monitor reacted — the same
    /// path the in-game recorder and the simulation use.
    /// </summary>
    public class ScenarioActionSchedulerTests
    {
        [Fact]
        public void ActionFiresOnceAtOrAfterItsTime()
        {
            var monitor = SingleInfantry(
                Stage(null, Hold()),
                Stage(PlayerSignal("go"), Charge()));
            var scheduler = new ScenarioActionScheduler(new[] { Signal(10f, "go") });

            Assert.Empty(scheduler.Tick(9.9f, monitor));
            Assert.Single(scheduler.Tick(10f, monitor));
            Assert.Empty(scheduler.Tick(11f, monitor)); // already fired
            Assert.True(scheduler.Done);
        }

        [Fact]
        public void ScriptedSignalReleasesAGatedStage()
        {
            var monitor = SingleInfantry(
                Stage(null, Hold()),
                Stage(PlayerSignal("go"), Charge()));
            var scheduler = new ScenarioActionScheduler(new[] { Signal(10f, "go") });

            // Battle start; hold activates.
            monitor.Tick(Field(0f));
            scheduler.Tick(0f, monitor);

            // Before the action: still holding.
            monitor.Tick(Field(5f));
            scheduler.Tick(5f, monitor);
            Assert.Equal(FormationPlanMode.Active, monitor.GetMode(PlannedFormationClass.Infantry));

            // Fire "go" at 10s; the charge activates on the next monitor tick.
            scheduler.Tick(10f, monitor);
            var events = monitor.Tick(Field(10.25f));
            Assert.Contains(events.OfType<StageActivated>(), e => e.Directive.Spec.Type == DirectiveType.Charge);
        }

        [Fact]
        public void ScriptedOverrideAndResumeDriveTheMode()
        {
            var monitor = SingleInfantry(
                Stage(null, Hold()),
                Stage(Timer(60f), Charge()));
            var scheduler = new ScenarioActionScheduler(new[]
            {
                Override(12f, "Infantry"),
                Resume(18f, "Infantry"),
            });

            monitor.Tick(Field(0f));

            scheduler.Tick(12f, monitor);
            monitor.Tick(Field(12.25f));
            Assert.Equal(FormationPlanMode.Suspended, monitor.GetMode(PlannedFormationClass.Infantry));

            scheduler.Tick(18f, monitor);
            monitor.Tick(Field(18.25f));
            Assert.Equal(FormationPlanMode.Active, monitor.GetMode(PlannedFormationClass.Infantry));
        }

        [Fact]
        public void AllSelectorTargetsEveryGovernedFormation()
        {
            var plan = new BattlePlan
            {
                Formations =
                {
                    Formation(PlannedFormationClass.Infantry, Stage(null, Hold()), Stage(Timer(60f), Charge())),
                    Formation(PlannedFormationClass.Ranged, Stage(null, Hold()), Stage(Timer(60f), Charge())),
                },
            };
            var monitor = new PlanMonitor(plan);
            var scheduler = new ScenarioActionScheduler(new[] { Override(5f, "all") });

            monitor.Tick(FieldWith(0f, PlannedFormationClass.Infantry, PlannedFormationClass.Ranged));
            scheduler.Tick(5f, monitor);
            monitor.Tick(FieldWith(5.25f, PlannedFormationClass.Infantry, PlannedFormationClass.Ranged));

            Assert.Equal(FormationPlanMode.Suspended, monitor.GetMode(PlannedFormationClass.Infantry));
            Assert.Equal(FormationPlanMode.Suspended, monitor.GetMode(PlannedFormationClass.Ranged));
        }

        [Fact]
        public void OutOfOrderActionsFireInTimeOrder()
        {
            var monitor = SingleInfantry(Stage(null, Hold()), Stage(PlayerSignal("go"), Charge()));
            var scheduler = new ScenarioActionScheduler(new[] { Signal(20f, "late"), Signal(10f, "go") });

            // At 10s only the earlier action fires, even though it was listed second.
            var fired = scheduler.Tick(10f, monitor);
            Assert.Single(fired);
            Assert.Contains("go", fired[0]);
        }

        [Fact]
        public void NullActionsAreHarmless()
        {
            var scheduler = new ScenarioActionScheduler(null);
            Assert.True(scheduler.Done);
            Assert.Empty(scheduler.Tick(100f, new PlanMonitor(new BattlePlan())));
        }

        // ---- builders ----

        private static PlanMonitor SingleInfantry(params Stage[] stages)
            => new(new BattlePlan { Formations = { Formation(PlannedFormationClass.Infantry, stages) } });

        private static FormationPlan Formation(PlannedFormationClass cls, params Stage[] stages)
        {
            var f = new FormationPlan { Formation = cls };
            f.Stages.AddRange(stages);
            return f;
        }

        private static Stage Stage(TriggerSpec[] when, DirectiveSpec directive)
        {
            var s = new Stage { Do = directive };
            if (when != null) s.When.AddRange(when);
            return s;
        }

        private static TriggerSpec[] PlayerSignal(string name) => new[] { new TriggerSpec { Type = TriggerType.PlayerSignal, Signal = name } };
        private static TriggerSpec[] Timer(float s) => new[] { new TriggerSpec { Type = TriggerType.TimerElapsed, Seconds = s } };
        private static DirectiveSpec Hold() => new() { Type = DirectiveType.Hold };
        private static DirectiveSpec Charge() => new() { Type = DirectiveType.Charge };

        private static ScenarioAction Signal(float at, string name) => new() { AtSeconds = at, Type = ScenarioActionType.Signal, Signal = name };
        private static ScenarioAction Override(float at, string formation) => new() { AtSeconds = at, Type = ScenarioActionType.Override, Formation = formation };
        private static ScenarioAction Resume(float at, string formation) => new() { AtSeconds = at, Type = ScenarioActionType.Resume, Formation = formation };

        private static FakeBattlefield Field(float time)
            => new FakeBattlefield(time).WithOwn(PlannedFormationClass.Infantry, 0, 0);

        private static FakeBattlefield FieldWith(float time, params PlannedFormationClass[] classes)
        {
            var f = new FakeBattlefield(time);
            foreach (var c in classes)
                f.WithOwn(c, 0, 0);
            return f;
        }
    }
}
