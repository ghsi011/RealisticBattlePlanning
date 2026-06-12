using System.Linq;
using RealisticBattlePlanning.Harness;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    public class HarnessSerializerTests
    {
        [Fact]
        public void ScenarioSpecRoundTripsWithEveryAssertionType()
        {
            var spec = new ScenarioSpec
            {
                Name = "round-trip",
                Description = "every assertion type",
                PlanFile = "round-trip.plan.json",
                TimeLimitSeconds = 300f,
                Assertions =
                {
                    new ScenarioAssertion { Type = AssertionType.StageActivatedBetween, Formation = PlannedFormationClass.Infantry, Stage = 1, MinSeconds = 0f, MaxSeconds = 5f },
                    new ScenarioAssertion { Type = AssertionType.StageActivatedAfterPrevious, Formation = PlannedFormationClass.Infantry, Stage = 3, MinSeconds = 29f, MaxSeconds = 40f },
                    new ScenarioAssertion { Type = AssertionType.SignalEmittedBetween, Signal = "go", MinSeconds = 14f, MaxSeconds = 25f },
                    new ScenarioAssertion { Type = AssertionType.StageAfterSignal, Formation = PlannedFormationClass.Ranged, Stage = 2, Signal = "go", MaxDelaySeconds = 2f },
                    new ScenarioAssertion { Type = AssertionType.ReachesAnchor, Formation = PlannedFormationClass.Cavalry, Anchor = "goal", WithinMeters = 15f, BySeconds = 120f },
                },
            };

            var json = HarnessSerializer.Serialize(spec);
            Assert.True(HarnessSerializer.TryDeserializeScenario(json, out var back, out var error), error);

            Assert.Equal(spec.Name, back.Name);
            Assert.Equal(spec.TimeLimitSeconds, back.TimeLimitSeconds);
            Assert.Equal(spec.Assertions.Count, back.Assertions.Count);
            for (var i = 0; i < spec.Assertions.Count; i++)
            {
                Assert.Equal(spec.Assertions[i].Type, back.Assertions[i].Type);
                Assert.Equal(spec.Assertions[i].Describe(), back.Assertions[i].Describe());
            }
        }

        [Fact]
        public void ScenarioSpecRejectsUnknownProperties()
        {
            var json = "{ \"planFile\": \"x.json\", \"asserts\": [] }";

            Assert.False(HarnessSerializer.TryDeserializeScenario(json, out _, out var error));
            Assert.Contains("asserts", error);
        }

        [Fact]
        public void BattleRecordRoundTrips()
        {
            var record = new BattleRecord
            {
                Scenario = "walk",
                DurationSeconds = 120.5f,
                Result = "PlayerVictory",
                Anchors = { new AnchorPosition { Anchor = "wp3", Formation = PlannedFormationClass.Infantry, X = 1f, Y = 90f } },
                Events =
                {
                    new RecordedEvent { TimeSeconds = 0.25f, Formation = PlannedFormationClass.Infantry, Kind = RecordedEventKind.StageActivated, Stage = 1, Name = "walk the path" },
                    new RecordedEvent { TimeSeconds = 15f, Formation = PlannedFormationClass.Infantry, Kind = RecordedEventKind.SignalEmitted, Name = "go" },
                    new RecordedEvent { TimeSeconds = 20f, Formation = PlannedFormationClass.Infantry, Kind = RecordedEventKind.WaypointReached, Waypoint = 2 },
                },
                Samples = { new PositionSample { TimeSeconds = 2f, Formation = PlannedFormationClass.Infantry, X = 0f, Y = 4f } },
            };

            var json = HarnessSerializer.Serialize(record);
            Assert.True(HarnessSerializer.TryDeserializeRecord(json, out var back, out var error), error);

            Assert.Equal(record.Scenario, back.Scenario);
            Assert.Equal(record.Events.Count, back.Events.Count);
            Assert.Equal(RecordedEventKind.WaypointReached, back.Events[2].Kind);
            Assert.Equal(2, back.Events[2].Waypoint);
            Assert.Equal(record.Samples.Single().Y, back.Samples.Single().Y);
        }

        [Fact]
        public void ResultsAreReadLenientlySoOldBaselinesStillDiff()
        {
            // A baseline written by a future version with an extra field must
            // still load on this one.
            var json = "{ \"runAt\": \"x\", \"futureField\": 1, \"scenarios\": [ { \"scenario\": \"walk\", \"pass\": true } ] }";

            Assert.True(HarnessSerializer.TryDeserializeResults(json, out var results, out var error), error);
            Assert.True(results.Scenarios.Single().Pass);
        }
    }
}
