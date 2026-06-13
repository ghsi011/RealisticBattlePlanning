using System.Linq;
using RealisticBattlePlanning.Harness;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// The diff is the harness regression gate: clean only when every current
    /// scenario passes and nothing from the baseline is missing.
    /// </summary>
    public class ResultsDiffTests
    {
        [Fact]
        public void IdenticalPassingRunsDiffClean()
        {
            var diff = ResultsDiff.Diff(Pack(Pass("walk")), Pack(Pass("walk")));

            Assert.True(diff.Clean);
            Assert.Empty(diff.Lines);
            Assert.StartsWith("DIFF CLEAN", diff.Summary());
        }

        [Fact]
        public void PassToFailIsNotCleanAndNamesTheFailedAssertion()
        {
            var failed = Fail("walk", "Infantry stage 2 activates between 10s and 30s", "activated at 45.2s");
            var diff = ResultsDiff.Diff(Pack(Pass("walk")), Pack(failed));

            Assert.False(diff.Clean);
            Assert.Contains(diff.Lines, l => l.Contains("PASS->FAIL walk"));
            Assert.Contains(diff.Lines, l => l.Contains("activated at 45.2s"));
        }

        [Fact]
        public void MissingScenarioIsNotClean()
        {
            var diff = ResultsDiff.Diff(Pack(Pass("walk"), Pass("signal")), Pack(Pass("walk")));

            Assert.False(diff.Clean);
            Assert.Contains(diff.Lines, l => l.StartsWith("MISSING signal"));
        }

        [Fact]
        public void NewPassingScenarioStaysCleanButIsNoted()
        {
            var diff = ResultsDiff.Diff(Pack(Pass("walk")), Pack(Pass("walk"), Pass("signal")));

            Assert.True(diff.Clean);
            Assert.Contains(diff.Lines, l => l.StartsWith("NEW signal"));
        }

        [Fact]
        public void FixedScenarioIsNoted()
        {
            var diff = ResultsDiff.Diff(
                Pack(Fail("walk", "Infantry stage 2 activates between 10s and 30s", "never activated")),
                Pack(Pass("walk")));

            Assert.True(diff.Clean);
            Assert.Contains(diff.Lines, l => l.StartsWith("FIXED walk"));
        }

        [Fact]
        public void LargeMeasuredDriftIsReportedSmallDriftIsNot()
        {
            var baseline = Pack(Pass("walk", measured: 20f));
            var drifted = Pack(Pass("walk", measured: 29f));   // +45%, > 2s floor
            var steady = Pack(Pass("walk", measured: 21f));    // +5%

            var driftDiff = ResultsDiff.Diff(baseline, drifted);
            Assert.True(driftDiff.Clean);
            Assert.Contains(driftDiff.Lines, l => l.StartsWith("drift walk") && l.Contains("29") && l.Contains("was 20"));

            Assert.Empty(ResultsDiff.Diff(baseline, steady).Lines);
        }

        [Fact]
        public void DuplicateScenarioNamesDegradeGracefullyInsteadOfThrowing()
        {
            // A malformed results file must produce an odd diff, never crash
            // the console command. Last entry wins.
            var diff = ResultsDiff.Diff(Pack(Pass("walk"), Pass("walk")), Pack(Pass("walk")));

            Assert.True(diff.Clean);
        }

        // ---- builders ----

        private static PackResult Pack(params ScenarioResult[] scenarios)
        {
            var pack = new PackResult { RunAt = "test" };
            pack.Scenarios.AddRange(scenarios);
            return pack;
        }

        private static ScenarioResult Pass(string name, float measured = 20f) => new()
        {
            Scenario = name,
            Pass = true,
            Assertions =
            {
                new AssertionResult
                {
                    Description = "Infantry stage 2 activates between 10s and 30s",
                    Pass = true,
                    Measured = measured,
                    Message = $"activated at {measured:0.#}s",
                },
            },
        };

        private static ScenarioResult Fail(string name, string description, string message) => new()
        {
            Scenario = name,
            Pass = false,
            Assertions =
            {
                new AssertionResult { Description = description, Pass = false, Message = message },
            },
        };
    }
}
