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
