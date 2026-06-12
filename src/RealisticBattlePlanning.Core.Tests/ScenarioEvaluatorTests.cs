using System.Collections.Generic;
using System.Linq;
using RealisticBattlePlanning.Harness;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// Tolerance-assertion checks against hand-built records: every assertion
    /// type passes inside its band, fails outside it, and fails readably when
    /// the expected event never happened.
    /// </summary>
    public class ScenarioEvaluatorTests
    {
        [Fact]
        public void StageActivatedBetweenPassesInsideAndFailsOutsideTheBand()
        {
            var record = Record(StageEvent(PlannedFormationClass.Infantry, stage: 2, time: 25f));

            Assert.True(Evaluate(record, StageBetween(2, 10f, 30f)).Pass);

            var late = Evaluate(record, StageBetween(2, 10f, 20f));
            Assert.False(late.Pass);
            Assert.Equal(25f, late.Assertions[0].Measured);
            Assert.Contains("activated at 25s", late.Assertions[0].Message);
        }

        [Fact]
        public void MissingStageFailsWithAReadableMessage()
        {
            var result = Evaluate(Record(), StageBetween(2, 10f, 30f));

            Assert.False(result.Pass);
            Assert.Contains("never activated", result.Assertions[0].Message);
        }

        [Fact]
        public void StageActivatedAfterPreviousMeasuresTheDelay()
        {
            var record = Record(
                StageEvent(PlannedFormationClass.Infantry, stage: 1, time: 40f),
                StageEvent(PlannedFormationClass.Infantry, stage: 2, time: 72f));

            var assertion = new ScenarioAssertion
            {
                Type = AssertionType.StageActivatedAfterPrevious,
                Formation = PlannedFormationClass.Infantry,
                Stage = 2,
                MinSeconds = 30f,
                MaxSeconds = 35f,
            };

            var result = Evaluate(record, assertion);
            Assert.True(result.Pass);
            Assert.Equal(32f, result.Assertions[0].Measured);

            assertion.MaxSeconds = 31f;
            Assert.False(Evaluate(record, assertion).Pass);
        }

        [Fact]
        public void SignalEmittedBetweenUsesTheFirstEmission()
        {
            var record = Record(
                SignalEvent("go", 18f),
                SignalEvent("go", 90f));

            var result = Evaluate(record, new ScenarioAssertion
            {
                Type = AssertionType.SignalEmittedBetween,
                Signal = "go",
                MinSeconds = 15f,
                MaxSeconds = 25f,
            });

            Assert.True(result.Pass);
            Assert.Equal(18f, result.Assertions[0].Measured);
        }

        [Fact]
        public void StageAfterSignalFailsWhenTheStagePrecededTheSignal()
        {
            var record = Record(
                StageEvent(PlannedFormationClass.Ranged, stage: 2, time: 10f),
                SignalEvent("go", 15f));

            var result = Evaluate(record, new ScenarioAssertion
            {
                Type = AssertionType.StageAfterSignal,
                Formation = PlannedFormationClass.Ranged,
                Stage = 2,
                Signal = "go",
                MaxDelaySeconds = 5f,
            });

            Assert.False(result.Pass);
        }

        [Fact]
        public void StageAfterSignalPassesWithinTheLatencyBudget()
        {
            var record = Record(
                SignalEvent("go", 15f),
                StageEvent(PlannedFormationClass.Ranged, stage: 2, time: 15.5f));

            var result = Evaluate(record, new ScenarioAssertion
            {
                Type = AssertionType.StageAfterSignal,
                Formation = PlannedFormationClass.Ranged,
                Stage = 2,
                Signal = "go",
                MaxDelaySeconds = 2f,
            });

            Assert.True(result.Pass);
            Assert.Equal(0.5f, result.Assertions[0].Measured.Value, 3);
        }

        [Fact]
        public void ReachesAnchorUsesClosestSampleWithinTheDeadline()
        {
            var record = Record();
            record.Anchors.Add(new AnchorPosition { Anchor = "goal", Formation = PlannedFormationClass.Infantry, X = 0f, Y = 90f });
            record.Samples.Add(Sample(10f, 0f, 40f));  // 50m away
            record.Samples.Add(Sample(50f, 0f, 82f));  // 8m away
            record.Samples.Add(Sample(100f, 0f, 90f)); // 0m, but after the deadline

            var assertion = new ScenarioAssertion
            {
                Type = AssertionType.ReachesAnchor,
                Formation = PlannedFormationClass.Infantry,
                Anchor = "goal",
                WithinMeters = 10f,
                BySeconds = 60f,
            };

            var result = Evaluate(record, assertion);
            Assert.True(result.Pass);
            Assert.Equal(8f, result.Assertions[0].Measured.Value, 3);

            assertion.WithinMeters = 5f;
            var tooFar = Evaluate(record, assertion);
            Assert.False(tooFar.Pass);
            Assert.Contains("closest approach 8m", tooFar.Assertions[0].Message);
        }

        [Fact]
        public void ReachesAnchorFailsWhenTheAnchorWasNeverResolved()
        {
            var result = Evaluate(Record(), new ScenarioAssertion
            {
                Type = AssertionType.ReachesAnchor,
                Formation = PlannedFormationClass.Infantry,
                Anchor = "missing",
                WithinMeters = 10f,
            });

            Assert.False(result.Pass);
            Assert.Contains("not resolved", result.Assertions[0].Message);
        }

        [Fact]
        public void AFaultedRunFailsEvenWhenEveryAssertionHolds()
        {
            // All asserted events happened before the fault — without the
            // fault check this would be a bogus PASS (R2).
            var record = Record(StageEvent(PlannedFormationClass.Infantry, stage: 2, time: 25f));
            record.Fault = "plan monitor tick failed mid-battle: boom";

            var result = Evaluate(record, StageBetween(2, 10f, 30f));

            Assert.False(result.Pass);
            var fault = result.Assertions.Single(a => !a.Pass);
            Assert.Contains("free of plan-logic faults", fault.Description);
            Assert.Contains("boom", fault.Message);
            Assert.True(result.Assertions.Single(a => a.Description.Contains("stage 2")).Pass);
        }

        [Fact]
        public void MessagesUseInvariantDecimalSeparatorRegardlessOfCulture()
        {
            var previous = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

                var record = Record(StageEvent(PlannedFormationClass.Infantry, stage: 2, time: 25.5f));
                var result = Evaluate(record, StageBetween(2, 10.5f, 30f));

                Assert.Contains("activated at 25.5s", result.Assertions[0].Message);
                Assert.Contains("between 10.5s", result.Assertions[0].Description);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void TimeLimitFailsARunThatOutlastsIt()
        {
            var record = Record();
            record.DurationSeconds = 300f;

            var spec = new ScenarioSpec { Name = "t", TimeLimitSeconds = 200f };
            var result = ScenarioEvaluator.Evaluate(spec, record);

            Assert.False(result.Pass);
            Assert.Contains("battle ran 300s", result.Assertions.Single().Message);
        }

        // ---- builders ----

        private static ScenarioResult Evaluate(BattleRecord record, ScenarioAssertion assertion)
            => ScenarioEvaluator.Evaluate(new ScenarioSpec { Name = "t", Assertions = { assertion } }, record);

        private static ScenarioAssertion StageBetween(int stage, float min, float max) => new()
        {
            Type = AssertionType.StageActivatedBetween,
            Formation = PlannedFormationClass.Infantry,
            Stage = stage,
            MinSeconds = min,
            MaxSeconds = max,
        };

        private static BattleRecord Record(params RecordedEvent[] events)
        {
            var record = new BattleRecord { Scenario = "t", DurationSeconds = 120f };
            record.Events.AddRange(events);
            return record;
        }

        private static RecordedEvent StageEvent(PlannedFormationClass formation, int stage, float time) => new()
        {
            TimeSeconds = time,
            Formation = formation,
            Kind = RecordedEventKind.StageActivated,
            Stage = stage,
        };

        private static RecordedEvent SignalEvent(string signal, float time) => new()
        {
            TimeSeconds = time,
            Formation = PlannedFormationClass.Infantry,
            Kind = RecordedEventKind.SignalEmitted,
            Name = signal,
        };

        private static PositionSample Sample(float time, float x, float y) => new()
        {
            TimeSeconds = time,
            Formation = PlannedFormationClass.Infantry,
            X = x,
            Y = y,
        };
    }
}
