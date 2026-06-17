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
    }
}
