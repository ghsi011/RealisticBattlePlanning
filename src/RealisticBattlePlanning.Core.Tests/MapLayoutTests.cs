using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Planning;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>De-overlap maths for the map markers (A2.6): proven without the game.</summary>
    public class MapLayoutTests
    {
        [Fact]
        public void PointsAlreadyApartAreUntouched()
        {
            var pts = new[] { new MapVec(0f, 0f), new MapVec(100f, 0f), new MapVec(0f, 100f) };
            var spread = MapLayout.SpreadOverlaps(pts, 40f);
            for (var i = 0; i < pts.Length; i++)
                Assert.True(pts[i].DistanceTo(spread[i]) < 0.001f, $"point {i} moved though it was clear");
        }

        [Fact]
        public void OverlappingPointsArePushedApart()
        {
            // Two blocks 8 apart with a 36 min separation end up at least ~min apart.
            var spread = MapLayout.SpreadOverlaps(new[] { new MapVec(0f, 0f), new MapVec(8f, 0f) }, 36f);
            Assert.True(spread[0].DistanceTo(spread[1]) >= 35f,
                $"separation {spread[0].DistanceTo(spread[1]):0.0} should reach the minimum");
        }

        [Fact]
        public void CoincidentPointsAreSeparatedDeterministically()
        {
            var a = MapLayout.SpreadOverlaps(new[] { new MapVec(5f, 5f), new MapVec(5f, 5f) }, 30f);
            var b = MapLayout.SpreadOverlaps(new[] { new MapVec(5f, 5f), new MapVec(5f, 5f) }, 30f);
            Assert.True(a[0].DistanceTo(a[1]) >= 29f, "coincident points should split apart");
            // Deterministic: same input -> same output (no RNG).
            Assert.Equal(a[0].X, b[0].X, 4);
            Assert.Equal(a[1].Y, b[1].Y, 4);
        }

        [Fact]
        public void PreservesCentroidRoughly()
        {
            // Symmetric pushes keep the group centred where it was (no global drift).
            var pts = new[] { new MapVec(10f, 10f), new MapVec(14f, 10f), new MapVec(12f, 12f) };
            var spread = MapLayout.SpreadOverlaps(pts, 30f);
            float SumX(MapVec[] p) { var s = 0f; foreach (var v in p) s += v.X; return s; }
            float SumY(MapVec[] p) { var s = 0f; foreach (var v in p) s += v.Y; return s; }
            Assert.Equal(SumX(pts), SumX(spread), 1);
            Assert.Equal(SumY(pts), SumY(spread), 1);
        }
    }
}
