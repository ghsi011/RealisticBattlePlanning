using System;
using RealisticBattlePlanning.Diagnostics;
using RealisticBattlePlanning.Execution;
using TaleWorlds.Core;
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
    /// Always-on mission HUD for the planning loop's discoverability (A1.1,
    /// B9, B5): a "Battle Plan" button during deployment (the keybind's
    /// on-screen twin), the Numpad1-4 signal legend once the battle runs, and
    /// a Resume chip while any formation is player-overridden. A non-focus,
    /// mouse-only Gauntlet layer: clicks on the chips work, everything else
    /// passes through to the game. Every Gauntlet call is guarded — a HUD
    /// fault must never take the mission down.
    /// </summary>
    public sealed class RbpHudView : MissionView
    {
        private const string MovieName = "RbpMissionHud";
        /// <summary>Below the planner (1000): the editor must cover the HUD when open.</summary>
        private const int HudLayerOrder = 140;
        private const float PollIntervalSeconds = 0.3f;

        private PlanMissionLogic _planLogic;
        private RbpHudVM _dataSource;
        private GauntletLayer _layer;
        private GauntletMovieIdentifier _movie;
        private MissionScreen _screen;
        private float _sincePoll;
        private string _pillsSignature = "";
        private bool _created;

        public RbpHudView()
        {
            ViewOrderPriority = 24; // just under PlanningModeView (25)
        }

        public override void OnMissionScreenInitialize()
        {
            base.OnMissionScreenInitialize();
            _planLogic = Mission.GetMissionBehavior<PlanMissionLogic>();
        }

        public override void OnMissionScreenFinalize()
        {
            base.OnMissionScreenFinalize();
            Teardown();
        }

        public override void OnRemoveBehavior()
        {
            Teardown();
            base.OnRemoveBehavior();
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            _sincePoll += dt;
            if (_sincePoll < PollIntervalSeconds)
                return;
            _sincePoll = 0f;

            try
            {
                UpdateHud();
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] HUD update failed; HUD disabled this mission.", e);
                Teardown();
                _planLogic = null; // stops future ticks from re-creating it
            }
        }

        private void UpdateHud()
        {
            if (_planLogic is not { Plannable: true })
                return;

            if (!_created)
                Create();
            if (_dataSource == null)
                return;

            var deployment = Mission.Mode == MissionMode.Deployment;
            var plannerOpen = PlanningModeView.Active?.IsShown == true;

            _dataSource.ShowEntry = deployment && !plannerOpen;
            if (_dataSource.ShowEntry)
                _dataSource.EntryText = "Battle Plan  (Numpad0)";

            // Signal legend: appears once the battle runs and the plan wired
            // numpad signals. Rebuilt only when membership or fired-state changes.
            var signals = _planLogic.DeploymentFinished ? _planLogic.SignalPaletteSignals : System.Array.Empty<string>();
            var show = signals.Count > 0 && !deployment;
            _dataSource.ShowPalette = show;
            if (show)
            {
                var signature = "";
                for (var i = 0; i < signals.Count && i < 4; i++)
                    signature += signals[i] + (_planLogic.SignalRaised(signals[i]) ? "!" : "?") + "|";
                if (signature != _pillsSignature)
                {
                    _pillsSignature = signature;
                    _dataSource.Pills.Clear();
                    for (var i = 0; i < signals.Count && i < 4; i++)
                    {
                        var fired = _planLogic.SignalRaised(signals[i]);
                        _dataSource.Pills.Add(new SignalPillVM($"Num{i + 1} · {signals[i]}" + (fired ? "  ✓" : ""), fired));
                    }
                }
            }

            var suspended = !deployment && _planLogic.AnySuspended;
            _dataSource.ShowResume = suspended;
            if (suspended)
                _dataSource.ResumeText = "Resume plan  (Numpad5)";
        }

        private void Create()
        {
            _created = true; // even on failure: never retry-spam layer creation
            try
            {
                _screen = MissionScreen ?? ScreenManager.TopScreen as MissionScreen;
                if (_screen == null)
                    return;
                _dataSource = new RbpHudVM(
                    onOpenPlanner: () => PlanningModeView.Active?.Toggle(),
                    onResumeAll: () =>
                    {
                        var logic = PlanMissionLogic.Active;
                        if (logic != null)
                            InformationManager.DisplayMessage(new InformationMessage(logic.RequestResume("all")));
                    });
                // Non-focus, mouse-only: chips are clickable, empty space and the
                // keyboard pass through to the game (unlike the planner's focus layer).
                _layer = new GauntletLayer("RbpHudLayer", HudLayerOrder, false);
                _layer.InputRestrictions.SetInputRestrictions(false, InputUsageMask.Mouse);
                _movie = _layer.LoadMovie(MovieName, _dataSource);
                _screen.AddLayer(_layer);
                RbpLog.Info("Mission HUD attached (entry chip / signal legend / resume).");
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] Creating the mission HUD failed; continuing without it.", e);
                Teardown();
            }
        }

        private void Teardown()
        {
            if (_layer != null)
            {
                Guard(() => _layer.InputRestrictions.ResetInputRestrictions());
                if (_movie != null)
                    Guard(() => _layer.ReleaseMovie(_movie));
                Guard(() => _screen?.RemoveLayer(_layer));
            }
            _movie = null;
            _layer = null;
            _dataSource = null;
            _screen = null;
        }

        private static void Guard(Action step)
        {
            try { step(); }
            catch (Exception e) { RbpLog.Error("[FAULT] HUD teardown step failed.", e); }
        }
    }
}
