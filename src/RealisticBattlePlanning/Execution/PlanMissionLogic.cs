using System;
using System.Collections.Generic;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using RealisticBattlePlanning.Diagnostics;
using RealisticBattlePlanning.Harness;
using RealisticBattlePlanning.Planning;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Execution
{
    /// <summary>
    /// The mission-side host of the battle plan: loads + validates the plan,
    /// feeds the Core PlanMonitor with snapshots a few times per second
    /// (spec B2), and turns its events into orders. Any failure disables the
    /// plan for the battle and logs a FAULT — never crashes the mission.
    /// </summary>
    public sealed class PlanMissionLogic : MissionLogic
    {
        /// <summary>~4 Hz: cheap, and well within trigger-latency needs (B2).</summary>
        private const float MonitorIntervalSeconds = 0.25f;

        /// <summary>
        /// Signal Palette keybinds (B9, R7: one input per signal). Numpad to
        /// avoid the vanilla battle keys (1-0 select formations, F-keys open
        /// order menus); MCM rebinding arrives with Area F.
        /// </summary>
        private static readonly InputKey[] PaletteKeys =
            { InputKey.Numpad1, InputKey.Numpad2, InputKey.Numpad3, InputKey.Numpad4 };

        /// <summary>
        /// Player orders that REDIRECT a formation (movement/targeting)
        /// suspend its plan (B5). Posture orders — arrangement, facing, fire
        /// control, mount, cohesion — pass through, so the player can tune how
        /// a formation fights without dropping it from the plan (the
        /// conductor, not micromanager, vision). Anything not in this set
        /// passes through.
        /// </summary>
        private static readonly HashSet<OrderType> OverridingOrders = new()
        {
            OrderType.Move, OrderType.MoveToLineSegment, OrderType.MoveToLineSegmentWithHorizontalLayout,
            OrderType.Charge, OrderType.ChargeWithTarget, OrderType.StandYourGround,
            OrderType.FollowMe, OrderType.FollowEntity, OrderType.Retreat,
            OrderType.AdvanceTenPaces, OrderType.FallBackTenPaces, OrderType.Advance, OrderType.FallBack,
            OrderType.AttackEntity, OrderType.PointDefence,
            // Explicitly handing a governed formation to the AI (or taking it
            // back) is a control change, not a posture tweak — it suspends.
            OrderType.AIControlOn, OrderType.AIControlOff,
        };

        private readonly Dictionary<PlannedFormationClass, int> _initialCounts = new();
        private readonly Dictionary<PlannedFormationClass, Agent> _initialCaptains = new();
        private BattlePlan _plan;
        private PlanMonitor _monitor;
        private FormationOrderExecutor _executor;
        private bool _deploymentFinished;
        private bool _isHarnessRun;
        private float _sinceLastMonitorTick;

        /// <summary>The live instance, for console commands (rbp.resume / rbp.plan_status).</summary>
        internal static PlanMissionLogic Current { get; private set; }

        /// <summary>The validated plan driving this mission; null when inert.</summary>
        internal BattlePlan ActivePlan => _monitor == null ? null : _plan;

        internal PlanMonitor Monitor => _monitor;

        /// <summary>
        /// Raised after each monitor tick with exactly what the monitor saw
        /// and decided — the harness recorder's feed (no parallel engine reads).
        /// </summary>
        internal event Action<IBattlefieldSnapshot, IReadOnlyList<PlanEvent>> MonitorTicked;

        /// <summary>
        /// Raised when a fault disables the plan mid-battle, so a harness
        /// run over this mission can mark its record invalid (R2: a crashed
        /// run must never read as a genuine scenario outcome).
        /// </summary>
        internal event Action<string> MonitorFaulted;

        public override void AfterStart()
        {
            base.AfterStart();
            try
            {
                LogMissionFacts();

                if (!PlannableMission.CheckAfterStart(Mission, out var reason))
                {
                    RbpLog.Info($"Plan logic stays inert: {reason}.");
                    return;
                }

                var harnessPlan = HarnessSession.PlanForNextBattle();
                _isHarnessRun = harnessPlan != null;
                _plan = harnessPlan ?? DebugPlanLoader.TryLoad();
                if (_plan == null)
                    return;

                var validation = PlanValidator.Validate(_plan);
                foreach (var warning in validation.Warnings)
                    RbpLog.Warn($"Plan: {warning}");
                foreach (var error in validation.Errors)
                    RbpLog.Error($"Plan: {error}");

                if (!validation.IsValid)
                {
                    RbpLog.Error("Debug plan rejected; plan logic stays inert this battle.");
                    _plan = null;
                    return;
                }

                RbpLog.Info(PlanFormatter.Describe(_plan));
                _monitor = new PlanMonitor(_plan);
                _executor = new FormationOrderExecutor(Mission);
                Current = this;
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] PlanMissionLogic.AfterStart failed; plan logic stays inert.", e);
                _plan = null;
                _monitor = null;
            }
        }

        public override void OnDeploymentFinished()
        {
            base.OnDeploymentFinished();
            try
            {
                _deploymentFinished = true;

                if (Mission.PlayerTeam == null)
                    return;

                // Harness runs auto-fill the scenario's formation slots so the
                // pack needs no manual Order-of-Battle setup (e.g. A6's four
                // slots). Never fires in normal play — only on an armed run.
                if (_isHarnessRun)
                    ApplySplit(PlannedFormationClasses());

                RbpLog.Info("Deployment finished. Player formations:");
                foreach (var formation in Mission.PlayerTeam.FormationsIncludingEmpty)
                {
                    if (formation.CountOfUnits > 0)
                        RbpLog.Info($"  {formation.FormationIndex}: {formation.CountOfUnits} unit(s), captain: {formation.Captain?.Name?.ToString() ?? "none"}");
                }

                AdoptPlannedFormations();
                SubscribeToPlayerOrders();
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] OnDeploymentFinished failed.", e);
            }
        }

        public override void OnRemoveBehavior()
        {
            if (Current == this)
                Current = null;
            try
            {
                var controller = Mission.PlayerTeam?.PlayerOrderController;
                if (controller != null)
                    controller.OnOrderIssued -= OnPlayerOrderIssued;
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] Unsubscribing from player orders failed.", e);
            }
            base.OnRemoveBehavior();
        }

        /// <summary>
        /// Override detection (B5): the player's orders flow through
        /// Team.PlayerOrderController; our own executor issues orders
        /// directly on Formation, so anything arriving here is genuinely
        /// player-issued. No Harmony needed.
        /// </summary>
        private void SubscribeToPlayerOrders()
        {
            if (_monitor == null)
                return;

            var controller = Mission.PlayerTeam?.PlayerOrderController;
            if (controller == null)
            {
                RbpLog.Warn("No PlayerOrderController; manual overrides will not suspend plans this battle.");
                return;
            }

            controller.OnOrderIssued += OnPlayerOrderIssued;
        }

        private void OnPlayerOrderIssued(OrderType orderType, MBReadOnlyList<Formation> appliedFormations, OrderController orderController, params object[] delegateParams)
        {
            try
            {
                if (_monitor == null || appliedFormations == null)
                    return;

                foreach (var formation in appliedFormations)
                {
                    var planned = FormationClassMap.ToPlanned(formation.FormationIndex);
                    if (planned is not { } cls || !_monitor.Governs(cls))
                        continue;
                    if (_monitor.GetMode(cls) != FormationPlanMode.Active)
                        continue;

                    // Posture tweaks (arrangement, facing, fire) don't redirect
                    // the formation, so they leave the plan running (B5 reading
                    // for the conductor vision).
                    if (!OverridingOrders.Contains(orderType))
                    {
                        RbpLog.Info($"[{cls}] posture order ({orderType}); plan continues.");
                        continue;
                    }

                    RbpLog.Info($"[{cls}] player order ({orderType}); suspending its plan.");
                    _monitor.NotifyPlayerOverride(cls);
                }
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] Player-order handler failed; overrides may not suspend plans.", e);
            }
        }

        /// <summary>Console-facing resume (B5). Returns a user-readable response.</summary>
        internal string RequestResume(string formationArg)
        {
            if (_monitor == null)
                return "no plan is active this battle";

            if (string.Equals(formationArg, "all", StringComparison.OrdinalIgnoreCase))
            {
                var any = false;
                foreach (var (planned, _) in FormationClassMap.All)
                {
                    if (_monitor.Governs(planned) && _monitor.GetMode(planned) == FormationPlanMode.Suspended)
                    {
                        _monitor.RequestResume(planned);
                        any = true;
                    }
                }
                return any ? "resuming all suspended formations" : "nothing is suspended";
            }

            // ParseClass, not Enum.TryParse: the latter accepts numeric
            // strings ("3", out-of-range "99") as enum values.
            if (FormationSelector.ParseClass(formationArg) is not { } cls)
                return $"unknown formation '{formationArg}'";
            if (!_monitor.Governs(cls))
                return $"{cls} has no plan this battle";

            switch (_monitor.GetMode(cls))
            {
                case FormationPlanMode.Suspended:
                    _monitor.RequestResume(cls);
                    return $"resuming {cls}";
                case FormationPlanMode.Aborted:
                    return $"{cls}'s plan aborted; it cannot resume";
                default:
                    return $"{cls} is not suspended";
            }
        }

        internal string DescribePlanStates()
        {
            if (_monitor == null)
                return "no plan is active this battle";

            var lines = new List<string>();
            foreach (var (planned, _) in FormationClassMap.All)
            {
                if (_monitor.Governs(planned))
                    lines.Add($"{planned}: {_monitor.GetMode(planned)}");
            }
            return string.Join("\n", lines);
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (_monitor == null)
                return;

            PollSignalPalette();

            _sinceLastMonitorTick += dt;
            if (_sinceLastMonitorTick < MonitorIntervalSeconds)
                return;
            _sinceLastMonitorTick = 0f;

            try
            {
                var snapshot = MissionSnapshot.Capture(Mission, _deploymentFinished, _initialCounts, _initialCaptains);
                var events = _monitor.Tick(snapshot);
                foreach (var planEvent in events)
                {
                    RbpLog.Info(planEvent.Describe());
                    ApplyEvent(planEvent);
                }

                MonitorTicked?.Invoke(snapshot, events);
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] Plan monitor tick failed; plan disabled for this battle.", e);
                _monitor = null;
                try
                {
                    MonitorFaulted?.Invoke($"plan monitor tick failed mid-battle: {e.Message}");
                }
                catch (Exception faultHandler)
                {
                    RbpLog.Error("[FAULT] MonitorFaulted handler failed.", faultHandler);
                }
            }
        }

        private void ApplyEvent(PlanEvent planEvent)
        {
            var formation = Mission.PlayerTeam?.GetFormation(FormationClassMap.ToEngine(planEvent.Formation));
            if (formation == null || formation.CountOfUnits == 0)
            {
                RbpLog.Warn($"[{planEvent.Formation}] has no units; event ignored.");
                return;
            }

            switch (planEvent)
            {
                case StageActivated stageActivated:
                    _executor.Apply(formation, stageActivated.Directive);
                    break;

                case MoveTargetChanged moveTarget:
                    _executor.Move(formation, moveTarget.Target);
                    break;

                case SteeringTargetChanged steering:
                    _executor.Move(formation, steering.Target);
                    break;

                case SignalEmitted:
                    // Logged above; the signal bus wires receipt in I4.
                    break;

                case PlanSuspended:
                    // The player's manual order stands; we only stop issuing
                    // plan orders. Vanilla already flags the formation
                    // player-controlled when an order is given.
                    Notify($"{CommanderName(formation)}: holding your order, plan suspended. (rbp.resume {planEvent.Formation})");
                    break;

                case PlanResumed:
                    _executor.Adopt(formation);
                    Notify($"{CommanderName(formation)}: resuming the plan.");
                    break;

                case PlanAborted aborted:
                    // B4: revert to vanilla AI with a notification.
                    formation.SetControlledByAI(true);
                    Notify($"{CommanderName(formation)}: plan aborted - {aborted.Reason}. Reverting to standard behavior.");
                    break;

                case StageSkipped:
                    // Logged above (B11); the follow-up activation/hold event
                    // carries the orders.
                    break;

                case PlanHolding:
                    _executor.Apply(formation, new ResolvedDirective(
                        new Planning.Model.DirectiveSpec { Type = Planning.Model.DirectiveType.Hold }, null, null));
                    Notify($"{CommanderName(formation)}: no executable orders remain; holding position.");
                    break;
            }
        }

        /// <summary>
        /// Key edges must be sampled per frame (not at monitor cadence).
        /// Up to four IsKeyReleased calls; nothing else runs unless a
        /// declared signal fires.
        /// </summary>
        private void PollSignalPalette()
        {
            if (!_deploymentFinished || _plan.PlayerSignals.Count == 0)
                return;

            try
            {
                for (var i = 0; i < _plan.PlayerSignals.Count && i < PaletteKeys.Length; i++)
                {
                    if (Input.IsKeyReleased(PaletteKeys[i]))
                        FirePlayerSignal(_plan.PlayerSignals[i]);
                }
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] Signal palette polling failed; use rbp.signal as the fallback.", e);
            }
        }

        /// <summary>
        /// B9: routes a player signal through the same latched bus as
        /// stage-emitted signals. Also the C7 drill-cue entry point.
        /// </summary>
        internal string FirePlayerSignal(string signal)
        {
            if (_monitor == null)
                return "no plan is active this battle";

            _monitor.RaiseExternalSignal(signal); // RbpLog entry happens in the bus
            Notify($"Signal '{signal}' raised.");

            var declared = false;
            foreach (var declaredSignal in _plan.PlayerSignals)
            {
                if (string.Equals(declaredSignal, signal, StringComparison.OrdinalIgnoreCase))
                    declared = true;
            }
            return declared
                ? $"signal '{signal}' raised"
                : $"signal '{signal}' raised (note: not a declared player signal of this plan)";
        }

        private static string CommanderName(Formation formation)
            => formation.Captain?.Name?.ToString() ?? formation.FormationIndex.ToString();

        /// <summary>B11: every perceivable plan event gets a battle message.</summary>
        private static void Notify(string message)
        {
            try
            {
                InformationManager.DisplayMessage(
                    new InformationMessage(message, Colors.Yellow));
            }
            catch (Exception)
            {
                // Battle messages must never take the plan down.
            }
        }

        /// <summary>The classes this plan actually drives (those with stages).</summary>
        private List<PlannedFormationClass> PlannedFormationClasses()
        {
            var list = new List<PlannedFormationClass>();
            if (_plan == null)
                return list;
            foreach (var formationPlan in _plan.Formations)
            {
                if (formationPlan.Stages.Count > 0)
                    list.Add(formationPlan.Formation);
            }
            return list;
        }

        /// <summary>
        /// Console-facing troop redistribution (rbp.harness_split): fills the
        /// active plan's formation slots so a multi-formation scenario needs no
        /// manual Order-of-Battle setup. Dev/test affordance.
        /// </summary>
        internal string SplitTroopsForPlan()
        {
            if (_monitor == null || _plan == null || Mission.PlayerTeam == null)
                return "no plan is active this battle";

            var planned = PlannedFormationClasses();
            if (planned.Count == 0)
                return "the active plan has no formations to fill";

            var moved = ApplySplit(planned);
            if (_deploymentFinished)
                AdoptPlannedFormations(); // re-capture counts + adopt the now-filled slots
            return $"spread {moved} unit(s) across {planned.Count} formation(s): {string.Join(", ", planned)}";
        }

        private int ApplySplit(List<PlannedFormationClass> planned)
        {
            if (planned.Count == 0)
                return 0;
            var targets = planned.ConvertAll(FormationClassMap.ToEngine);
            var moved = FormationSplitter.SpreadAcross(Mission.PlayerTeam, targets, Mission.MainAgent);
            RbpLog.Info($"Harness split: redistributed {moved} unit(s) across {string.Join(", ", planned)}.");
            return moved;
        }

        /// <summary>
        /// Only formations that actually have a plan are touched — zero-touch
        /// guarantee (G3) for everything else.
        /// </summary>
        private void AdoptPlannedFormations()
        {
            if (_plan == null || _monitor == null || Mission.PlayerTeam == null)
                return;

            // Casualty percentages are measured against deployment-end strength.
            foreach (var (planned, engine) in FormationClassMap.All)
            {
                var initial = Mission.PlayerTeam.GetFormation(engine);
                if (initial is { CountOfUnits: > 0 })
                {
                    _initialCounts[planned] = initial.CountOfUnits;
                    if (initial.Captain != null)
                        _initialCaptains[planned] = initial.Captain;
                }
            }

            foreach (var formationPlan in _plan.Formations)
            {
                if (formationPlan.Stages.Count == 0)
                    continue;

                var formation = Mission.PlayerTeam.GetFormation(FormationClassMap.ToEngine(formationPlan.Formation));
                if (formation is { CountOfUnits: > 0 })
                {
                    _executor.Adopt(formation);
                    RbpLog.Info($"[{formationPlan.Formation}] adopted by the plan (team AI suppressed).");
                }
                else
                {
                    RbpLog.Warn($"[{formationPlan.Formation}] is planned but has no units this battle.");
                }
            }
        }

        private void LogMissionFacts()
        {
            RbpLog.Info(
                $"Mission attached: scene '{Mission.SceneName}', mode {Mission.Mode}, " +
                $"fieldBattle={Mission.IsFieldBattle}, playerTeam={(Mission.PlayerTeam != null ? "yes" : "no")}, " +
                $"playerGeneral={Mission.PlayerTeam?.IsPlayerGeneral ?? false}");
        }
    }
}
