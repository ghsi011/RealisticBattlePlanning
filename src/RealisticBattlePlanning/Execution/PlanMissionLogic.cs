using System;
using System.Collections.Generic;
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

        private readonly Dictionary<PlannedFormationClass, int> _initialCounts = new();
        private BattlePlan _plan;
        private PlanMonitor _monitor;
        private FormationOrderExecutor _executor;
        private bool _deploymentFinished;
        private float _sinceLastMonitorTick;

        /// <summary>The validated plan driving this mission; null when inert.</summary>
        internal BattlePlan ActivePlan => _monitor == null ? null : _plan;

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

                _plan = HarnessSession.PlanForNextBattle() ?? DebugPlanLoader.TryLoad();
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

                RbpLog.Info("Deployment finished. Player formations:");
                foreach (var formation in Mission.PlayerTeam.FormationsIncludingEmpty)
                {
                    if (formation.CountOfUnits > 0)
                        RbpLog.Info($"  {formation.FormationIndex}: {formation.CountOfUnits} unit(s), captain: {formation.Captain?.Name?.ToString() ?? "none"}");
                }

                AdoptPlannedFormations();
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] OnDeploymentFinished failed.", e);
            }
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (_monitor == null)
                return;

            _sinceLastMonitorTick += dt;
            if (_sinceLastMonitorTick < MonitorIntervalSeconds)
                return;
            _sinceLastMonitorTick = 0f;

            try
            {
                var snapshot = MissionSnapshot.Capture(Mission, _deploymentFinished, _initialCounts);
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

                case SignalEmitted:
                    // Logged above; the signal bus wires receipt in I4.
                    break;
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
                    _initialCounts[planned] = initial.CountOfUnits;
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
