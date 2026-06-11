using System.Collections.Generic;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    public class AnchorResolverTests
    {
        private static ResolvedAnchors Build(MapVec attackDirection, params MapAnchor[] anchors)
        {
            var starts = new Dictionary<PlannedFormationClass, MapVec>
            {
                [PlannedFormationClass.Infantry] = new MapVec(100f, 200f),
            };
            return new ResolvedAnchors(anchors, starts, teamCenter: new MapVec(50f, 60f), attackDirection);
        }

        [Fact]
        public void OwnStartResolvesAlongAttackAxes()
        {
            // Facing north: forward = +Y, right = +X.
            var anchors = Build(new MapVec(0f, 1f),
                new MapAnchor { Id = "a", Basis = AnchorBasis.OwnStart, Forward = 50f, Right = 10f });

            Assert.Equal(new MapVec(110f, 250f), anchors.Resolve(PlannedFormationClass.Infantry, "a"));
        }

        [Fact]
        public void OwnStartFollowsARotatedAttackDirection()
        {
            // Facing east: forward = +X, right = -Y.
            var anchors = Build(new MapVec(1f, 0f),
                new MapAnchor { Id = "a", Basis = AnchorBasis.OwnStart, Forward = 50f, Right = 10f });

            Assert.Equal(new MapVec(150f, 190f), anchors.Resolve(PlannedFormationClass.Infantry, "a"));
        }

        [Fact]
        public void TeamCenterUsesTeamGeometry()
        {
            var anchors = Build(new MapVec(0f, 1f),
                new MapAnchor { Id = "a", Basis = AnchorBasis.TeamCenter, Forward = -20f, Right = 5f });

            Assert.Equal(new MapVec(55f, 40f), anchors.Resolve(PlannedFormationClass.Infantry, "a"));
        }

        [Fact]
        public void SceneAnchorsAreAbsolute()
        {
            var anchors = Build(new MapVec(0f, 1f),
                new MapAnchor { Id = "a", Basis = AnchorBasis.Scene, X = 333f, Y = 444f });

            Assert.Equal(new MapVec(333f, 444f), anchors.Resolve(PlannedFormationClass.Ranged, "a"));
        }

        [Fact]
        public void OwnStartForAFormationWithoutAStartPositionIsNull()
        {
            var anchors = Build(new MapVec(0f, 1f),
                new MapAnchor { Id = "a", Basis = AnchorBasis.OwnStart, Forward = 50f });

            Assert.Null(anchors.Resolve(PlannedFormationClass.Cavalry, "a"));
        }

        [Fact]
        public void UnknownAnchorIdIsNull()
        {
            var anchors = Build(new MapVec(0f, 1f));
            Assert.Null(anchors.Resolve(PlannedFormationClass.Infantry, "ghost"));
        }

        [Fact]
        public void AnchorIdsAreCaseInsensitive()
        {
            var anchors = Build(new MapVec(0f, 1f),
                new MapAnchor { Id = "Goal-Line", Basis = AnchorBasis.Scene, X = 1f, Y = 2f });

            Assert.NotNull(anchors.Resolve(PlannedFormationClass.Infantry, "goal-line"));
        }
    }
}
