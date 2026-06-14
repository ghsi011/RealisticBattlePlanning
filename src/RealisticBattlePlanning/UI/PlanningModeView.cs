using System;
using RealisticBattlePlanning.Diagnostics;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Planning;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.Data;
using TaleWorlds.InputSystem;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

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

        /// <summary>
        /// The active mission's planning view, for the rbp.plan console command
        /// (input-independent toggle). Resolved live against Mission.Current, so
        /// it is never a stale reference from a previous mission.
        /// </summary>
        internal static PlanningModeView Active => Mission.Current?.GetMissionBehavior<PlanningModeView>();

        private PlanMissionLogic _planLogic;
        private PlanningModeVM _dataSource;
        private GauntletLayer _layer;
        private GauntletMovieIdentifier _movie;
        private MissionScreen _screen;
        private bool _shown;

        public PlanningModeView()
        {
            ViewOrderPriority = 25;
        }

        public override void OnMissionScreenInitialize()
        {
            base.OnMissionScreenInitialize();
            _planLogic = Mission.GetMissionBehavior<PlanMissionLogic>();
            RbpLog.Info($"Planning Mode view ready (press {ToggleKey}, or run rbp.plan).");
        }

        public override void OnMissionScreenFinalize()
        {
            base.OnMissionScreenFinalize();
            Teardown();
        }

        public override void OnRemoveBehavior()
        {
            // Belt-and-braces: the screen-finalize path normally runs, but a
            // behavior removal that bypasses it must not leak the Gauntlet
            // layer. Both paths are idempotent.
            Teardown();
            base.OnRemoveBehavior();
        }

        private void Teardown()
        {
            Hide();
            _planLogic = null;
        }

        // Polled on the MissionBehavior tick, not the screen tick: the screen
        // does not tick views during the deployment phase, but the behavior
        // tick runs throughout (the Signal Palette reads keys here too).
        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            try
            {
                if (TaleWorlds.InputSystem.Input.IsKeyPressed(ToggleKey))
                    Toggle();
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] Planning Mode key poll failed.", e);
            }
        }

        internal void Toggle()
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
                // The view's MissionScreen property can be null: when we add
                // the view at OnMissionBehaviorInitialize, the screen's
                // RegisterView pass (which sets it) has already run, so only
                // OnMissionScreenInitialize fires on us. The setter is
                // internal, so resolve the live screen instead.
                _screen = MissionScreen ?? ScreenManager.TopScreen as MissionScreen;
                if (_screen == null)
                {
                    RbpLog.Error("[FAULT] Planning Mode: no MissionScreen to attach the panel to.");
                    return;
                }

                var plan = _planLogic?.ActivePlan;
                // Non-blocking feasibility warnings (A3.8) surface in the footer.
                var validation = plan != null ? PlanValidator.Validate(plan) : null;
                _dataSource = new PlanningModeVM("Battle Plan", $"{ToggleKey} to close", plan, validation);
                _layer = new GauntletLayer("RbpPlanningLayer", ViewOrderPriority);
                _movie = _layer.LoadMovie(MovieName, _dataSource);
                _screen.AddLayer(_layer);
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
                    _screen?.RemoveLayer(_layer);
                }
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] Closing Planning Mode failed.", e);
            }
            finally
            {
                _dataSource?.OnFinalize();
                _movie = null;
                _layer = null;
                _screen = null;
                _dataSource = null;
                _shown = false;
            }
        }

    }
}
