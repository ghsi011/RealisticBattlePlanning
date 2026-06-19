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
        public void UnprojectInvertsProject()
        {
            // off-origin team centre + a non-trivial bounding box, so scale/centre matter.
            var proj = PlanMapProjection.Build(new MapVec(100f, 200f), North,
                new[] { new MapVec(70f, 160f), new MapVec(140f, 260f) });
            foreach (var world in new[] { new MapVec(100f, 200f), new MapVec(120f, 240f), new MapVec(85f, 175f) })
            {
                var back = proj.Unproject(proj.Project(world));
                Assert.True(world.DistanceTo(back) < 0.5f, $"round-trip drifted: ({world.X},{world.Y}) -> ({back.X},{back.Y})");
            }
        }

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

        // A 50 m-wide friendly line; the lateral span it occupies on the map under a
        // given projection — the smaller it is, the more "zoomed out" / true-to-scale.
        private static readonly MapVec[] FriendlyLine = { new(25f, 0f), new(-25f, 0f) };

        private static float LateralSpan(PlanMapProjection proj)
            => proj.Project(new MapVec(25f, 0f)).X - proj.Project(new MapVec(-25f, 0f)).X;

        [Fact]
        public void TacticalViewCompactArmyDoesNotFillFrame()
        {
            // The accuracy fix: a 50 m line never blows up to fill the map — the bounded
            // fixed scale keeps it small, so nearby units no longer read as far apart.
            var proj = PlanMapProjection.BuildTacticalView(Origin, North, FriendlyLine, new[] { new MapVec(0f, 150f) });
            Assert.True(LateralSpan(proj) < 0.4f, $"compact army footprint {LateralSpan(proj):0.000} should stay small");
        }

        [Fact]
        public void TacticalViewEnemyProjectsAheadOfFriendlies()
        {
            var proj = PlanMapProjection.BuildTacticalView(Origin, North, FriendlyLine, new[] { new MapVec(0f, 120f) });
            Assert.True(proj.Project(new MapVec(0f, 120f)).Y > proj.Project(Origin).Y,
                "enemy should sit forward (up) of the army");
        }

        [Fact]
        public void TacticalViewSeatsArmyInTheLowerHalf()
        {
            // The team centre is biased forward, so it sits below the middle (Y-up < 0.5) —
            // i.e. lower on screen, leaving the upper map for the advance toward the enemy.
            var proj = PlanMapProjection.BuildTacticalView(Origin, North, FriendlyLine, new[] { new MapVec(0f, 150f) });
            var c = proj.Project(Origin);
            Assert.Equal(0.5f, c.X, 3);
            Assert.True(c.Y < 0.5f, $"team-centre Y-up {c.Y:0.00} should be below middle (army seated low)");
        }

        [Fact]
        public void TacticalViewDistantEnemyKeepsArmyReadable()
        {
            // A very distant enemy must not collapse the army to a dot: the span saturates at
            // MaxView, so a far and a very-far enemy frame the army identically (and readably).
            var far = PlanMapProjection.BuildTacticalView(Origin, North, FriendlyLine, new[] { new MapVec(0f, 400f) });
            var veryFar = PlanMapProjection.BuildTacticalView(Origin, North, FriendlyLine, new[] { new MapVec(0f, 5000f) });

            Assert.True(LateralSpan(far) > 0.15f, $"distant-enemy army collapsed to {LateralSpan(far):0.000}");
            Assert.Equal(LateralSpan(far), LateralSpan(veryFar), 3); // MaxView saturated -> same framing
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
