using System;
using RealisticBattlePlanning.Diagnostics;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Planning.Editing;
using RealisticBattlePlanning.Planning.Model;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.Data;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

namespace RealisticBattlePlanning.UI
{
    /// <summary>
    /// Planning Mode editor (spec A1/A3): during the deployment phase of a
    /// plannable battle, the toggle key opens an interactive panel over a deep
    /// copy of the loaded plan (PlanDraft). The player adds/removes formations
    /// and stages and clicks Apply to commit — at which point the edited plan
    /// governs this battle. Edits never touch the live plan until Apply, so
    /// closing without applying discards them. The layer takes input focus
    /// while open (deployment is already paused, A1.1) and releases it on
    /// close. Every Gauntlet call is guarded — a UI fault must never take the
    /// mission down.
    /// </summary>
    public sealed class PlanningModeView : MissionView
    {
        private const string MovieName = "RbpPlanningPanel";

        /// <summary>Toggle key. Numpad0 to avoid the deployment hotkeys; MCM rebinding arrives with Area F.</summary>
        private static readonly InputKey ToggleKey = InputKey.Numpad0;

        /// <summary>Gauntlet layer local order — high so the editor sits above the deployment HUD for input and rendering.</summary>
        private const int PlanningLayerOrder = 1000;

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

                // Edit a deep copy: in-progress edits are discarded unless the
                // player clicks Apply, which commits the built plan back to the
                // mission (rebuilding the monitor) via ApplyPlan.
                var draft = PlanDraft.EditingCopyOf(_planLogic?.ActivePlan);
                // Label each numbered formation by its live troop composition so
                // the cards read "1 — Ranged-Infantry", not the slot's class name.
                var labels = FormationReader.CompositionLabels(Mission?.PlayerTeam);
                _dataSource = new PlanningModeVM("Battle Plan", $"{ToggleKey} to close", draft, ApplyEditedPlan, Hide, labels);
                // High local order so the layer sits above the deployment UI for
                // both rendering and input (the deployment HUD/order layers are
                // low-order; a focus layer underneath them never gets clicks).
                _layer = new GauntletLayer("RbpPlanningLayer", PlanningLayerOrder, false);

                // Make the panel interactive — this is the exact setup the game's
                // own MissionGauntletEscapeMenuBase uses for an in-mission overlay:
                // focus the layer, claim mouse + keyboard (InputUsageMask.All), then
                // attach and take focus. (For buttons to actually fire, the widgets
                // themselves must also set DoNotPassEventsToChildren — see the prefab.)
                _layer.IsFocusLayer = true;
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                _movie = _layer.LoadMovie(MovieName, _dataSource);
                _screen.AddLayer(_layer);
                ScreenManager.TrySetFocus(_layer);

                _shown = true;
                RbpLog.Info("Planning Mode opened.");
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] Opening Planning Mode failed.", e);
                Hide();
            }
        }

        // Apply callback handed to the VM: commit the edited plan to the
        // mission so it governs this battle. Guarded — an apply fault must not
        // crash the panel or the mission.
        private void ApplyEditedPlan(BattlePlan plan)
        {
            try
            {
                _planLogic?.ApplyPlan(plan);
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] Applying the edited plan failed.", e);
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
                    // Release input focus before tearing the layer down, so the
                    // deployment screen regains control of the mouse/keyboard.
                    _layer.InputRestrictions.ResetInputRestrictions();
                    _layer.IsFocusLayer = false;
                    ScreenManager.TryLoseFocus(_layer);
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
