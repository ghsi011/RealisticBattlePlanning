using System;
using System.Globalization;
using RealisticBattlePlanning.Diagnostics;
using TaleWorlds.Core;
using TaleWorlds.Engine;
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
    /// Alternative authoring surface (docs/field-planning-design.md): plan move orders directly
    /// on the deployment field. Vanilla deployment lets you select a formation (number key) and
    /// click/drag in the deploy area to position it, drawing the per-soldier ghost dots and
    /// stretching the line. This view extends that exact gesture — a click/drag that starts
    /// BEYOND the deployment boundary becomes a planned MOVE WAYPOINT instead, reusing the
    /// engine's own OrderController.SimulateNewOrderWithPositionAndDirection for the soldier/line
    /// preview. The vanilla OrderTroopPlacer ignores out-of-bounds gestures (its boundary check
    /// rejects them), so the two coexist without conflict and no vanilla code is rewritten.
    ///
    /// Iteration 1: DETECTION only — confirm the view ticks during deployment, reads the selected
    /// formation, projects the cursor to the field, and tells in- vs out-of-boundary. A dev
    /// sentinel (&lt;module&gt;\Debug\field.cmd) drives it deterministically over the file channel,
    /// since the gesture is mouse-driven: "select &lt;n&gt;" | "click &lt;sx&gt; &lt;sy&gt;" (ranged screen 0..1).
    /// </summary>
    public sealed class FieldDeploymentPlanView : MissionView
    {
        private float _heartbeat;
        private float _devPollAccum;
        private bool _devPathResolved;
        private string _devCmdPath;

        private bool IsDeployment => Mission != null && Mission.Mode == MissionMode.Deployment;

        // The screen does NOT tick MissionViews during the deployment phase, but the mission
        // ticks behaviors throughout (same finding as PlanningModeView), so the field gesture
        // runs on the behavior tick. The MissionScreen can be null here, so resolve it live.
        private MissionScreen ResolvedScreen => MissionScreen ?? (ScreenManager.TopScreen as MissionScreen);
        private TaleWorlds.InputSystem.IInputContext Inp => ResolvedScreen?.SceneLayer?.Input;

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            try
            {
                if (!IsDeployment)
                    return;

                _heartbeat += dt;
                if (_heartbeat >= 3f)
                {
                    _heartbeat = 0f;
                    RbpLog.Info($"[FIELD] alive (deployment, behavior-tick); selected={SelectedCount()} screen={(ResolvedScreen != null)} input={(Inp != null)}.");
                }

                var input = Inp;
                if (input != null && input.IsKeyPressed(InputKey.LeftMouseButton))
                    OnFieldClick(input.GetMousePositionRanged(), "mouse");

                PollDevCommand(dt);
            }
            catch (Exception e)
            {
                RbpLog.Error("[FIELD] tick failed.", e);
            }
        }

        private int SelectedCount() => Mission?.PlayerTeam?.PlayerOrderController?.SelectedFormations?.Count ?? 0;

        // Iteration 1: classify the click (in/out of the deployment boundary) and log the intent.
        private void OnFieldClick(Vec2 screenRanged, string source)
        {
            if (!TryProjectToWorld(screenRanged, out var world))
            {
                RbpLog.Info($"[FIELD] {source} click: no ground under cursor.");
                return;
            }

            var team = Mission.PlayerTeam;
            var plan = Mission.DeploymentPlan;
            var hasBoundaries = plan != null && plan.HasDeploymentBoundaries(team);
            var inside = !hasBoundaries || plan.IsPositionInsideDeploymentBoundaries(team, world.AsVec2);
            var p = world.AsVec2;
            RbpLog.Info($"[FIELD] {source} click at ({p.x:0.0},{p.y:0.0}) selected={SelectedCount()} boundaries={hasBoundaries} inside={inside} -> {(inside ? "DEPLOY (vanilla handles)" : "WAYPOINT (ours)")}");
        }

        private bool TryProjectToWorld(Vec2 screenRanged, out WorldPosition world)
        {
            world = WorldPosition.Invalid;
            var screen = ResolvedScreen;
            if (screen == null)
                return false;
            screen.ScreenPointToWorldRay(screenRanged, out var rayBegin, out var rayEnd);
            if (Mission.Scene.RayCastForClosestEntityOrTerrain(rayBegin, rayEnd, out var distance, out WeakGameEntity _, 0.3f,
                    BodyFlags.CommonFocusRayCastExcludeFlags | BodyFlags.BodyOwnerFlora))
            {
                var dir = rayEnd - rayBegin;
                dir.Normalize();
                world = new WorldPosition(Mission.Scene, UIntPtr.Zero, rayBegin + dir * distance, hasValidZ: false);
                return true;
            }
            return false;
        }

        // --- Dev sentinel (no mouse needed) -----------------------------------------
        private string DevCmdPath
        {
            get
            {
                if (!_devPathResolved)
                {
                    _devPathResolved = true;
                    try { _devCmdPath = System.IO.Path.Combine(ModuleHelper.GetModuleFullPath(SubModule.ModId), "Debug", "field.cmd"); }
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
            try { cmd = System.IO.File.ReadAllText(path).Trim(); System.IO.File.WriteAllText(path, ""); }
            catch { return; }
            if (string.IsNullOrEmpty(cmd))
                return;

            try
            {
                var parts = cmd.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var verb = parts[0].ToLowerInvariant();
                switch (verb)
                {
                    case "select" when parts.Length >= 2 && int.TryParse(parts[1], out var n):
                        DevSelect(n);
                        break;
                    case "click" when parts.Length >= 3 && TryF(parts[1], out var sx) && TryF(parts[2], out var sy):
                        OnFieldClick(new Vec2(sx, sy), "dev");
                        break;
                    default:
                        RbpLog.Info($"[FIELD] field.cmd: unknown '{cmd}'.");
                        break;
                }
            }
            catch (Exception e)
            {
                RbpLog.Error($"[FIELD] field.cmd '{cmd}' failed.", e);
            }
        }

        private void DevSelect(int formationNumber)
        {
            var oc = Mission?.PlayerTeam?.PlayerOrderController;
            var formation = Mission?.PlayerTeam?.GetFormation((FormationClass)(formationNumber - 1));
            if (oc == null || formation == null)
            {
                RbpLog.Info($"[FIELD] dev select {formationNumber}: no formation.");
                return;
            }
            oc.ClearSelectedFormations();
            oc.SelectFormation(formation);
            RbpLog.Info($"[FIELD] dev selected formation {formationNumber} (count={formation.CountOfUnits}).");
        }

        private static bool TryF(string s, out float v)
            => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }
}
