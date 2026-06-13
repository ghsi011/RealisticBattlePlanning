using System.Collections.Generic;
using System.Linq;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Planning.Editing
{
    /// <summary>
    /// The editor's logic layer (spec A3, Planning Mode): an in-progress plan
    /// plus the safe mutation operations the UI drives — add/remove/reorder
    /// stages, set triggers and directives, declare signals and anchors. All
    /// engine-free and unit-tested; the Gauntlet editor is a thin view over
    /// this, so authoring behavior is verified without the game. Operations
    /// are bounds-checked and no-op rather than throw, so a misclick in the UI
    /// can never crash the mission.
    /// </summary>
    public sealed class PlanDraft
    {
        private readonly BattlePlan _plan;

        public PlanDraft(BattlePlan plan = null)
        {
            _plan = plan ?? new BattlePlan();
        }

        /// <summary>The formation classes that already have a plan.</summary>
        public IReadOnlyList<PlannedFormationClass> Formations
            => _plan.Formations.Select(f => f.Formation).ToList();

        public IReadOnlyList<string> PlayerSignals => _plan.PlayerSignals;

        /// <summary>
        /// Adds a plan for a formation, seeded with the default opening stage
        /// so it is valid immediately (A3.9). No-ops if it already has one.
        /// </summary>
        public PlanDraft AddFormation(PlannedFormationClass formation)
        {
            if (Find(formation) == null)
                _plan.Formations.Add(new FormationPlan
                {
                    Formation = formation,
                    Stages = { EditorDefaults.OpeningStage() },
                });
            return this;
        }

        public PlanDraft RemoveFormation(PlannedFormationClass formation)
        {
            var plan = Find(formation);
            if (plan != null)
                _plan.Formations.Remove(plan);
            return this;
        }

        /// <summary>Appends a stage (defaults to a hold) to a formation, adding the formation if needed.</summary>
        public PlanDraft AddStage(PlannedFormationClass formation, Stage stage = null)
        {
            var plan = Find(formation) ?? AddAndReturn(formation);
            plan.Stages.Add(stage ?? EditorDefaults.OpeningStage());
            return this;
        }

        public PlanDraft RemoveStage(PlannedFormationClass formation, int index)
        {
            var plan = Find(formation);
            if (plan != null && index >= 0 && index < plan.Stages.Count)
                plan.Stages.RemoveAt(index);
            return this;
        }

        /// <summary>Reorders a stage. Out-of-range indices are clamped; identical positions no-op.</summary>
        public PlanDraft MoveStage(PlannedFormationClass formation, int from, int to)
        {
            var plan = Find(formation);
            if (plan == null)
                return this;

            var count = plan.Stages.Count;
            if (from < 0 || from >= count)
                return this;
            if (to < 0) to = 0;
            if (to >= count) to = count - 1;
            if (from == to)
                return this;

            var stage = plan.Stages[from];
            plan.Stages.RemoveAt(from);
            plan.Stages.Insert(to, stage);
            return this;
        }

        public PlanDraft SetTrigger(PlannedFormationClass formation, int stageIndex, params TriggerSpec[] conditions)
        {
            var stage = StageAt(formation, stageIndex);
            if (stage != null)
            {
                stage.When.Clear();
                stage.When.AddRange(conditions.Where(c => c != null));
            }
            return this;
        }

        public PlanDraft SetDirective(PlannedFormationClass formation, int stageIndex, DirectiveSpec directive)
        {
            var stage = StageAt(formation, stageIndex);
            if (stage != null && directive != null)
                stage.Do = directive;
            return this;
        }

        public PlanDraft EmitSignal(PlannedFormationClass formation, int stageIndex, string signal)
        {
            var stage = StageAt(formation, stageIndex);
            if (stage != null && !string.IsNullOrWhiteSpace(signal) && !stage.Emit.Contains(signal))
                stage.Emit.Add(signal);
            return this;
        }

        /// <summary>Declares a player signal (Signal Palette, B9). Caps at 4 (A3.5/B9); duplicates ignored.</summary>
        public PlanDraft DeclarePlayerSignal(string signal)
        {
            if (!string.IsNullOrWhiteSpace(signal)
                && _plan.PlayerSignals.Count < 4
                && !_plan.PlayerSignals.Contains(signal))
                _plan.PlayerSignals.Add(signal);
            return this;
        }

        public PlanDraft AddAnchor(MapAnchor anchor)
        {
            if (anchor != null && !string.IsNullOrWhiteSpace(anchor.Id)
                && _plan.Anchors.All(a => !string.Equals(a.Id, anchor.Id, System.StringComparison.OrdinalIgnoreCase)))
                _plan.Anchors.Add(anchor);
            return this;
        }

        /// <summary>Live feasibility feedback for the editor (A3.8): non-blocking.</summary>
        public PlanValidationResult Validate() => PlanValidator.Validate(_plan);

        /// <summary>Plain-language plan summary for the editor panel (R4).</summary>
        public string Summary() => PlanFormatter.Describe(_plan);

        /// <summary>The plan being authored. Mission-scoped; handed to the monitor on battle start.</summary>
        public BattlePlan Build() => _plan;

        private FormationPlan Find(PlannedFormationClass formation)
            => _plan.Formations.FirstOrDefault(f => f.Formation == formation);

        private FormationPlan AddAndReturn(PlannedFormationClass formation)
        {
            var plan = new FormationPlan { Formation = formation };
            _plan.Formations.Add(plan);
            return plan;
        }

        private Stage StageAt(PlannedFormationClass formation, int index)
        {
            var plan = Find(formation);
            return plan != null && index >= 0 && index < plan.Stages.Count ? plan.Stages[index] : null;
        }
    }
}
