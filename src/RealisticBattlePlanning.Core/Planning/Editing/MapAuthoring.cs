using System;
using System.Collections.Generic;
using System.Linq;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Planning.Editing
{
    /// <summary>
    /// Engine-free logic behind the map-first editor (spec A2.6): turns a click at
    /// a world point into a <see cref="PlanDraft"/> edit, so the Gauntlet map is a
    /// thin front-end over tested logic. Click-to-march (A2.6.2): each click appends
    /// a move stage to that point whose default trigger is "the previous move
    /// completed" — <see cref="TriggerType.PositionReached"/> the prior waypoint —
    /// or "on battle start" for the formation's first stage, so a chain of clicks
    /// builds a waypoint march with no menu trips.
    /// </summary>
    public static class MapAuthoring
    {
        /// <summary>Default arrival radius for an auto-authored "reached the previous waypoint" trigger.</summary>
        public const float DefaultReachToleranceMeters = 8f;

        /// <summary>
        /// Appends a "march to <paramref name="worldPoint"/>" stage to
        /// <paramref name="formation"/>: a Scene anchor at the point (id
        /// <paramref name="anchorId"/>) plus a MoveTo stage triggered by reaching the
        /// previous waypoint (or battle start if it's the first stage). The caller
        /// owns anchor-id naming/uniqueness so this stays pure. No-ops on a null draft
        /// or blank id; returns the anchor id used (null if it no-opped).
        /// </summary>
        public static string AppendMarchStage(PlanDraft draft, PlannedFormationClass formation, MapVec worldPoint, string anchorId)
        {
            if (draft == null || string.IsNullOrWhiteSpace(anchorId))
                return null;

            draft.AddAnchor(new MapAnchor { Id = anchorId, Basis = AnchorBasis.Scene, X = worldPoint.X, Y = worldPoint.Y });

            var previousWaypoint = PreviousWaypointAnchor(draft.StagesOf(formation));
            var trigger = previousWaypoint != null
                ? new TriggerSpec { Type = TriggerType.PositionReached, Anchor = previousWaypoint, ToleranceMeters = DefaultReachToleranceMeters }
                : new TriggerSpec { Type = TriggerType.BattleStart };

            draft.AddStage(formation, new Stage
            {
                When = { trigger },
                Do = new DirectiveSpec { Type = DirectiveType.MoveTo, Anchor = anchorId },
            });
            return anchorId;
        }

        /// <summary>
        /// Multi-select drag-to-line (A2.6.3): <paramref name="count"/> formation positions
        /// spread evenly along the drag from <paramref name="a"/> to <paramref name="b"/>
        /// (endpoints inclusive; the single-formation case is the midpoint). The line's span
        /// and orientation come straight from the drag.
        /// </summary>
        public static IReadOnlyList<MapVec> LinePositions(MapVec a, MapVec b, int count)
        {
            var result = new List<MapVec>();
            if (count <= 0)
                return result;
            if (count == 1)
            {
                result.Add((a + b) * 0.5f);
                return result;
            }
            for (var i = 0; i < count; i++)
                result.Add(a + (b - a) * ((float)i / (count - 1)));
            return result;
        }

        /// <summary>
        /// Arrays the selected <paramref name="formations"/> along the drag line A→B and
        /// appends a march stage sending each to its spot (reusing <see cref="AppendMarchStage"/>,
        /// so the default "previous stage completed / battle start" trigger applies and they
        /// move together). <paramref name="anchorIdFor"/> supplies a unique anchor id per
        /// formation. Returns the anchor ids placed, in formation order.
        /// </summary>
        public static IReadOnlyList<string> AppendLineFormation(
            PlanDraft draft, IReadOnlyList<PlannedFormationClass> formations,
            MapVec a, MapVec b, Func<PlannedFormationClass, string> anchorIdFor)
        {
            var ids = new List<string>();
            if (draft == null || formations == null || formations.Count == 0 || anchorIdFor == null)
                return ids;
            var positions = LinePositions(a, b, formations.Count);
            for (var i = 0; i < formations.Count; i++)
                ids.Add(AppendMarchStage(draft, formations[i], positions[i], anchorIdFor(formations[i])));
            return ids;
        }

        /// <summary>The destination anchor of a formation's current last stage when it is a
        /// move (single anchor or the last path waypoint), else null — i.e. "where the
        /// previous stage ends", the point a follow-on march waits to reach.</summary>
        private static string PreviousWaypointAnchor(IReadOnlyList<Stage> stages)
        {
            var last = stages.LastOrDefault();
            if (last?.Do is not { Type: DirectiveType.MoveTo } move)
                return null;
            if (!string.IsNullOrWhiteSpace(move.Anchor))
                return move.Anchor;
            return move.Path?.LastOrDefault(w => !string.IsNullOrWhiteSpace(w));
        }
    }
}
