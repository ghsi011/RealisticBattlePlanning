using System;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using RealisticBattlePlanning.Diagnostics;
using RealisticBattlePlanning.Planning;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Execution
{
    /// <summary>
    /// The mission-side host of the battle plan. Iteration 1 scope: log the
    /// mission facts, load + validate the debug plan, and dump it — no orders
    /// are issued yet. The Plan Monitor (I2) builds on this class.
    /// </summary>
    public sealed class PlanMissionLogic : MissionLogic
    {
        private BattlePlan _plan;
        private bool _active;

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

                _active = true;

                _plan = DebugPlanLoader.TryLoad();
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
            }
            catch (Exception e)
            {
                RbpLog.Error("PlanMissionLogic.AfterStart failed; plan logic stays inert.", e);
                _plan = null;
            }
        }

        public override void OnDeploymentFinished()
        {
            base.OnDeploymentFinished();
            try
            {
                if (!_active || Mission.PlayerTeam == null)
                    return;

                RbpLog.Info("Deployment finished. Player formations:");
                foreach (var formation in Mission.PlayerTeam.FormationsIncludingEmpty)
                {
                    if (formation.CountOfUnits > 0)
                        RbpLog.Info($"  {formation.FormationIndex}: {formation.CountOfUnits} unit(s), captain: {formation.Captain?.Name?.ToString() ?? "none"}");
                }
            }
            catch (Exception e)
            {
                RbpLog.Error("PlanMissionLogic.OnDeploymentFinished logging failed.", e);
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
