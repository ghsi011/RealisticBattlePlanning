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
    public static class PlanRemapper
    {
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
