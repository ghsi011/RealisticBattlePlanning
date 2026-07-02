using System.Collections.Generic;
using System.Linq;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Planning
{
    /// <summary>The outcome of remapping a previous plan onto this battle's formations.</summary>
    public sealed class RemapResult
    {
        public RemapResult(BattlePlan plan, IReadOnlyList<PlannedFormationClass> dropped)
        {
            Plan = plan;
            Dropped = dropped;
        }

        /// <summary>The remapped plan, ready to apply (a fresh copy — editing it won't touch the source).</summary>
        public BattlePlan Plan { get; }

        /// <summary>Formation plans dropped because that class isn't fielded this battle (A3.9 "flagged for review").</summary>
        public IReadOnlyList<PlannedFormationClass> Dropped { get; }

        public bool HasDrops => Dropped.Count > 0;
    }

    /// <summary>
    /// "Repeat last plan" (spec A3.9): re-apply a previous battle's plan to the
    /// formations available now. Plans are already keyed by formation class and
    /// anchors are RELATIVE (OwnStart/TeamCenter resolve against this battle's
    /// deployment at execution), so remapping is simply: deep-copy the previous
    /// plan, KEEP the formation plans whose class is fielded this battle, DROP and
    /// report the rest, and carry the shared anchors + player signals across.
    /// Engine-free and unit-tested; the engine stores the last applied plan and
    /// offers the one-click re-apply over this.
    ///
    /// Note: absolute (Scene) anchors are map-specific and won't make sense on a
    /// new battlefield — they're carried but the validator/executor degrades them
    /// (G3). Relative anchors adapt automatically. Formations present this battle
    /// but absent from the previous plan simply hold (they aren't "drops").
    /// </summary>
    /// <summary>The outcome of carrying a plan onto a NEW battlefield (see <see cref="PlanRemapper.StripSceneAnchors"/>).</summary>
    public sealed class CarryResult
    {
        public CarryResult(BattlePlan plan, IReadOnlyList<string> removedAnchors, int removedStages,
            IReadOnlyList<PlannedFormationClass> emptiedFormations)
        {
            Plan = plan;
            RemovedAnchors = removedAnchors;
            RemovedStages = removedStages;
            EmptiedFormations = emptiedFormations;
        }

        /// <summary>The carried plan (a fresh copy), with map-specific parts removed.</summary>
        public BattlePlan Plan { get; }

        /// <summary>Scene-basis anchor ids that were dropped (their coordinates belong to the previous map).</summary>
        public IReadOnlyList<string> RemovedAnchors { get; }

        /// <summary>Stages removed because they were wired (directive or trigger) to a dropped anchor.</summary>
        public int RemovedStages { get; }

        /// <summary>Formations whose whole plan was map-specific and got emptied (removed from the plan).</summary>
        public IReadOnlyList<PlannedFormationClass> EmptiedFormations { get; }

        public bool Changed => RemovedAnchors.Count > 0;
    }

    public static class PlanRemapper
    {
        /// <summary>
        /// Carry hygiene (A3.9): Scene-basis anchors are ABSOLUTE coordinates of
        /// the battlefield they were clicked on — on the next map they'd draw
        /// phantom markers and march formations to meaningless spots (the classic
        /// trust-killer). Drop them, plus every stage whose directive or trigger
        /// references one (a later stage with an emptied trigger would be
        /// unreachable dead weight anyway), plus any formation left stage-less.
        /// Relative anchors (OwnStart/TeamCenter) adapt automatically and pass
        /// through untouched. Absolute→relative CONVERSION is a Phase-3 design
        /// (needs the original deployment reference) — do not attempt it here.
        /// </summary>
        public static CarryResult StripSceneAnchors(BattlePlan previous)
        {
            var copy = PlanSerializer.DeepCopy(previous) ?? new BattlePlan();

            var sceneIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (var i = copy.Anchors.Count - 1; i >= 0; i--)
            {
                if (copy.Anchors[i].Basis != AnchorBasis.Scene)
                    continue;
                sceneIds.Add(copy.Anchors[i].Id);
                copy.Anchors.RemoveAt(i);
            }

            var removedStages = 0;
            var emptied = new List<PlannedFormationClass>();
            if (sceneIds.Count > 0)
            {
                for (var f = copy.Formations.Count - 1; f >= 0; f--)
                {
                    var formation = copy.Formations[f];
                    for (var s = formation.Stages.Count - 1; s >= 0; s--)
                    {
                        if (!StageReferencesAny(formation.Stages[s], sceneIds))
                            continue;
                        formation.Stages.RemoveAt(s);
                        removedStages++;
                    }
                    if (formation.Stages.Count == 0)
                    {
                        emptied.Add(formation.Formation);
                        copy.Formations.RemoveAt(f);
                    }
                }
                emptied = emptied.OrderBy(d => (int)d).ToList();
            }

            return new CarryResult(copy, sceneIds.OrderBy(id => id, System.StringComparer.OrdinalIgnoreCase).ToList(),
                removedStages, emptied);
        }

        private static bool StageReferencesAny(Stage stage, HashSet<string> anchorIds)
        {
            if (stage.Do != null)
            {
                if (stage.Do.Anchor != null && anchorIds.Contains(stage.Do.Anchor))
                    return true;
                if (stage.Do.Path != null)
                    foreach (var waypoint in stage.Do.Path)
                        if (waypoint != null && anchorIds.Contains(waypoint))
                            return true;
            }
            foreach (var condition in stage.When)
            {
                if (condition?.Anchor != null && anchorIds.Contains(condition.Anchor))
                    return true;
            }
            return false;
        }

        public static RemapResult RemapToFormations(BattlePlan previous, IEnumerable<PlannedFormationClass> available)
        {
            var present = new HashSet<PlannedFormationClass>(available ?? Enumerable.Empty<PlannedFormationClass>());
            var copy = PlanSerializer.DeepCopy(previous) ?? new BattlePlan();

            var dropped = new List<PlannedFormationClass>();
            for (var i = copy.Formations.Count - 1; i >= 0; i--)
            {
                if (present.Contains(copy.Formations[i].Formation))
                    continue;
                dropped.Add(copy.Formations[i].Formation);
                copy.Formations.RemoveAt(i);
            }

            // Report drops in formation-slot order (the iteration above is reverse).
            dropped = dropped.OrderBy(d => (int)d).ToList();
            return new RemapResult(copy, dropped);
        }
    }
}
