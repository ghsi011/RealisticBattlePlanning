using System.Linq;
using ModDebugKit.Battles;
using Xunit;

namespace ModDebugKit.Tests
{
    public class RosterDistributionTests
    {
        private static int CountFor(RosterDistributionResult r, int cls) =>
            r.Assignments.Where(a => a.ClassIndex == cls).Sum(a => a.Count);

        [Fact]
        public void Single_troop_per_class_gets_the_whole_count()
        {
            // The common case: each class resolves to exactly its culture-default troop.
            var r = RosterDistribution.Distribute(new[] { 150, 49, 0, 0 }, new[] { 1, 1, 1, 1 });
            Assert.Equal(150, CountFor(r, 0));
            Assert.Equal(49, CountFor(r, 1));
            Assert.Equal(0, CountFor(r, 2));
            Assert.Empty(r.Dropped);
            // exactly one assignment per non-zero class, at troop index 0
            Assert.All(r.Assignments, a => Assert.Equal(0, a.TroopIndex));
        }

        [Fact]
        public void Even_split_across_multiple_troops_preserves_the_total()
        {
            // 100 infantry across 3 troop types: total must be preserved, last absorbs the remainder.
            var r = RosterDistribution.Distribute(new[] { 100, 0, 0, 0 }, new[] { 3, 0, 0, 0 });
            Assert.Equal(100, CountFor(r, 0));
            var infAssignments = r.Assignments.Where(a => a.ClassIndex == 0).OrderBy(a => a.TroopIndex).Select(a => a.Count).ToArray();
            Assert.Equal(3, infAssignments.Length);
            Assert.Equal(new[] { 33, 33, 34 }, infAssignments); // 33/33/34 = 100, remainder on the last
        }

        [Fact]
        public void Horse_archer_count_is_redistributed_when_no_HA_troop()
        {
            // counts[3]=30 with troopsPerClass[3]=0 -> folded into inf/rng/cav (10/10/10), HA zeroed.
            var r = RosterDistribution.Distribute(new[] { 0, 0, 0, 30 }, new[] { 1, 1, 1, 0 });
            Assert.Equal(0, CountFor(r, 3));
            Assert.Equal(30, CountFor(r, 0) + CountFor(r, 1) + CountFor(r, 2));
            Assert.Equal(10, CountFor(r, 2));
            Assert.Equal(10, CountFor(r, 1));
            Assert.Equal(10, CountFor(r, 0));
            Assert.Empty(r.Dropped);
        }

        [Fact]
        public void Ha_redistribution_remainder_goes_to_infantry()
        {
            // 31 / 3 = 10 each + remainder 1 -> infantry gets 11.
            var r = RosterDistribution.Distribute(new[] { 0, 0, 0, 31 }, new[] { 1, 1, 1, 0 });
            Assert.Equal(11, CountFor(r, 0));
            Assert.Equal(10, CountFor(r, 1));
            Assert.Equal(10, CountFor(r, 2));
        }

        [Fact]
        public void Class_with_count_but_no_troop_is_reported_dropped_not_silently_lost()
        {
            // cavalry (class 2) has 30 requested but zero resolvable troops, and it is NOT the HA class.
            var r = RosterDistribution.Distribute(new[] { 100, 0, 30, 0 }, new[] { 1, 0, 0, 1 });
            Assert.Equal(100, CountFor(r, 0));
            Assert.Equal(0, CountFor(r, 2));
            var dropped = Assert.Single(r.Dropped);
            Assert.Equal(2, dropped.ClassIndex);
            Assert.Equal(30, dropped.Count);
        }

        [Fact]
        public void Zero_counts_produce_no_assignments()
        {
            var r = RosterDistribution.Distribute(new[] { 0, 0, 0, 0 }, new[] { 1, 1, 1, 1 });
            Assert.Empty(r.Assignments);
            Assert.Empty(r.Dropped);
        }
    }

    public class GoldArgTests
    {
        [Theory]
        [InlineData("5000", 1000, 4000)]   // set: delta = 5000 - 1000
        [InlineData("+250", 1000, 250)]    // adjust up
        [InlineData("-300", 1000, -300)]   // adjust down
        [InlineData("0", 1000, -1000)]     // set to zero
        public void Resolves_set_vs_adjust(string arg, int current, int expectedDelta)
        {
            Assert.True(GoldArg.TryResolveDelta(arg, current, out var delta));
            Assert.Equal(expectedDelta, delta);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("lots")]
        public void Rejects_non_numbers(string arg)
        {
            Assert.False(GoldArg.TryResolveDelta(arg, 1000, out _));
        }
    }
}
