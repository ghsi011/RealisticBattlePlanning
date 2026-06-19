using System.Linq;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Planning.Editing;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// Map-first authoring logic (spec A2.6): a click resolves to a world point and
    /// appends a march stage. Proven here so the Gauntlet map stays a thin front-end.
    /// </summary>
    public class MapAuthoringTests
    {
        [Fact]
        public void ClickToMarchBuildsAWaypointChainWithDefaultTriggers()
        {
            var draft = new PlanDraft();

            var first = MapAuthoring.AppendMarchStage(draft, PlannedFormationClass.Cavalry, new MapVec(50f, 60f), "wp1");
            var second = MapAuthoring.AppendMarchStage(draft, PlannedFormationClass.Cavalry, new MapVec(80f, 100f), "wp2");

            Assert.Equal("wp1", first);
            Assert.Equal("wp2", second);

            var stages = draft.StagesOf(PlannedFormationClass.Cavalry);
            Assert.Equal(2, stages.Count);

            // First click: march to wp1, gated on battle start (the formation's first stage).
            Assert.Equal(DirectiveType.MoveTo, stages[0].Do.Type);
            Assert.Equal("wp1", stages[0].Do.Anchor);
            Assert.Equal(TriggerType.BattleStart, Assert.Single(stages[0].When).Type);

            // Second click: march to wp2, gated on reaching the previous waypoint.
            Assert.Equal("wp2", stages[1].Do.Anchor);
            var trigger = Assert.Single(stages[1].When);
            Assert.Equal(TriggerType.PositionReached, trigger.Type);
            Assert.Equal("wp1", trigger.Anchor);

            // Scene anchors were placed at the clicked world points, and the result is valid.
            Assert.Contains(draft.Anchors, a => a.Id == "wp1" && a.Basis == AnchorBasis.Scene && a.X == 50f && a.Y == 60f);
            Assert.Empty(draft.Validate().Errors);
        }

        [Fact]
        public void AppendMarchStageNoOpsOnNullDraftOrBlankId()
        {
            Assert.Null(MapAuthoring.AppendMarchStage(null, PlannedFormationClass.Infantry, new MapVec(0f, 0f), "x"));
            var draft = new PlanDraft();
            Assert.Null(MapAuthoring.AppendMarchStage(draft, PlannedFormationClass.Infantry, new MapVec(0f, 0f), " "));
            Assert.Empty(draft.StagesOf(PlannedFormationClass.Infantry));
        }

        [Fact]
        public void LinePositionsSpreadEvenlyEndpointsInclusive()
        {
            var p = MapAuthoring.LinePositions(new MapVec(0f, 0f), new MapVec(60f, 0f), 3);
            Assert.Equal(3, p.Count);
            Assert.Equal(0f, p[0].X, 3);
            Assert.Equal(30f, p[1].X, 3);
            Assert.Equal(60f, p[2].X, 3);

            var one = MapAuthoring.LinePositions(new MapVec(0f, 0f), new MapVec(60f, 0f), 1);
            Assert.Equal(30f, Assert.Single(one).X, 3);  // single formation -> midpoint
            Assert.Empty(MapAuthoring.LinePositions(new MapVec(0f, 0f), new MapVec(1f, 0f), 0));
        }

        [Fact]
        public void DragToLineArraysFormationsAndGivesEachAMarchStage()
        {
            var draft = new PlanDraft();
            var forms = new[] { PlannedFormationClass.Infantry, PlannedFormationClass.Ranged, PlannedFormationClass.Cavalry };

            var ids = MapAuthoring.AppendLineFormation(draft, forms, new MapVec(0f, 0f), new MapVec(60f, 0f), f => $"line-{f}");

            Assert.Equal(new[] { "line-Infantry", "line-Ranged", "line-Cavalry" }, ids);
            // each got a move stage to its evenly-spaced spot, gated on battle start (first stage = move together)
            foreach (var (f, x) in new[] { (PlannedFormationClass.Infantry, 0f), (PlannedFormationClass.Ranged, 30f), (PlannedFormationClass.Cavalry, 60f) })
            {
                var stage = Assert.Single(draft.StagesOf(f));
                Assert.Equal(DirectiveType.MoveTo, stage.Do.Type);
                Assert.Equal(TriggerType.BattleStart, Assert.Single(stage.When).Type);
                Assert.Equal(x, draft.Anchors.Single(a => a.Id == $"line-{f}").X, 3);
            }
            Assert.Empty(draft.Validate().Errors);
        }
    }
}
