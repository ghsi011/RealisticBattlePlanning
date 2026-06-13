using System.Collections.Generic;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Execution
{
    /// <summary>
    /// Anchor ids resolved to world positions against battle-start geometry.
    /// OwnStart anchors differ per formation; TeamCenter/Scene are shared.
    /// Axes: "forward" = team attack direction, "right" = its right
    /// perpendicular (MapVec.Right).
    /// </summary>
    public sealed class ResolvedAnchors
    {
        private readonly Dictionary<string, MapAnchor> _anchors;
        private readonly Dictionary<PlannedFormationClass, MapVec> _startPositions;
        private readonly MapVec _teamCenter;
        private readonly MapVec _forward;
        private readonly MapVec _right;

        public ResolvedAnchors(
            IEnumerable<MapAnchor> anchors,
            IReadOnlyDictionary<PlannedFormationClass, MapVec> formationStartPositions,
            MapVec teamCenter,
            MapVec attackDirection)
        {
            _anchors = new Dictionary<string, MapAnchor>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var anchor in anchors)
            {
                if (anchor.Id != null)
                    _anchors[anchor.Id] = anchor;
            }

            _startPositions = new Dictionary<PlannedFormationClass, MapVec>();
            foreach (var pair in formationStartPositions)
                _startPositions[pair.Key] = pair.Value;
            _teamCenter = teamCenter;
            _forward = attackDirection.Normalized();
            _right = _forward.Right();
        }

        public bool HasStartPosition(PlannedFormationClass formationClass)
            => _startPositions.ContainsKey(formationClass);

        /// <summary>
        /// A formation that had no units at battle start gets its OwnStart
        /// basis from wherever it first appears (reinforcement waves).
        /// </summary>
        public void RegisterLateStart(PlannedFormationClass formationClass, MapVec position)
        {
            if (!_startPositions.ContainsKey(formationClass))
                _startPositions[formationClass] = position;
        }

        /// <summary>Captures battle-start geometry from a snapshot.</summary>
        public static ResolvedAnchors FromSnapshot(IEnumerable<MapAnchor> anchors, IBattlefieldSnapshot snapshot)
        {
            var startPositions = new Dictionary<PlannedFormationClass, MapVec>();
            foreach (PlannedFormationClass formationClass in System.Enum.GetValues(typeof(PlannedFormationClass)))
            {
                var formation = snapshot.GetOwn(formationClass);
                if (formation is { Exists: true })
                    startPositions[formationClass] = formation.Position;
            }

            return new ResolvedAnchors(anchors, startPositions, snapshot.TeamCenter, snapshot.AttackDirection);
        }

        /// <summary>Null when the anchor is unknown or its basis can't be resolved for this formation.</summary>
        public MapVec? Resolve(PlannedFormationClass formationClass, string anchorId)
        {
            if (anchorId == null || !_anchors.TryGetValue(anchorId, out var anchor))
                return null;

            switch (anchor.Basis)
            {
                case AnchorBasis.OwnStart:
                    if (!_startPositions.TryGetValue(formationClass, out var start))
                        return null;
                    return start + _forward * anchor.Forward + _right * anchor.Right;

                case AnchorBasis.TeamCenter:
                    return _teamCenter + _forward * anchor.Forward + _right * anchor.Right;

                case AnchorBasis.Scene:
                    return new MapVec(anchor.X, anchor.Y);

                default:
                    return null;
            }
        }
    }
}
