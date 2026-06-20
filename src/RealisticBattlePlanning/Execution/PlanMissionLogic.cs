using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using RealisticBattlePlanning.Diagnostics;
using RealisticBattlePlanning.Fidelity;
using RealisticBattlePlanning.Harness;
using RealisticBattlePlanning.Planning;
using RealisticBattlePlanning.Planning.Model;
using RealisticBattlePlanning.Progression;

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
        private readonly Dictionary<PlannedFormationClass, CommanderKey> _commanders = new();
        private BattlePlan _plan;
        private PlanMonitor _monitor;
        private FormationOrderExecutor _executor;

        /// <summary>
        /// The campaign's D4 progression service, resolved at deployment — null in
        /// Custom Battle / the harness (no campaign) and when fidelity is off (D
        /// progression is part of the fidelity subsystem; off means zero-touch, G3).
        /// When set, completed stages earn each commander Plan-Familiarity XP that
        /// raises the competence the fidelity model rolls against next time.
        /// </summary>
        private ProgressionService _progression;
        private bool _battleCounted;
        private bool _deploymentFinished;
        private bool _isHarnessRun;
        private bool _fidelityActive;
        private bool _ordersSubscribed;
        private float _sinceLastMonitorTick;

        /// <summary>
        /// The active mission's plan logic, for console commands (rbp.resume /
        /// rbp.plan_status / rbp.signal). Resolved live against Mission.Current
        /// each call, so it can never be a stale reference outliving its
        /// mission; the command-facing methods below handle the inert case.
        /// </summary>
        internal static PlanMissionLogic Active => Mission.Current?.GetMissionBehavior<PlanMissionLogic>();

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
                // The first battle of a session starts BLANK; later battles carry the plan that
                // was last applied (SessionPlanStore). The hand-written debug plan only loads when
                // explicitly enabled for dev — never by default — so a fresh game has no plan.
                _plan = harnessPlan
                        ?? SessionPlanStore.Current
                        ?? (DebugPlanLoader.Enabled ? DebugPlanLoader.TryLoad() : null);
                if (_plan == null)
                {
                    RbpLog.Info("No plan for this battle (blank start); plan logic stays inert until one is applied.");
                    return;
                }

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
                _fidelityActive = FidelityConfig.Enabled;
                _monitor = new PlanMonitor(_plan, FidelityConfig.CreateModel(), FidelityConfig.NextBattleSeed());
                if (_fidelityActive)
                    RbpLog.Info($"Fidelity: {FidelityConfig.Describe()}.");
                _executor = new FormationOrderExecutor();
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

                // Log each formation by NUMBER and its LIVE composition, not the
                // engine slot's class name. Bannerlord files troops into the eight
                // slots by its own classification, so e.g. the "HorseArcher" slot
                // can hold ranged infantry — a directive must target the slot that
                // actually holds the right troops (see the formation-slots-vs-
                // composition gotcha). Showing the real mix here makes that mismatch
                // impossible to miss when reading the log or authoring a plan.
                var liveComposition = UI.FormationReader.CompositionLabels(Mission.PlayerTeam);
                RbpLog.Info("Deployment finished. Player formations (number — live composition [slot]):");
                foreach (var formation in Mission.PlayerTeam.FormationsIncludingEmpty)
                {
                    if (formation.CountOfUnits == 0)
                        continue;
                    var slot = FormationClassMap.ToPlanned(formation.FormationIndex);
                    var comp = slot is { } s && liveComposition.TryGetValue(s, out var label) ? label : "?";
                    RbpLog.Info($"  Formation {(int)formation.FormationIndex + 1} — {comp} [{formation.FormationIndex} slot]: " +
                                $"{formation.CountOfUnits} unit(s), captain: {formation.Captain?.Name?.ToString() ?? "none"}");
                }

                AdoptPlannedFormations();
                if (_fidelityActive)
                    SetCommanderProfiles();
                SubscribeToPlayerOrders();
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] OnDeploymentFinished failed.", e);
            }
        }

        public override void OnRemoveBehavior()
        {
            try
            {
                var controller = Mission.PlayerTeam?.PlayerOrderController;
                if (controller != null)
                    controller.OnOrderIssued -= OnPlayerOrderIssued;
                _ordersSubscribed = false;
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] Unsubscribing from player orders failed.", e);
            }

            try
            {
                // D1 service record: count this battle once per commander who led a
                // governed formation in it. Guarded so a re-entrant teardown can't
                // double-count.
                if (_progression != null && _deploymentFinished && !_battleCounted)
                {
                    _battleCounted = true;
                    _progression.OnBattleConcluded(_commanders);
                }
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] Recording battles-under-command failed.", e);
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
            // Idempotent: ApplyPlan can call this post-deployment when the first
            // monitor is created from an edited plan, and the deployment path may
            // have already subscribed — a second '+=' would double-fire overrides.
            if (_monitor == null || _ordersSubscribed)
                return;

            var controller = Mission.PlayerTeam?.PlayerOrderController;
            if (controller == null)
            {
                RbpLog.Warn("No PlayerOrderController; manual overrides will not suspend plans this battle.");
                return;
            }

            controller.OnOrderIssued += OnPlayerOrderIssued;
            _ordersSubscribed = true;
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

                // D4: a completed stage earns its commander familiarity, a
                // skipped/aborted one trickles. Inert outside a campaign (_progression
                // null) and harmless if a formation has no mapped commander.
                _progression?.OnBattleEvents(events, _commanders);

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

            // The engine action for the event (orders / AI handoff). The
            // player-facing message is a single centralized bark below, so no case
            // emits its own Notify — that keeps the wording in one tested place (B11)
            // and guarantees no event is silently applied.
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

                case PlanResumed:
                    _executor.Adopt(formation);
                    break;

                case PlanAborted:
                    // B4: revert to vanilla AI.
                    formation.SetControlledByAI(true);
                    break;

                case PlanHolding:
                    _executor.Apply(formation, new ResolvedDirective(
                        new Planning.Model.DirectiveSpec { Type = Planning.Model.DirectiveType.Hold }, null, null));
                    break;

                case ChargeOrdered:
                    // A FlankArc (or other steering directive) that committed to a
                    // charge — hand it to the vanilla charge order.
                    _executor.Charge(formation);
                    break;

                // SignalEmitted, PlanSuspended, StageSkipped, StageCompleted,
                // ReactionDelayed carry no engine action here — their orders ride
                // the surrounding activation events; the bark is their visible effect.
            }

            EmitBark(planEvent, formation);
        }

        /// <summary>
        /// The one place a plan event becomes a battle message (B11/R2: attributed,
        /// and no deviation is silent). The wording lives in Core <see cref="CommanderBarks"/>
        /// (tested); the engine adds only the speaker's name and the suspend-event's
        /// resume affordance.
        /// </summary>
        private void EmitBark(PlanEvent planEvent, Formation formation)
        {
            var bark = CommanderBarks.Line(planEvent, CommanderName(formation));
            if (bark == null)
                return;
            if (planEvent is PlanSuspended)
                bark += $" (rbp.resume {planEvent.Formation})";
            Notify(bark);
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
        /// Replaces the active plan from the editor (Planning Mode "Apply").
        /// Rebuilds the monitor so the edited plan governs this battle; if
        /// deployment already finished, re-adopts the planned formations now.
        /// </summary>
        internal void ApplyPlan(BattlePlan newPlan)
        {
            if (newPlan == null)
                return;
            try
            {
                _plan = newPlan;
                // Carry the applied plan to the next battle of this session (spec Area G).
                SessionPlanStore.Current = newPlan;
                _fidelityActive = FidelityConfig.Enabled;
                _monitor = new PlanMonitor(_plan, FidelityConfig.CreateModel(), FidelityConfig.NextBattleSeed());
                RbpLog.Info("Plan applied from the editor:\n" + PlanFormatter.Describe(_plan));
                if (_deploymentFinished)
                {
                    AdoptPlannedFormations();
                    if (_fidelityActive)
                        SetCommanderProfiles();
                    // If no plan existed at deployment, the order-override
                    // subscription was skipped (monitor was null then); now that
                    // the editor has created one, subscribe (idempotent).
                    SubscribeToPlayerOrders();
                }
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] Applying the edited plan failed.", e);
            }
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

        /// <summary>
        /// Fidelity switch-on (D1): derive each governed formation's commander
        /// competence from its captain's vanilla Tactics/Leadership and hand it
        /// to the monitor, so the configured fidelity model rolls against the
        /// real officer. A formation with no captain (e.g. the harness
        /// auto-split) reads 0/0 -> Untrained. Only called when fidelity is on,
        /// so normal play stays zero-touch (G3).
        ///
        /// Also resolves the D4 progression seam: in a campaign it folds each
        /// commander's earned Plan-Familiarity XP into the profile (so a drilled
        /// officer rolls better than his raw stats), and records the
        /// formation-&gt;commander map this battle's events will accrue against.
        /// </summary>
        private void SetCommanderProfiles()
        {
            if (_monitor == null || _plan == null)
                return;

            // The harness must stay reproducible and fields no campaign heroes, so it
            // never accrues progression; real play resolves the campaign service
            // (null when this isn't a campaign — then it's stats-only, as before).
            _progression = _isHarnessRun ? null : CommanderProgressionBehavior.Current?.Service;
            _commanders.Clear();

            foreach (var formationPlan in _plan.Formations)
            {
                if (formationPlan.Stages.Count == 0)
                    continue;

                _initialCaptains.TryGetValue(formationPlan.Formation, out var captain);
                var character = captain?.Character;
                var tactics = character?.GetSkillValue(DefaultSkills.Tactics) ?? 0;
                var leadership = character?.GetSkillValue(DefaultSkills.Leadership) ?? 0;

                var key = CommanderKeyFor(captain);
                _commanders[formationPlan.Formation] = key;

                var profile = _progression != null
                    ? _progression.ProfileFor(key, tactics, leadership)
                    : CommanderProfile.FromStats(tactics, leadership);
                _monitor.SetCommander(formationPlan.Formation, profile);
                RbpLog.Info($"[{formationPlan.Formation}] commander {character?.Name?.ToString() ?? "(none)"}: " +
                            $"Tactics {tactics}, Leadership {leadership} -> {profile.Competence}" +
                            (_progression != null && key.IsPersistent ? " (with earned familiarity)" : "") + ".");
            }
        }

        /// <summary>
        /// A campaign captain's Character is a CharacterObject carrying a HeroObject
        /// with a stable id; a custom-battle basic-troop captain is a plain
        /// BasicCharacterObject (no hero) -> None, so it accrues and persists nothing.
        /// </summary>
        private static CommanderKey CommanderKeyFor(Agent captain)
        {
            var hero = (captain?.Character as CharacterObject)?.HeroObject;
            return hero != null ? new CommanderKey(hero.StringId) : CommanderKey.None;
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
