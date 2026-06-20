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
                if (input != null)
                    HandleGesture(input);
                else
                    HidePreview();

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

        // --- Out-of-boundary waypoint gesture (mirrors the vanilla placer's down/drag/up) ----
        private bool _isMouseDown;
        private bool _gestureActive;
        private bool _stickyPreview; // dev-only: keep a one-shot preview visible for a screenshot
        private WorldPosition _gestureStart;
        private readonly System.Collections.Generic.List<GameEntity> _previewEntities = new System.Collections.Generic.List<GameEntity>();
        // Blue tint for the planned-move ghost dots (vs vanilla green), 0xAARRGGBB.
        private const uint PreviewTint = 0xFF3F7BFFu;

        private void HandleGesture(TaleWorlds.InputSystem.IInputContext input)
        {
            if (input.IsKeyPressed(InputKey.LeftMouseButton))
            {
                _isMouseDown = true;
                BeginGesture(input.GetMousePositionRanged());
            }
            else if (input.IsKeyReleased(InputKey.LeftMouseButton) && _isMouseDown)
            {
                _isMouseDown = false;
                EndGesture(input.GetMousePositionRanged());
            }
            else if (input.IsKeyDown(InputKey.LeftMouseButton) && _isMouseDown && _gestureActive)
            {
                UpdateGesture(input.GetMousePositionRanged());
            }
            else if (!_gestureActive && !_stickyPreview)
            {
                HidePreview();
            }
        }

        // A press that lands BEYOND the deployment boundary (with a formation selected) starts a
        // waypoint gesture; a press inside is left to the vanilla placer (it deploys there).
        private void BeginGesture(Vec2 screen)
        {
            _gestureActive = false;
            _stickyPreview = false;
            HidePreview();
            if (SelectedCount() == 0 || !TryProjectToWorld(screen, out var world) || IsInsideBoundary(world))
                return;
            _gestureActive = true;
            _gestureStart = world;
        }

        private void UpdateGesture(Vec2 screen)
        {
            if (TryProjectToWorld(screen, out var end))
                RenderPreviewFor(_gestureStart, end);
        }

        private void EndGesture(Vec2 screen)
        {
            if (!_gestureActive)
                return;
            _gestureActive = false;
            if (TryProjectToWorld(screen, out var end))
                RbpLog.Info($"[FIELD] waypoint gesture ({_gestureStart.AsVec2.x:0},{_gestureStart.AsVec2.y:0})->({end.AsVec2.x:0},{end.AsVec2.y:0}) — commit pending (iter 3).");
            HidePreview();
        }

        private bool IsInsideBoundary(WorldPosition world)
        {
            var team = Mission.PlayerTeam;
            var plan = Mission.DeploymentPlan;
            return plan == null || !plan.HasDeploymentBoundaries(team)
                   || plan.IsPositionInsideDeploymentBoundaries(team, world.AsVec2);
        }

        // Reuse the engine's own per-soldier line simulation for the ghost dots (the "line depth"
        // math) — we only render the result in blue, so no vanilla maths are rewritten.
        private void RenderPreviewFor(WorldPosition lineBegin, WorldPosition lineEnd)
        {
            var oc = Mission?.PlayerTeam?.PlayerOrderController;
            if (oc == null)
            {
                HidePreview();
                return;
            }
            oc.SimulateNewOrderWithPositionAndDirection(lineBegin, lineEnd, out var frames, isFormationLayoutVertical: true);
            RenderPreview(frames);
        }

        private void RenderPreview(System.Collections.Generic.List<WorldPosition> frames)
        {
            var count = frames?.Count ?? 0;
            while (_previewEntities.Count < count)
            {
                var e = GameEntity.CreateEmpty(Mission.Scene);
                e.EntityFlags |= EntityFlags.NotAffectedBySeason;
                var mesh = MetaMesh.GetCopy("order_flag_small");
                mesh?.SetFactor1(PreviewTint);
                e.AddComponent(mesh);
                e.SetVisibilityExcludeParents(visible: false);
                _previewEntities.Add(e);
            }
            for (var i = 0; i < _previewEntities.Count; i++)
            {
                var e = _previewEntities[i];
                if (i < count)
                {
                    var frame = new MatrixFrame(Mat3.Identity, frames[i].GetGroundVec3());
                    e.SetFrame(ref frame);
                    e.SetVisibilityExcludeParents(visible: true);
                }
                else
                {
                    e.SetVisibilityExcludeParents(visible: false);
                }
            }
        }

        private void HidePreview()
        {
            foreach (var e in _previewEntities)
                e.SetVisibilityExcludeParents(visible: false);
        }

        public override void OnRemoveBehavior()
        {
            try
            {
                foreach (var e in _previewEntities)
                    e?.Remove(0);
                _previewEntities.Clear();
            }
            catch (Exception e) { RbpLog.Error("[FIELD] cleanup failed.", e); }
            base.OnRemoveBehavior();
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
                    case "preview" when parts.Length >= 5 && TryF(parts[1], out var px0) && TryF(parts[2], out var py0) && TryF(parts[3], out var px1) && TryF(parts[4], out var py1):
                        DevPreview(new Vec2(px0, py0), new Vec2(px1, py1));
                        break;
                    case "clearpreview":
                        _stickyPreview = false;
                        HidePreview();
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

        private void DevPreview(Vec2 screen0, Vec2 screen1)
        {
            if (SelectedCount() == 0)
            {
                RbpLog.Info("[FIELD] dev preview: no formation selected.");
                return;
            }
            if (!TryProjectToWorld(screen0, out var a) || !TryProjectToWorld(screen1, out var b))
            {
                RbpLog.Info("[FIELD] dev preview: no ground under one of the points.");
                return;
            }
            RenderPreviewFor(a, b);
            _stickyPreview = true;
            RbpLog.Info($"[FIELD] dev preview ({a.AsVec2.x:0},{a.AsVec2.y:0})->({b.AsVec2.x:0},{b.AsVec2.y:0}) rendered ({_previewEntities.Count} dots pooled).");
        }

        private static bool TryF(string s, out float v)
            => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }
}
