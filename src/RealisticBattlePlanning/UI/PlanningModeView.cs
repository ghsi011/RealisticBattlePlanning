using System;
using RealisticBattlePlanning.Diagnostics;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Planning.Editing;
using RealisticBattlePlanning.Planning.Model;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.Data;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ModuleManager;
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
            PollDevCommand(dt);
        }

        // --- Dev loop (no keyboard) -------------------------------------------------
        // The in-game keyboard is unreliable when the game is driven over automation/
        // remote, so the visual-iteration loop can't depend on the toggle key. Instead a
        // sentinel file (<module>\Debug\planner.cmd) is polled here: an external script
        // writes one verb and RBP acts on the next tick, then truncates the file. Inert in
        // normal play — the file never exists, so the poll is a cheap File.Exists miss.
        // Verbs: open | close | toggle | reopen | shot <name> | reshot <name>. 'reopen'
        // tears down and rebuilds the movie, so a prefab/brush edit hot-reloaded via
        // tools\deploy-ui.ps1 shows immediately; 'reshot' reopens then screenshots.
        private float _devPollAccum;
        private string _devCmdPath;
        private bool _devPathResolved;

        private string DevCmdPath
        {
            get
            {
                if (!_devPathResolved)
                {
                    _devPathResolved = true;
                    try { _devCmdPath = System.IO.Path.Combine(ModuleHelper.GetModuleFullPath(SubModule.ModId), "Debug", "planner.cmd"); }
                    catch { _devCmdPath = null; }
                }
                return _devCmdPath;
            }
        }

        private void PollDevCommand(float dt)
        {
            _devPollAccum += dt;
            if (_devPollAccum < 0.2f)
                return;
            _devPollAccum = 0f;

            var path = DevCmdPath;
            if (path == null || !System.IO.File.Exists(path))
                return;

            string cmd;
            try
            {
                cmd = System.IO.File.ReadAllText(path).Trim();
                System.IO.File.WriteAllText(path, ""); // consume so it runs once
            }
            catch { return; } // writer may still hold it — retry next poll
            if (string.IsNullOrEmpty(cmd))
                return;

            try
            {
                var space = cmd.IndexOf(' ');
                var verb = (space < 0 ? cmd : cmd.Substring(0, space)).ToLowerInvariant();
                var arg = space < 0 ? null : cmd.Substring(space + 1).Trim();
                switch (verb)
                {
                    case "open": Show(); break;
                    case "close": Hide(); break;
                    case "toggle": Toggle(); break;
                    case "reopen": Hide(); Show(); break;
                    case "brushes": ReloadBrushes(); break;
                    case "click": DevClick(arg, rightClick: false); break;
                    case "rightclick": DevClick(arg, rightClick: true); break;
                    case "drag": DevDrag(arg); break;
                    case "apply": _dataSource?.ExecuteApply(); break;
                    case "removestage" when int.TryParse(arg, out var rmSlot): _dataSource?.DevRemoveStage(rmSlot); break;
                    case "shot": Diagnostics.ScreenshotCommand.CaptureNamed(arg); break;
                    case "reshot": Hide(); Show(); Diagnostics.ScreenshotCommand.CaptureNamed(arg); break;
                    default: RbpLog.Info($"[DEV] planner.cmd: unknown verb '{verb}'."); return;
                }
                RbpLog.Info($"[DEV] planner.cmd '{cmd}' handled (shown={_shown}).");
            }
            catch (Exception e)
            {
                RbpLog.Error($"[DEV] planner.cmd '{cmd}' failed.", e);
            }
        }

        // Dev only: re-reads Module\GUI\Brushes\RbpBrushes.xml into the global brush factory
        // (LoadBrushFile REPLACES existing entries) so a brush edit hot-reloaded via deploy-ui.ps1
        // takes effect on the next 'reopen' — no relaunch. Brushes are otherwise cached at startup.
        // Dev only: dispatch a normalized map click straight into the VM, bypassing the
        // (unreliable-over-automation) mouse — so the file loop can test select / place /
        // remove deterministically. arg = "nx ny" in [0,1].
        private void DevClick(string arg, bool rightClick)
        {
            if (_dataSource == null) { RbpLog.Info("[DEV] click: planner not open."); return; }
            var parts = (arg ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2
                || !float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var nx)
                || !float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ny))
            {
                RbpLog.Info($"[DEV] click: bad args '{arg}' (want: nx ny in 0..1).");
                return;
            }
            if (rightClick) _dataSource.OnMapRightClicked(nx, ny);
            else _dataSource.OnMapClicked(nx, ny);
            RbpLog.Info($"[DEV] {(rightClick ? "rightclick" : "click")} ({nx:0.00},{ny:0.00}) dispatched.");
        }

        // Dev only: dispatch a normalized map DRAG (box-select / drag-to-line) into the VM.
        // arg = "nx0 ny0 nx1 ny1" in [0,1].
        private void DevDrag(string arg)
        {
            if (_dataSource == null) { RbpLog.Info("[DEV] drag: planner not open."); return; }
            var p = (arg ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var f = new float[4];
            if (p.Length < 4 || !TryF(p[0], out f[0]) || !TryF(p[1], out f[1]) || !TryF(p[2], out f[2]) || !TryF(p[3], out f[3]))
            {
                RbpLog.Info($"[DEV] drag: bad args '{arg}' (want: nx0 ny0 nx1 ny1).");
                return;
            }
            _dataSource.OnMapDragged(f[0], f[1], f[2], f[3]);
            RbpLog.Info($"[DEV] drag ({f[0]:0.00},{f[1]:0.00})->({f[2]:0.00},{f[3]:0.00}) dispatched.");
        }

        private static bool TryF(string s, out float v)
            => float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);

        private static void ReloadBrushes()
        {
            try
            {
                UIResourceManager.BrushFactory.LoadBrushFile("RbpBrushes");
                RbpLog.Info("[DEV] reloaded RbpBrushes.xml into the brush factory.");
            }
            catch (Exception e)
            {
                RbpLog.Error("[DEV] brush reload failed.", e);
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
                // Drop any planned formation that has no troops in THIS battle: a plan carried
                // from a different army composition (session store) must not show phantom
                // formations the player can't field. Only when live composition is known.
                if (labels.Count > 0)
                    foreach (var f in draft.Formations) // a fresh snapshot list each call
                        if (!labels.ContainsKey(f))
                            draft.RemoveFormation(f);
                // Live deployment geometry for the battlefield map view.
                var geometry = BattlefieldReader.Read(Mission?.PlayerTeam);
                _dataSource = new PlanningModeVM("Battle Plan", $"{ToggleKey} to close", draft, ApplyEditedPlan, Hide, labels, geometry);
                // Route bare-canvas map clicks (the custom MapCanvasWidget) into the VM's
                // point-and-click move authoring. Cleared in Hide so a stale closure can't fire.
                MapCanvasWidget.Clicked = (x, y) => _dataSource?.OnMapClicked(x, y);
                MapCanvasWidget.RightClicked = (x, y) => _dataSource?.OnMapRightClicked(x, y);
                MapCanvasWidget.Dragged = (x0, y0, x1, y1) => _dataSource?.OnMapDragged(x0, y0, x1, y1);
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
            Guard(() => MapCanvasWidget.Clicked = null);
            Guard(() => MapCanvasWidget.RightClicked = null);
            Guard(() => MapCanvasWidget.Dragged = null);
            if (_layer != null)
            {
                // Each release step is guarded on its own: if one throws (e.g.
                // ResetInputRestrictions), the others must still run — above all
                // RemoveLayer, or the layer stays attached and keeps input focus,
                // soft-locking the deployment screen. Release focus before removing.
                Guard(() => _layer.InputRestrictions.ResetInputRestrictions());
                Guard(() => _layer.IsFocusLayer = false);
                Guard(() => ScreenManager.TryLoseFocus(_layer));
                if (_movie != null)
                    Guard(() => _layer.ReleaseMovie(_movie));
                Guard(() => _screen?.RemoveLayer(_layer));
            }
            Guard(() => _dataSource?.OnFinalize());
            _movie = null;
            _layer = null;
            _screen = null;
            _dataSource = null;
            _shown = false;
        }

        // Runs a teardown step in isolation: a throw in one release call must not
        // skip the others (especially RemoveLayer) or null-out the state below.
        private static void Guard(Action step)
        {
            try { step(); }
            catch (Exception e) { RbpLog.Error("[FAULT] Planning Mode teardown step failed.", e); }
        }
    }
}
