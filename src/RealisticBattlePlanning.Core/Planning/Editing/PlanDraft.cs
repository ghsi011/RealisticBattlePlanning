using System;
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

        /// <summary>
        /// A draft over a deep copy of <paramref name="plan"/>, so the editor's
        /// in-progress edits never touch the live plan until the caller commits
        /// the built result. Round-trips through <see cref="PlanSerializer"/>;
        /// falls back to an empty draft if the plan can't be cloned.
        /// </summary>
        public static PlanDraft EditingCopyOf(BattlePlan plan)
        {
            if (plan == null)
                return new PlanDraft();
            try { return new PlanDraft(PlanSerializer.DeepCopy(plan)); }
            catch { return new PlanDraft(); } // a broken plan must never crash the editor (G3)
        }

        /// <summary>The formation classes that already have a plan.</summary>
        public IReadOnlyList<PlannedFormationClass> Formations
            => _plan.Formations.Select(f => f.Formation).ToList();

        /// <summary>A formation's stages in order (a snapshot list; the Stage objects are
        /// live — read them for display/the stage rail, but mutate only through this
        /// draft's methods). Empty if the formation has no plan.</summary>
        public IReadOnlyList<Stage> StagesOf(PlannedFormationClass formation)
            => Find(formation)?.Stages.ToList() ?? new List<Stage>();

        // Snapshots, not the live backing lists (matches Formations): callers can't
        // cast back to List and bypass the dedup/cap/blank-id invariants.
        public IReadOnlyList<string> PlayerSignals => _plan.PlayerSignals.ToList();

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

        /// <summary>
        /// Appends a stage (defaults to a hold) to a formation, adding the
        /// formation if needed. Note: unlike <see cref="AddFormation"/>, a
        /// formation first created via AddStage is NOT seeded with an opening
        /// stage — the appended stage is its first, so one-click patterns
        /// produce exactly their own stages.
        /// </summary>
        public PlanDraft AddStage(PlannedFormationClass formation, Stage stage = null)
        {
            var plan = Find(formation) ?? AddAndReturn(formation);
            plan.Stages.Add(stage ?? EditorDefaults.OpeningStage());
            return this;
        }

        /// <summary>
        /// Authors one stage across several formations at once (A3.6: "instruct
        /// the two horse-archer commanders together"). The factory is invoked
        /// once per formation, so each gets its own independent stage —
        /// editing one later never touches the others. Formations that don't
        /// exist yet are created. Null factory/list is a no-op.
        /// </summary>
        public PlanDraft AddStageToEach(IEnumerable<PlannedFormationClass> formations, Func<Stage> stageFactory)
        {
            if (formations == null || stageFactory == null)
                return this;
            foreach (var formation in formations)
                AddStage(formation, stageFactory());
            return this;
        }

        public PlanDraft RemoveStage(PlannedFormationClass formation, int index)
        {
            var plan = Find(formation);
            if (plan != null && index >= 0 && index < plan.Stages.Count)
                plan.Stages.RemoveAt(index);
            return this;
        }

        /// <summary>Inserts a deep copy of a stage immediately after it (so the player
        /// can author one stage then tweak a clone). Bounds-checked; out-of-range or
        /// absent-formation no-op. The copy round-trips through JSON, so its nested
        /// trigger/directive/emit state is fully independent of the original.</summary>
        public PlanDraft DuplicateStage(PlannedFormationClass formation, int index)
        {
            var plan = Find(formation);
            if (plan == null || index < 0 || index >= plan.Stages.Count)
                return this;
            plan.Stages.Insert(index + 1, PlanSerializer.DeepCopy(plan.Stages[index]));
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

        /// <summary>Bulk-replaces a stage's trigger conditions. Null array is a no-op
        /// (the no-throw contract); caps at <see cref="MaxTriggerConditions"/> (A3.5).</summary>
        public PlanDraft SetTrigger(PlannedFormationClass formation, int stageIndex, params TriggerSpec[] conditions)
        {
            var stage = StageAt(formation, stageIndex);
            if (stage != null && conditions != null)
            {
                stage.When.Clear();
                stage.When.AddRange(conditions.Where(c => c != null).Take(MaxTriggerConditions));
            }
            return this;
        }

        /// <summary>Max atomic conditions ANDed in one stage's trigger (A3.5).</summary>
        public const int MaxTriggerConditions = 3;

        /// <summary>Appends one ANDed condition to a stage's trigger (A3.5). No-ops if
        /// the stage is missing, the condition is null, or it's already at the cap.</summary>
        public PlanDraft AddTriggerCondition(PlannedFormationClass formation, int stageIndex, TriggerSpec condition)
        {
            var stage = StageAt(formation, stageIndex);
            if (stage != null && condition != null && stage.When.Count < MaxTriggerConditions)
                stage.When.Add(condition);
            return this;
        }

        /// <summary>Replaces one of a stage's ANDed conditions. Bounds-checked; null/out-of-range no-op.</summary>
        public PlanDraft SetTriggerCondition(PlannedFormationClass formation, int stageIndex, int conditionIndex, TriggerSpec condition)
        {
            var stage = StageAt(formation, stageIndex);
            if (stage != null && condition != null && conditionIndex >= 0 && conditionIndex < stage.When.Count)
                stage.When[conditionIndex] = condition;
            return this;
        }

        /// <summary>Removes one of a stage's ANDed conditions. Bounds-checked; out-of-range no-op.
        /// Leaving a non-opening stage with no condition is invalid — PlanValidator flags it.</summary>
        public PlanDraft RemoveTriggerCondition(PlannedFormationClass formation, int stageIndex, int conditionIndex)
        {
            var stage = StageAt(formation, stageIndex);
            if (stage != null && conditionIndex >= 0 && conditionIndex < stage.When.Count)
                stage.When.RemoveAt(conditionIndex);
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
            if (stage != null && !string.IsNullOrWhiteSpace(signal)
                && !stage.Emit.Any(s => string.Equals(s, signal, System.StringComparison.OrdinalIgnoreCase)))
                stage.Emit.Add(signal);
            return this;
        }

        /// <summary>Stops a stage from broadcasting <paramref name="signal"/> (case-insensitive). No-ops if absent.</summary>
        public PlanDraft RemoveEmitSignal(PlannedFormationClass formation, int stageIndex, string signal)
        {
            var stage = StageAt(formation, stageIndex);
            stage?.Emit.RemoveAll(s => string.Equals(s, signal, System.StringComparison.OrdinalIgnoreCase));
            return this;
        }

        /// <summary>
        /// Sets a formation's abort conditions (A3.7); only the provided values
        /// change. The casualty threshold is clamped to the valid (0, 100]
        /// range so the draft stays valid (A3.9). No-ops if the formation has
        /// no plan. (OnCommanderIncapacitated is reserved — Phase 1 always
        /// aborts on commander death — but is editable for forward-compat.)
        /// </summary>
        public PlanDraft SetAbortConditions(
            PlannedFormationClass formation,
            float? casualtiesAbovePercent = null,
            bool? onFormationBroken = null,
            bool? onCommanderIncapacitated = null)
        {
            var plan = Find(formation);
            if (plan == null)
                return this;
            if (casualtiesAbovePercent is { } pct)
                plan.Abort.CasualtiesAbovePercent = System.Math.Max(1f, System.Math.Min(100f, pct));
            if (onFormationBroken is { } broken)
                plan.Abort.OnFormationBroken = broken;
            if (onCommanderIncapacitated is { } incapacitated)
                plan.Abort.OnCommanderIncapacitated = incapacitated;
            return this;
        }

        /// <summary>Declares a player signal (Signal Palette, B9). Caps at 4 (A3.5/B9); duplicates ignored.</summary>
        public PlanDraft DeclarePlayerSignal(string signal)
        {
            if (!string.IsNullOrWhiteSpace(signal)
                && _plan.PlayerSignals.Count < 4
                && !_plan.PlayerSignals.Any(s => string.Equals(s, signal, System.StringComparison.OrdinalIgnoreCase)))
                _plan.PlayerSignals.Add(signal);
            return this;
        }

        /// <summary>Removes a declared player signal (case-insensitive). No-ops if absent.
        /// Stages that still reference it are left dangling — PlanValidator flags those.</summary>
        public PlanDraft RemovePlayerSignal(string signal)
        {
            _plan.PlayerSignals.RemoveAll(s => string.Equals(s, signal, System.StringComparison.OrdinalIgnoreCase));
            return this;
        }

        public PlanDraft AddAnchor(MapAnchor anchor)
        {
            if (anchor != null && !string.IsNullOrWhiteSpace(anchor.Id)
                && _plan.Anchors.All(a => !string.Equals(a.Id, anchor.Id, System.StringComparison.OrdinalIgnoreCase)))
                _plan.Anchors.Add(anchor);
            return this;
        }

        /// <summary>The map anchors referenced by triggers/directives (a snapshot).</summary>
        public IReadOnlyList<MapAnchor> Anchors => _plan.Anchors.ToList();

        /// <summary>Removes a map anchor by id (case-insensitive). No-ops if absent.
        /// Stages still referencing it are left dangling — PlanValidator flags those.</summary>
        public PlanDraft RemoveAnchor(string id)
        {
            _plan.Anchors.RemoveAll(a => string.Equals(a.Id, id, System.StringComparison.OrdinalIgnoreCase));
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
