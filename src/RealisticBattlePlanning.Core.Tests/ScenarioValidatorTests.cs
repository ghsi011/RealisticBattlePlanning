using System.Linq;
using RealisticBattlePlanning.Harness;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// A scenario with a missing required parameter must fail at arm time
    /// with the parameter named — not after a full battle as a misleading
    /// always-false assertion (lifted float? comparisons).
    /// </summary>
    public class ScenarioValidatorTests
    {
        [Fact]
        public void AValidSpecHasNoErrors()
        {
            Assert.Empty(ScenarioValidator.Validate(ValidSpec()));
        }

        [Fact]
        public void MissingPlanFileAndAssertionsAreErrors()
        {
            var errors = ScenarioValidator.Validate(new ScenarioSpec { Name = "t" });

            Assert.Contains(errors, e => e.Contains("planFile"));
            Assert.Contains(errors, e => e.Contains("no assertions"));
        }

        [Fact]
        public void MissingBandBoundIsAnErrorNamingTheParameter()
        {
            var spec = ValidSpec();
            spec.Assertions[0].MaxSeconds = null;

            var error = Assert.Single(ScenarioValidator.Validate(spec));
            Assert.Contains("Assertion 1", error);
            Assert.Contains("maxSeconds", error);
        }

        [Fact]
        public void InvertedBandIsAnError()
        {
            var spec = ValidSpec();
            spec.Assertions[0].MinSeconds = 30f;
            spec.Assertions[0].MaxSeconds = 10f;

            Assert.Contains(ScenarioValidator.Validate(spec), e => e.Contains("exceeds"));
        }

        [Fact]
        public void StageActivatedAfterPreviousNeedsAPreviousStage()
        {
            var spec = Spec(new ScenarioAssertion
            {
                Type = AssertionType.StageActivatedAfterPrevious,
                Formation = PlannedFormationClass.Infantry,
                Stage = 1,
                MinSeconds = 10f,
                MaxSeconds = 20f,
            });

            Assert.Contains(ScenarioValidator.Validate(spec), e => e.Contains(">= 2"));
        }

        [Fact]
        public void StageAfterSignalRequiresSignalAndDelay()
        {
            var spec = Spec(new ScenarioAssertion
            {
                Type = AssertionType.StageAfterSignal,
                Formation = PlannedFormationClass.Ranged,
                Stage = 2,
            });

            var errors = ScenarioValidator.Validate(spec);
            Assert.Contains(errors, e => e.Contains("'signal'"));
            Assert.Contains(errors, e => e.Contains("maxDelaySeconds"));
        }

        [Fact]
        public void ReachesAnchorRequiresAnchorAndDistance()
        {
            var spec = Spec(new ScenarioAssertion
            {
                Type = AssertionType.ReachesAnchor,
                Formation = PlannedFormationClass.Cavalry,
                BySeconds = -1f,
            });

            var errors = ScenarioValidator.Validate(spec);
            Assert.Contains(errors, e => e.Contains("'anchor'"));
            Assert.Contains(errors, e => e.Contains("withinMeters"));
            Assert.Contains(errors, e => e.Contains("bySeconds"));
        }

        [Fact]
        public void MissingFormationIsAnError()
        {
            var spec = ValidSpec();
            spec.Assertions[0].Formation = null;

            Assert.Contains(ScenarioValidator.Validate(spec), e => e.Contains("'formation'"));
        }

        private static ScenarioSpec ValidSpec() => Spec(new ScenarioAssertion
        {
            Type = AssertionType.StageActivatedBetween,
            Formation = PlannedFormationClass.Infantry,
            Stage = 1,
            MinSeconds = 0f,
            MaxSeconds = 5f,
        });

        private static ScenarioSpec Spec(params ScenarioAssertion[] assertions)
        {
            var spec = new ScenarioSpec { Name = "t", PlanFile = "t.plan.json" };
            spec.Assertions.AddRange(assertions);
            return spec;
        }
    }
}
