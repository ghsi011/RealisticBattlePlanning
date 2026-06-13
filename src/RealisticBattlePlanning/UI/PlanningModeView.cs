using System;
using RealisticBattlePlanning.Diagnostics;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Planning;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.Data;
using TaleWorlds.InputSystem;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;

namespace RealisticBattlePlanning.UI
{
    /// <summary>
    /// Planning Mode panel (spec A1/A3): during the deployment phase of a
    /// plannable battle, the toggle key shows a read-only view of the loaded
    /// plan. First slice of the editor UI — it proves deployment-phase
    /// Gauntlet injection works; the PlanDraft-backed editing widgets build on
    /// it. Deployment is already paused, so no time control is needed yet
    /// (A1.1). Every Gauntlet call is guarded — a UI fault must never take the
    /// mission down.
    /// </summary>
    public sealed class PlanningModeView : MissionView
    {
        private const string MovieName = "RbpPlanningPanel";

        /// <summary>Toggle key. Numpad0 to avoid the deployment hotkeys; MCM rebinding arrives with Area F.</summary>
        private static readonly InputKey ToggleKey = InputKey.Numpad0;

        private PlanMissionLogic _planLogic;
        private PlanningModeVM _dataSource;
        private GauntletLayer _layer;
        private GauntletMovieIdentifier _movie;
        private bool _shown;

        public PlanningModeView()
        {
            ViewOrderPriority = 25;
        }

        public override void OnMissionScreenInitialize()
        {
            base.OnMissionScreenInitialize();
            _planLogic = Mission.GetMissionBehavior<PlanMissionLogic>();
            RbpLog.Info($"Planning Mode view ready (press {ToggleKey} during deployment).");
        }

        public override void OnMissionScreenFinalize()
        {
            base.OnMissionScreenFinalize();
            Hide();
            _planLogic = null;
        }

        public override void OnMissionScreenTick(float dt)
        {
            base.OnMissionScreenTick(dt);

            // Planning is a deployment-phase activity (A1.1); once the battle
            // starts, the panel closes — the in-battle surface is the HUD (B7).
            if (Mission.Mode != MissionMode.Deployment)
            {
                if (_shown)
                    Hide();
                return;
            }

            if (TaleWorlds.InputSystem.Input.IsKeyPressed(ToggleKey))
                Toggle();
        }

        private void Toggle()
        {
            if (_shown)
                Hide();
            else
                Show();
        }

        private void Show()
        {
            if (_shown)
                return;
            try
            {
                _dataSource = new PlanningModeVM("— Battle Plan —", BuildSummary());
                _layer = new GauntletLayer("RbpPlanningLayer", ViewOrderPriority);
                _movie = _layer.LoadMovie(MovieName, _dataSource);
                MissionScreen.AddLayer(_layer);
                _shown = true;
                RbpLog.Info("Planning Mode opened.");
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] Opening Planning Mode failed.", e);
                Hide();
            }
        }

        private void Hide()
        {
            if (!_shown && _layer == null)
                return;
            try
            {
                if (_layer != null)
                {
                    if (_movie != null)
                        _layer.ReleaseMovie(_movie);
                    MissionScreen.RemoveLayer(_layer);
                }
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] Closing Planning Mode failed.", e);
            }
            finally
            {
                _movie = null;
                _layer = null;
                _dataSource = null;
                _shown = false;
            }
        }

        private string BuildSummary()
        {
            var plan = _planLogic?.ActivePlan;
            return plan == null
                ? "No plan is loaded for this battle.\n(Drop an rbp_debug_plan.json in ModuleData; in-panel authoring lands next.)"
                : PlanFormatter.Describe(plan);
        }
    }
}
