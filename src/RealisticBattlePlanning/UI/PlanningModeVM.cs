using System;
using System.Collections.Generic;
using System.Linq;
using RealisticBattlePlanning.Planning;
using RealisticBattlePlanning.Planning.Editing;
using RealisticBattlePlanning.Planning.Model;
using TaleWorlds.Library;

namespace RealisticBattlePlanning.UI
{
    /// <summary>
    /// Gauntlet datasource for the Planning Mode editor. Wraps Core's PlanDraft
    /// (the tested mutation layer) and exposes it as a structured, styled,
    /// editable view: a header, one card per formation (its abort rule + its
    /// stages as "When → Do" rows whose trigger/directive cycle on click), plus
    /// add/remove-stage controls, and a footer with signals, live validation,
    /// and Apply/Close. Every edit mutates the draft and re-renders; Apply hands
    /// the built plan back to the mission so this battle runs it. Per-element
    /// wording comes from Core's PlanFormatter (the single tested source of
    /// truth); cycling sets a valid default spec for the next type so the plan
    /// stays applyable mid-edit.
    /// </summary>
    public sealed class PlanningModeVM : ViewModel
    {
        private readonly PlanDraft _draft;
        private readonly Action<BattlePlan> _onApply;
        private readonly Action _onClose;
        // Composition label per slot for the formations that actually have troops
        // (e.g. {Infantry: "Ranged-Infantry"}). Empty/absent slots are not keys.
        private readonly Dictionary<PlannedFormationClass, string> _compositionLabels;

        private string _titleText;
        private string _hintText;
        private string _signalsText;
        private string _statusText;
        private string _warningsText;
        private string _errorsText;
        private string _addFormationText;
        private string _emptyText;
        private bool _hasWarnings;
        private bool _hasErrors;
        private bool _hasStatus;
        private bool _hasPlan;
        private bool _isAddFormationDisabled;
        private MBBindingList<FormationPlanItemVM> _formations;

        public PlanningModeVM(string title, string hint, PlanDraft draft, Action<BattlePlan> onApply, Action onClose,
            Dictionary<PlannedFormationClass, string> compositionLabels = null)
        {
            _titleText = title;
            _hintText = hint;
            _draft = draft;
            _onApply = onApply;
            _onClose = onClose;
            _compositionLabels = compositionLabels ?? new Dictionary<PlannedFormationClass, string>();
            _formations = new MBBindingList<FormationPlanItemVM>();
            Refresh();
        }

        /// <summary>Formation slot number (1-8) — the deployment formation index + 1.</summary>
        private static int SlotNumber(PlannedFormationClass cls) => (int)cls + 1;

        /// <summary>Header for a formation card: "3 — Ranged-Infantry" (composition label
        /// when troops are known, otherwise the slot's class name).</summary>
        private string HeaderFor(PlannedFormationClass cls)
        {
            var label = _compositionLabels.TryGetValue(cls, out var l) ? l : cls.ToString();
            return $"{SlotNumber(cls)}   —   {label}";
        }

        private void Refresh()
        {
            var plan = _draft.Build();
            _formations.Clear();
            foreach (var formation in plan.Formations)
                _formations.Add(new FormationPlanItemVM(formation, HeaderFor(formation.Formation), AddStage, RemoveStage, CycleTrigger, CycleDirective));

            HasPlan = plan.Formations.Count > 0;
            EmptyText = HasPlan ? "" :
                "No formations planned yet.\nAdd a formation, give it stages (click When / Do to change them), then Apply.";

            SignalsText = plan.PlayerSignals.Count > 0
                ? "SIGNALS    " + string.Join("     ", plan.PlayerSignals.Select(s => $"[ {s} ]"))
                : "";

            // Surface BOTH errors (red, blocks Apply) and warnings (amber, informational)
            // live as the player edits, so cycling into an un-appliable type (e.g. a
            // Position Reached trigger with no anchor defined) is visibly flagged
            // instead of silently failing at Apply.
            var validation = PlanValidator.Validate(plan);
            HasErrors = validation.Errors.Count > 0;
            ErrorsText = HasErrors ? $"{validation.Errors.Count} error(s):   " + string.Join("      ", validation.Errors) : "";
            HasWarnings = validation.Warnings.Count > 0;
            WarningsText = HasWarnings ? $"{validation.Warnings.Count} warning(s):   " + string.Join("      ", validation.Warnings) : "";

            var next = NextUnplannedClass(plan);
            IsAddFormationDisabled = next == null;
            AddFormationText = next is { } n ? $"+  Add Formation {SlotNumber(n)}" : "All formations planned";
        }

