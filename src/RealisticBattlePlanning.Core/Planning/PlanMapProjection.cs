using System.Collections.Generic;
using RealisticBattlePlanning.Execution;

namespace RealisticBattlePlanning.Planning
{
    /// <summary>A world position projected into the normalized tactical map frame:
    /// <see cref="X"/> right, <see cref="Y"/> forward (toward the enemy), both in
    /// [0,1] with the battlefield centred. Y is "up" — the renderer flips it for
    /// screen space (where down is positive).</summary>
    public readonly struct MapPoint
    {
        public MapPoint(float x, float y) { X = x; Y = y; }
        public float X { get; }
        public float Y { get; }
    }

    /// <summary>
    /// Projects battlefield world positions (the editor's deployment geometry, or
    /// a live snapshot) into a normalized [0,1]² tactical map: forward = the
    /// team's attack direction = up; right = its right perpendicular. The axes
    /// match <see cref="ResolvedAnchors"/> so the map and execution agree on where
    /// "forward 40 m" is. Engine-free and unit-tested; the Gauntlet map view is a
    /// thin render over the points this produces.
    ///
    /// Scaling is UNIFORM (shape-preserving): the bounding box of all supplied
    /// points is centred and fit inside the unit square with a padding margin, so
    /// relative distances and angles read true. A minimum span keeps a tight
    /// cluster (or a single point) from zooming to absurd scale.
    /// </summary>
    public sealed class PlanMapProjection
    {
        private readonly MapVec _center;     // tactical-frame centre (right, forward) of the bounding box
        private readonly MapVec _forward;
        private readonly MapVec _right;
        private readonly float _scale;       // normalized units per meter

        /// <summary>Smallest battlefield span (meters) the map will zoom to, so a
        /// cluster of nearly-coincident points doesn't fill the whole frame.</summary>
        public const float MinSpanMeters = 40f;

        private PlanMapProjection(MapVec teamCenter, MapVec forward, MapVec right, MapVec center, float scale)
        {
            TeamCenter = teamCenter;
            _forward = forward;
            _right = right;
            _center = center;
            _scale = scale;
        }

        public MapVec TeamCenter { get; }

        /// <summary>
        /// Builds a projection that fits <paramref name="worldPoints"/> (plus the
        /// team centre) into the unit square. <paramref name="paddingFraction"/> is
        /// the empty margin kept on each edge (0.12 = 12%).
        /// </summary>
        public static PlanMapProjection Build(
            MapVec teamCenter,
            MapVec attackDirection,
            IEnumerable<MapVec> worldPoints,
            float paddingFraction = 0.12f)
        {
            var forward = attackDirection.Normalized();
            var right = forward.Right();

            // Tactical-frame extent of every point relative to the team centre,
            // always including the origin (the team centre itself).
            float minR = 0f, maxR = 0f, minF = 0f, maxF = 0f;
            if (worldPoints != null)
            {
                foreach (var p in worldPoints)
                {
                    var d = p - teamCenter;
                    var r = d.Dot(right);
                    var f = d.Dot(forward);
                    if (r < minR) minR = r; if (r > maxR) maxR = r;
                    if (f < minF) minF = f; if (f > maxF) maxF = f;
                }
            }

            var spanR = maxR - minR;
            var spanF = maxF - minF;
            var span = spanR > spanF ? spanR : spanF;
            if (span < MinSpanMeters) span = MinSpanMeters;

            var center = new MapVec((minR + maxR) * 0.5f, (minF + maxF) * 0.5f);
            // (1 - 2*pad) of the unit box spans `span` meters; uniform on both axes.
            if (paddingFraction < 0f) paddingFraction = 0f;
            if (paddingFraction > 0.45f) paddingFraction = 0.45f;
            var scale = (1f - 2f * paddingFraction) / span;

            return new PlanMapProjection(teamCenter, forward, right, center, scale);
        }

        /// <summary>Projects a world position to normalized map coordinates (Y up).</summary>
        public MapPoint Project(MapVec world)
        {
            var d = world - TeamCenter;
            var r = d.Dot(_right) - _center.X;
            var f = d.Dot(_forward) - _center.Y;
            return new MapPoint(0.5f + r * _scale, 0.5f + f * _scale);
        }

        /// <summary>The inverse of <see cref="Project"/>: a normalized map point (Y up)
        /// back to a world position. Round-trips with Project within float error, so a
        /// click on the map resolves to the world point the renderer drew there — the
        /// basis for point-and-click move authoring (A2.6.2).</summary>
        public MapVec Unproject(MapPoint p)
        {
            var r = (p.X - 0.5f) / _scale + _center.X;
            var f = (p.Y - 0.5f) / _scale + _center.Y;
            return TeamCenter + _right * r + _forward * f;
        }
    }
}
