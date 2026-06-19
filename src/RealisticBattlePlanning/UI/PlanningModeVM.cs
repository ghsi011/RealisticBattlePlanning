using System;
using System.Collections.Generic;
using System.Linq;
using RealisticBattlePlanning.Execution;
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
        private string _anchorsText;
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
        private bool _hasMap;
        private bool _showMap;
        private bool _showMapBody;
        private bool _showListBody;
        private string _mapToggleText;
        private MBBindingList<FormationPlanItemVM> _formations;
        private MBBindingList<PickerOptionVM> _pickerOptions;
        private MBBindingList<MapMarkerVM> _mapMarkers;
        private MBBindingList<MapMarkerVM> _enemyMarkers;
        private MBBindingList<MapMarkerVM> _anchorMarkers;
        // Live deployment geometry (formation positions, attack direction, enemy
        // positions) for the map view. Null outside a mission — the map is hidden.
        private readonly BattlefieldGeometry _geometry;
        // Map-first authoring state (spec A2.6): the current selection, the live
        // projection (for click→world), and a running id for click-placed waypoints.
        private readonly HashSet<PlannedFormationClass> _selectedFormations = new();
        // Friendly-marker hit rects in map design units, so a canvas click resolves to a
        // marker (select) vs bare map (place) in the VM — robust against Gauntlet failing
        // to hit-test the zero-size marker wrappers (the 2026-06-12 review's finding).
        private readonly List<(PlannedFormationClass Cls, float X, float Y, float Size)> _markerHits = new();
        private PlanMapProjection _projection;
        private int _waypointCounter;
        private string _selectedText;

        public PlanningModeVM(string title, string hint, PlanDraft draft, Action<BattlePlan> onApply, Action onClose,
            Dictionary<PlannedFormationClass, string> compositionLabels = null,
            BattlefieldGeometry geometry = null)
        {
            _titleText = title;
            _hintText = hint;
            _draft = draft;
            _onApply = onApply;
            _onClose = onClose;
            _compositionLabels = compositionLabels ?? new Dictionary<PlannedFormationClass, string>();
            _geometry = geometry;
            _formations = new MBBindingList<FormationPlanItemVM>();
            _pickerOptions = new MBBindingList<PickerOptionVM>();
            _mapMarkers = new MBBindingList<MapMarkerVM>();
            _enemyMarkers = new MBBindingList<MapMarkerVM>();
            _anchorMarkers = new MBBindingList<MapMarkerVM>();
            // Open on the map (the primary authoring surface, A2.6); falls back to the
            // list automatically when there's no live geometry (UpdateBodyVisibility).
            _showMap = true;
            _mapToggleText = "☰  List";
            Refresh();
        }

        /// <summary>Map area size in design units (the prefab scales to the window).</summary>
        private const float MapWidth = 620f;
        private const float MapHeight = 330f;
        // Uniform pixels-per-normalized-unit so the shape-preserving square projection isn't
        // stretched into the 620x330 canvas (which made nearby units look far apart). Scale by
        // the smaller dimension and centre horizontally, so distances/angles read true.
        private const float MapScale = MapHeight;
        private const float MapOffsetX = (MapWidth - MapHeight) / 2f;
        private const float MarkerSize = 36f;
        private const float EnemySize = 26f;
        private const float AnchorSize = 18f;

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
                    OpenTriggerPicker, OpenDirectivePicker, OpenTriggerParamPicker, OpenTriggerFormationPicker, OpenDirectiveParamPicker, OpenEmitPicker,
                    AddCondition, RemoveCondition, OpenAbortPicker, ClearFormation,
                    MoveStageUp, MoveStageDown, DuplicateStage, ToggleDirectiveOption));
            }

            HasPlan = _formations.Count > 0;
            EmptyText = HasPlan ? "" : "No formations with troops to command.";

            BuildMap();
            UpdateBodyVisibility();

            SignalsText = plan.PlayerSignals.Count > 0
                ? "SIGNALS    " + string.Join("     ", plan.PlayerSignals.Select(s => $"[ {s} ]")) + "        (click to manage)"
                : "SIGNALS    (none — click to add player signals)";

            AnchorsText = plan.Anchors.Count > 0
                ? "ANCHORS    " + string.Join("     ", plan.Anchors.Select(a => $"[ {a.Id} ]")) + "        (click to manage)"
                : "ANCHORS    (none — click to add map anchors for Move To / Position Reached)";

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

        // Projects the live deployment geometry into the map view: one marker per
        // commanded formation, placed in the tactical frame (forward = up). Rebuilt
        // on every Refresh so later anchor/destination edits move live.
        private void BuildMap()
        {
            _mapMarkers.Clear();
            _enemyMarkers.Clear();
            _anchorMarkers.Clear();
            _markerHits.Clear();
            HasMap = _geometry != null && _geometry.HasFormations;
            if (!HasMap)
                return;

            // Fit the scale to the own formations AND the authored anchors, so the
            // plan's spatial extent (e.g. a "push" anchor 120 m forward) frames the
            // map. The enemy — typically far beyond that — is a direction band:
            // its markers are CLAMPED to the top edge ("the enemy is this way")
            // rather than dragged into the fit, which would collapse the own markers.
            var anchors = _draft.Build().Anchors;
            var fitPoints = new List<MapVec>(_geometry.FormationPositions.Values);
            fitPoints.AddRange(anchors.Select(AnchorDisplayPosition));
            var projection = PlanMapProjection.Build(
                _geometry.TeamCenter, _geometry.AttackDirection, fitPoints);
            _projection = projection;  // kept so a map click can un-project back to a world point

            foreach (var kv in _geometry.FormationPositions.OrderBy(p => (int)p.Key))
            {
                var p = projection.Project(kv.Value);
                var cls = kv.Key;
                var mx = MapOffsetX + p.X * MapScale - MarkerSize / 2f;
                var my = (1f - p.Y) * MapScale - MarkerSize / 2f; // flip Y: forward (up) -> small screen-y
                _mapMarkers.Add(new MapMarkerVM(
                    x: mx, y: my,
                    label: SlotNumber(cls).ToString(),
                    sub: _compositionLabels.TryGetValue(cls, out var l) ? l : cls.ToString(),
                    baseColor: "#2C3C2CDD",
                    onSelect: () => SelectFormation(cls),
                    isSelected: _selectedFormations.Contains(cls)));
                _markerHits.Add((cls, mx, my, MarkerSize));
            }

            foreach (var e in _geometry.EnemyPositions)
            {
                var p = projection.Project(e);
                _enemyMarkers.Add(new MapMarkerVM(
                    x: MapOffsetX + Clamp(p.X, 0.05f, 0.95f) * MapScale - EnemySize / 2f,
                    y: (1f - Clamp(p.Y, 0.05f, 0.95f)) * MapScale - EnemySize / 2f,
                    label: "", sub: ""));
            }

            foreach (var a in anchors)
            {
                var p = projection.Project(AnchorDisplayPosition(a));
                _anchorMarkers.Add(new MapMarkerVM(
                    x: MapOffsetX + Clamp(p.X, 0.03f, 0.97f) * MapScale - AnchorSize / 2f,
                    y: (1f - Clamp(p.Y, 0.03f, 0.97f)) * MapScale - AnchorSize / 2f,
                    label: a.Id, sub: ""));
            }

            SelectedText = _selectedFormations.Count == 0
                ? "Click a formation number to select it, then click the map to add a move waypoint."
                : "Selected:  " + string.Join(", ", _selectedFormations.OrderBy(c => (int)c).Select(SlotNumber))
                  + "    —    click the map to add a move waypoint (default: after the previous one).";
        }

        /// <summary>A single map position for an anchor. Scene anchors are absolute;
        /// relative ones are shown from the team centre (OwnStart resolves per
        /// formation in execution, but a shared display point reads fine on the map).</summary>
        private MapVec AnchorDisplayPosition(MapAnchor a)
        {
            if (a.Basis == AnchorBasis.Scene)
                return new MapVec(a.X, a.Y);
            var forward = _geometry.AttackDirection.Normalized();
            return _geometry.TeamCenter + forward * a.Forward + forward.Right() * a.Right;
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

        // Header toggle between the formation-list editor and the battlefield map.
        public void ExecuteToggleMap()
        {
            _showMap = !_showMap;
            MapToggleText = _showMap ? "☰  List" : "▦  Map";
            UpdateBodyVisibility();
        }

        // The map and the list share the body area: exactly one shows at a time.
        private void UpdateBodyVisibility()
        {
            ShowMapBody = _showMap && HasMap;
            ShowListBody = HasPlan && !ShowMapBody;
        }

        // Map marker click (A2.6.1): toggle this formation in the selection, so the
        // player can pick one or several to command together, then re-render.
        internal void SelectFormation(PlannedFormationClass cls)
        {
            if (!_selectedFormations.Remove(cls))
                _selectedFormations.Add(cls);
            Refresh();
        }

        // A click on the map canvas (A2.6.1/A2.6.2). nx/ny are normalized canvas coords,
        // y DOWN (screen). A click on a formation marker toggles its selection; a click on
        // bare map appends a move stage to that world point for each selected formation
        // (default trigger = reached the previous waypoint / battle start).
        internal void OnMapClicked(float nx, float ny)
        {
            if (_projection == null)
                return;

            // Marker hit-test in design space (markers are placed in MapWidth x MapHeight units).
            var dx = nx * MapWidth;
            var dy = ny * MapHeight;
            var rects = string.Join("  ", _markerHits.Select(h => $"{SlotNumber(h.Cls)}@[{h.X:0}-{h.X + h.Size:0},{h.Y:0}-{h.Y + h.Size:0}]"));
            Diagnostics.RbpLog.Info($"[MAP] OnMapClicked n=({nx:0.00},{ny:0.00}) design=({dx:0},{dy:0}) selected={_selectedFormations.Count} markers: {rects}");
            foreach (var hit in _markerHits)
                if (dx >= hit.X && dx <= hit.X + hit.Size && dy >= hit.Y && dy <= hit.Y + hit.Size)
                {
                    Diagnostics.RbpLog.Info($"[MAP] hit formation {SlotNumber(hit.Cls)} -> select");
                    SelectFormation(hit.Cls);
                    return;
                }

            if (_selectedFormations.Count == 0)
                return;
            // Invert the centred uniform mapping (canvas px -> normalized square -> world).
            var world = _projection.Unproject(new MapPoint((nx * MapWidth - MapOffsetX) / MapScale, 1f - ny));
            foreach (var cls in _selectedFormations.OrderBy(c => (int)c))
                MapAuthoring.AppendMarchStage(_draft, cls, world, $"wp{++_waypointCounter}");
            Refresh();
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

        // Clones a stage (all its conditions/directive/emit) right after it, so the
        // player can author one stage then tweak a copy instead of rebuilding it.
        private void DuplicateStage(PlannedFormationClass cls, int index)
        {
            _draft.DuplicateStage(cls, index);
            StatusText = $"Formation {SlotNumber(cls)}: duplicated stage {index + 1}.";
            Refresh();
        }

        // A few directives carry a secondary on/off option beyond their main
        // parameter (FeignRetreat: keep firing; FlankArc: missile-only; PullBack:
        // keep facing the enemy). Its chip flips the flag directly — no picker for a
        // boolean — and the chip label reflects the new state.
        private void ToggleDirectiveOption(PlannedFormationClass cls, int stageIndex)
        {
            var stage = StageAt(_draft.Build(), cls, stageIndex);
            var d = stage?.Do;
            if (d == null)
                return;
            string what;
            switch (d.Type)
            {
                case DirectiveType.FeignRetreat:
                    d.FireWhileWithdrawing = !(d.FireWhileWithdrawing ?? false);
                    what = $"fire while withdrawing {(d.FireWhileWithdrawing == true ? "ON" : "off")}";
                    break;
                case DirectiveType.FlankArc:
                    d.MissileOnly = !(d.MissileOnly ?? false);
                    what = $"missile-only {(d.MissileOnly == true ? "ON" : "off")}";
                    break;
                case DirectiveType.Skirmish:
                    d.Circle = !(d.Circle ?? false);
                    what = $"circling {(d.Circle == true ? "ON" : "off")}";
                    break;
                case DirectiveType.PullBack:
                    d.MaintainFacing = !(d.MaintainFacing ?? false);
                    what = $"maintain facing {(d.MaintainFacing == true ? "ON" : "off")}";
                    break;
                default:
                    return;
            }
            StatusText = $"Formation {SlotNumber(cls)} stage {stageIndex + 1}: {what}.";
            Refresh();
        }

        // A stage's "When" is an AND of up to 3 conditions (A3.5). These edit one
        // condition at a time by its index; the stage row renders one row each.

        // Appends a default AND condition to a stage (the "+ condition" / "+ AND"
        // affordance). The first condition makes a battle-start stage conditional.
        private void AddCondition(PlannedFormationClass cls, int stageIndex)
        {
            _draft.AddTriggerCondition(cls, stageIndex, DefaultTrigger(TriggerType.EnemyWithinDistance, _draft.Build()));
            StatusText = $"Formation {SlotNumber(cls)} stage {stageIndex + 1}: added a trigger condition.";
            Refresh();
        }

        private void RemoveCondition(PlannedFormationClass cls, int stageIndex, int condIndex)
        {
            _draft.RemoveTriggerCondition(cls, stageIndex, condIndex);
            StatusText = $"Formation {SlotNumber(cls)} stage {stageIndex + 1}: removed a trigger condition.";
            Refresh();
        }

        // Clicking a condition's type field opens the picker: a modal list of every
        // TriggerType. Selecting one replaces THAT condition with a valid default
        // spec for it (using the plan's anchors/signals where the type needs them).
        private void OpenTriggerPicker(PlannedFormationClass cls, int stageIndex, int condIndex)
        {
            var plan = _draft.Build();
            var stage = StageAt(plan, cls, stageIndex);
            if (stage == null || condIndex < 0 || condIndex >= stage.When.Count)
                return;
            var current = stage.When[condIndex].Type;
            _pickerOptions.Clear();
            foreach (TriggerType t in Enum.GetValues(typeof(TriggerType)))
            {
                var type = t; // capture per option
                _pickerOptions.Add(new PickerOptionVM(Spaced(type.ToString()), type == current, () =>
                {
                    _draft.SetTriggerCondition(cls, stageIndex, condIndex, DefaultTrigger(type, _draft.Build()));
                    StatusText = $"Formation {SlotNumber(cls)} stage {stageIndex + 1}: When → {Spaced(type.ToString())}.";
                    ClosePicker();
                    Refresh();
                }, TriggerHelp(type)));
            }
            PickerTitle = $"Formation {SlotNumber(cls)}  ·  Stage {stageIndex + 1}  ·  When"
                          + (stage.When.Count > 1 ? $" (condition {condIndex + 1})" : "");
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
                }, DirectiveHelp(type)));
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

        // Clicking a condition's value chip opens a picker for that condition's
        // editable parameter (distance / %, / time / anchor / signal). Selecting a
        // value mutates the draft's condition in place and re-renders. Types without
        // an editable parameter have no chip.
        private void OpenTriggerParamPicker(PlannedFormationClass cls, int stageIndex, int condIndex)
        {
            var plan = _draft.Build();
            var stage = StageAt(plan, cls, stageIndex);
            if (stage == null || condIndex < 0 || condIndex >= stage.When.Count)
                return;
            var t = stage.When[condIndex];
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

        // Clicking a condition's formation chip picks WHICH formation it watches
        // (a3.10): enemy selector for Enemy Within/Broken, friendly selector for
        // Friendly Within / Enemy Commits / Casualties. A "(default)" option clears
        // it back to nearest-enemy / own-formation where that is valid.
        private void OpenTriggerFormationPicker(PlannedFormationClass cls, int stageIndex, int condIndex)
        {
            var stage = StageAt(_draft.Build(), cls, stageIndex);
            if (stage == null || condIndex < 0 || condIndex >= stage.When.Count)
                return;
            var t = stage.When[condIndex];

            IEnumerable<(string Label, string Value)> choices;
            string defaultLabel; // null => no "(default)" option (selector is required)
            switch (t.Type)
            {
                case TriggerType.EnemyWithinDistance:
                case TriggerType.EnemyBroken:
                    // Enemy formations are referenced by class, not the player's numbered slots.
                    choices = FormationClasses().Select(c => (Label: c, Value: c)); defaultLabel = "Any enemy (nearest)"; break;
                case TriggerType.FriendlyWithinDistance:
                    choices = FriendlyTargets(); defaultLabel = null; break; // required
                case TriggerType.EnemyCommits:
                    choices = FriendlyTargets(); defaultLabel = "Any (no specific unit)"; break;
                case TriggerType.CasualtiesAbove:
                    choices = FriendlyTargets(); defaultLabel = "This formation"; break;
                default:
                    return;
            }

            _pickerOptions.Clear();
            if (defaultLabel != null)
                AddPickerOption(defaultLabel, t.Formation == null, () =>
                { t.Formation = null; ParamPicked(cls, stageIndex, "Formation", defaultLabel); });
            foreach (var (label, value) in choices)
            {
                var lbl = label;
                var val = value;
                AddPickerOption(lbl, string.Equals(val, t.Formation, StringComparison.OrdinalIgnoreCase), () =>
                { t.Formation = val; ParamPicked(cls, stageIndex, "Formation", lbl); });
            }
            ShowParamPicker(cls, stageIndex, "When", "Formation");
        }

        private static IEnumerable<string> FormationClasses()
        {
            foreach (PlannedFormationClass c in Enum.GetValues(typeof(PlannedFormationClass)))
                yield return c.ToString();
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
            // Like Choices, but the display label and the stored value differ
            // (e.g. show "1 — Infantry", store the "Infantry" slot selector).
            void ChoicesLabeled(string param, IEnumerable<(string Label, string Value)> options, string current, Action<string> set)
            {
                name = param;
                foreach (var (label, value) in options)
                {
                    var lbl = label;
                    var val = value;
                    AddPickerOption(lbl, string.Equals(val, current, StringComparison.OrdinalIgnoreCase), () =>
                    { set(val); ParamPicked(cls, stageIndex, param, lbl); });
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
                    // Pick the formation to guard (it screens any friendly, not just the
                    // player); the gap keeps its default.
                    ChoicesLabeled("Guard", FriendlyTargets(), d.Target, x => d.Target = x); break;
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
                    ChoicesLabeled("Follow", FriendlyTargets(), d.Target, x => d.Target = x); break;
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

        // Clicking a stage's "emit" chip manages the signals it broadcasts when it
        // activates (A3.4): other formations' "Signal Received" triggers react to
        // them. Emit and receive share one vocabulary — the declared signals in the
        // SIGNALS footer — so a stage can only emit a signal that's been declared.
        private void OpenEmitPicker(PlannedFormationClass cls, int stageIndex)
        {
            var plan = _draft.Build();
            var stage = StageAt(plan, cls, stageIndex);
            if (stage == null)
                return;
            _pickerOptions.Clear();
            foreach (var s in stage.Emit.ToList())
            {
                var sig = s;
                AddPickerOption($"Stop emitting  '{sig}'", true, () =>
                { _draft.RemoveEmitSignal(cls, stageIndex, sig); EmitPicked(cls, stageIndex, $"stopped emitting '{sig}'"); });
            }
            foreach (var sig in plan.PlayerSignals)
            {
                if (stage.Emit.Any(e => string.Equals(e, sig, StringComparison.OrdinalIgnoreCase)))
                    continue;
                var s = sig;
                AddPickerOption($"Emit  '{s}'", false, () =>
                { _draft.EmitSignal(cls, stageIndex, s); EmitPicked(cls, stageIndex, $"emits '{s}'"); });
            }
            if (_pickerOptions.Count == 0)
                // No declared signals to emit — point the player at the footer manager.
                AddPickerOption("(no signals declared — add them in the SIGNALS footer first)", false, ClosePicker);
            PickerTitle = $"Formation {SlotNumber(cls)}  ·  Stage {stageIndex + 1}  ·  Emit on activate";
            PickerOpen = true;
        }

        private void EmitPicked(PlannedFormationClass cls, int stageIndex, string what)
        {
            StatusText = $"Formation {SlotNumber(cls)} stage {stageIndex + 1}: {what}.";
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
            // Capture the intended new value before the call: SetAbortConditions
            // mutates abort.* in place (abort IS the live plan.Abort), so reading
            // the field again afterwards for the status text would report it flipped.
            var nextBroken = !abort.OnFormationBroken;
            AddPickerOption(abort.OnFormationBroken ? "Abort if broken:  ON  →  turn off" : "Abort if broken:  off  →  turn on", abort.OnFormationBroken, () =>
            { _draft.SetAbortConditions(cls, onFormationBroken: nextBroken); AbortPicked(cls, $"abort-if-broken {(nextBroken ? "on" : "off")}"); });
            var nextCmdr = !abort.OnCommanderIncapacitated;
            AddPickerOption(abort.OnCommanderIncapacitated ? "Abort if commander down:  ON  →  turn off" : "Abort if commander down:  off  →  turn on", abort.OnCommanderIncapacitated, () =>
            { _draft.SetAbortConditions(cls, onCommanderIncapacitated: nextCmdr); AbortPicked(cls, $"abort-if-commander-down {(nextCmdr ? "on" : "off")}"); });
            PickerTitle = $"Formation {SlotNumber(cls)}  ·  Abort conditions";
            PickerOpen = true;
        }

        private void AbortPicked(PlannedFormationClass cls, string what)
        {
            StatusText = $"Formation {SlotNumber(cls)}: {what}.";
            ClosePicker();
            Refresh();
        }

        /// <summary>Common player-signal names offered by the manager (B9 caps at 4).
        /// Text input isn't practical in Gauntlet, so the player picks from presets.</summary>
        private static readonly string[] SignalPresets = { "advance", "charge", "retreat", "hold", "flank", "regroup", "rally", "fire" };

        // Clicking the SIGNALS footer manages the declared player signals: remove
        // an existing one or add a preset (the palette fires these in battle, B9).
        public void ExecuteEditSignals()
        {
            var plan = _draft.Build();
            _pickerOptions.Clear();
            foreach (var s in plan.PlayerSignals.ToList())
            {
                var sig = s;
                AddPickerOption($"Remove signal  '{sig}'", true, () =>
                { _draft.RemovePlayerSignal(sig); StatusText = $"Removed player signal '{sig}'."; ClosePicker(); Refresh(); });
            }
            if (plan.PlayerSignals.Count < 4)
            {
                foreach (var preset in SignalPresets)
                {
                    if (plan.PlayerSignals.Any(s => string.Equals(s, preset, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    var p = preset;
                    AddPickerOption($"Add signal  '{p}'", false, () =>
                    { _draft.DeclarePlayerSignal(p); StatusText = $"Declared player signal '{p}'."; ClosePicker(); Refresh(); });
                }
            }
            PickerTitle = "Player signals  ·  declare or remove  (max 4)";
            PickerOpen = true;
        }

        /// <summary>Curated tactical anchors offered by the manager. Text input isn't
        /// practical in Gauntlet, so anchors are placed from presets (relative to the
        /// formation's deployment / team center) until the interactive map lands (#38).
        /// Each is a (id, basis, forward m, right m) — forward is along the attack
        /// axis, right is positive to the right of it.</summary>
        private static readonly (string Id, AnchorBasis Basis, float Forward, float Right)[] AnchorPresets =
        {
            ("advance",     AnchorBasis.OwnStart,   60f,   0f),
            ("push",        AnchorBasis.OwnStart,  120f,   0f),
            ("hold-line",   AnchorBasis.OwnStart,    0f,   0f),
            ("fall-back",   AnchorBasis.OwnStart,  -40f,   0f),
            ("left-flank",  AnchorBasis.OwnStart,   40f, -60f),
            ("right-flank", AnchorBasis.OwnStart,   40f,  60f),
            ("center",      AnchorBasis.TeamCenter,  0f,   0f),
        };

        // Clicking the ANCHORS footer manages the plan's map anchors: remove an
        // existing one, or add a tactical preset. Anchors are the destinations/
        // reference points for Move To, Feign Retreat, Pull Back and Position
        // Reached — without one, those directives can't be aimed.
        public void ExecuteEditAnchors()
        {
            var plan = _draft.Build();
            _pickerOptions.Clear();
            foreach (var a in plan.Anchors.ToList())
            {
                var anchor = a;
                AddPickerOption($"Remove anchor  '{anchor.Id}'   ({DescribeAnchor(anchor)})", true, () =>
                { _draft.RemoveAnchor(anchor.Id); StatusText = $"Removed anchor '{anchor.Id}'."; ClosePicker(); Refresh(); });
            }
            foreach (var preset in AnchorPresets)
            {
                if (plan.Anchors.Any(a => string.Equals(a.Id, preset.Id, StringComparison.OrdinalIgnoreCase)))
                    continue;
                var p = preset;
                AddPickerOption($"Add anchor  '{p.Id}'   ({DescribeAnchor(new MapAnchor { Id = p.Id, Basis = p.Basis, Forward = p.Forward, Right = p.Right })})", false, () =>
                {
                    _draft.AddAnchor(new MapAnchor { Id = p.Id, Basis = p.Basis, Forward = p.Forward, Right = p.Right });
                    StatusText = $"Added anchor '{p.Id}'.";
                    ClosePicker();
                    Refresh();
                });
            }
            PickerTitle = "Map anchors  ·  add or remove";
            PickerOpen = true;
        }

        /// <summary>Short human description of an anchor's position for the picker/footer.</summary>
        private static string DescribeAnchor(MapAnchor a)
        {
            if (a.Basis == AnchorBasis.Scene)
                return "scene position";
            var baseName = a.Basis == AnchorBasis.TeamCenter ? "team center" : "deployment";
            var parts = new List<string>();
            if (System.Math.Abs(a.Forward) > 0.01f) parts.Add($"{(a.Forward >= 0 ? "+" : "")}{a.Forward:0} m fwd");
            if (System.Math.Abs(a.Right) > 0.01f) parts.Add($"{System.Math.Abs(a.Right):0} m {(a.Right >= 0 ? "right" : "left")}");
            return parts.Count == 0 ? baseName : $"{baseName}, " + string.Join(", ", parts);
        }

        private static IEnumerable<string> EnemyTargets()
        {
            yield return "Nearest";
            foreach (PlannedFormationClass c in Enum.GetValues(typeof(PlannedFormationClass)))
                yield return c.ToString();
        }

        // Friendly formation targets shown as "N — composition" ("1 — Infantry",
        // "2 — Mixed") rather than the slot's class name: with eight numbered
        // formations the class name is ambiguous and often wrong once troops are
        // mixed. The stored value stays the slot selector (the class name / "Player").
        private IEnumerable<(string Label, string Value)> FriendlyTargets()
        {
            yield return ("Player (you)", "Player");
            foreach (PlannedFormationClass c in Enum.GetValues(typeof(PlannedFormationClass)))
                yield return (FriendlyTargetLabel(c.ToString()), c.ToString());
        }

        /// <summary>"N — composition" for a friendly slot selector; passes "Player" through.</summary>
        private string FriendlyTargetLabel(string selector)
        {
            if (string.IsNullOrEmpty(selector) || string.Equals(selector, "Player", StringComparison.OrdinalIgnoreCase))
                return "Player (you)";
            if (Enum.TryParse<PlannedFormationClass>(selector, ignoreCase: true, out var cls))
            {
                var label = _compositionLabels.TryGetValue(cls, out var l) ? l : cls.ToString();
                return $"{(int)cls + 1} — {label}";
            }
            return selector;
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

        /// <summary>One-line inline help shown under each trigger type in the picker.</summary>
        private static string TriggerHelp(TriggerType t)
        {
            switch (t)
            {
                case TriggerType.BattleStart: return "Fires the moment the battle begins.";
                case TriggerType.EnemyCommits: return "Fires when the enemy advances into engagement range.";
                case TriggerType.EnemyWithinDistance: return "Fires when an enemy closes within a set distance.";
                case TriggerType.FriendlyWithinDistance: return "Fires when a friendly formation is within range.";
                case TriggerType.PositionReached: return "Fires when this formation reaches a map anchor.";
                case TriggerType.CasualtiesAbove: return "Fires once casualties pass a set percentage.";
                case TriggerType.TimerElapsed: return "Fires a set time after the stage began.";
                case TriggerType.SignalReceived: return "Fires when another stage emits the named signal.";
                case TriggerType.PlayerSignal: return "Fires when you press the signal in the palette.";
                case TriggerType.EnemyBroken: return "Fires when the targeted enemy formation routs.";
                default: return "";
            }
        }

        /// <summary>One-line inline help shown under each directive type in the picker.</summary>
        private static string DirectiveHelp(DirectiveType d)
        {
            switch (d)
            {
                case DirectiveType.Hold: return "Hold position in the chosen arrangement.";
                case DirectiveType.MoveTo: return "Advance to a map anchor and hold there.";
                case DirectiveType.Skirmish: return "Harass at range, keeping a standoff distance.";
                case DirectiveType.FeignRetreat: return "Withdraw to bait the enemy, then re-engage.";
                case DirectiveType.Charge: return "Charge the target straight into melee.";
                case DirectiveType.FlankArc: return "Sweep around a flank onto the target.";
                case DirectiveType.PullBack: return "Withdraw to an anchor, keeping order.";
                case DirectiveType.Screen: return "Shield a friendly formation, holding a gap.";
                case DirectiveType.Follow: return "Move with a friendly formation at an offset.";
                case DirectiveType.FireControl: return "Set ranged troops to hold fire or fire at will.";
                default: return "";
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
        [DataSourceProperty] public string TitleText { get => _titleText; set { if (value != _titleText) { _titleText = value; OnPropertyChangedWithValue(value, "TitleText"); } } }
        [DataSourceProperty] public string HintText { get => _hintText; set { if (value != _hintText) { _hintText = value; OnPropertyChangedWithValue(value, "HintText"); } } }
        [DataSourceProperty] public string SignalsText { get => _signalsText; set { if (value != _signalsText) { _signalsText = value; OnPropertyChangedWithValue(value, "SignalsText"); } } }
        [DataSourceProperty] public string AnchorsText { get => _anchorsText; set { if (value != _anchorsText) { _anchorsText = value; OnPropertyChangedWithValue(value, "AnchorsText"); } } }
        [DataSourceProperty] public string WarningsText { get => _warningsText; set { if (value != _warningsText) { _warningsText = value; OnPropertyChangedWithValue(value, "WarningsText"); } } }
        [DataSourceProperty] public string ErrorsText { get => _errorsText; set { if (value != _errorsText) { _errorsText = value; OnPropertyChangedWithValue(value, "ErrorsText"); } } }
        [DataSourceProperty] public string EmptyText { get => _emptyText; set { if (value != _emptyText) { _emptyText = value; OnPropertyChangedWithValue(value, "EmptyText"); } } }
        [DataSourceProperty] public string StatusText { get => _statusText; set { if (value != _statusText) { _statusText = value; OnPropertyChangedWithValue(value, "StatusText"); HasStatus = !string.IsNullOrEmpty(value); } } }
        [DataSourceProperty] public MBBindingList<FormationPlanItemVM> Formations { get => _formations; set { if (value != _formations) { _formations = value; OnPropertyChangedWithValue(value, "Formations"); } } }
        [DataSourceProperty] public bool PickerOpen { get => _pickerOpen; set { if (value != _pickerOpen) { _pickerOpen = value; OnPropertyChangedWithValue(value, "PickerOpen"); } } }
        [DataSourceProperty] public string PickerTitle { get => _pickerTitle; set { if (value != _pickerTitle) { _pickerTitle = value; OnPropertyChangedWithValue(value, "PickerTitle"); } } }
        [DataSourceProperty] public MBBindingList<PickerOptionVM> PickerOptions { get => _pickerOptions; set { if (value != _pickerOptions) { _pickerOptions = value; OnPropertyChangedWithValue(value, "PickerOptions"); } } }
        [DataSourceProperty] public MBBindingList<MapMarkerVM> MapMarkers { get => _mapMarkers; set { if (value != _mapMarkers) { _mapMarkers = value; OnPropertyChangedWithValue(value, "MapMarkers"); } } }
        [DataSourceProperty] public MBBindingList<MapMarkerVM> EnemyMarkers { get => _enemyMarkers; set { if (value != _enemyMarkers) { _enemyMarkers = value; OnPropertyChangedWithValue(value, "EnemyMarkers"); } } }
        [DataSourceProperty] public MBBindingList<MapMarkerVM> AnchorMarkers { get => _anchorMarkers; set { if (value != _anchorMarkers) { _anchorMarkers = value; OnPropertyChangedWithValue(value, "AnchorMarkers"); } } }
        [DataSourceProperty] public bool HasMap { get => _hasMap; set { if (value != _hasMap) { _hasMap = value; OnPropertyChangedWithValue(value, "HasMap"); } } }
        [DataSourceProperty] public bool ShowMapBody { get => _showMapBody; set { if (value != _showMapBody) { _showMapBody = value; OnPropertyChangedWithValue(value, "ShowMapBody"); } } }
        [DataSourceProperty] public bool ShowListBody { get => _showListBody; set { if (value != _showListBody) { _showListBody = value; OnPropertyChangedWithValue(value, "ShowListBody"); } } }
        [DataSourceProperty] public string MapToggleText { get => _mapToggleText; set { if (value != _mapToggleText) { _mapToggleText = value; OnPropertyChangedWithValue(value, "MapToggleText"); } } }
        [DataSourceProperty] public string SelectedText { get => _selectedText; set { if (value != _selectedText) { _selectedText = value; OnPropertyChangedWithValue(value, "SelectedText"); } } }
    }

    /// <summary>One marker on the battlefield map: its top-left pixel offset (design
    /// units) within the map area, a short label (formation number / anchor id), and
    /// an optional sub-label (composition). Absolute-positioned via PositionXOffset.</summary>
    public sealed class MapMarkerVM : ViewModel
    {
        private readonly Action _onSelect;
        private readonly string _baseColor;
        private readonly string _selectedColor;
        private bool _isSelected;
        private string _color;

        public MapMarkerVM(float x, float y, string label, string sub,
            string baseColor = "#2C3C2CDD", Action onSelect = null, bool isSelected = false)
        {
            X = x;
            Y = y;
            Label = label;
            Sub = sub ?? "";
            HasSub = !string.IsNullOrEmpty(Sub);
            _baseColor = baseColor;
            _selectedColor = "#6E9A3EFF";  // bright green highlight when selected
            _onSelect = onSelect;
            _isSelected = isSelected;
            _color = isSelected ? _selectedColor : baseColor;
        }

        [DataSourceProperty] public float X { get; }
        [DataSourceProperty] public float Y { get; }
        [DataSourceProperty] public string Label { get; }
        [DataSourceProperty] public string Sub { get; }
        [DataSourceProperty] public bool HasSub { get; }

        /// <summary>Fill color (bound by the friendly-marker template), brightened when selected.</summary>
        [DataSourceProperty]
        public string Color { get => _color; set { if (value != _color) { _color = value; OnPropertyChangedWithValue(value, "Color"); } } }

        [DataSourceProperty]
        public bool IsSelected
        {
            get => _isSelected;
            set { if (value != _isSelected) { _isSelected = value; OnPropertyChangedWithValue(value, "IsSelected"); Color = value ? _selectedColor : _baseColor; } }
        }

        /// <summary>Marker click (A2.6.1): selects/toggles this formation. No-op for enemy/anchor markers.</summary>
        public void ExecuteSelect() => _onSelect?.Invoke();
    }

    /// <summary>One selectable row in a picker menu. Type pickers carry a one-line
    /// description (inline help); value pickers leave it blank.</summary>
    public sealed class PickerOptionVM : ViewModel
    {
        private readonly Action _onSelect;

        public PickerOptionVM(string label, bool isCurrent, Action onSelect, string description = null)
        {
            _onSelect = onSelect;
            Label = label;
            IsCurrent = isCurrent;
            Description = description ?? "";
            HasDescription = !string.IsNullOrEmpty(Description);
        }

        public void ExecuteSelect() => _onSelect?.Invoke();

        [DataSourceProperty] public string Label { get; }
        [DataSourceProperty] public bool IsCurrent { get; }
        [DataSourceProperty] public string Description { get; }
        [DataSourceProperty] public bool HasDescription { get; }
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
            Action<PlannedFormationClass, int, int> editConditionType,
            Action<PlannedFormationClass, int> editDirective,
            Action<PlannedFormationClass, int, int> editConditionParam,
            Action<PlannedFormationClass, int, int> editConditionFormation,
            Action<PlannedFormationClass, int> editDirectiveParam,
            Action<PlannedFormationClass, int> editEmit,
            Action<PlannedFormationClass, int> addCondition,
            Action<PlannedFormationClass, int, int> removeCondition,
            Action<PlannedFormationClass> editAbort,
            Action<PlannedFormationClass> clearFormation,
            Action<PlannedFormationClass, int> moveStageUp,
            Action<PlannedFormationClass, int> moveStageDown,
            Action<PlannedFormationClass, int> duplicateStage,
            Action<PlannedFormationClass, int> toggleDirectiveOption)
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
            IsRemoveDisabled = formation.Stages.Count == 0;
            Stages = new MBBindingList<StageItemVM>();
            var count = formation.Stages.Count;
            for (var i = 0; i < count; i++)
            {
                var index = i; // capture for the per-stage picker closures
                var cls = _formation;
                Stages.Add(new StageItemVM(
                    i, count, formation.Stages[i],
                    condIdx => editConditionType?.Invoke(cls, index, condIdx),
                    condIdx => editConditionParam?.Invoke(cls, index, condIdx),
                    condIdx => editConditionFormation?.Invoke(cls, index, condIdx),
                    condIdx => removeCondition?.Invoke(cls, index, condIdx),
                    () => addCondition?.Invoke(cls, index),
                    () => editDirective?.Invoke(cls, index),
                    () => editDirectiveParam?.Invoke(cls, index),
                    () => editEmit?.Invoke(cls, index),
                    () => moveStageUp?.Invoke(cls, index),
                    () => moveStageDown?.Invoke(cls, index),
                    () => duplicateStage?.Invoke(cls, index),
                    () => toggleDirectiveOption?.Invoke(cls, index)));
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
        [DataSourceProperty] public bool IsRemoveDisabled { get; }
        [DataSourceProperty] public MBBindingList<StageItemVM> Stages { get; }
    }

    /// <summary>One stage row: number, the trigger as a list of ANDed "When"
    /// condition rows (each click-to-edit), the directive ("Do"), and emitted
    /// signals. The condition list lets a stage AND up to 3 atomic triggers (A3.5).</summary>
    public sealed class StageItemVM : ViewModel
    {
        private readonly Action _editDirective;
        private readonly Action _editDirectiveParam;
        private readonly Action _editEmit;
        private readonly Action _addCondition;
        private readonly Action _moveUp;
        private readonly Action _moveDown;
        private readonly Action _duplicate;
        private readonly Action _toggleDirectiveOption;

        public StageItemVM(int index, int stageCount, Stage stage,
            Action<int> editConditionType, Action<int> editConditionParam, Action<int> editConditionFormation, Action<int> removeCondition, Action addCondition,
            Action editDirective, Action editDirectiveParam, Action editEmit, Action moveUp, Action moveDown, Action duplicate,
            Action toggleDirectiveOption)
        {
            _editDirective = editDirective;
            _editDirectiveParam = editDirectiveParam;
            _editEmit = editEmit;
            _addCondition = addCondition;
            _moveUp = moveUp;
            _moveDown = moveDown;
            _duplicate = duplicate;
            _toggleDirectiveOption = toggleDirectiveOption;
            IsMoveUpDisabled = index <= 0;
            IsMoveDownDisabled = index >= stageCount - 1;
            NumberText = (index + 1).ToString();
            DirectiveText = "Do:  " + PlanFormatter.DescribeDirective(stage.Do);
            // The emit chip is always present (click to manage); its label shows the
            // broadcast signals, or a "+ emit signal" affordance when there are none.
            EmitText = stage.Emit.Count > 0 ? "→ emits  " + string.Join(", ", stage.Emit) : "+ emit signal";

            // One row per ANDed trigger condition; an empty When shows a placeholder
            // ("On battle start" for the opening stage, else a needs-a-trigger note).
            Conditions = new MBBindingList<ConditionItemVM>();
            for (var c = 0; c < stage.When.Count; c++)
            {
                var ci = c;
                Conditions.Add(new ConditionItemVM(ci, stage.When[ci],
                    () => editConditionType?.Invoke(ci),
                    () => editConditionParam?.Invoke(ci),
                    () => editConditionFormation?.Invoke(ci),
                    () => removeCondition?.Invoke(ci)));
            }
            ShowWhenEmpty = stage.When.Count == 0;
            WhenEmptyText = index == 0 ? "When:  On battle start" : "When:  (no trigger — add a condition)";
            CanAddCondition = stage.When.Count < PlanDraft.MaxTriggerConditions;
            AddConditionText = stage.When.Count == 0 ? "+ trigger condition" : "+ AND condition";

            (HasDirectiveParam, DirectiveParamLabel) = DirectiveParam(stage.Do);
            (HasDirectiveToggle, DirectiveToggleLabel) = DirectiveToggle(stage.Do);
        }

        public void ExecuteEditDirective() => _editDirective?.Invoke();
        public void ExecuteEditDirectiveParam() => _editDirectiveParam?.Invoke();
        public void ExecuteEditEmit() => _editEmit?.Invoke();
        public void ExecuteAddCondition() => _addCondition?.Invoke();
        public void ExecuteMoveUp() => _moveUp?.Invoke();
        public void ExecuteMoveDown() => _moveDown?.Invoke();
        public void ExecuteDuplicate() => _duplicate?.Invoke();
        public void ExecuteToggleDirectiveOption() => _toggleDirectiveOption?.Invoke();

        private static (bool has, string label) DirectiveParam(DirectiveSpec d)
        {
            if (d == null) return (false, "");
            switch (d.Type)
            {
                case DirectiveType.MoveTo:
                case DirectiveType.FeignRetreat:
                case DirectiveType.PullBack: return (true, string.IsNullOrEmpty(d.Anchor) ? "pick anchor" : d.Anchor);
                case DirectiveType.Skirmish: return (true, $"standoff {d.StandoffMeters ?? 60:0.#} m");
                case DirectiveType.Screen: return (true, $"guard {d.Target ?? "Player"}");
                case DirectiveType.FlankArc: return (true, (d.Side ?? FlankSide.Left).ToString());
                case DirectiveType.FireControl: return (true, (d.Fire ?? FireMode.Free).ToString());
                case DirectiveType.Hold: return (true, (d.Arrangement ?? Arrangement.Line).ToString());
                case DirectiveType.Charge: return (true, d.Target ?? "Nearest");
                case DirectiveType.Follow: return (true, d.Target ?? "Player");
                default: return (false, "");
            }
        }

        /// <summary>The optional secondary on/off flag some directives carry, and its chip label.</summary>
        private static (bool has, string label) DirectiveToggle(DirectiveSpec d)
        {
            if (d == null) return (false, "");
            switch (d.Type)
            {
                case DirectiveType.FeignRetreat: return (true, (d.FireWhileWithdrawing ?? false) ? "firing while withdrawing" : "not firing");
                case DirectiveType.FlankArc: return (true, (d.MissileOnly ?? false) ? "missile-only" : "may charge in");
                case DirectiveType.PullBack: return (true, (d.MaintainFacing ?? false) ? "facing the enemy" : "facing away");
                case DirectiveType.Skirmish: return (true, (d.Circle ?? false) ? "circling the enemy" : "holding at standoff");
                default: return (false, "");
            }
        }

        [DataSourceProperty] public string NumberText { get; }
        [DataSourceProperty] public MBBindingList<ConditionItemVM> Conditions { get; }
        [DataSourceProperty] public bool ShowWhenEmpty { get; }
        [DataSourceProperty] public string WhenEmptyText { get; }
        [DataSourceProperty] public bool CanAddCondition { get; }
        [DataSourceProperty] public string AddConditionText { get; }
        [DataSourceProperty] public string DirectiveText { get; }
        [DataSourceProperty] public string EmitText { get; }
        [DataSourceProperty] public bool HasDirectiveParam { get; }
        [DataSourceProperty] public string DirectiveParamLabel { get; }
        [DataSourceProperty] public bool HasDirectiveToggle { get; }
        [DataSourceProperty] public string DirectiveToggleLabel { get; }
        [DataSourceProperty] public bool IsMoveUpDisabled { get; }
        [DataSourceProperty] public bool IsMoveDownDisabled { get; }
    }

    /// <summary>One ANDed trigger condition in a stage's "When": its plain-language
    /// description as a click-to-edit type field, an optional value chip, and a
    /// remove (×) control. The first reads "When: …", the rest "AND …".</summary>
    public sealed class ConditionItemVM : ViewModel
    {
        private readonly Action _editType;
        private readonly Action _editParam;
        private readonly Action _editFormation;
        private readonly Action _remove;

        public ConditionItemVM(int index, TriggerSpec condition, Action editType, Action editParam, Action editFormation, Action remove)
        {
            _editType = editType;
            _editParam = editParam;
            _editFormation = editFormation;
            _remove = remove;
            RowText = (index == 0 ? "When:  " : "AND  ") + PlanFormatter.DescribeTrigger(condition);
            (HasParam, ParamLabel) = TriggerParam(condition);
            (HasFormation, FormationLabel) = TriggerFormation(condition);
        }

        public void ExecuteEditType() => _editType?.Invoke();
        public void ExecuteEditParam() => _editParam?.Invoke();
        public void ExecuteEditFormation() => _editFormation?.Invoke();
        public void ExecuteRemove() => _remove?.Invoke();

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

        /// <summary>The optional "which formation" selector some triggers carry, and its chip label.</summary>
        private static (bool has, string label) TriggerFormation(TriggerSpec t)
        {
            if (t == null) return (false, "");
            switch (t.Type)
            {
                case TriggerType.EnemyWithinDistance:
                case TriggerType.EnemyBroken: return (true, "vs " + (t.Formation ?? "any enemy"));
                case TriggerType.FriendlyWithinDistance: return (true, "of " + (t.Formation ?? "pick unit"));
                case TriggerType.EnemyCommits: return (true, "vs " + (t.Formation ?? "any unit"));
                case TriggerType.CasualtiesAbove: return (true, "of " + (t.Formation ?? "this unit"));
                default: return (false, "");
            }
        }

        [DataSourceProperty] public string RowText { get; }
        [DataSourceProperty] public bool HasParam { get; }
        [DataSourceProperty] public string ParamLabel { get; }
        [DataSourceProperty] public bool HasFormation { get; }
        [DataSourceProperty] public string FormationLabel { get; }
    }
}
