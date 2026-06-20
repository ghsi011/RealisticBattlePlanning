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
        /// <paramref name="widthMeters"/>, when given, sets the formation's frontage at the
        /// destination (the field-planning drag carries the line you stretched, so the formation
        /// forms the width you drew — what the soldier ghost showed is what executes). A
        /// non-positive width is ignored.
        /// </summary>
        public static string AppendMarchStage(PlanDraft draft, PlannedFormationClass formation, MapVec worldPoint, string anchorId, float? widthMeters = null)
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
                Do = new DirectiveSpec
                {
                    Type = DirectiveType.MoveTo,
                    Anchor = anchorId,
                    WidthMeters = widthMeters is { } w && w > 0f ? widthMeters : null,
                },
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

        /// <summary>
        /// Removes the march stage whose destination is <paramref name="anchorId"/> (a
        /// click-placed waypoint) and RE-LINKS the chain: if a following stage waited on that
        /// waypoint (PositionReached), it is re-pointed to the removed stage's own previous
        /// waypoint, or to BattleStart if the removed one was the first — so right-clicking a
        /// waypoint out of the middle of a march leaves wp1->wp3 intact, not a dangling trigger.
        /// The caller prunes the now-unreferenced anchor. Returns true if a stage was removed.
        /// Scoped to the click-authored chain: matches a single-anchor MoveTo (not a Path) and
        /// re-links the next stage's first PositionReached — a hand-authored multi-trigger stage
        /// would re-link only that one condition.
        /// </summary>
        public static bool RemoveMarchWaypoint(PlanDraft draft, string anchorId)
        {
            if (draft == null || string.IsNullOrWhiteSpace(anchorId))
                return false;

            foreach (var formation in draft.Formations)
            {
                var stages = draft.StagesOf(formation); // snapshot list of LIVE stage objects
                for (var i = 0; i < stages.Count; i++)
                {
                    var move = stages[i].Do;
                    if (move is not { Type: DirectiveType.MoveTo } || !Same(move.Anchor, anchorId))
                        continue;

                    // What this stage waited on (its previous waypoint), to re-link the next stage.
                    var prevAnchor = stages[i].When
                        .FirstOrDefault(t => t != null && t.Type == TriggerType.PositionReached)?.Anchor;

                    if (i + 1 < stages.Count)
                    {
                        var nextTrigger = stages[i + 1].When
                            .FirstOrDefault(t => t != null && t.Type == TriggerType.PositionReached && Same(t.Anchor, anchorId));
                        if (nextTrigger != null)
                        {
                            if (!string.IsNullOrWhiteSpace(prevAnchor))
                            {
                                nextTrigger.Anchor = prevAnchor; // chain onto the earlier waypoint
                            }
                            else
                            {
                                nextTrigger.Type = TriggerType.BattleStart; // removed one was first
                                nextTrigger.Anchor = null;
                                nextTrigger.ToleranceMeters = null;
                            }
                        }
                    }

                    draft.RemoveStage(formation, i);
                    return true;
                }
            }
            return false;
        }

        private static bool Same(string a, string b)
            => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

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