        // The next slot to offer "Add Formation" for: the lowest-numbered slot
        // that has troops but no plan yet. Falls back to every slot when there is
        // no live composition data (engine read failed / not in a mission), so the
        // editor never locks out adding.
        private PlannedFormationClass? NextUnplannedClass(BattlePlan plan)
        {
            var planned = new HashSet<PlannedFormationClass>(plan.Formations.Select(f => f.Formation));
            var candidates = _compositionLabels.Count > 0
                ? _compositionLabels.Keys.AsEnumerable()
                : Enum.GetValues(typeof(PlannedFormationClass)).Cast<PlannedFormationClass>();
            foreach (var c in candidates.OrderBy(c => (int)c))
                if (!planned.Contains(c))
                    return c;
            return null;
        }

        private void AddStage(PlannedFormationClass cls)
        {
            _draft.AddStage(cls);
            // A non-first stage with no trigger is invalid (only stage 1 may omit
            // it). Seed a valid default so the plan stays applyable; the player
            // cycles When/Do from there.
            var formation = _draft.Build().Formations.FirstOrDefault(f => f.Formation == cls);
            if (formation != null && formation.Stages.Count > 1)
                _draft.SetTrigger(cls, formation.Stages.Count - 1, new TriggerSpec { Type = TriggerType.EnemyCommits });
            StatusText = $"Added a stage to Formation {SlotNumber(cls)}.";
            Refresh();
        }

        private void RemoveStage(PlannedFormationClass cls)
        {
            var formation = _draft.Build().Formations.FirstOrDefault(f => f.Formation == cls);
            if (formation == null || formation.Stages.Count == 0)
                return;
            _draft.RemoveStage(cls, formation.Stages.Count - 1);
            StatusText = $"Removed a stage from {cls}.";
            Refresh();
        }

        // Cycle a stage's trigger ("When") to the next TriggerType, applying a
        // valid default spec for it (using the plan's anchors/signals where the
        // type needs them). Replaces the stage's conditions with the single new
        // one — single-condition editing for v1.
        private void CycleTrigger(PlannedFormationClass cls, int stageIndex)
        {
            var plan = _draft.Build();
            var stage = StageAt(plan, cls, stageIndex);
            if (stage == null)
                return;
            var current = stage.When.Count > 0 ? stage.When[0].Type : TriggerType.BattleStart;
            var next = (TriggerType)NextEnumValue(typeof(TriggerType), current);
            _draft.SetTrigger(cls, stageIndex, DefaultTrigger(next, plan));
            StatusText = $"{cls} stage {stageIndex + 1}: When → {Spaced(next.ToString())}.";
            Refresh();
        }

        // Cycle a stage's directive ("Do") to the next DirectiveType, applying a
        // valid default spec for it.
        private void CycleDirective(PlannedFormationClass cls, int stageIndex)
        {
            var plan = _draft.Build();
            var stage = StageAt(plan, cls, stageIndex);
            if (stage == null)
                return;
            var current = stage.Do?.Type ?? DirectiveType.Hold;
            var next = (DirectiveType)NextEnumValue(typeof(DirectiveType), current);
            _draft.SetDirective(cls, stageIndex, DefaultDirective(next, plan));
            StatusText = $"{cls} stage {stageIndex + 1}: Do → {Spaced(next.ToString())}.";
            Refresh();
        }

        private static Stage StageAt(BattlePlan plan, PlannedFormationClass cls, int stageIndex)
        {
            var formation = plan.Formations.FirstOrDefault(f => f.Formation == cls);
            return formation != null && stageIndex >= 0 && stageIndex < formation.Stages.Count
                ? formation.Stages[stageIndex]
                : null;
        }

        private static object NextEnumValue(Type enumType, object current)
        {
            var values = Enum.GetValues(enumType);
            var idx = Array.IndexOf(values, current);
            return values.GetValue((idx + 1) % values.Length);
        }

