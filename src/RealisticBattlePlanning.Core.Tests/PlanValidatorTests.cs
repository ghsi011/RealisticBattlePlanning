using System.Collections.Generic;
using System.Linq;
using RealisticBattlePlanning.Planning;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    public class PlanValidatorTests
    {
        [Fact]
        public void ValidPlanHasNoErrorsOrWarnings()
        {
            var result = PlanValidator.Validate(TestPlans.SimpleValid());
            Assert.Empty(result.Errors);
            Assert.Empty(result.Warnings);
            Assert.True(result.IsValid);
        }

        [Fact]
        public void EveryEnumPlanIsValid()
        {
            var result = PlanValidator.Validate(TestPlans.EveryEnumValue());
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void EmptyPlanWarnsButIsValid()
        {
            var result = PlanValidator.Validate(new BattlePlan());
            Assert.True(result.IsValid);
            Assert.Contains(result.Warnings, w => w.Contains("no formations"));
        }

        [Fact]
        public void MoreThanThreeConditionsIsAnError()
        {
            var plan = TestPlans.SimpleValid();
            var when = plan.Formations[0].Stages[1].When;
            when.Add(new TriggerSpec { Type = TriggerType.BattleStart });
            when.Add(new TriggerSpec { Type = TriggerType.BattleStart });
            when.Add(new TriggerSpec { Type = TriggerType.BattleStart });

            var result = PlanValidator.Validate(plan);
            Assert.Contains(result.Errors, e => e.Contains("maximum is 3"));
        }

        [Fact]
        public void LaterStageWithoutTriggerIsAnError()
        {
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Stages[1].When.Clear();

            var result = PlanValidator.Validate(plan);
            Assert.Contains(result.Errors, e => e.Contains("no trigger"));
        }

        [Fact]
        public void FirstStageWithoutTriggerIsFine()
        {
            var result = PlanValidator.Validate(TestPlans.SimpleValid());
            Assert.DoesNotContain(result.Errors, e => e.Contains("no trigger"));
        }

        [Fact]
        public void MoreThanFourPlayerSignalsIsAnError()
        {
            var plan = TestPlans.SimpleValid();
            plan.PlayerSignals.AddRange(new[] { "a", "b", "c", "d" });

            var result = PlanValidator.Validate(plan);
            Assert.Contains(result.Errors, e => e.Contains("player signals"));
        }

        [Fact]
        public void DuplicateFormationIsAnError()
        {
            var plan = TestPlans.SimpleValid();
            plan.Formations.Add(new FormationPlan { Formation = PlannedFormationClass.Infantry });

            var result = PlanValidator.Validate(plan);
            Assert.Contains(result.Errors, e => e.Contains("more than one plan"));
        }

        [Fact]
        public void DuplicateAnchorIdIsAnError()
        {
            var plan = TestPlans.SimpleValid();
            plan.Anchors.Add(new MapAnchor { Id = "ADVANCE-50" });

            var result = PlanValidator.Validate(plan);
            Assert.Contains(result.Errors, e => e.Contains("Duplicate anchor"));
        }

        [Fact]
        public void UndefinedAnchorReferenceIsAnError()
        {
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Stages[1].Do.Anchor = "nowhere";

            var result = PlanValidator.Validate(plan);
            Assert.Contains(result.Errors, e => e.Contains("'nowhere' is not defined"));
        }

        [Fact]
        public void UndefinedPathWaypointIsAnError()
        {
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Stages[1].Do.Path = new List<string> { "advance-50", "ghost" };

            var result = PlanValidator.Validate(plan);
            Assert.Contains(result.Errors, e => e.Contains("'ghost' is not a defined anchor"));
        }

        [Fact]
        public void TimerWithoutSecondsIsAnError()
        {
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Stages[1].When[0].Seconds = null;

            var result = PlanValidator.Validate(plan);
            Assert.Contains(result.Errors, e => e.Contains("seconds > 0"));
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-10f)]
        [InlineData(150f)]
        public void CasualtiesPercentOutOfRangeIsAnError(float percent)
        {
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Stages[1].When[0] = new TriggerSpec { Type = TriggerType.CasualtiesAbove, Percent = percent };

            var result = PlanValidator.Validate(plan);
            Assert.Contains(result.Errors, e => e.Contains("(0, 100]"));
        }

        [Fact]
        public void DistanceTriggerWithoutMetersIsAnError()
        {
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Stages[1].When[0] = new TriggerSpec { Type = TriggerType.EnemyWithinDistance };

            var result = PlanValidator.Validate(plan);
            Assert.Contains(result.Errors, e => e.Contains("meters > 0"));
        }

        [Fact]
        public void ListenedButNeverEmittedSignalIsAWarningNotAnError()
        {
            var plan = TestPlans.SimpleValid();
            plan.Formations[1].Stages[1].When[0].Signal = "never-sent";

            var result = PlanValidator.Validate(plan);
            Assert.True(result.IsValid);
            Assert.Contains(result.Warnings, w => w.Contains("never-sent"));
        }

        [Fact]
        public void PlayerSignalCountsAsEmitted()
        {
            var plan = TestPlans.SimpleValid();
            plan.Formations[1].Stages[1].When[0] = new TriggerSpec { Type = TriggerType.PlayerSignal, Signal = "hammer" };

            var result = PlanValidator.Validate(plan);
            Assert.DoesNotContain(result.Warnings, w => w.Contains("hammer"));
        }

        [Fact]
        public void StageWithoutDirectiveIsAnError()
        {
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Stages[0].Do = null;

            var result = PlanValidator.Validate(plan);
            Assert.Contains(result.Errors, e => e.Contains("no directive"));
        }

        [Fact]
        public void MoveToWithoutDestinationIsAnError()
        {
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Stages[1].Do.Anchor = null;

            var result = PlanValidator.Validate(plan);
            Assert.Contains(result.Errors, e => e.Contains("destination"));
        }

        [Fact]
        public void FlankArcWithoutSideIsAnError()
        {
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Stages[0].Do = new DirectiveSpec { Type = DirectiveType.FlankArc };

            var result = PlanValidator.Validate(plan);
            Assert.Contains(result.Errors, e => e.Contains("side"));
        }

        [Fact]
        public void ScreenWithoutTargetIsAnError()
        {
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Stages[0].Do = new DirectiveSpec { Type = DirectiveType.Screen };

            var result = PlanValidator.Validate(plan);
            Assert.Contains(result.Errors, e => e.Contains("target formation"));
        }

        [Fact]
        public void FireControlWithoutModeIsAnError()
        {
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Stages[0].Do = new DirectiveSpec { Type = DirectiveType.FireControl };

            var result = PlanValidator.Validate(plan);
            Assert.Contains(result.Errors, e => e.Contains("Hold or Free"));
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(101f)]
        public void AbortCasualtiesOutOfRangeIsAnError(float percent)
        {
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Abort.CasualtiesAbovePercent = percent;

            var result = PlanValidator.Validate(plan);
            Assert.Contains(result.Errors, e => e.Contains("abort casualties"));
        }
    }
}
