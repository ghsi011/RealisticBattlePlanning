using System.Linq;
using RealisticBattlePlanning.Planning.Editing;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// Multi-select rail coloring (spec A2.6.5): a row is "shared" only where the
    /// stage is identical across every selected formation.
    /// </summary>
    public class StageComparisonTests
    {
        [Fact]
        public void SharedRowsTrueOnlyWhereStagesMatchAcrossFormations()
        {
            var draft = new PlanDraft();
            var forms = new[] { PlannedFormationClass.HorseArcher, PlannedFormationClass.LightCavalry };

            // Row 0: identical skirmish (authored once across both, A3.6).
            draft.AddStageToEach(forms, () => new Stage
            {
                Name = "harass",  // cosmetic name differs in copies later — must not matter
                Do = new DirectiveSpec { Type = DirectiveType.Skirmish, Target = "Nearest", StandoffMeters = 60f },
            });
            // Row 1: diverges — flank Left vs Right.
            draft.AddStage(PlannedFormationClass.HorseArcher, new Stage
            {
                When = { new TriggerSpec { Type = TriggerType.TimerElapsed, Seconds = 10f } },
                Do = new DirectiveSpec { Type = DirectiveType.FlankArc, Side = FlankSide.Left, Target = "Nearest" },
            });
            draft.AddStage(PlannedFormationClass.LightCavalry, new Stage
            {
                When = { new TriggerSpec { Type = TriggerType.TimerElapsed, Seconds = 10f } },
                Do = new DirectiveSpec { Type = DirectiveType.FlankArc, Side = FlankSide.Right, Target = "Nearest" },
            });

            var shared = StageComparison.SharedRows(forms.Select(draft.StagesOf).ToList());

            Assert.Equal(2, shared.Count);
            Assert.True(shared[0]);   // identical skirmish
            Assert.False(shared[1]);  // Left vs Right
        }

        [Fact]
        public void AMissingStageMakesItsRowNotShared()
        {
            var draft = new PlanDraft();
            var a = PlannedFormationClass.Infantry;
            var b = PlannedFormationClass.Ranged;
            draft.AddStageToEach(new[] { a, b }, () => new Stage { Do = new DirectiveSpec { Type = DirectiveType.Hold } });
            draft.AddStage(a, new Stage { When = { new TriggerSpec { Type = TriggerType.TimerElapsed, Seconds = 5f } }, Do = new DirectiveSpec { Type = DirectiveType.Charge } });

            var shared = StageComparison.SharedRows(new[] { draft.StagesOf(a), draft.StagesOf(b) });

            Assert.Equal(2, shared.Count);
            Assert.True(shared[0]);   // both hold
            Assert.False(shared[1]);  // only A has a second stage
        }

        [Fact]
        public void NameDoesNotAffectEquivalence()
        {
            var x = new Stage { Name = "alpha", Do = new DirectiveSpec { Type = DirectiveType.Hold, Arrangement = Arrangement.ShieldWall } };
            var y = new Stage { Name = "beta", Do = new DirectiveSpec { Type = DirectiveType.Hold, Arrangement = Arrangement.ShieldWall } };
            Assert.True(StageComparison.AreEquivalent(x, y));

            y.Do.Arrangement = Arrangement.Loose;
            Assert.False(StageComparison.AreEquivalent(x, y));
        }
    }
}