        /// <summary>A valid default trigger for <paramref name="type"/>, filling in
        /// the parameters PlanValidator requires (anchors/signals taken from the
        /// plan when the type needs them; if none exist the field is left blank
        /// and the footer flags it).</summary>
        private static TriggerSpec DefaultTrigger(TriggerType type, BattlePlan plan)
        {
            var anchor = plan.Anchors.FirstOrDefault()?.Id;
            var signal = plan.PlayerSignals.FirstOrDefault();
            switch (type)
            {
                case TriggerType.EnemyWithinDistance: return new TriggerSpec { Type = type, Meters = 40f };
                case TriggerType.FriendlyWithinDistance: return new TriggerSpec { Type = type, Meters = 40f, Formation = "Player" };
                case TriggerType.PositionReached: return new TriggerSpec { Type = type, Anchor = anchor };
                case TriggerType.CasualtiesAbove: return new TriggerSpec { Type = type, Percent = 30f };
                case TriggerType.TimerElapsed: return new TriggerSpec { Type = type, Seconds = 30f };
                case TriggerType.SignalReceived: return new TriggerSpec { Type = type, Signal = signal ?? "advance" };
                case TriggerType.PlayerSignal: return new TriggerSpec { Type = type, Signal = signal };
                default: return new TriggerSpec { Type = type }; // BattleStart, EnemyCommits, EnemyBroken
            }
        }

        /// <summary>A valid default directive for <paramref name="type"/>.</summary>
        private static DirectiveSpec DefaultDirective(DirectiveType type, BattlePlan plan)
        {
            var anchor = plan.Anchors.FirstOrDefault()?.Id;
            switch (type)
            {
                case DirectiveType.MoveTo: return new DirectiveSpec { Type = type, Anchor = anchor };
                case DirectiveType.Skirmish: return new DirectiveSpec { Type = type, Target = "Nearest" };
                case DirectiveType.FeignRetreat: return new DirectiveSpec { Type = type, Anchor = anchor };
                case DirectiveType.FlankArc: return new DirectiveSpec { Type = type, Side = FlankSide.Left };
                case DirectiveType.PullBack: return new DirectiveSpec { Type = type, Anchor = anchor };
                case DirectiveType.Screen: return new DirectiveSpec { Type = type, Target = "Player" };
                case DirectiveType.Follow: return new DirectiveSpec { Type = type, Target = "Player" };
                case DirectiveType.FireControl: return new DirectiveSpec { Type = type, Fire = FireMode.Free };
                case DirectiveType.Hold: return new DirectiveSpec { Type = type, Arrangement = Arrangement.Line };
                default: return new DirectiveSpec { Type = type }; // Charge
            }
        }

        /// <summary>"EnemyWithinDistance" → "Enemy Within Distance" for status text.</summary>
        private static string Spaced(string pascal)
            => string.Concat(pascal.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));

        public void ExecuteAddFormation()
        {
            if (NextUnplannedClass(_draft.Build()) is { } next)
            {
                _draft.AddFormation(next);
                StatusText = $"Added Formation {SlotNumber(next)} (with a default opening stage).";
                Refresh();
            }
        }

        public void ExecuteApply()
        {
            var plan = _draft.Build();
            var validation = PlanValidator.Validate(plan);
            if (!validation.IsValid)
            {
                // Name the actual blocker (the footer also lists them all in red).
                StatusText = "Can't apply — " + validation.Errors[0];
                return;
            }
            _onApply?.Invoke(plan);
            // Close on success: re-Applying would rebuild the monitor and reset
            // live formation state. The player reopens to edit further.
            _onClose?.Invoke();
        }

        public void ExecuteClose() => _onClose?.Invoke();

