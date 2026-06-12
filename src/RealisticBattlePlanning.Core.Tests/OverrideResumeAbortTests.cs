using System.Collections.Generic;
using System.Linq;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// I7 (spec A3.7, B4-B6): player overrides suspend, resume picks the
    /// most recent stage whose trigger currently holds, aborts end the plan
    /// for good, and invalidated directives skip forward or hold loudly.
    /// </summary>
    public class OverrideResumeAbortTests
    {
        // ---- aborts (A3.7 / B4) ----

        [Fact]
        public void CasualtiesThresholdIsHonored()
        {
            var monitor = Monitor(HoldThenTimerCharge(abort: new AbortConditions { CasualtiesAbovePercent = 50f }));

            monitor.Tick(Infantry(0f, casualties: 0f));
            Assert.Empty(monitor.Tick(Infantry(1f, casualties: 49f)).OfType<PlanAborted>());

            var aborted = monitor.Tick(Infantry(2f, casualties: 51f));
            var abort = Assert.Single(aborted.OfType<PlanAborted>());
            Assert.Contains("casualties", abort.Reason);
            Assert.Equal(FormationPlanMode.Aborted, monitor.GetMode(PlannedFormationClass.Infantry));

            // Terminal: the timer stage never fires afterwards.
            Assert.Empty(monitor.Tick(Infantry(500f, casualties: 51f)));
        }

        [Fact]
        public void CommanderDeathAbortsRegardlessOfConfiguredThresholds()
        {
            var monitor = Monitor(HoldThenTimerCharge(abort: new AbortConditions
            {
                CasualtiesAbovePercent = 99f,
                OnCommanderIncapacitated = false,
                OnFormationBroken = false,
            }));

            monitor.Tick(Infantry(0f));
            var events = monitor.Tick(Infantry(1f, commanderDown: true));

            var abort = Assert.Single(events.OfType<PlanAborted>());
            Assert.Contains("commander down", abort.Reason);
        }

        [Fact]
        public void FormationBreakingAbortsOnlyWhenConfigured()
        {
            var tolerant = Monitor(HoldThenTimerCharge(abort: new AbortConditions { OnFormationBroken = false }));
            tolerant.Tick(Infantry(0f));
            Assert.Empty(tolerant.Tick(Infantry(1f, broken: true)).OfType<PlanAborted>());

            var strict = Monitor(HoldThenTimerCharge(abort: new AbortConditions { OnFormationBroken = true }));
            strict.Tick(Infantry(0f));
            var abort = Assert.Single(strict.Tick(Infantry(1f, broken: true)).OfType<PlanAborted>());
            Assert.Contains("broke", abort.Reason);
        }

        [Fact]
        public void AWipedOutFormationAborts()
        {
            var monitor = Monitor(HoldThenTimerCharge());
            monitor.Tick(Infantry(0f));

            var events = monitor.Tick(new FakeBattlefield(1f)); // formation gone

            var abort = Assert.Single(events.OfType<PlanAborted>());
            Assert.Contains("wiped out", abort.Reason);
        }

        // ---- override & resume (B5) ----

        [Fact]
        public void PlayerOverrideSuspendsTriggerEvaluationAndSteering()
        {
            var monitor = new PlanMonitor(Plan(Formation(
                PlannedFormationClass.Infantry,
                StageOf(null, new DirectiveSpec { Type = DirectiveType.Skirmish, StandoffMeters = 50f }),
                StageOf(Timer(10f), new DirectiveSpec { Type = DirectiveType.Charge }))));

            monitor.Tick(Infantry(0f).WithEnemy(1, 0, 100));
            monitor.NotifyPlayerOverride(PlannedFormationClass.Infantry);

            var suspendTick = monitor.Tick(Infantry(1f).WithEnemy(1, 0, 100));
            Assert.Single(suspendTick.OfType<PlanSuspended>());
            Assert.Equal(FormationPlanMode.Suspended, monitor.GetMode(PlannedFormationClass.Infantry));

            // Timer long elapsed and the enemy relocated: nothing may happen.
            var later = monitor.Tick(Infantry(60f).WithEnemy(1, 80, 30));
            Assert.Empty(later);
        }

        [Fact]
        public void ResumePicksTheMostRecentStageWhoseTriggerHolds()
        {
            var monitor = new PlanMonitor(Plan(Formation(
                PlannedFormationClass.Infantry,
                StageOf(null, Hold()),
                StageOf(Timer(10f), Hold()),
                StageOf(new[] { new TriggerSpec { Type = TriggerType.SignalReceived, Signal = "go" } }, Charge()))));

            monitor.Tick(Infantry(0f));
            monitor.NotifyPlayerOverride(PlannedFormationClass.Infantry);
            monitor.Tick(Infantry(1f));

            // While the player held the formation, the battle moved on.
            monitor.RaiseExternalSignal("go");
            monitor.Tick(Infantry(2f));
            monitor.RequestResume(PlannedFormationClass.Infantry);

            var events = monitor.Tick(Infantry(3f));
            var resumed = Assert.Single(events.OfType<PlanResumed>());
            Assert.Equal(2, resumed.StageIndex);
            var activated = Assert.Single(events.OfType<StageActivated>());
            Assert.Equal(2, activated.StageIndex);
            Assert.Equal(DirectiveType.Charge, activated.Directive.Spec.Type);
        }

        [Fact]
        public void ResumeFallsBackToTheSuspendedStageWhenNoTriggerHolds()
        {
            var monitor = new PlanMonitor(Plan(Formation(
                PlannedFormationClass.Infantry,
                StageOf(null, Hold()),
                StageOf(Timer(100f), Hold()),
                StageOf(new[] { new TriggerSpec { Type = TriggerType.SignalReceived, Signal = "go" } }, Charge()))));

            monitor.Tick(Infantry(0f));
            monitor.NotifyPlayerOverride(PlannedFormationClass.Infantry);
            monitor.Tick(Infantry(1f));
            monitor.RequestResume(PlannedFormationClass.Infantry);

            var events = monitor.Tick(Infantry(2f));
            Assert.Equal(0, Assert.Single(events.OfType<PlanResumed>()).StageIndex);
            Assert.Equal(0, Assert.Single(events.OfType<StageActivated>()).StageIndex);
        }

        [Fact]
        public void ResumeIsRefusedAfterAnAbort()
        {
            var monitor = Monitor(HoldThenTimerCharge());
            monitor.Tick(Infantry(0f));
            monitor.Tick(Infantry(1f, commanderDown: true)); // aborts

            monitor.RequestResume(PlannedFormationClass.Infantry);
            Assert.Empty(monitor.Tick(Infantry(2f, commanderDown: true)));
            Assert.Equal(FormationPlanMode.Aborted, monitor.GetMode(PlannedFormationClass.Infantry));
        }

        [Fact]
        public void AnAbortConditionArisenUnderManualControlAbortsInsteadOfResuming()
        {
            var monitor = Monitor(HoldThenTimerCharge());
            monitor.Tick(Infantry(0f));
            monitor.NotifyPlayerOverride(PlannedFormationClass.Infantry);
            monitor.Tick(Infantry(1f));

            // Commander dies while the player holds the formation.
            monitor.RequestResume(PlannedFormationClass.Infantry);
            var events = monitor.Tick(Infantry(2f, commanderDown: true));

            Assert.Single(events.OfType<PlanAborted>());
            Assert.Empty(events.OfType<PlanResumed>());
        }

        [Fact]
        public void ResumeScansDoNotSeedEnemyCommitsSustainState()
        {
            // The resume scan evaluates stages that will NOT be continuously
            // observed afterwards. If it touched the sustain/approach
            // trackers, a stale entry could fire EnemyCommits instantly when
            // its stage finally becomes pending — the same defect class as
            // the reinforcement id-reuse bug.
            var monitor = new PlanMonitor(Plan(Formation(
                PlannedFormationClass.Infantry,
                StageOf(null, Hold()),
                StageOf(Timer(50f), Hold()),
                StageOf(new[] { new TriggerSpec { Type = TriggerType.EnemyCommits, SpeedThreshold = 2f, SustainSeconds = 4f } }, Charge()))));

            // Two suspend/resume rounds while an enemy closes fast in range:
            // two resume scans, one tick apart, would seed approach + sustain.
            monitor.Tick(Infantry(0f).WithEnemy(1, 0, 140));
            monitor.NotifyPlayerOverride(PlannedFormationClass.Infantry);
            monitor.Tick(Infantry(1f).WithEnemy(1, 0, 134));
            monitor.RequestResume(PlannedFormationClass.Infantry);
            monitor.Tick(Infantry(2f).WithEnemy(1, 0, 128));
            monitor.NotifyPlayerOverride(PlannedFormationClass.Infantry);
            monitor.Tick(Infantry(3f).WithEnemy(1, 0, 122));
            monitor.RequestResume(PlannedFormationClass.Infantry);
            monitor.Tick(Infantry(4f).WithEnemy(1, 0, 116));

            // The timer stage activates; stage 3's EnemyCommits becomes the
            // pending trigger only now, with the enemy much closer.
            Assert.Single(monitor.Tick(Infantry(54f).WithEnemy(1, 0, 14)).OfType<StageActivated>());

            // First pending evaluations must NOT fire instantly off stale
            // resume-scan state: the sustain window starts fresh here.
            Assert.Empty(monitor.Tick(Infantry(55f).WithEnemy(1, 0, 12)));
            Assert.Empty(monitor.Tick(Infantry(56f).WithEnemy(1, 0, 10)));
            Assert.Empty(monitor.Tick(Infantry(57f).WithEnemy(1, 0, 8)));
            Assert.Empty(monitor.Tick(Infantry(58f).WithEnemy(1, 0, 6)));
            Assert.Empty(monitor.Tick(Infantry(59f).WithEnemy(1, 0, 4)));

            var fired = monitor.Tick(Infantry(60f).WithEnemy(1, 0, 2));
            Assert.Single(fired.OfType<StageActivated>());
        }

        // ---- invalidation (B6) ----

        [Fact]
        public void AVanishedLiveReferenceSkipsToTheNextEvaluableStage()
        {
            var plan = Plan(
                Formation(PlannedFormationClass.Cavalry,
                    StageOf(null, new DirectiveSpec { Type = DirectiveType.Screen, Target = "Infantry", GapMeters = 25f }),
                    StageOf(Timer(9999f), new DirectiveSpec { Type = DirectiveType.MoveTo, Anchor = "rear" })),
                new MapAnchor { Id = "rear", Basis = AnchorBasis.TeamCenter, Forward = -40f });
            var monitor = new PlanMonitor(plan);

            monitor.Tick(new FakeBattlefield(0f)
                .WithOwn(PlannedFormationClass.Cavalry, 0, 0)
                .WithOwn(PlannedFormationClass.Infantry, 0, 20)
                .WithEnemy(1, 0, 100));

            // The escorted infantry is wiped out.
            var events = monitor.Tick(new FakeBattlefield(5f)
                .WithOwn(PlannedFormationClass.Cavalry, 0, 10)
                .WithEnemy(1, 0, 100));

            var skipped = Assert.Single(events.OfType<StageSkipped>());
            Assert.Equal(0, skipped.StageIndex);
            Assert.Contains("Infantry", skipped.Reason);

            var activated = Assert.Single(events.OfType<StageActivated>());
            Assert.Equal(1, activated.StageIndex);
            Assert.Equal(DirectiveType.MoveTo, activated.Directive.Spec.Type);
        }

        [Fact]
        public void NoEvaluableStageLeftHoldsAndNotifiesOnce()
        {
            var monitor = new PlanMonitor(Plan(Formation(
                PlannedFormationClass.Cavalry,
                StageOf(null, new DirectiveSpec { Type = DirectiveType.Screen, Target = "Infantry", GapMeters = 25f }))));

            monitor.Tick(new FakeBattlefield(0f)
                .WithOwn(PlannedFormationClass.Cavalry, 0, 0)
                .WithOwn(PlannedFormationClass.Infantry, 0, 20));

            var events = monitor.Tick(new FakeBattlefield(5f).WithOwn(PlannedFormationClass.Cavalry, 0, 0));
            Assert.Single(events.OfType<StageSkipped>());
            var holding = Assert.Single(events.OfType<PlanHolding>());
            Assert.Contains("no evaluable stage", holding.Reason);

            // No event spam while nothing changes.
            Assert.Empty(monitor.Tick(new FakeBattlefield(6f).WithOwn(PlannedFormationClass.Cavalry, 0, 0)));
            Assert.Empty(monitor.Tick(new FakeBattlefield(7f).WithOwn(PlannedFormationClass.Cavalry, 0, 0)));
        }

        [Fact]
        public void ASkippedToStageActivatesImmediatelyAndRebasesItsTimers()
        {
            // Pins the B6 semantics: the skip FIRES the next evaluable stage
            // (its trigger is bypassed), and downstream timers measure from
            // skip time. Phase 2 reaction delays layer on activation — do not
            // "fix" this to arm triggers instead.
            var plan = Plan(
                Formation(PlannedFormationClass.Cavalry,
                    StageOf(null, new DirectiveSpec { Type = DirectiveType.Screen, Target = "Infantry", GapMeters = 25f }),
                    StageOf(Timer(30f), Hold()),
                    StageOf(Timer(5f), Charge())));
            var monitor = new PlanMonitor(plan);

            monitor.Tick(new FakeBattlefield(0f)
                .WithOwn(PlannedFormationClass.Cavalry, 0, 0)
                .WithOwn(PlannedFormationClass.Infantry, 0, 20));

            // Escort wiped at t=10: stage 2 activates NOW (its 30 s timer is
            // bypassed), and stage 3's 5 s timer counts from t=10.
            var skipTick = monitor.Tick(new FakeBattlefield(10f).WithOwn(PlannedFormationClass.Cavalry, 0, 0));
            Assert.Single(skipTick.OfType<StageSkipped>());
            Assert.Equal(1, Assert.Single(skipTick.OfType<StageActivated>()).StageIndex);

            Assert.Empty(monitor.Tick(new FakeBattlefield(14.9f).WithOwn(PlannedFormationClass.Cavalry, 0, 0)));
            var charge = monitor.Tick(new FakeBattlefield(15.1f).WithOwn(PlannedFormationClass.Cavalry, 0, 0));
            Assert.Equal(2, Assert.Single(charge.OfType<StageActivated>()).StageIndex);
        }

        [Fact]
        public void ActivationSkipsAnInevaluableStage()
        {
            var monitor = new PlanMonitor(Plan(Formation(
                PlannedFormationClass.Infantry,
                StageOf(null, Hold()),
                StageOf(Timer(5f), new DirectiveSpec { Type = DirectiveType.Skirmish }), // no enemies on the field
                StageOf(Timer(1f), Charge()))));

            monitor.Tick(Infantry(0f));
            var events = monitor.Tick(Infantry(6f));

            var skipped = Assert.Single(events.OfType<StageSkipped>());
            Assert.Equal(1, skipped.StageIndex);
            Assert.Contains("no enemies", skipped.Reason);
            var activated = Assert.Single(events.OfType<StageActivated>());
            Assert.Equal(2, activated.StageIndex);
            Assert.Equal(DirectiveType.Charge, activated.Directive.Spec.Type);
        }

        // ---- builders ----

        private static FormationPlan HoldThenTimerCharge(AbortConditions abort = null)
        {
            var formation = Formation(PlannedFormationClass.Infantry,
                StageOf(null, Hold()),
                StageOf(Timer(300f), Charge()));
            if (abort != null)
                formation.Abort = abort;
            return formation;
        }

        private static PlanMonitor Monitor(FormationPlan formation) => new(Plan(formation));

        private static FakeBattlefield Infantry(float time, float casualties = 0f, bool commanderDown = false, bool broken = false)
            => new FakeBattlefield(time).WithOwn(PlannedFormationClass.Infantry, 0, 0, casualties, commanderDown, broken);

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

        private static IEnumerable<TriggerSpec> Timer(float seconds)
            => new[] { new TriggerSpec { Type = TriggerType.TimerElapsed, Seconds = seconds } };

        private static DirectiveSpec Hold() => new() { Type = DirectiveType.Hold };
        private static DirectiveSpec Charge() => new() { Type = DirectiveType.Charge };
    }
}
