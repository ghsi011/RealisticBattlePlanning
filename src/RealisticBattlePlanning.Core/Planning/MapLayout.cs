using System;
using System.Collections.Generic;
using RealisticBattlePlanning.Execution;

namespace RealisticBattlePlanning.Planning
{
    /// <summary>
    /// Map-marker layout helpers (A2.6). Engine-free + unit-tested; the Gauntlet map calls
    /// these so the placement maths are proven without the game.
    /// </summary>
    public static class MapLayout
    {
        /// <summary>
        /// Nudges marker centres apart so blocks that sit almost on top of each other (e.g.
        /// infantry + ranged sharing a column, only metres apart on a 200 m view) become
        /// separately clickable, while points already at least <paramref name="minDistance"/>
        /// apart stay put. A few relaxation passes push each overlapping pair symmetrically
        /// along their separation; perfectly-coincident points are split along a deterministic
        /// per-index direction (no RNG, so the result is reproducible). True positions move
        /// only as far as needed to separate, so the map stays faithful.
        /// </summary>
        public static MapVec[] SpreadOverlaps(IReadOnlyList<MapVec> centers, float minDistance, int iterations = 12)
        {
            var n = centers?.Count ?? 0;
            var result = new MapVec[n];
            for (var i = 0; i < n; i++) result[i] = centers[i];
            if (n < 2 || minDistance <= 0f)
                return result;

            for (var iter = 0; iter < iterations; iter++)
            {
                var moved = false;
                for (var i = 0; i < n; i++)
                    for (var j = i + 1; j < n; j++)
                    {
                        var delta = result[j] - result[i];
                        var dist = delta.Length;
                        if (dist >= minDistance)
                            continue;

                        MapVec dir;
                        if (dist < 1e-4f)
                            // Coincident: split along a stable per-index angle so it's deterministic.
                            dir = new MapVec((float)Math.Cos(i * 2.39996f), (float)Math.Sin(i * 2.39996f));
                        else
                            dir = delta * (1f / dist);

                        var push = (minDistance - dist) * 0.5f;
                        result[i] = result[i] - dir * push;
                        result[j] = result[j] + dir * push;
                        moved = true;
                    }
                if (!moved)
                    break;
            }
            return result;
        }
    }
}
