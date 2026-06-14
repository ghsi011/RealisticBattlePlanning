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
    /// stages as "When → Do" rows, with add/remove-stage controls), and a
    /// footer with signals, live validation, and Apply/Close. Every edit
    /// mutates the draft and re-renders; Apply hands the built plan back to the
    /// mission so this battle runs it. Per-element wording comes from Core's
    /// PlanFormatter (the single tested source of truth).
    /// </summary>
    public sealed class PlanningModeVM : ViewModel
    {
        private readonly PlanDraft _draft;
        private readonly Action<BattlePlan> _onApply;
        private readonly Action _onClose;

        private string _titleText;
        private string _hintText;
        private string _signalsText;
        private string _statusText;
        private string _warningsText;
        private string _addFormationText;
        private string _emptyText;
        private bool _hasWarnings;
        private bool _hasStatus;
        private bool _hasPlan;
        private bool _canAddFormation;
        private bool _isAddFormationDisabled;
        private MBBindingList<FormationPlanItemVM> _formations;

        public PlanningModeVM(string title, string hint, PlanDraft draft, Action<BattlePlan> onApply, Action onClose)
        {
            _titleText = title;
            _hintText = hint;
            _draft = draft;
            _onApply = onApply;
            _onClose = onClose;
            _formations = new MBBindingList<FormationPlanItemVM>();
            Refresh();
        }

        private void Refresh()
        {
            var plan = _draft.Build();
            _formations.Clear();
            foreach (var formation in plan.Formations)
                _formations.Add(new FormationPlanItemVM(formation, AddStage, RemoveStage));

            HasPlan = plan.Formations.Count > 0;
            EmptyText = HasPlan ? "" :
                "No formations planned yet.\nAdd a formation, give it stages (When → Do), then Apply.";

            SignalsText = plan.PlayerSignals.Count > 0
                ? "SIGNALS    " + string.Join("     ", plan.PlayerSignals.Select(s => $"[ {s} ]"))
                : "";

            var warnings = PlanValidator.Validate(plan).Warnings;
            HasWarnings = warnings.Count > 0;
            WarningsText = HasWarnings ? $"{warnings.Count} warning(s):   " + string.Join("      ", warnings) : "";

            var next = NextUnplannedClass(plan);
            CanAddFormation = next != null;
            IsAddFormationDisabled = next == null;
            AddFormationText = next != null ? $"+  Add {next}" : "All formations planned";
        }

        private static PlannedFormationClass? NextUnplannedClass(BattlePlan plan)
        {
            var planned = new HashSet<PlannedFormationClass>(plan.Formations.Select(f => f.Formation));
            foreach (PlannedFormationClass c in Enum.GetValues(typeof(PlannedFormationClass)))
                if (!planned.Contains(c))
                    return c;
            return null;
        }

        private void AddStage(PlannedFormationClass cls)
        {
            _draft.AddStage(cls);
            StatusText = $"Added a stage to {cls}.";
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

        public void ExecuteAddFormation()
        {
            if (NextUnplannedClass(_draft.Build()) is { } next)
            {
                _draft.AddFormation(next);
                StatusText = $"Added {next} (with a default opening stage).";
                Refresh();
            }
        }

        public void ExecuteApply()
        {
            var plan = _draft.Build();
            var validation = PlanValidator.Validate(plan);
            if (!validation.IsValid)
            {
                StatusText = "Plan has errors — fix them before applying.";
                return;
            }
            _onApply?.Invoke(plan);
            StatusText = "Plan applied — it governs your formations when the battle begins.";
        }

        public void ExecuteClose() => _onClose?.Invoke();

        private void SetStatus(string value)
        {
            StatusText = value;
        }

        [DataSourceProperty] public bool HasPlan { get => _hasPlan; set { if (value != _hasPlan) { _hasPlan = value; OnPropertyChangedWithValue(value, "HasPlan"); } } }
        [DataSourceProperty] public bool HasWarnings { get => _hasWarnings; set { if (value != _hasWarnings) { _hasWarnings = value; OnPropertyChangedWithValue(value, "HasWarnings"); } } }
        [DataSourceProperty] public bool HasStatus { get => _hasStatus; set { if (value != _hasStatus) { _hasStatus = value; OnPropertyChangedWithValue(value, "HasStatus"); } } }
        [DataSourceProperty] public bool CanAddFormation { get => _canAddFormation; set { if (value != _canAddFormation) { _canAddFormation = value; OnPropertyChangedWithValue(value, "CanAddFormation"); } } }
        [DataSourceProperty] public bool IsAddFormationDisabled { get => _isAddFormationDisabled; set { if (value != _isAddFormationDisabled) { _isAddFormationDisabled = value; OnPropertyChangedWithValue(value, "IsAddFormationDisabled"); } } }
        [DataSourceProperty] public string TitleText { get => _titleText; set { if (value != _titleText) { _titleText = value; OnPropertyChangedWithValue(value, "TitleText"); } } }
        [DataSourceProperty] public string HintText { get => _hintText; set { if (value != _hintText) { _hintText = value; OnPropertyChangedWithValue(value, "HintText"); } } }
        [DataSourceProperty] public string SignalsText { get => _signalsText; set { if (value != _signalsText) { _signalsText = value; OnPropertyChangedWithValue(value, "SignalsText"); } } }
        [DataSourceProperty] public string WarningsText { get => _warningsText; set { if (value != _warningsText) { _warningsText = value; OnPropertyChangedWithValue(value, "WarningsText"); } } }
        [DataSourceProperty] public string EmptyText { get => _emptyText; set { if (value != _emptyText) { _emptyText = value; OnPropertyChangedWithValue(value, "EmptyText"); } } }
        [DataSourceProperty] public string AddFormationText { get => _addFormationText; set { if (value != _addFormationText) { _addFormationText = value; OnPropertyChangedWithValue(value, "AddFormationText"); } } }
        [DataSourceProperty] public string StatusText { get => _statusText; set { if (value != _statusText) { _statusText = value; _hasStatus = !string.IsNullOrEmpty(value); OnPropertyChangedWithValue(value, "StatusText"); OnPropertyChangedWithValue(_hasStatus, "HasStatus"); } } }
        [DataSourceProperty] public MBBindingList<FormationPlanItemVM> Formations { get => _formations; set { if (value != _formations) { _formations = value; OnPropertyChangedWithValue(value, "Formations"); } } }
    }

    /// <summary>One formation's card: name, abort rule, its stage rows, and add/remove-stage controls.</summary>
    public sealed class FormationPlanItemVM : ViewModel
    {
        private readonly PlannedFormationClass _formation;
        private readonly Action<PlannedFormationClass> _addStage;
        private readonly Action<PlannedFormationClass> _removeStage;

        public FormationPlanItemVM(FormationPlan formation, Action<PlannedFormationClass> addStage, Action<PlannedFormationClass> removeStage)
        {
            _formation = formation.Formation;
            _addStage = addStage;
            _removeStage = removeStage;
            NameText = formation.Formation.ToString();
            AbortText = PlanFormatter.DescribeAbort(formation.Abort);
            CanRemove = formation.Stages.Count > 0;
            IsRemoveDisabled = !CanRemove;
            Stages = new MBBindingList<StageItemVM>();
            for (var i = 0; i < formation.Stages.Count; i++)
                Stages.Add(new StageItemVM(i, formation.Stages[i]));
        }

        public void ExecuteAddStage() => _addStage?.Invoke(_formation);
        public void ExecuteRemoveStage() => _removeStage?.Invoke(_formation);

        [DataSourceProperty] public string NameText { get; }
        [DataSourceProperty] public string AbortText { get; }
        [DataSourceProperty] public bool CanRemove { get; }
        [DataSourceProperty] public bool IsRemoveDisabled { get; }
        [DataSourceProperty] public MBBindingList<StageItemVM> Stages { get; }
    }

    /// <summary>One stage row: number, the trigger ("When"), the directive ("Do"), and any emitted signals.</summary>
    public sealed class StageItemVM : ViewModel
    {
        public StageItemVM(int index, Stage stage)
        {
            NumberText = (index + 1).ToString();
            TriggerText = PlanFormatter.DescribeWhen(stage, index);
            DirectiveText = PlanFormatter.DescribeDirective(stage.Do);
            EmitText = stage.Emit.Count > 0 ? "→ signals  " + string.Join(", ", stage.Emit) : "";
            HasEmit = stage.Emit.Count > 0;
        }

        [DataSourceProperty] public string NumberText { get; }
        [DataSourceProperty] public string TriggerText { get; }
        [DataSourceProperty] public string DirectiveText { get; }
        [DataSourceProperty] public string EmitText { get; }
        [DataSourceProperty] public bool HasEmit { get; }
    }
}
