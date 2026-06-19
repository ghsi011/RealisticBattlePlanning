using System.Linq;
using RealisticBattlePlanning.Planning.Editing;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>The KSP-style stage rail view (spec A2.6.4/A2.6.5), built engine-free.</summary>
    public class StageRailTests
    {
        private static PlanDraft TwoFormationDraft(out PlannedFormationClass a, out PlannedFormationClass b)
        {
            a = PlannedFormationClass.HorseArcher;
            b = PlannedFormationClass.LightCavalry;
            var forms = new[] { a, b };
            var draft = new PlanDraft();
            // row 0 shared (skirmish), row 1 diverges (flank Left vs Right)
            draft.AddStageToEach(forms, () => new Stage { Do = new DirectiveSpec { Type = DirectiveType.Skirmish, Target = "Nearest", StandoffMeters = 60f } });
            draft.AddStage(a, new Stage { When = { new TriggerSpec { Type = TriggerType.TimerElapsed, Seconds = 10f } }, Do = new DirectiveSpec { Type = DirectiveType.FlankArc, Side = FlankSide.Left, Target = "Nearest" } });
            draft.AddStage(b, new Stage { When = { new TriggerSpec { Type = TriggerType.TimerElapsed, Seconds = 10f } }, Do = new DirectiveSpec { Type = DirectiveType.FlankArc, Side = FlankSide.Right, Target = "Nearest" } });
            return draft;
        }

        [Fact]
        public void RailFlagsSharedRowsAndSummarizesStages()
        {
            var draft = TwoFormationDraft(out var a, out var b);

            var rows = StageRail.Build(draft, new[] { a, b });

            Assert.Equal(2, rows.Count);
            Assert.True(rows[0].SharedAcrossSelection);   // identical skirmish
            Assert.True(rows[0].PresentInAll);
            Assert.Contains("Skirmish", rows[0].Summary);
            Assert.False(rows[1].SharedAcrossSelection);  // Left vs Right
            Assert.Contains("Flank arc", rows[1].Summary);
        }

        [Fact]
        public void SingleSelectionRowsAreTriviallyShared()
        {
            var draft = TwoFormationDraft(out var a, out _);
            var rows = StageRail.Build(draft, new[] { a });
            Assert.All(rows, r => Assert.True(r.SharedAcrossSelection && r.PresentInAll));
        }

        [Fact]
        public void ReorderRowMovesTheStageInEverySelectedFormation()
        {
            var draft = TwoFormationDraft(out var a, out var b);
            var sel = new[] { a, b };

            // move row 0 (skirmish) to the end in both formations
            StageRail.ReorderRow(draft, sel, from: 0, to: 1);

            foreach (var f in sel)
            {
                var stages = draft.StagesOf(f);
                Assert.Equal(DirectiveType.FlankArc, stages[0].Do.Type);  // flank is now first
                Assert.Equal(DirectiveType.Skirmish, stages[1].Do.Type);  // skirmish moved to the end
            }
        }
    }
}
