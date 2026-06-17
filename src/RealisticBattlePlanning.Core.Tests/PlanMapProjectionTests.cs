using System.Linq;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Planning;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// The map view is a thin render over PlanMapProjection, so the tactical-frame
    /// geometry (forward = up, right = right, uniform shape-preserving scale,
    /// centred bounding box) is proven here without the game.
    /// </summary>
    public class PlanMapProjectionTests
    {
        private static readonly MapVec Origin = new(0f, 0f);
        private static readonly MapVec North = new(0f, 1f); // attack direction "up"

        [Fact]
        public void ForwardPointMapsHigherAndOnTheCentreColumn()
        {
            var proj = PlanMapProjection.Build(Origin, North,
                new[] { new MapVec(0f, 50f), new MapVec(0f, -50f) });

            var ahead = proj.Project(new MapVec(0f, 50f));
            var behind = proj.Project(new MapVec(0f, -50f));

            Assert.True(ahead.Y > behind.Y, "toward-enemy point should be higher (Y up)");
            Assert.Equal(0.5f, ahead.X, 3);  // both on the forward axis -> centre column
            Assert.Equal(0.5f, behind.X, 3);
        }

        [Fact]
        public void RightHandPointMapsToTheRight()
        {
            // Facing north, "right" is east (+X world). A point to the east maps to X > 0.5.
            var proj = PlanMapProjection.Build(Origin, North,
                new[] { new MapVec(50f, 0f), new MapVec(-50f, 0f) });

            Assert.True(proj.Project(new MapVec(50f, 0f)).X > 0.5f);
            Assert.True(proj.Project(new MapVec(-50f, 0f)).X < 0.5f);
            Assert.Equal(0.5f, proj.Project(new MapVec(50f, 0f)).Y, 3); // no forward offset -> centre row
        }

        [Fact]
        public void AttackDirectionRotatesTheFrame()
        {
            // Attack east: "forward" is +X world, so an east point is now "up".
            var east = new MapVec(1f, 0f);
            var proj = PlanMapProjection.Build(Origin, east,
                new[] { new MapVec(50f, 0f), new MapVec(-50f, 0f) });

            Assert.True(proj.Project(new MapVec(50f, 0f)).Y > 0.5f);  // ahead = up
            Assert.Equal(0.5f, proj.Project(new MapVec(50f, 0f)).X, 3);
        }

        [Fact]
        public void BoundingBoxCentreMapsToTheMiddle()
        {
            var proj = PlanMapProjection.Build(Origin, North,
                new[] { new MapVec(0f, 0f), new MapVec(0f, 100f) });

            var mid = proj.Project(new MapVec(0f, 50f)); // centre of the 0..100 box
            Assert.Equal(0.5f, mid.X, 3);
            Assert.Equal(0.5f, mid.Y, 3);
        }

        [Fact]
        public void PaddingKeepsExtremesInsideTheUnitBox()
        {
            const float pad = 0.12f;
            var proj = PlanMapProjection.Build(Origin, North,
                new[] { new MapVec(0f, 200f), new MapVec(0f, -200f) }, pad);

            var top = proj.Project(new MapVec(0f, 200f));
            var bottom = proj.Project(new MapVec(0f, -200f));
            Assert.Equal(1f - pad, top.Y, 3);
            Assert.Equal(pad, bottom.Y, 3);
        }

        [Fact]
        public void UniformScalePreservesAspect()
        {
            // A 100x100 world square must project to a square: its X-span equals its Y-span.
            var pts = new[]
            {
                new MapVec(50f, 50f), new MapVec(50f, -50f),
                new MapVec(-50f, 50f), new MapVec(-50f, -50f),
            };
            var proj = PlanMapProjection.Build(Origin, North, pts);
            var projected = pts.Select(proj.Project).ToList();

            var xSpan = projected.Max(p => p.X) - projected.Min(p => p.X);
            var ySpan = projected.Max(p => p.Y) - projected.Min(p => p.Y);
            Assert.Equal(xSpan, ySpan, 3);
        }

        [Fact]
        public void EmptyInputClampsZoomToMinSpan()
        {
            // No points -> the frame represents MinSpan meters, centred: the team
            // centre is dead centre (no divide-by-zero) and a point half a MinSpan
            // forward lands exactly on the padded top edge.
            const float pad = 0.12f;
            var proj = PlanMapProjection.Build(Origin, North, System.Array.Empty<MapVec>(), pad);

            var centre = proj.Project(Origin);
            Assert.Equal(0.5f, centre.X, 3);
            Assert.Equal(0.5f, centre.Y, 3);

            var edge = proj.Project(new MapVec(0f, PlanMapProjection.MinSpanMeters / 2f));
            Assert.Equal(1f - pad, edge.Y, 3);
        }
    }
}
