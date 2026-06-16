using System.Collections.Generic;
using System.Linq;

namespace RealisticBattlePlanning.Planning
{
    /// <summary>
    /// Plain-language label for a numbered formation's troop mix. The player's
    /// formations 1-8 are the objects of a plan, and "Infantry / Ranged" class
    /// names are misleading once troops are mixed — so each formation is labelled
    /// from its actual composition:
    /// <list type="bullet">
    /// <item>one type above 70% names the formation alone ("Infantry");</item>
    /// <item>otherwise the types at/above a small share threshold are listed
    /// dominant-first ("Ranged-Infantry");</item>
    /// <item>three or more significant types read as "Mixed".</item>
    /// </list>
    /// A type below the threshold does not count (e.g. 5% cavalry is ignored, so
    /// 55% ranged / 40% infantry / 5% cavalry is still "Ranged-Infantry"). Pure
    /// and unit-tested; the engine just feeds it live troop counts.
    /// </summary>
    public static class FormationComposition
    {
        /// <summary>A troop type at or above this share (percent of the formation) counts toward the label.</summary>
        public const float SignificantSharePercent = 15f;

        /// <summary>A troop type strictly above this share (percent) names the formation on its own.</summary>
        public const float DominantSharePercent = 70f;

        public static string Label(int infantry, int ranged, int cavalry, int horseArcher)
        {
            var parts = new List<(string Name, int Count)>
            {
                ("Infantry", infantry),
                ("Ranged", ranged),
                ("Cavalry", cavalry),
                ("Horse Archer", horseArcher),
            };

            var total = infantry + ranged + cavalry + horseArcher;
            if (total <= 0)
                return "Empty";

            var ranked = parts
                .Where(p => p.Count > 0)
                .Select(p => (p.Name, Share: 100f * p.Count / total))
                .OrderByDescending(p => p.Share)
                .ToList();

            // One dominant type names the formation regardless of the long tail.
            if (ranked[0].Share > DominantSharePercent)
                return ranked[0].Name;

            var significant = ranked.Where(p => p.Share >= SignificantSharePercent).ToList();
            if (significant.Count == 0)
                return ranked[0].Name; // degenerate (many tiny slivers): name the largest
            if (significant.Count >= 3)
                return "Mixed";
            return string.Join("-", significant.Select(p => p.Name));
        }
    }
}
