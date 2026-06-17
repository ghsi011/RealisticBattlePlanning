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
        public void EnemyWithinDistanceAcceptsADefinedAnchorReference()
        {
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Stages[1].When[0] = new TriggerSpec
            {
                Type = TriggerType.EnemyWithinDistance,
                Meters = 40f,
                Anchor = "advance-50",
            };

            Assert.True(PlanValidator.Validate(plan).IsValid);
        }

        [Fact]
        public void EnemyWithinDistanceWithAnUndefinedAnchorIsAnError()
        {
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Stages[1].When[0] = new TriggerSpec
            {
                Type = TriggerType.EnemyWithinDistance,
                Meters = 40f,
                Anchor = "no-such-anchor",
            };

            var result = PlanValidator.Validate(plan);
            Assert.Contains(result.Errors, e => e.Contains("no-such-anchor"));
        }

        [Fact]
        public void TypoedEnemySelectorIsAnError()
        {
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Stages[1].When[0] = new TriggerSpec
            {
                Type = TriggerType.EnemyWithinDistance,
                Meters = 40f,
                Formation = "Nearset", // would silently mean "any enemy"
            };

            Assert.Contains(PlanValidator.Validate(plan).Errors, e => e.Contains("Nearset"));
        }

        [Fact]
        public void NumericFormationSelectorIsRejected()
        {
            // Enum.TryParse would accept "3" (HorseArcher) and even
            // out-of-range "99"; only class names are valid selectors.
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Stages[1].When[0] = new TriggerSpec
            {
                Type = TriggerType.EnemyWithinDistance,
                Meters = 40f,
                Formation = "3",
            };

            Assert.Contains(PlanValidator.Validate(plan).Errors, e => e.Contains("'3'"));
        }

        [Fact]
        public void DisablingTheCommanderAbortWarnsThatItHasNoEffectYet()
        {
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Abort.OnCommanderIncapacitated = false;

            var result = PlanValidator.Validate(plan);
            Assert.True(result.IsValid);
            Assert.Contains(result.Warnings, w => w.Contains("commander death always aborts"));
        }

        [Fact]
        public void BlankEmittedSignalIsAnError()
        {
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Stages[1].Emit.Add("  ");

            Assert.Contains(PlanValidator.Validate(plan).Errors, e => e.Contains("blank signal"));
        }

        [Fact]
        public void FriendlyWithinDistanceWithoutAFormationIsAnError()
        {
            // No formation means the trigger could never fire (no sensible
            // self-distance default exists).
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Stages[1].When[0] = new TriggerSpec
            {
                Type = TriggerType.FriendlyWithinDistance,
                Meters = 25f,
            };

            Assert.Contains(PlanValidator.Validate(plan).Errors, e => e.Contains("FriendlyWithinDistance needs a formation"));
        }

        [Fact]
        public void NonPositiveDistancesAreErrors()
        {
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Stages[1].Do = new DirectiveSpec
            {
                Type = DirectiveType.Skirmish,
                StandoffMeters = -5f,
            };
            plan.Formations[1].Stages[1].When[0] = new TriggerSpec
            {
                Type = TriggerType.EnemyCommits,
                Meters = -1f,
                SpeedThreshold = 0f,
            };

            var result = PlanValidator.Validate(plan);
            Assert.Contains(result.Errors, e => e.Contains("standoffMeters"));
            Assert.Contains(result.Errors, e => e.Contains("engagement range"));
            Assert.Contains(result.Errors, e => e.Contains("speedThreshold"));
        }

        [Fact]
        public void MoveToWithBothAnchorAndPathWarnsThatThePathWins()
        {
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Stages[1].Do = new DirectiveSpec
            {
                Type = DirectiveType.MoveTo,
                Anchor = "advance-50",
                Path = new List<string> { "advance-50" },
            };

            var result = PlanValidator.Validate(plan);
            Assert.True(result.IsValid);
            Assert.Contains(result.Warnings, w => w.Contains("the path wins"));
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
        public void EmittedSignalNobodyReactsToIsAWarning()
        {
            // The inverse of the listen-side warning: a signal broadcast that no
            // stage gates on is dead coordination glue (A3.8, non-blocking).
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Stages[0].Emit.Add("orphan");

            var result = PlanValidator.Validate(plan);
            Assert.True(result.IsValid);
            Assert.Contains(result.Warnings, w => w.Contains("orphan") && w.Contains("no stage reacts"));
        }

        [Fact]
        public void StandoffBeyondWeaponRangeIsAWarning()
        {
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Stages[0].Do = new DirectiveSpec
            {
                Type = DirectiveType.Skirmish,
                Target = "Nearest",
                StandoffMeters = 300f,
            };

            var result = PlanValidator.Validate(plan);
            Assert.True(result.IsValid); // contradictory but executable -> warning, not error
            Assert.Contains(result.Warnings, w => w.Contains("standoffMeters") && w.Contains("never engage"));
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

        [Fact]
        public void DeclaredButUnusedAnchorIsAWarning()
        {
            // A stranded anchor (rename/delete leftover) is dead glue: warn, never block.
            var plan = TestPlans.SimpleValid();
            plan.Anchors.Add(new MapAnchor { Id = "stranded", Basis = AnchorBasis.OwnStart, Forward = 99f });

            var result = PlanValidator.Validate(plan);
            Assert.True(result.IsValid);
            Assert.Contains(result.Warnings, w => w.Contains("stranded") && w.Contains("never used"));
        }

        [Fact]
        public void EveryDeclaredAnchorBeingReferencedRaisesNoUnusedWarning()
        {
            // The shipped sample references its one anchor from both formations.
            var result = PlanValidator.Validate(TestPlans.SimpleValid());
            Assert.DoesNotContain(result.Warnings, w => w.Contains("never used"));
        }

        [Fact]
        public void RepeatedConditionOfTheSameKindIsAWarning()
        {
            // Two timers on one stage: ANDed, so only the longer has any effect.
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Stages[1].When.Add(new TriggerSpec { Type = TriggerType.TimerElapsed, Seconds = 60f });

            var result = PlanValidator.Validate(plan);
            Assert.True(result.IsValid);
            Assert.Contains(result.Warnings, w => w.Contains("repeats a TimerElapsed"));
        }

        [Fact]
        public void TwoDistanceConditionsWithDifferentSelectorsAreNotRedundant()
        {
            // "enemy infantry within 50 m AND enemy cavalry within 80 m" is a real
            // conjunction, not a duplicate — it must not be flagged.
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Stages[1].When[0] = new TriggerSpec { Type = TriggerType.EnemyWithinDistance, Formation = "Infantry", Meters = 50f };
            plan.Formations[0].Stages[1].When.Add(new TriggerSpec { Type = TriggerType.EnemyWithinDistance, Formation = "Cavalry", Meters = 80f });

            var result = PlanValidator.Validate(plan);
            Assert.DoesNotContain(result.Warnings, w => w.Contains("repeats"));
        }

        [Fact]
        public void CasualtyTriggerAtOrPastTheAbortThresholdIsAWarning()
        {
            // Abort fires at 50%; a stage gated on casualties >= 60% can never be reached.
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Abort.CasualtiesAbovePercent = 50f;
            plan.Formations[0].Stages[1].When[0] = new TriggerSpec { Type = TriggerType.CasualtiesAbove, Percent = 60f };

            var result = PlanValidator.Validate(plan);
            Assert.True(result.IsValid);
            Assert.Contains(result.Warnings, w => w.Contains("may never be reached"));
        }

        [Fact]
        public void CasualtyTriggerBelowTheAbortThresholdIsFine()
        {
            var plan = TestPlans.SimpleValid();
            plan.Formations[0].Abort.CasualtiesAbovePercent = 50f;
            plan.Formations[0].Stages[1].When[0] = new TriggerSpec { Type = TriggerType.CasualtiesAbove, Percent = 20f };

            var result = PlanValidator.Validate(plan);
            Assert.DoesNotContain(result.Warnings, w => w.Contains("may never be reached"));
        }
    }
}