        [DataSourceProperty] public bool HasPlan { get => _hasPlan; set { if (value != _hasPlan) { _hasPlan = value; OnPropertyChangedWithValue(value, "HasPlan"); } } }
        [DataSourceProperty] public bool HasWarnings { get => _hasWarnings; set { if (value != _hasWarnings) { _hasWarnings = value; OnPropertyChangedWithValue(value, "HasWarnings"); } } }
        [DataSourceProperty] public bool HasErrors { get => _hasErrors; set { if (value != _hasErrors) { _hasErrors = value; OnPropertyChangedWithValue(value, "HasErrors"); } } }
        [DataSourceProperty] public bool HasStatus { get => _hasStatus; set { if (value != _hasStatus) { _hasStatus = value; OnPropertyChangedWithValue(value, "HasStatus"); } } }
        [DataSourceProperty] public bool IsAddFormationDisabled { get => _isAddFormationDisabled; set { if (value != _isAddFormationDisabled) { _isAddFormationDisabled = value; OnPropertyChangedWithValue(value, "IsAddFormationDisabled"); } } }
        [DataSourceProperty] public string TitleText { get => _titleText; set { if (value != _titleText) { _titleText = value; OnPropertyChangedWithValue(value, "TitleText"); } } }
        [DataSourceProperty] public string HintText { get => _hintText; set { if (value != _hintText) { _hintText = value; OnPropertyChangedWithValue(value, "HintText"); } } }
        [DataSourceProperty] public string SignalsText { get => _signalsText; set { if (value != _signalsText) { _signalsText = value; OnPropertyChangedWithValue(value, "SignalsText"); } } }
        [DataSourceProperty] public string WarningsText { get => _warningsText; set { if (value != _warningsText) { _warningsText = value; OnPropertyChangedWithValue(value, "WarningsText"); } } }
        [DataSourceProperty] public string ErrorsText { get => _errorsText; set { if (value != _errorsText) { _errorsText = value; OnPropertyChangedWithValue(value, "ErrorsText"); } } }
        [DataSourceProperty] public string EmptyText { get => _emptyText; set { if (value != _emptyText) { _emptyText = value; OnPropertyChangedWithValue(value, "EmptyText"); } } }
        [DataSourceProperty] public string AddFormationText { get => _addFormationText; set { if (value != _addFormationText) { _addFormationText = value; OnPropertyChangedWithValue(value, "AddFormationText"); } } }
        [DataSourceProperty] public string StatusText { get => _statusText; set { if (value != _statusText) { _statusText = value; OnPropertyChangedWithValue(value, "StatusText"); HasStatus = !string.IsNullOrEmpty(value); } } }
        [DataSourceProperty] public MBBindingList<FormationPlanItemVM> Formations { get => _formations; set { if (value != _formations) { _formations = value; OnPropertyChangedWithValue(value, "Formations"); } } }
    }

    /// <summary>One formation's card: name, abort rule, its stage rows, and add/remove-stage controls.</summary>
    public sealed class FormationPlanItemVM : ViewModel
    {
        private readonly PlannedFormationClass _formation;
        private readonly Action<PlannedFormationClass> _addStage;
        private readonly Action<PlannedFormationClass> _removeStage;

        public FormationPlanItemVM(
            FormationPlan formation,
            string headerText,
            Action<PlannedFormationClass> addStage,
            Action<PlannedFormationClass> removeStage,
            Action<PlannedFormationClass, int> cycleTrigger,
            Action<PlannedFormationClass, int> cycleDirective)
        {
            _formation = formation.Formation;
            _addStage = addStage;
            _removeStage = removeStage;
            NameText = headerText;
            AbortText = PlanFormatter.DescribeAbort(formation.Abort);
            CanRemove = formation.Stages.Count > 0;
            IsRemoveDisabled = !CanRemove;
            Stages = new MBBindingList<StageItemVM>();
            for (var i = 0; i < formation.Stages.Count; i++)
            {
                var index = i; // capture for the per-stage cycle closures
                var cls = _formation;
                Stages.Add(new StageItemVM(
                    i, formation.Stages[i],
                    () => cycleTrigger?.Invoke(cls, index),
                    () => cycleDirective?.Invoke(cls, index)));
            }
        }

        public void ExecuteAddStage() => _addStage?.Invoke(_formation);
        public void ExecuteRemoveStage() => _removeStage?.Invoke(_formation);

        [DataSourceProperty] public string NameText { get; }
        [DataSourceProperty] public string AbortText { get; }
        [DataSourceProperty] public bool CanRemove { get; }
        [DataSourceProperty] public bool IsRemoveDisabled { get; }
        [DataSourceProperty] public MBBindingList<StageItemVM> Stages { get; }
    }

    /// <summary>One stage row: number, the trigger ("When") and directive ("Do") as
    /// click-to-cycle fields, and any emitted signals.</summary>
    public sealed class StageItemVM : ViewModel
    {
        private readonly Action _cycleTrigger;
        private readonly Action _cycleDirective;

        public StageItemVM(int index, Stage stage, Action cycleTrigger, Action cycleDirective)
        {
            _cycleTrigger = cycleTrigger;
            _cycleDirective = cycleDirective;
            NumberText = (index + 1).ToString();
            TriggerText = "When:  " + PlanFormatter.DescribeWhen(stage, index);
            DirectiveText = "Do:  " + PlanFormatter.DescribeDirective(stage.Do);
            EmitText = stage.Emit.Count > 0 ? "→ signals  " + string.Join(", ", stage.Emit) : "";
            HasEmit = stage.Emit.Count > 0;
        }

        public void ExecuteCycleTrigger() => _cycleTrigger?.Invoke();
        public void ExecuteCycleDirective() => _cycleDirective?.Invoke();

        [DataSourceProperty] public string NumberText { get; }
        [DataSourceProperty] public string TriggerText { get; }
        [DataSourceProperty] public string DirectiveText { get; }
        [DataSourceProperty] public string EmitText { get; }
        [DataSourceProperty] public bool HasEmit { get; }
    }
}
