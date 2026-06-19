using System.Collections.Generic;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Planning.Editing
{
    /// <summary>
    /// Map selection geometry (spec A2.6.1): which formation markers fall inside a drag-box,
    /// for multi-select. Operates in the normalized [0,1] map frame the projection produces,
    /// so it's resolution-independent. Engine-free + unit-tested; the Gauntlet map turns a
    /// mouse drag into two corners and feeds the projected marker points here.
    /// </summary>
    public static class MapSelection
    {
        /// <summary>
        /// The formations whose marker point lies within the axis-aligned box defined by two
        /// opposite corners (corner order does not matter), edges inclusive — in marker order.
        /// </summary>
        public static IReadOnlyList<PlannedFormationClass> InBox(
            IEnumerable<(PlannedFormationClass Formation, MapPoint Point)> markers, MapPoint corner1, MapPoint corner2)
        {
            var result = new List<PlannedFormationClass>();
            if (markers == null)
                return result;

            var minX = corner1.X < corner2.X ? corner1.X : corner2.X;
            var maxX = corner1.X < corner2.X ? corner2.X : corner1.X;
            var minY = corner1.Y < corner2.Y ? corner1.Y : corner2.Y;
            var maxY = corner1.Y < corner2.Y ? corner2.Y : corner1.Y;

            foreach (var (formation, p) in markers)
                if (p.X >= minX && p.X <= maxX && p.Y >= minY && p.Y <= maxY)
                    result.Add(formation);
            return result;
        }
    }
}
