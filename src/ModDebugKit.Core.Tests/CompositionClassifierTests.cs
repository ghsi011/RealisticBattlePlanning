using ModDebugKit.Snapshots;
using Xunit;

namespace ModDebugKit.Tests
{
    public class CompositionClassifierTests
    {
        [Fact]
        public void Pure_bucket_names_the_formation_outright()
        {
            var c = CompositionClassifier.Classify(infantry: 40, ranged: 0, cavalry: 0, horseArcher: 0);
            Assert.Equal(40, c.Total);
            Assert.Equal("Infantry", c.Label);
        }

        [Fact]
        public void Empty_formation_reads_empty()
        {
            var c = CompositionClassifier.Classify(0, 0, 0, 0);
            Assert.Equal(0, c.Total);
            Assert.Equal("Empty", c.Label);
        }

        [Fact]
        public void Dominant_bucket_at_threshold_reads_mostly()
        {
            // 70% ranged, 30% infantry -> at the dominant threshold.
            var c = CompositionClassifier.Classify(infantry: 30, ranged: 70, cavalry: 0, horseArcher: 0);
            Assert.Equal("Mostly Ranged", c.Label);
        }

        [Fact]
        public void Below_threshold_reads_mixed_with_the_leader_noted()
        {
            // 60% cavalry, under the 70% threshold.
            var c = CompositionClassifier.Classify(infantry: 40, ranged: 0, cavalry: 60, horseArcher: 0);
            Assert.Equal("Mixed (mostly Cavalry)", c.Label);
        }

        [Fact]
        public void Horse_archers_are_labelled_distinctly()
        {
            var c = CompositionClassifier.Classify(infantry: 0, ranged: 0, cavalry: 0, horseArcher: 25);
            Assert.Equal("Horse Archer", c.Label);
            Assert.Equal(25, c.HorseArcher);
        }

        [Fact]
        public void Exactly_at_the_dominant_threshold_reads_mostly()
        {
            // 7/10 = exactly 0.70 -> "Mostly" (boundary is inclusive).
            var c = CompositionClassifier.Classify(infantry: 7, ranged: 3, cavalry: 0, horseArcher: 0);
            Assert.Equal("Mostly Infantry", c.Label);
        }

        [Fact]
        public void A_tie_for_max_picks_the_earlier_class_deterministically()
        {
            // 50/50 infantry/ranged: max is a tie; the scan keeps the first (Infantry), under threshold -> Mixed.
            var c = CompositionClassifier.Classify(infantry: 50, ranged: 50, cavalry: 0, horseArcher: 0);
            Assert.Equal("Mixed (mostly Infantry)", c.Label);
        }

        [Fact]
        public void Counts_are_preserved_on_the_dto()
        {
            var c = CompositionClassifier.Classify(1, 2, 3, 4);
            Assert.Equal(1, c.Infantry);
            Assert.Equal(2, c.Ranged);
            Assert.Equal(3, c.Cavalry);
            Assert.Equal(4, c.HorseArcher);
            Assert.Equal(10, c.Total);
        }
    }
}
