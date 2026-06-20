using System.Collections.Generic;
using System.Linq;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Planning.Editing
{
    /// <summary>One row of the KSP-style stage rail (spec A2.6.4/A2.6.5).</summary>
    public sealed class StageRailRow
    {
        /// <summary>Stage index (0-based), its execution order in the plan.</summary>
        public int Index { get; set; }

        /// <summary>Plain-language one-line summary ("When … → Do …") of the stage shown for this row.</summary>
        public string Summary { get; set; }

        /// <summary>True when this stage is identical across every selected formation
        /// (A2.6.5) — the rail renders these in full color (edit once), divergent rows dimmed.
        /// Trivially true for a single selection.</summary>
        public bool SharedAcrossSelection { get; set; }

        /// <summary>True when every selected formation has a stage at this row.</summary>
        public bool PresentInAll { get; set; }

        /// <summary>The stage's directive type (for the rail's compact action icon); null if empty.</summary>
        public DirectiveType? Directive { get; set; }
    }

    /// <summary>
    /// Builds the stage-rail view (spec A2.6.4): the engine-free shape the Gauntlet rail
    /// binds to. The rail follows the current selection, aligns stages by index, summarizes
    /// each, and flags which rows are shared across a multi-selection (A2.6.5). Reordering a
    /// row drags the stage in every selected formation. Logic lives here so the rail is a
    /// thin, tested front-end.
    /// </summary>
    public static class StageRail
    {
        public static IReadOnlyList<StageRailRow> Build(PlanDraft draft, IReadOnlyList<PlannedFormationClass> selection)
        {
            var rows = new List<StageRailRow>();
            if (draft == null || selection == null || selection.Count == 0)
                return rows;

            var stageLists = selection.Select(draft.StagesOf).ToList();
            var shared = StageComparison.SharedRows(stageLists);
            var rowCount = stageLists.Max(s => s.Count);

            for (var i = 0; i < rowCount; i++)
            {
                // Summarize the primary (first-selected) formation's stage at this row,
                // else the first selection that has one.
                var stage = stageLists.Select(s => i < s.Count ? s[i] : null).FirstOrDefault(s => s != null);
                rows.Add(new StageRailRow
                {
                    Index = i,
                    Summary = stage != null
                        ? $"{PlanFormatter.DescribeWhen(stage, i)} → {PlanFormatter.DescribeDirective(stage.Do)}"
                        : "(none)",
                    Directive = stage?.Do?.Type,
                    SharedAcrossSelection = i < shared.Count && shared[i],
                    PresentInAll = stageLists.All(s => i < s.Count),
                });
            }
            return rows;
        }

        /// <summary>Drags a rail row from one position to another across every selected
        /// formation (A2.6.4 reorder), so the multi-select rail reorders all at once.</summary>
        public static void ReorderRow(PlanDraft draft, IReadOnlyList<PlannedFormationClass> selection, int from, int to)
        {
            if (draft == null || selection == null)
                return;
            foreach (var formation in selection)
                draft.MoveStage(formation, from, to);
        }
    }
}
