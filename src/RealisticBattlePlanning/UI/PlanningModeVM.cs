using System.Collections.Generic;
using System.Linq;
using RealisticBattlePlanning.Planning;
using RealisticBattlePlanning.Planning.Model;
using TaleWorlds.Library;

namespace RealisticBattlePlanning.UI
{
    /// <summary>
    /// Gauntlet datasource for the Planning Mode panel — a structured, styled
    /// read of the loaded plan: a header, one card per formation (its abort
    /// rule + its stages as "When → Do" rows), and a footer with the declared
    /// player signals and any feasibility warnings. The per-element text comes
    /// from Core's PlanFormatter so the wording is the tested, single source of
    /// truth. The PlanDraft-backed editing controls build on this VM next.
    /// </summary>
    public sealed class PlanningModeVM : ViewModel
    {
        private string _titleText;
        private string _hintText;
        private string _signalsText;
        private string _warningsText;
        private bool _hasWarnings;
        private string _emptyText;
        private bool _hasPlan;
        private MBBindingList<FormationPlanItemVM> _formations;

        public PlanningModeVM(string title, string hint, BattlePlan plan, PlanValidationResult validation)
        {
            _titleText = title;
            _hintText = hint;
            _formations = new MBBindingList<FormationPlanItemVM>();
            _hasPlan = plan != null && plan.Formations.Count > 0;

            if (!_hasPlan)
            {
                _emptyText = "No plan is loaded for this battle.\n" +
                             "Drop an rbp_debug_plan.json in ModuleData — in-panel authoring lands next.";
                _signalsText = "";
                _warningsText = "";
                return;
            }

            foreach (var formation in plan.Formations)
                _formations.Add(new FormationPlanItemVM(formation));

            _signalsText = plan.PlayerSignals.Count > 0
                ? "SIGNALS    " + string.Join("     ", plan.PlayerSignals.Select(s => $"[ {s} ]"))
                : "No player signals declared.";

            var warnings = validation?.Warnings ?? new List<string>();
            _hasWarnings = warnings.Count > 0;
            _warningsText = _hasWarnings
                ? $"{warnings.Count} warning(s):   " + string.Join("      ", warnings)
                : "";
        }

        [DataSourceProperty]
        public bool HasPlan { get => _hasPlan; set { if (value != _hasPlan) { _hasPlan = value; OnPropertyChangedWithValue(value, "HasPlan"); } } }

        [DataSourceProperty]
        public bool HasWarnings { get => _hasWarnings; set { if (value != _hasWarnings) { _hasWarnings = value; OnPropertyChangedWithValue(value, "HasWarnings"); } } }

        [DataSourceProperty]
        public string TitleText { get => _titleText; set { if (value != _titleText) { _titleText = value; OnPropertyChangedWithValue(value, "TitleText"); } } }

        [DataSourceProperty]
        public string HintText { get => _hintText; set { if (value != _hintText) { _hintText = value; OnPropertyChangedWithValue(value, "HintText"); } } }

        [DataSourceProperty]
        public string SignalsText { get => _signalsText; set { if (value != _signalsText) { _signalsText = value; OnPropertyChangedWithValue(value, "SignalsText"); } } }

        [DataSourceProperty]
        public string WarningsText { get => _warningsText; set { if (value != _warningsText) { _warningsText = value; OnPropertyChangedWithValue(value, "WarningsText"); } } }

        [DataSourceProperty]
        public string EmptyText { get => _emptyText; set { if (value != _emptyText) { _emptyText = value; OnPropertyChangedWithValue(value, "EmptyText"); } } }

        [DataSourceProperty]
        public MBBindingList<FormationPlanItemVM> Formations { get => _formations; set { if (value != _formations) { _formations = value; OnPropertyChangedWithValue(value, "Formations"); } } }
    }

    /// <summary>One formation's card: its name, abort rule, and stage rows.</summary>
    public sealed class FormationPlanItemVM : ViewModel
    {
        public FormationPlanItemVM(FormationPlan formation)
        {
            NameText = formation.Formation.ToString();
            AbortText = PlanFormatter.DescribeAbort(formation.Abort);
            Stages = new MBBindingList<StageItemVM>();
            for (var i = 0; i < formation.Stages.Count; i++)
                Stages.Add(new StageItemVM(i, formation.Stages[i]));
        }

        [DataSourceProperty] public string NameText { get; }
        [DataSourceProperty] public string AbortText { get; }
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
