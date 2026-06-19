using RealisticBattlePlanning.Planning;
using RealisticBattlePlanning.Planning.Editing;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>Drag-box multi-select geometry (spec A2.6.1) in the normalized map frame.</summary>
    public class MapSelectionTests
    {
        [Fact]
        public void InBoxReturnsMarkersInsideTheRectOrderIndependent()
        {
            var markers = new (PlannedFormationClass, MapPoint)[]
            {
                (PlannedFormationClass.Infantry, new MapPoint(0.2f, 0.2f)),
                (PlannedFormationClass.Ranged,   new MapPoint(0.5f, 0.5f)),
                (PlannedFormationClass.Cavalry,  new MapPoint(0.9f, 0.9f)),
            };

            // corners given top-right then bottom-left to prove order independence.
            var inside = MapSelection.InBox(markers, new MapPoint(0.6f, 0.6f), new MapPoint(0.1f, 0.1f));

            Assert.Equal(new[] { PlannedFormationClass.Infantry, PlannedFormationClass.Ranged }, inside);
        }

        [Fact]
        public void InBoxEdgesAreInclusiveAndEmptyBoxSelectsNothingExtra()
        {
            var markers = new (PlannedFormationClass, MapPoint)[]
            {
                (PlannedFormationClass.Infantry, new MapPoint(0.5f, 0.5f)),
            };
            Assert.Single(MapSelection.InBox(markers, new MapPoint(0.5f, 0.5f), new MapPoint(0.5f, 0.5f)));  // edge/point inclusive
            Assert.Empty(MapSelection.InBox(markers, new MapPoint(0f, 0f), new MapPoint(0.4f, 0.4f)));        // outside
        }
    }
}
