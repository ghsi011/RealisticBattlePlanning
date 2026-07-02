using System.Linq;
using RealisticBattlePlanning.Planning;
using RealisticBattlePlanning.Planning.Editing;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    public class PlanRemapperTests
    {
        [Fact]
        public void KeepsPresentClassesAndReportsDroppedOnes()
        {
            var previous = new PlanDraft()
                .AddFormation(PlannedFormationClass.Infantry)
                .AddFormation(PlannedFormationClass.Ranged)
                .AddFormation(PlannedFormationClass.Cavalry)
                .DeclarePlayerSignal("charge")
                .AddAnchor(new MapAnchor { Id = "rally", Forward = -30f })
                .Build();

            // This battle fields Infantry + Ranged + HorseArcher (no Cavalry).
            var result = PlanRemapper.RemapToFormations(previous,
                new[] { PlannedFormationClass.Infantry, PlannedFormationClass.Ranged, PlannedFormationClass.HorseArcher });

            Assert.Equal(new[] { PlannedFormationClass.Infantry, PlannedFormationClass.Ranged },
                result.Plan.Formations.Select(f => f.Formation).OrderBy(c => (int)c));
            Assert.Equal(new[] { PlannedFormationClass.Cavalry }, result.Dropped);   // flagged for review
            Assert.True(result.HasDrops);
            // Shared anchors + player signals carry across.
            Assert.Contains(result.Plan.Anchors, a => a.Id == "rally");
            Assert.Contains("charge", result.Plan.PlayerSignals);
        }

        [Fact]
        public void RemappedPlanIsADeepCopy()
        {
            var previous = new PlanDraft().AddFormation(PlannedFormationClass.Infantry).Build();
            var result = PlanRemapper.RemapToFormations(previous, new[] { PlannedFormationClass.Infantry });

            result.Plan.Formations[0].Stages.Clear();           // mutate the remapped copy
            Assert.NotEmpty(previous.Formations[0].Stages);     // the source is untouched
        }

        [Fact]
        public void NullPreviousYieldsAnEmptyPlan()
        {
            var result = PlanRemapper.RemapToFormations(null, new[] { PlannedFormationClass.Infantry });
            Assert.Empty(result.Plan.Formations);
            Assert.False(result.HasDrops);
        }

        // ---- carry hygiene (StripSceneAnchors, A3.9) ----

        [Fact]
        public void StripDropsSceneAnchorsAndTheStagesWiredToThem()
        {
            var previous = new BattlePlan
            {
                Anchors =
                {
                    new MapAnchor { Id = "wp1", Basis = AnchorBasis.Scene, Forward = 606f, Right = 839f },
                    new MapAnchor { Id = "rally", Basis = AnchorBasis.TeamCenter, Forward = -30f },
                },
                Formations =
                {
                    new FormationPlan
                    {
                        Formation = PlannedFormationClass.Infantry,
                        Stages =
                        {
                            new Stage { Do = new DirectiveSpec { Type = DirectiveType.Hold } },
                            new Stage { Do = new DirectiveSpec { Type = DirectiveType.MoveTo, Path = new System.Collections.Generic.List<string> { "wp1" } } },
                            new Stage
                            {
                                When = { new TriggerSpec { Type = TriggerType.PositionReached, Anchor = "wp1" } },
                                Do = new DirectiveSpec { Type = DirectiveType.Charge },
                            },
                            new Stage { Do = new DirectiveSpec { Type = DirectiveType.MoveTo, Anchor = "rally" } },
                        },
                    },
                },
            };

            var result = PlanRemapper.StripSceneAnchors(previous);

            Assert.True(result.Changed);
            Assert.Equal(new[] { "wp1" }, result.RemovedAnchors);
            Assert.Equal(2, result.RemovedStages); // the wp1 march and the wp1-triggered charge
            var formation = Assert.Single(result.Plan.Formations);
            Assert.Equal(2, formation.Stages.Count); // Hold + the relative rally march survive
            Assert.DoesNotContain(result.Plan.Anchors, a => a.Id == "wp1");
            Assert.Contains(result.Plan.Anchors, a => a.Id == "rally");
            // The source plan is untouched (fresh copy).
            Assert.Equal(4, previous.Formations[0].Stages.Count);
        }

        [Fact]
        public void StripRemovesAFormationWhoseWholePlanWasMapSpecific()
        {
            var previous = new BattlePlan
            {
                Anchors = { new MapAnchor { Id = "fw1", Basis = AnchorBasis.Scene } },
                Formations =
                {
                    new FormationPlan
                    {
                        Formation = PlannedFormationClass.Cavalry,
                        Stages = { new Stage { Do = new DirectiveSpec { Type = DirectiveType.MoveTo, Anchor = "fw1" } } },
                    },
                    new FormationPlan
                    {
                        Formation = PlannedFormationClass.Infantry,
                        Stages = { new Stage { Do = new DirectiveSpec { Type = DirectiveType.Hold } } },
                    },
                },
            };

            var result = PlanRemapper.StripSceneAnchors(previous);

            Assert.Equal(new[] { PlannedFormationClass.Cavalry }, result.EmptiedFormations);
            var formation = Assert.Single(result.Plan.Formations);
            Assert.Equal(PlannedFormationClass.Infantry, formation.Formation);
        }

        [Fact]
        public void StripLeavesARelativeOnlyPlanUntouched()
        {
            var previous = new PlanDraft()
                .AddFormation(PlannedFormationClass.Infantry)
                .AddAnchor(new MapAnchor { Id = "rally", Basis = AnchorBasis.OwnStart, Forward = 40f })
                .Build();

            var result = PlanRemapper.StripSceneAnchors(previous);

            Assert.False(result.Changed);
            Assert.Equal(0, result.RemovedStages);
            Assert.Single(result.Plan.Formations);
            Assert.Contains(result.Plan.Anchors, a => a.Id == "rally");
        }
    }
}
