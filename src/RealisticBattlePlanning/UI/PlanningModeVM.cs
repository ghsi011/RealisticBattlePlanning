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
        private string _emptyText;
        private bool _hasWarnings;
        private bool _hasErrors;
        private bool _hasStatus;
        private bool _hasPlan;
        private bool _pickerOpen;
        private string _pickerTitle;
        private MBBindingList<FormationPlanItemVM> _formations;
        private MBBindingList<PickerOptionVM> _pickerOptions;

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
            _pickerOptions = new MBBindingList<PickerOptionVM>();
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

            // One card per REAL formation, ordered by number: every deployment slot
            // that has troops (from live composition) plus any slot that already has
            // a plan. A slot with stages is commanded; a slot without is shown as
            // "uncommanded" (holds by default) with a + Stage to start giving orders.
            // When there is no live composition data (engine read failed / not in a
            // mission) we fall back to whatever the plan already contains.
            var slots = new SortedSet<PlannedFormationClass>();
            foreach (var k in _compositionLabels.Keys) slots.Add(k);
            foreach (var f in plan.Formations) slots.Add(f.Formation);
            foreach (var cls in slots)
            {
                var formationPlan = plan.Formations.FirstOrDefault(f => f.Formation == cls)
                                    ?? new FormationPlan { Formation = cls };
                _formations.Add(new FormationPlanItemVM(formationPlan, HeaderFor(cls), AddStage, RemoveStage,
                    OpenTriggerPicker, OpenDirectivePicker, OpenTriggerParamPicker, OpenDirectiveParamPicker, OpenAbortPicker, ClearFormation,
                    MoveStageUp, MoveStageDown));
            }

            HasPlan = _formations.Count > 0;
            EmptyText = HasPlan ? "" : "No formations with troops to command.";

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
            StatusText = $"Removed a stage from Formation {SlotNumber(cls)}.";
            Refresh();
        }

        // Drops a formation's whole plan; the card stays (it has troops) but
        // reverts to uncommanded — holds by default.
        private void ClearFormation(PlannedFormationClass cls)
        {
            _draft.RemoveFormation(cls);
            StatusText = $"Formation {SlotNumber(cls)} cleared — now uncommanded.";
            Refresh();
        }

        // Stages execute strictly in order (A3.3); ▲/▼ reorder them.
        private void MoveStageUp(PlannedFormationClass cls, int index)
        {
            if (index <= 0) return;
            _draft.MoveStage(cls, index, index - 1);
            StatusText = $"Formation {SlotNumber(cls)}: moved stage {index + 1} up.";
            Refresh();
        }

        private void MoveStageDown(PlannedFormationClass cls, int index)
        {
            _draft.MoveStage(cls, index, index + 1);
            StatusText = $"Formation {SlotNumber(cls)}: moved stage {index + 1} down.";
            Refresh();
        }

        // Clicking a stage's "When" field opens the picker: a modal list of every
        // TriggerType. Selecting one sets a valid default spec for it (using the
        // plan's anchors/signals where the type needs them) and closes the picker.
        private void OpenTriggerPicker(PlannedFormationClass cls, int stageIndex)
        {
            var plan = _draft.Build();
            var stage = StageAt(plan, cls, stageIndex);
            if (stage == null)
                return;
            var current = stage.When.Count > 0 ? stage.When[0].Type : TriggerType.BattleStart;
            _pickerOptions.Clear();
            foreach (TriggerType t in Enum.GetValues(typeof(TriggerType)))
            {
                var type = t; // capture per option
                _pickerOptions.Add(new PickerOptionVM(Spaced(type.ToString()), type == current, () =>
                {
                    _draft.SetTrigger(cls, stageIndex, DefaultTrigger(type, _draft.Build()));
                    StatusText = $"Formation {SlotNumber(cls)} stage {stageIndex + 1}: When → {Spaced(type.ToString())}.";
                    ClosePicker();
                    Refresh();
                }));
            }
            PickerTitle = $"Formation {SlotNumber(cls)}  ·  Stage {stageIndex + 1}  ·  When";
            PickerOpen = true;
        }

        // Clicking a stage's "Do" field opens the picker with every DirectiveType.
        private void OpenDirectivePicker(PlannedFormationClass cls, int stageIndex)
        {
            var plan = _draft.Build();
            var stage = StageAt(plan, cls, stageIndex);
            if (stage == null)
                return;
            var current = stage.Do?.Type ?? DirectiveType.Hold;
            _pickerOptions.Clear();
            foreach (DirectiveType t in Enum.GetValues(typeof(DirectiveType)))
            {
                var type = t; // capture per option
                _pickerOptions.Add(new PickerOptionVM(Spaced(type.ToString()), type == current, () =>
                {
                    _draft.SetDirective(cls, stageIndex, DefaultDirective(type, _draft.Build()));
                    StatusText = $"Formation {SlotNumber(cls)} stage {stageIndex + 1}: Do → {Spaced(type.ToString())}.";
                    ClosePicker();
                    Refresh();
                }));
            }
            PickerTitle = $"Formation {SlotNumber(cls)}  ·  Stage {stageIndex + 1}  ·  Do";
            PickerOpen = true;
        }

        private void ClosePicker()
        {
            PickerOpen = false;
            _pickerOptions.Clear();
        }

        public void ExecuteClosePicker() => ClosePicker();

        // Clicking a When line's value chip opens a picker for the current
        // trigger type's editable parameter (distance / %, / time / anchor /
        // signal). Selecting a value mutates the draft's trigger in place and
        // re-renders. Types without an editable parameter have no chip.
        private void OpenTriggerParamPicker(PlannedFormationClass cls, int stageIndex)
        {
            var plan = _draft.Build();
            var stage = StageAt(plan, cls, stageIndex);
            if (stage == null || stage.When.Count == 0)
                return;
            var t = stage.When[0];
            var name = "";

            void Numeric(string param, string unit, float? current, float[] presets, Action<float> set)
            {
                name = param;
                foreach (var v in presets)
                {
                    var val = v;
                    AddPickerOption($"{val:0.#} {unit}", current is { } c && System.Math.Abs(c - val) < 0.01f, () =>
                    { set(val); ParamPicked(cls, stageIndex, param, $"{val:0.#} {unit}"); });
                }
            }
            void Choices(string param, IEnumerable<string> choices, string current, Action<string> set)
            {
                name = param;
                foreach (var c in choices)
                {
                    var ch = c;
                    AddPickerOption(ch, string.Equals(ch, current, StringComparison.OrdinalIgnoreCase), () =>
                    { set(ch); ParamPicked(cls, stageIndex, param, ch); });
                }
            }

            _pickerOptions.Clear();
            switch (t.Type)
            {
                case TriggerType.EnemyWithinDistance:
                case TriggerType.FriendlyWithinDistance:
                case TriggerType.EnemyCommits:
                    Numeric("Distance", "m", t.Meters, new[] { 20f, 40f, 60f, 80f, 100f, 120f, 150f }, v => t.Meters = v); break;
                case TriggerType.CasualtiesAbove:
                    Numeric("Casualties", "%", t.Percent, new[] { 10f, 20f, 30f, 40f, 50f, 60f, 70f, 80f }, v => t.Percent = v); break;
                case TriggerType.TimerElapsed:
                    Numeric("Time", "s", t.Seconds, new[] { 5f, 10f, 15f, 30f, 45f, 60f, 90f, 120f }, v => t.Seconds = v); break;
                case TriggerType.PositionReached:
                    Choices("Anchor", plan.Anchors.Select(a => a.Id), t.Anchor, a => t.Anchor = a); break;
                case TriggerType.SignalReceived:
                case TriggerType.PlayerSignal:
                    Choices("Signal", plan.PlayerSignals, t.Signal, s => t.Signal = s); break;
                default:
                    return;
            }
            ShowParamPicker(cls, stageIndex, "When", name);
        }

        // Clicking a Do line's value chip opens a picker for the current
        // directive type's editable parameter.
        private void OpenDirectiveParamPicker(PlannedFormationClass cls, int stageIndex)
        {
            var plan = _draft.Build();
            var stage = StageAt(plan, cls, stageIndex);
            if (stage?.Do == null)
                return;
            var d = stage.Do;
            var name = "";

            void Numeric(string param, string unit, float? current, float[] presets, Action<float> set)
            {
                name = param;
                foreach (var v in presets)
                {
                    var val = v;
                    AddPickerOption($"{val:0.#} {unit}", current is { } c && System.Math.Abs(c - val) < 0.01f, () =>
                    { set(val); ParamPicked(cls, stageIndex, param, $"{val:0.#} {unit}"); });
                }
            }
            void Choices(string param, IEnumerable<string> choices, string current, Action<string> set)
            {
                name = param;
                foreach (var c in choices)
                {
                    var ch = c;
                    AddPickerOption(ch, string.Equals(ch, current, StringComparison.OrdinalIgnoreCase), () =>
                    { set(ch); ParamPicked(cls, stageIndex, param, ch); });
                }
            }

            _pickerOptions.Clear();
            switch (d.Type)
            {
                case DirectiveType.MoveTo:
                case DirectiveType.FeignRetreat:
                case DirectiveType.PullBack:
                    Choices("Anchor", plan.Anchors.Select(a => a.Id), d.Anchor, a => d.Anchor = a); break;
                case DirectiveType.Skirmish:
                    Numeric("Standoff", "m", d.StandoffMeters ?? 60f, new[] { 20f, 40f, 60f, 80f, 100f }, v => d.StandoffMeters = v); break;
                case DirectiveType.Screen:
                    Numeric("Gap", "m", d.GapMeters ?? 30f, new[] { 10f, 20f, 30f, 40f, 60f }, v => d.GapMeters = v); break;
                case DirectiveType.FlankArc:
                    Choices("Side", new[] { "Left", "Right" }, d.Side?.ToString(), s => d.Side = (FlankSide)Enum.Parse(typeof(FlankSide), s)); break;
                case DirectiveType.FireControl:
                    Choices("Fire", new[] { "Hold", "Free" }, d.Fire?.ToString(), s => d.Fire = (FireMode)Enum.Parse(typeof(FireMode), s)); break;
                case DirectiveType.Hold:
                    Choices("Arrangement", Enum.GetNames(typeof(Arrangement)), (d.Arrangement ?? Arrangement.Line).ToString(),
                        s => d.Arrangement = (Arrangement)Enum.Parse(typeof(Arrangement), s)); break;
                case DirectiveType.Charge:
                    Choices("Target", EnemyTargets(), d.Target, x => d.Target = x); break;
                case DirectiveType.Follow:
                    Choices("Follow", FriendlyTargets(), d.Target, x => d.Target = x); break;
                default:
                    return;
            }
            ShowParamPicker(cls, stageIndex, "Do", name);
        }

        private void AddPickerOption(string label, bool isCurrent, Action set) =>
            _pickerOptions.Add(new PickerOptionVM(label, isCurrent, set));

        private void ShowParamPicker(PlannedFormationClass cls, int stageIndex, string line, string param)
        {
            if (_pickerOptions.Count == 0) { ClosePicker(); return; }
            PickerTitle = $"Formation {SlotNumber(cls)}  ·  Stage {stageIndex + 1}  ·  {line} {param}";
            PickerOpen = true;
        }

        private void ParamPicked(PlannedFormationClass cls, int stageIndex, string param, string value)
        {
            StatusText = $"Formation {SlotNumber(cls)} stage {stageIndex + 1}: {param} → {value}.";
            ClosePicker();
            Refresh();
        }

        // Clicking a formation's abort line opens a picker to set the casualty
        // threshold and toggle the broken / commander-down clauses (A3.7).
        private void OpenAbortPicker(PlannedFormationClass cls)
        {
            var formation = _draft.Build().Formations.FirstOrDefault(f => f.Formation == cls);
            if (formation == null)
                return; // only commanded formations have an abort rule to edit
            var abort = formation.Abort;
            _pickerOptions.Clear();
            foreach (var pct in new[] { 30f, 40f, 50f, 60f, 70f, 80f, 90f })
            {
                var p = pct;
                AddPickerOption($"Abort above {p:0}% casualties", System.Math.Abs(abort.CasualtiesAbovePercent - p) < 0.5f, () =>
                { _draft.SetAbortConditions(cls, casualtiesAbovePercent: p); AbortPicked(cls, $"abort above {p:0}% casualties"); });
            }
            AddPickerOption(abort.OnFormationBroken ? "Abort if broken:  ON  →  turn off" : "Abort if broken:  off  →  turn on", abort.OnFormationBroken, () =>
            { _draft.SetAbortConditions(cls, onFormationBroken: !abort.OnFormationBroken); AbortPicked(cls, $"abort-if-broken {(!abort.OnFormationBroken ? "on" : "off")}"); });
            AddPickerOption(abort.OnCommanderIncapacitated ? "Abort if commander down:  ON  →  turn off" : "Abort if commander down:  off  →  turn on", abort.OnCommanderIncapacitated, () =>
            { _draft.SetAbortConditions(cls, onCommanderIncapacitated: !abort.OnCommanderIncapacitated); AbortPicked(cls, $"abort-if-commander-down {(!abort.OnCommanderIncapacitated ? "on" : "off")}"); });
            PickerTitle = $"Formation {SlotNumber(cls)}  ·  Abort conditions";
            PickerOpen = true;
        }

        private void AbortPicked(PlannedFormationClass cls, string what)
        {
            StatusText = $"Formation {SlotNumber(cls)}: {what}.";
            ClosePicker();
            Refresh();
        }

        private static IEnumerable<string> EnemyTargets()
        {
            yield return "Nearest";
            foreach (PlannedFormationClass c in Enum.GetValues(typeof(PlannedFormationClass)))
                yield return c.ToString();
        }

        private static IEnumerable<string> FriendlyTargets()
        {
            yield return "Player";
            foreach (PlannedFormationClass c in Enum.GetValues(typeof(PlannedFormationClass)))
                yield return c.ToString();
        }

        private static Stage StageAt(BattlePlan plan, PlannedFormationClass cls, int stageIndex)
        {
            var formation = plan.Formations.FirstOrDefault(f => f.Formation == cls);
            return formation != null && stageIndex >= 0 && stageIndex < formation.Stages.Count
                ? formation.Stages[stageIndex]
                : null;
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
        [DataSourceProperty] public string TitleText { get => _titleText; set { if (value != _titleText) { _titleText = value; OnPropertyChangedWithValue(value, "TitleText"); } } }
        [DataSourceProperty] public string HintText { get => _hintText; set { if (value != _hintText) { _hintText = value; OnPropertyChangedWithValue(value, "HintText"); } } }
        [DataSourceProperty] public string SignalsText { get => _signalsText; set { if (value != _signalsText) { _signalsText = value; OnPropertyChangedWithValue(value, "SignalsText"); } } }
        [DataSourceProperty] public string WarningsText { get => _warningsText; set { if (value != _warningsText) { _warningsText = value; OnPropertyChangedWithValue(value, "WarningsText"); } } }
        [DataSourceProperty] public string ErrorsText { get => _errorsText; set { if (value != _errorsText) { _errorsText = value; OnPropertyChangedWithValue(value, "ErrorsText"); } } }
        [DataSourceProperty] public string EmptyText { get => _emptyText; set { if (value != _emptyText) { _emptyText = value; OnPropertyChangedWithValue(value, "EmptyText"); } } }
        [DataSourceProperty] public string StatusText { get => _statusText; set { if (value != _statusText) { _statusText = value; OnPropertyChangedWithValue(value, "StatusText"); HasStatus = !string.IsNullOrEmpty(value); } } }
        [DataSourceProperty] public MBBindingList<FormationPlanItemVM> Formations { get => _formations; set { if (value != _formations) { _formations = value; OnPropertyChangedWithValue(value, "Formations"); } } }
        [DataSourceProperty] public bool PickerOpen { get => _pickerOpen; set { if (value != _pickerOpen) { _pickerOpen = value; OnPropertyChangedWithValue(value, "PickerOpen"); } } }
        [DataSourceProperty] public string PickerTitle { get => _pickerTitle; set { if (value != _pickerTitle) { _pickerTitle = value; OnPropertyChangedWithValue(value, "PickerTitle"); } } }
        [DataSourceProperty] public MBBindingList<PickerOptionVM> PickerOptions { get => _pickerOptions; set { if (value != _pickerOptions) { _pickerOptions = value; OnPropertyChangedWithValue(value, "PickerOptions"); } } }
    }

    /// <summary>One selectable row in the trigger/directive picker menu.</summary>
    public sealed class PickerOptionVM : ViewModel
    {
        private readonly Action _onSelect;

        public PickerOptionVM(string label, bool isCurrent, Action onSelect)
        {
            _onSelect = onSelect;
            Label = label;
            IsCurrent = isCurrent;
        }

        public void ExecuteSelect() => _onSelect?.Invoke();

        [DataSourceProperty] public string Label { get; }
        [DataSourceProperty] public bool IsCurrent { get; }
    }

    /// <summary>One formation's card: name, abort rule, its stage rows, and add/remove-stage controls.</summary>
    public sealed class FormationPlanItemVM : ViewModel
    {
        private readonly PlannedFormationClass _formation;
        private readonly Action<PlannedFormationClass> _addStage;
        private readonly Action<PlannedFormationClass> _removeStage;
        private readonly Action<PlannedFormationClass> _editAbort;
        private readonly Action<PlannedFormationClass> _clearFormation;

        public FormationPlanItemVM(
            FormationPlan formation,
            string headerText,
            Action<PlannedFormationClass> addStage,
            Action<PlannedFormationClass> removeStage,
            Action<PlannedFormationClass, int> editTrigger,
            Action<PlannedFormationClass, int> editDirective,
            Action<PlannedFormationClass, int> editTriggerParam,
            Action<PlannedFormationClass, int> editDirectiveParam,
            Action<PlannedFormationClass> editAbort,
            Action<PlannedFormationClass> clearFormation,
            Action<PlannedFormationClass, int> moveStageUp,
            Action<PlannedFormationClass, int> moveStageDown)
        {
            _formation = formation.Formation;
            _addStage = addStage;
            _removeStage = removeStage;
            _editAbort = editAbort;
            _clearFormation = clearFormation;
            NameText = headerText;
            // A formation with stages is commanded (show its abort rule + stages);
            // one without is uncommanded — it holds by default until given orders.
            HasStages = formation.Stages.Count > 0;
            IsUncommanded = !HasStages;
            AbortText = PlanFormatter.DescribeAbort(formation.Abort);
            UncommandedText = "Uncommanded — holds position. Add a stage to give it orders.";
            CanRemove = formation.Stages.Count > 0;
            IsRemoveDisabled = !CanRemove;
            Stages = new MBBindingList<StageItemVM>();
            var count = formation.Stages.Count;
            for (var i = 0; i < count; i++)
            {
                var index = i; // capture for the per-stage picker closures
                var cls = _formation;
                Stages.Add(new StageItemVM(
                    i, count, formation.Stages[i],
                    () => editTrigger?.Invoke(cls, index),
                    () => editDirective?.Invoke(cls, index),
                    () => editTriggerParam?.Invoke(cls, index),
                    () => editDirectiveParam?.Invoke(cls, index),
                    () => moveStageUp?.Invoke(cls, index),
                    () => moveStageDown?.Invoke(cls, index)));
            }
        }

        public void ExecuteAddStage() => _addStage?.Invoke(_formation);
        public void ExecuteRemoveStage() => _removeStage?.Invoke(_formation);
        public void ExecuteEditAbort() => _editAbort?.Invoke(_formation);
        public void ExecuteClearFormation() => _clearFormation?.Invoke(_formation);

        [DataSourceProperty] public string NameText { get; }
        [DataSourceProperty] public string AbortText { get; }
        [DataSourceProperty] public string UncommandedText { get; }
        [DataSourceProperty] public bool HasStages { get; }
        [DataSourceProperty] public bool IsUncommanded { get; }
        [DataSourceProperty] public bool CanRemove { get; }
        [DataSourceProperty] public bool IsRemoveDisabled { get; }
        [DataSourceProperty] public MBBindingList<StageItemVM> Stages { get; }
    }

    /// <summary>One stage row: number, the trigger ("When") and directive ("Do") as
    /// click-to-edit fields (open a picker menu), and any emitted signals.</summary>
    public sealed class StageItemVM : ViewModel
    {
        private readonly Action _editTrigger;
        private readonly Action _editDirective;
        private readonly Action _editTriggerParam;
        private readonly Action _editDirectiveParam;
        private readonly Action _moveUp;
        private readonly Action _moveDown;

        public StageItemVM(int index, int stageCount, Stage stage, Action editTrigger, Action editDirective,
            Action editTriggerParam, Action editDirectiveParam, Action moveUp, Action moveDown)
        {
            _editTrigger = editTrigger;
            _editDirective = editDirective;
            _editTriggerParam = editTriggerParam;
            _editDirectiveParam = editDirectiveParam;
            _moveUp = moveUp;
            _moveDown = moveDown;
            IsMoveUpDisabled = index <= 0;
            IsMoveDownDisabled = index >= stageCount - 1;
            NumberText = (index + 1).ToString();
            TriggerText = "When:  " + PlanFormatter.DescribeWhen(stage, index);
            DirectiveText = "Do:  " + PlanFormatter.DescribeDirective(stage.Do);
            EmitText = stage.Emit.Count > 0 ? "→ signals  " + string.Join(", ", stage.Emit) : "";
            HasEmit = stage.Emit.Count > 0;

            var trig = stage.When.Count > 0 ? stage.When[0] : null;
            (HasTriggerParam, TriggerParamLabel) = TriggerParam(trig);
            (HasDirectiveParam, DirectiveParamLabel) = DirectiveParam(stage.Do);
        }

        public void ExecuteEditTrigger() => _editTrigger?.Invoke();
        public void ExecuteEditDirective() => _editDirective?.Invoke();
        public void ExecuteEditTriggerParam() => _editTriggerParam?.Invoke();
        public void ExecuteEditDirectiveParam() => _editDirectiveParam?.Invoke();
        public void ExecuteMoveUp() => _moveUp?.Invoke();
        public void ExecuteMoveDown() => _moveDown?.Invoke();

        /// <summary>Whether a trigger type has an editable parameter, and the chip text for it.</summary>
        private static (bool has, string label) TriggerParam(TriggerSpec t)
        {
            if (t == null) return (false, "");
            switch (t.Type)
            {
                case TriggerType.EnemyWithinDistance:
                case TriggerType.FriendlyWithinDistance:
                case TriggerType.EnemyCommits: return (true, t.Meters is { } m ? $"{m:0.#} m" : "range");
                case TriggerType.CasualtiesAbove: return (true, t.Percent is { } p ? $"{p:0.#}%" : "%");
                case TriggerType.TimerElapsed: return (true, t.Seconds is { } s ? $"{s:0.#} s" : "time");
                case TriggerType.PositionReached: return (true, string.IsNullOrEmpty(t.Anchor) ? "pick anchor" : t.Anchor);
                case TriggerType.SignalReceived:
                case TriggerType.PlayerSignal: return (true, string.IsNullOrEmpty(t.Signal) ? "pick signal" : t.Signal);
                default: return (false, "");
            }
        }

        private static (bool has, string label) DirectiveParam(DirectiveSpec d)
        {
            if (d == null) return (false, "");
            switch (d.Type)
            {
                case DirectiveType.MoveTo:
                case DirectiveType.FeignRetreat:
                case DirectiveType.PullBack: return (true, string.IsNullOrEmpty(d.Anchor) ? "pick anchor" : d.Anchor);
                case DirectiveType.Skirmish: return (true, $"standoff {d.StandoffMeters ?? 60:0.#} m");
                case DirectiveType.Screen: return (true, $"gap {d.GapMeters ?? 30:0.#} m");
                case DirectiveType.FlankArc: return (true, (d.Side ?? FlankSide.Left).ToString());
                case DirectiveType.FireControl: return (true, (d.Fire ?? FireMode.Free).ToString());
                case DirectiveType.Hold: return (true, (d.Arrangement ?? Arrangement.Line).ToString());
                case DirectiveType.Charge: return (true, d.Target ?? "Nearest");
                case DirectiveType.Follow: return (true, d.Target ?? "Player");
                default: return (false, "");
            }
        }

        [DataSourceProperty] public string NumberText { get; }
        [DataSourceProperty] public string TriggerText { get; }
        [DataSourceProperty] public string DirectiveText { get; }
        [DataSourceProperty] public string EmitText { get; }
        [DataSourceProperty] public bool HasEmit { get; }
        [DataSourceProperty] public bool HasTriggerParam { get; }
        [DataSourceProperty] public string TriggerParamLabel { get; }
        [DataSourceProperty] public bool HasDirectiveParam { get; }
        [DataSourceProperty] public string DirectiveParamLabel { get; }
        [DataSourceProperty] public bool IsMoveUpDisabled { get; }
        [DataSourceProperty] public bool IsMoveDownDisabled { get; }
    }
}
