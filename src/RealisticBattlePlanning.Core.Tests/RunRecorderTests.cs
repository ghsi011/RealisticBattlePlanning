using System.Linq;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Harness;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// The recorder consumes exactly what the monitor saw and emitted; these
    /// tests drive both over scripted snapshots and assert on the record.
    /// </summary>
    public class RunRecorderTests
    {
        [Fact]
        public void NothingIsRecordedBeforeBattleStart()
        {
            var (monitor, recorder) = Setup(TestPlans.SimpleValid());

            Tick(monitor, recorder, Snapshot(0f, started: false));
            Tick(monitor, recorder, Snapshot(60f, started: false));

            Assert.False(recorder.Started);
            var record = recorder.Complete(null);
            Assert.Empty(record.Events);
            Assert.Empty(record.Samples);
            Assert.Empty(record.Anchors);
        }

        [Fact]
        public void TimesAreBattleRelativeAndStagesOneBased()
        {
            // Battle starts at mission time 7; the infantry timer stage (30s)
            // must be recorded as stage 2 at ~30s, not mission time ~37.
            var (monitor, recorder) = Setup(TestPlans.SimpleValid());

            Tick(monitor, recorder, Snapshot(7f, started: true));
            Tick(monitor, recorder, Snapshot(37.5f, started: true));

            var record = recorder.Complete("PlayerVictory");

            var first = record.Events.First(e =>
                e.Kind == RecordedEventKind.StageActivated && e.Formation == PlannedFormationClass.Infantry && e.Stage == 1);
            Assert.Equal(0f, first.TimeSeconds, 3);
            Assert.Equal("hold the line", first.Name);

            var second = record.Events.Single(e =>
                e.Kind == RecordedEventKind.StageActivated && e.Formation == PlannedFormationClass.Infantry && e.Stage == 2);
            Assert.Equal(30.5f, second.TimeSeconds, 3);

            Assert.Equal("PlayerVictory", record.Result);
        }

        [Fact]
        public void SignalsAndWaypointsAreRecorded()
        {
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
                            new Stage
                            {
                                Do = new DirectiveSpec
                                {
                                    Type = DirectiveType.MoveTo,
                                    Path = new System.Collections.Generic.List<string> { "wp1", "wp2" },
                                },
                                Emit = { "moving" },
                            },
                        },
                    },
                },
            };
            var (monitor, recorder) = Setup(plan);

            Tick(monitor, recorder, Snapshot(0f, started: true));
            Tick(monitor, recorder, Snapshot(10f, started: true, infantryY: 15f)); // within 10m of wp1

            var record = recorder.Complete(null);

            var signal = record.Events.Single(e => e.Kind == RecordedEventKind.SignalEmitted);
            Assert.Equal("moving", signal.Name);
            Assert.Equal(0f, signal.TimeSeconds, 3);

            var waypoint = record.Events.Single(e => e.Kind == RecordedEventKind.WaypointReached);
            Assert.Equal(2, waypoint.Waypoint);
            Assert.Equal(10f, waypoint.TimeSeconds, 3);
        }

        [Fact]
        public void AnchorsAreResolvedPerFormationAgainstBattleStartGeometry()
        {
            // OwnStart anchors resolve differently for infantry (0,0) and
            // ranged (15,0): forward 50 with attack direction north.
            var (monitor, recorder) = Setup(TestPlans.SimpleValid());

            Tick(monitor, recorder, Snapshot(0f, started: true));

            var record = recorder.Complete(null);

            var infantry = record.Anchors.Single(a => a.Formation == PlannedFormationClass.Infantry);
            Assert.Equal("advance-50", infantry.Anchor);
            Assert.Equal(0f, infantry.X, 3);
            Assert.Equal(50f, infantry.Y, 3);

            var ranged = record.Anchors.Single(a => a.Formation == PlannedFormationClass.Ranged);
            Assert.Equal(15f, ranged.X, 3);
            Assert.Equal(50f, ranged.Y, 3);
        }

        [Fact]
        public void PositionsAreSampledAtTheConfiguredInterval()
        {
            var monitor = new PlanMonitor(TestPlans.SimpleValid());
            var recorder = new RunRecorder("test", TestPlans.SimpleValid(), sampleIntervalSeconds: 2f);

            // 0.25s monitor cadence for 10s: samples at 0, 2, 4, 6, 8, 10.
            for (var time = 0f; time <= 10.01f; time += 0.25f)
                Tick(monitor, recorder, Snapshot(time, started: true));

            var record = recorder.Complete(null);
            var infantrySamples = record.Samples.Where(s => s.Formation == PlannedFormationClass.Infantry).ToList();
            Assert.Equal(6, infantrySamples.Count);
        }

        [Fact]
        public void FirstFaultWinsAndSurvivesComplete()
        {
            var (_, recorder) = Setup(TestPlans.SimpleValid());

            recorder.MarkFault("first");
            recorder.MarkFault("second");

            Assert.Equal("first", recorder.Complete("PlayerVictory").Fault);
        }

        [Fact]
        public void PlannedFormationWithoutUnitsIsRecordedAsMissing()
        {
            // SimpleValid plans Infantry + Ranged; field only Infantry.
            var (monitor, recorder) = Setup(TestPlans.SimpleValid());

            Tick(monitor, recorder, new FakeBattlefield(0f).WithOwn(PlannedFormationClass.Infantry, 0f, 0f));

            var record = recorder.Complete(null);
            Assert.Equal(new[] { PlannedFormationClass.Ranged }, record.MissingFormations);
        }

        [Fact]
        public void NoMissingFormationsWhenAllPlannedSlotsAreFielded()
        {
            var (monitor, recorder) = Setup(TestPlans.SimpleValid());

            Tick(monitor, recorder, Snapshot(0f, started: true));

            Assert.Empty(recorder.Complete(null).MissingFormations);
        }

        private static (PlanMonitor, RunRecorder) Setup(BattlePlan plan)
            => (new PlanMonitor(plan), new RunRecorder("test", plan));

        private static void Tick(PlanMonitor monitor, RunRecorder recorder, FakeBattlefield snapshot)
            => recorder.Tick(snapshot, monitor.Tick(snapshot));

        private static FakeBattlefield Snapshot(float time, bool started, float infantryY = 0f)
            => new FakeBattlefield(time, started)
                .WithOwn(PlannedFormationClass.Infantry, 0f, infantryY)
                .WithOwn(PlannedFormationClass.Ranged, 15f, 0f);
    }
}
