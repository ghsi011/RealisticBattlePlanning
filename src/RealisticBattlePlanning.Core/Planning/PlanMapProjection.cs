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

        /// <summary>Default tactical-view framing constants (A2.6, the map "better framing").</summary>
        public const float MinViewSpanMeters = 110f;   // never zoom in tighter than this (compact armies don't fill the frame)
        public const float MaxViewSpanMeters = 170f;   // never zoom out past this (the army stays readable; a far enemy edge-clamps)
        public const float MaxArmyFill = 0.55f;         // the friendly army occupies at most this fraction of the view span
        public const float TeamScreenY = 0.66f;         // the team centre sits this fraction down the map (army low, enemy up)

        /// <summary>
        /// Frames a stable, Total-War-style tactical view (A2.6, "better framing"): a
        /// bounded FIXED scale centred on the team, forward = up. The span is
        /// <c>clamp(max(enemy reach ×1.2, army size / MaxArmyFill), MinView, MaxView)</c>,
        /// so a compact deployment never blows up to fill the frame (the old "nearby units
        /// look far apart" bug) and a distant enemy never shrinks the army to a dot — it
        /// just edge-clamps (the marker loop clamps it on-canvas). The team centre is biased
        /// to <see cref="TeamScreenY"/> down, seating the army in the lower third with the
        /// enemy across the top, like the reference staff map. Scale stays consistent across
        /// battles, so players build intuition. With no points this is a clean centred view.
        /// </summary>
        public static PlanMapProjection BuildTacticalView(
            MapVec teamCenter,
            MapVec attackDirection,
            IReadOnlyList<MapVec> friendlyPoints,
            IReadOnlyList<MapVec> enemyPoints,
            float minSpanMeters = MinViewSpanMeters,
            float maxSpanMeters = MaxViewSpanMeters,
            float maxArmyFill = MaxArmyFill,
            float teamScreenY = TeamScreenY,
            float paddingFraction = 0.12f)
        {
            var forward = attackDirection.Normalized();
            var right = forward.Right();

            // Friendly extent (right/forward), always including the team centre (the origin).
            float minR = 0f, maxR = 0f, minF = 0f, maxF = 0f;
            if (friendlyPoints != null)
                foreach (var p in friendlyPoints)
                {
                    var d = p - teamCenter;
                    var r = d.Dot(right); var f = d.Dot(forward);
                    if (r < minR) minR = r; if (r > maxR) maxR = r;
                    if (f < minF) minF = f; if (f > maxF) maxF = f;
                }
            var armySize = System.Math.Max(maxR - minR, maxF - minF);

            // How far forward the enemy reaches (0 if none) — the view tries to include it.
            var enemyForward = 0f;
            if (enemyPoints != null)
                foreach (var e in enemyPoints)
                {
                    var f = (e - teamCenter).Dot(forward);
                    if (f > enemyForward) enemyForward = f;
                }

            if (maxArmyFill < 0.1f) maxArmyFill = 0.1f;
            var rawSpan = System.Math.Max(enemyForward * 1.2f, armySize / maxArmyFill);
            var span = Clamp(rawSpan, minSpanMeters, System.Math.Max(minSpanMeters, maxSpanMeters));

            if (paddingFraction < 0f) paddingFraction = 0f;
            if (paddingFraction > 0.45f) paddingFraction = 0.45f;
            var scale = (1f - 2f * paddingFraction) / span;

            // Bias the view forward so the team centre lands at teamScreenY down the (Y-up,
            // then renderer-flipped) map: screenDown(teamCentre) = 0.5 + bias*scale = teamScreenY.
            var biasForward = (teamScreenY - 0.5f) / scale;
            return new PlanMapProjection(teamCenter, forward, right, new MapVec(0f, biasForward), scale);
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

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
