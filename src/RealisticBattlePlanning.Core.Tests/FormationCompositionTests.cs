using RealisticBattlePlanning.Planning;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// The composition labeller turns a numbered formation's troop mix into a
    /// label (spec: plan by formations 1-8, each self-describing). Cases mirror
    /// the rules the user gave directly.
    /// </summary>
    public class FormationCompositionTests
    {
        [Fact]
        public void OneTypeOverSeventyPercentNamesTheFormation()
        {
            Assert.Equal("Infantry", FormationComposition.Label(infantry: 80, ranged: 20, cavalry: 0, horseArcher: 0));
            Assert.Equal("Infantry", FormationComposition.Label(infantry: 100, ranged: 0, cavalry: 0, horseArcher: 0));
            Assert.Equal("Ranged", FormationComposition.Label(infantry: 10, ranged: 90, cavalry: 0, horseArcher: 0));
        }

        [Fact]
        public void TwoTypesAreListedDominantFirst()
        {
            // 60% ranged + 40% infantry -> "Ranged-Infantry" (the user's example).
            Assert.Equal("Ranged-Infantry", FormationComposition.Label(infantry: 40, ranged: 60, cavalry: 0, horseArcher: 0));
            Assert.Equal("Infantry-Cavalry", FormationComposition.Label(infantry: 65, ranged: 0, cavalry: 35, horseArcher: 0));
        }

        [Fact]
        public void ASmallSliverDoesNotCount()
        {
            // 55% ranged / 40% infantry / 5% cavalry -> cavalry ignored, still "Ranged-Infantry".
            Assert.Equal("Ranged-Infantry", FormationComposition.Label(infantry: 40, ranged: 55, cavalry: 5, horseArcher: 0));
        }

        [Fact]
        public void ThreeSignificantTypesAreMixed()
        {
            Assert.Equal("Mixed", FormationComposition.Label(infantry: 35, ranged: 35, cavalry: 30, horseArcher: 0));
            Assert.Equal("Mixed", FormationComposition.Label(infantry: 25, ranged: 25, cavalry: 25, horseArcher: 25));
        }

        [Fact]
        public void ThresholdBoundaryIsExactlyFifteenPercent()
        {
            // A third type at 15% counts -> three significant types -> "Mixed".
            Assert.Equal("Mixed", FormationComposition.Label(infantry: 50, ranged: 35, cavalry: 15, horseArcher: 0));
            // The same split with that type at 14% drops below the threshold -> two-type label.
            Assert.Equal("Infantry-Ranged", FormationComposition.Label(infantry: 51, ranged: 35, cavalry: 14, horseArcher: 0));
        }

        [Fact]
        public void DominantRuleIsStrictlyOverSeventy()
        {
            // 71% is over the line -> single name; the 29% tail is dropped.
            Assert.Equal("Infantry", FormationComposition.Label(infantry: 71, ranged: 0, cavalry: 0, horseArcher: 29));
            // Exactly 70% is NOT over the line -> the 30% second type is listed.
            Assert.Equal("Infantry-Cavalry", FormationComposition.Label(infantry: 70, ranged: 0, cavalry: 30, horseArcher: 0));
        }

        [Fact]
        public void TiesAreOrderedDeterministicallyDominantFirstByDeclarationOrder()
        {
            // A 50/50 split must produce a stable, dominant-first order (the sort
            // is stable, so the earlier-declared type wins the tie).
            Assert.Equal("Infantry-Ranged", FormationComposition.Label(infantry: 50, ranged: 50, cavalry: 0, horseArcher: 0));
            Assert.Equal("Cavalry-Horse Archer", FormationComposition.Label(infantry: 0, ranged: 0, cavalry: 50, horseArcher: 50));
        }

        [Fact]
        public void DominantTypeDropsAnOtherwiseSignificantTail()
        {
            // 80% infantry names it alone even though the 20% ranged tail is itself
            // above the 15% threshold — the >70% rule wins.
            Assert.Equal("Infantry", FormationComposition.Label(infantry: 80, ranged: 20, cavalry: 0, horseArcher: 0));
        }

        [Fact]
        public void OneSignificantTypeBelowSeventyNamesItAlone()
        {
            // 60% infantry, the rest split into sub-15% slivers -> only infantry is
            // significant -> "Infantry" (not "Mixed", not a hyphenated pair).
            Assert.Equal("Infantry", FormationComposition.Label(infantry: 60, ranged: 14, cavalry: 13, horseArcher: 13));
        }

        [Fact]
        public void EmptyFormationIsLabelledEmpty()
        {
            Assert.Equal("Empty", FormationComposition.Label(0, 0, 0, 0));
        }

        [Fact]
        public void HorseArcherParticipatesInLabels()
        {
            Assert.Equal("Horse Archer", FormationComposition.Label(infantry: 10, ranged: 0, cavalry: 0, horseArcher: 90));
            Assert.Equal("Horse Archer-Cavalry", FormationComposition.Label(infantry: 0, ranged: 0, cavalry: 40, horseArcher: 60));
        }
    }
}
