using System;
using System.Collections.Generic;
using System.Globalization;
using RealisticBattlePlanning.Diagnostics;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Planning.Editing;
using RealisticBattlePlanning.Planning.Model;
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
                {
                    RestoreVanillaCursor();
                    HidePreview();
                    HideCommitted();
                    return;
                }

                _heartbeat += dt;
                if (_heartbeat >= 3f)
                {
                    _heartbeat = 0f;
                    RbpLog.Info($"[FIELD] alive (deployment, behavior-tick); selected={SelectedCount()} screen={(ResolvedScreen != null)} input={(Inp != null)}.");
                }

                var input = Inp;
                if (input != null)
                    HandleField(input);
                else
                    HidePreview();

                RenderCommitted();
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
        private readonly List<GameEntity> _previewEntities = new List<GameEntity>();
        // Blue tint for the planned-move ghost dots (vs vanilla green), 0xAARRGGBB.
        private const uint PreviewTint = 0xFF3F7BFFu;

        // Committed waypoints: one persistent marker per Scene anchor in the active plan, so the
        // field always shows exactly what the plan holds — placed waypoints stay visible after the
        // drag releases, and a waypoint removed in the planner makes its flag disappear (the markers
        // are derived from the live plan each tick, not a private list). Ground Z per anchor is
        // resolved once and cached (anchors never move in place; removal reuses fresh ids).
        private int _fieldWaypointCounter;
        private readonly Dictionary<string, Vec3> _markerGroundCache = new Dictionary<string, Vec3>();
        private readonly List<GameEntity> _committedEntities = new List<GameEntity>();
        private const uint CommittedTint = 0xFF2E66FFu;

        // One pass per tick: project the cursor, suppress the vanilla "invalid deploy" cursor over
        // a valid waypoint spot, run the click/drag gesture, and draw the ghost preview (hovering
        // shows a point preview; dragging stretches the line).
        private void HandleField(TaleWorlds.InputSystem.IInputContext input)
        {
            var hasWorld = TryProjectToWorld(input.GetMousePositionRanged(), out var world);
            var overWaypoint = hasWorld && SelectedCount() > 0 && (_gestureActive || !IsInsideBoundary(world));

            UpdateVanillaCursor(overWaypoint);

            if (input.IsKeyPressed(InputKey.LeftMouseButton))
            {
                _isMouseDown = true;
                _gestureActive = hasWorld && SelectedCount() > 0 && !IsInsideBoundary(world);
                if (_gestureActive) { _gestureStart = world; _stickyPreview = false; }
            }
            else if (input.IsKeyReleased(InputKey.LeftMouseButton) && _isMouseDown)
            {
                _isMouseDown = false;
                if (_gestureActive)
                    CommitWaypoint(world, hasWorld);
                _gestureActive = false;
            }

            if (_gestureActive && hasWorld)
                RenderPreviewFor(_gestureStart, world);   // dragging the line
            else if (overWaypoint && hasWorld && !_isMouseDown)
                RenderPreviewFor(world, world);           // hovering a waypoint spot -> point preview
            else if (!_stickyPreview)
                HidePreview();
        }

        // Hide the vanilla deploy cursor (its red "invalid" mark) over a valid waypoint spot, and
        // restore it elsewhere, so beyond-boundary reads as a legal move rather than a forbidden one.
        private bool _hidVanillaFlag;
        private void UpdateVanillaCursor(bool overWaypoint)
        {
            var flag = ResolvedScreen?.OrderFlag;
            if (flag == null)
                return;
            if (overWaypoint)
            {
                if (flag.IsVisible) { flag.IsVisible = false; _hidVanillaFlag = true; }
            }
            else if (_hidVanillaFlag)
            {
                flag.IsVisible = true;
                _hidVanillaFlag = false;
            }
        }

        private void RestoreVanillaCursor()
        {
            if (!_hidVanillaFlag)
                return;
            var flag = ResolvedScreen?.OrderFlag;
            if (flag != null)
                flag.IsVisible = true;
            _hidVanillaFlag = false;
        }

        // Mouse-up over a waypoint spot: turn the gesture into a real MOVE WAYPOINT in the active
        // plan. The drag's centre is the destination; each selected formation gets a MoveTo stage
        // (reusing MapAuthoring.AppendMarchStage — same Core logic the parchment map uses, so it
        // appears in the KSP stage rail and the monitor marches it in battle). A persistent blue
        // marker is dropped at the spot so placed waypoints stay visible after release.
        private void CommitWaypoint(WorldPosition end, bool hasEnd)
        {
            HidePreview();
            if (!hasEnd)
                return;

            var logic = Mission?.GetMissionBehavior<PlanMissionLogic>();
            var oc = Mission?.PlayerTeam?.PlayerOrderController;
            if (logic == null || oc == null || oc.SelectedFormations.Count == 0)
            {
                RbpLog.Info("[FIELD] commit skipped: no plan logic / order controller / selection.");
                return;
            }

            // The drag centre is where the formation centre lands (a click is start==end).
            var center = new MapVec((_gestureStart.AsVec2.x + end.AsVec2.x) * 0.5f,
                                    (_gestureStart.AsVec2.y + end.AsVec2.y) * 0.5f);
            // Keep ids unique across a carried plan / reopen (else "fw1" collides — the same bug
            // the parchment map fixed by seeding its counter from existing waypoint anchors).
            _fieldWaypointCounter = Math.Max(_fieldWaypointCounter, MaxFieldWaypointNumber(logic.ActivePlan));
            var draft = PlanDraft.EditingCopyOf(logic.ActivePlan);
            var committed = 0;
            foreach (var formation in oc.SelectedFormations)
            {
                var planned = FormationClassMap.ToPlanned(formation.FormationIndex);
                if (!planned.HasValue)
                    continue;
                MapAuthoring.AppendMarchStage(draft, planned.Value, center, $"fw{++_fieldWaypointCounter}");
                committed++;
            }
            if (committed == 0)
            {
                RbpLog.Info("[FIELD] commit skipped: selection had no plannable formation class.");
                return;
            }

            logic.ApplyPlan(draft.Build());
            RbpLog.Info($"[FIELD] committed move waypoint at ({center.X:0},{center.Y:0}) for {committed} formation(s); plan now has {draft.Formations.Count} planned formation(s).");
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

        // One persistent flag per Scene anchor in the active plan, so the field mirrors the plan:
        // a committed waypoint shows after the drag releases, and a waypoint removed in the planner
        // makes its flag disappear (the markers are derived from the plan, never a private list).
        private void RenderCommitted()
        {
            var anchors = Mission?.GetMissionBehavior<PlanMissionLogic>()?.ActivePlan?.Anchors;
            var shown = 0;
            if (anchors != null)
            {
                foreach (var a in anchors)
                {
                    if (a == null || a.Basis != AnchorBasis.Scene)
                        continue;
                    EnsureCommittedEntity(shown);
                    var frame = new MatrixFrame(Mat3.Identity, GroundFor(a));
                    var e = _committedEntities[shown];
                    e.SetFrame(ref frame);
                    e.SetVisibilityExcludeParents(visible: true);
                    shown++;
                }
            }
            for (var i = shown; i < _committedEntities.Count; i++)
                _committedEntities[i].SetVisibilityExcludeParents(visible: false);
        }

        private void EnsureCommittedEntity(int index)
        {
            while (_committedEntities.Count <= index)
            {
                var e = GameEntity.CreateEmpty(Mission.Scene);
                e.EntityFlags |= EntityFlags.NotAffectedBySeason;
                var mesh = MetaMesh.GetCopy("order_flag_small");
                mesh?.SetFactor1(CommittedTint);
                e.AddComponent(mesh);
                e.SetVisibilityExcludeParents(visible: false);
                _committedEntities.Add(e);
            }
        }

        // Ground position for a Scene anchor, resolved off the terrain once and cached by id.
        private Vec3 GroundFor(MapAnchor anchor)
        {
            if (!_markerGroundCache.TryGetValue(anchor.Id, out var pos))
            {
                pos = new WorldPosition(Mission.Scene, UIntPtr.Zero, new Vec3(anchor.X, anchor.Y, 0f), hasValidZ: false).GetGroundVec3();
                _markerGroundCache[anchor.Id] = pos;
            }
            return pos;
        }

        private static int MaxFieldWaypointNumber(BattlePlan plan)
        {
            var max = 0;
            if (plan?.Anchors != null)
                foreach (var a in plan.Anchors)
                    if (a?.Id != null && a.Id.Length > 2 && a.Id.StartsWith("fw", StringComparison.Ordinal)
                        && int.TryParse(a.Id.Substring(2), out var n) && n > max)
                        max = n;
            return max;
        }

        private void HideCommitted()
        {
            foreach (var e in _committedEntities)
                e.SetVisibilityExcludeParents(visible: false);
        }

        public override void OnRemoveBehavior()
        {
            try
            {
                foreach (var e in _previewEntities)
                    e?.Remove(0);
                _previewEntities.Clear();
                foreach (var e in _committedEntities)
                    e?.Remove(0);
                _committedEntities.Clear();
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
                    case "commit" when parts.Length >= 5 && TryF(parts[1], out var cx0) && TryF(parts[2], out var cy0) && TryF(parts[3], out var cx1) && TryF(parts[4], out var cy1):
                        DevCommit(new Vec2(cx0, cy0), new Vec2(cx1, cy1));
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

        // Dev-only: drive the full down→drag→up commit over the file channel (no mouse). Projects
        // both screen points, sets the gesture start to the first, and commits at the second.
        private void DevCommit(Vec2 screen0, Vec2 screen1)
        {
            if (SelectedCount() == 0)
            {
                RbpLog.Info("[FIELD] dev commit: no formation selected.");
                return;
            }
            if (!TryProjectToWorld(screen0, out var a) || !TryProjectToWorld(screen1, out var b))
            {
                RbpLog.Info("[FIELD] dev commit: no ground under one of the points.");
                return;
            }
            _gestureStart = a;
            CommitWaypoint(b, hasEnd: true);
        }

        private static bool TryF(string s, out float v)
            => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }
}
