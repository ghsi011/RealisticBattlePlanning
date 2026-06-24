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
        private bool _rightDown;
        private Vec2 _rightDownRanged;
        private const float RightClickMaxRangedMove = 0.012f; // beyond this it's a camera drag, not a click
        private const float RemoveWaypointToleranceMeters = 22f; // how near the click must land to a waypoint
        private readonly List<GameEntity> _previewEntities = new List<GameEntity>();
        // Blue tint for the planned-move ghost dots (vs vanilla green), 0xAARRGGBB.
        private const uint PreviewTint = 0xFF3F7BFFu;
        // Below this drag length (m) the gesture is a click, not a line — keep the default width.
        private const float MinDragWidthMeters = 4f;

        // Committed waypoints: the FULL per-soldier ghost (the same line the live preview drew) is
        // frozen at commit and re-rendered every deployment tick, so the player can read how each
        // placed formation will array — for the rest of deployment, not just on hover. Each
        // placement is keyed to the plan anchors it created (one per formation); it is rendered only
        // while at least one of those anchors is still in the plan, so deleting the stage deletes its
        // soldier flags. A carried-plan anchor with no frozen frames (e.g. battle 2) falls back to a
        // single marker (ground Z resolved off the terrain once and cached).
        private int _fieldWaypointCounter;
        private readonly Dictionary<string, Vec3> _markerGroundCache = new Dictionary<string, Vec3>();
        private readonly List<GameEntity> _committedEntities = new List<GameEntity>();
        private readonly List<PlacedPreview> _placements = new List<PlacedPreview>();
        private const uint CommittedTint = 0xFF2E66FFu;

        /// <summary>A placed move waypoint's frozen soldier-ghost: the per-soldier ground positions
        /// captured at commit, plus the plan anchor id(s) the placement created. Rendered while any
        /// of those anchors survives in the plan; dropped once they're all gone (stage deleted).</summary>
        private sealed class PlacedPreview
        {
            public List<string> AnchorIds;
            public List<Vec3> Frames;
        }

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

            // Self-heal stale input state: a missed release (focus loss / screen swap) would otherwise
            // leave _isMouseDown or _gestureActive latched — _isMouseDown suppresses the hover preview
            // (even after an in-bounds press), and _gestureActive draws a stale line. If the button
            // isn't actually held, neither can be in progress, regardless of where the press started.
            if (!input.IsKeyDown(InputKey.LeftMouseButton))
            {
                _isMouseDown = false;
                _gestureActive = false;
            }
            else if (_gestureActive && SelectedCount() == 0)
            {
                _gestureActive = false; // formation deselected mid-drag
            }

            // Right-CLICK (not a camera drag) near a field waypoint removes it — the place/remove loop
            // lives on the field, no parchment-planner trip. The vanilla camera only enters drag-mode
            // past a movement threshold, so a quick click here doesn't pan it.
            if (input.IsKeyPressed(InputKey.RightMouseButton))
            {
                _rightDown = true;
                _rightDownRanged = input.GetMousePositionRanged();
            }
            else if (input.IsKeyReleased(InputKey.RightMouseButton) && _rightDown)
            {
                _rightDown = false;
                var moved = (input.GetMousePositionRanged() - _rightDownRanged).Length;
                if (moved < RightClickMaxRangedMove && hasWorld && !IsInsideBoundary(world))
                    TryRemoveFieldWaypoint(world);
            }
            // Self-heal a missed right-release (focus loss / screen swap): otherwise _rightDown stays
            // latched and the next click's distance is measured from a stale press position.
            if (_rightDown && !input.IsKeyDown(InputKey.RightMouseButton))
                _rightDown = false;

            if (_gestureActive && hasWorld)
                RenderPreviewFor(_gestureStart, world);   // dragging the line
            else if (overWaypoint && hasWorld && !_isMouseDown)
                RenderPreviewFor(world, world);           // hovering a waypoint spot -> point preview
            else if (!_stickyPreview)
                HidePreview();
        }

        // Hide the vanilla deploy cursor (its red "invalid" mark) over a valid waypoint spot, and
        // restore it elsewhere, so beyond-boundary reads as a legal move rather than a forbidden one.
        // The desired state is re-derived every tick from overWaypoint, so the flag can't get stuck
        // hidden across ticks: as soon as the cursor leaves a waypoint spot it is handed back to
        // vanilla. We only force-restore once (via the latch) so we don't fight vanilla's own
        // visibility logic while the cursor is over an in-bounds deploy spot.
        private bool _hidVanillaFlag;
        private void UpdateVanillaCursor(bool overWaypoint)
        {
            var flag = ResolvedScreen?.OrderFlag;
            if (flag == null)
                return;
            if (overWaypoint)
            {
                // Set every tick (idempotent), so even if vanilla re-shows the flag we keep it hidden.
                flag.IsVisible = false;
                _hidVanillaFlag = true;
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
            // Clear the latch regardless of whether the screen was resolvable: if it isn't, there's no
            // flag to leave stuck, and we must not stay latched into the battle.
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
            var lineA = new MapVec(_gestureStart.AsVec2.x, _gestureStart.AsVec2.y);
            var lineB = new MapVec(end.AsVec2.x, end.AsVec2.y);

            // Collect the plannable selected formations first, so we can tell single- from multi-select.
            var plannable = new List<Formation>();
            foreach (var f in oc.SelectedFormations)
                if (FormationClassMap.ToPlanned(f.FormationIndex).HasValue)
                    plannable.Add(f);
            if (plannable.Count == 0)
            {
                RbpLog.Info("[FIELD] commit skipped: selection had no plannable formation class.");
                return;
            }

            // Keep ids unique across a carried plan / reopen (else "fw1" collides — the same bug
            // the parchment map fixed by seeding its counter from existing waypoint anchors).
            _fieldWaypointCounter = Math.Max(_fieldWaypointCounter, MaxFieldWaypointNumber(logic.ActivePlan));
            var draft = PlanDraft.EditingCopyOf(logic.ActivePlan);
            List<string> anchorIds;
            float? width = null;

            if (plannable.Count == 1)
            {
                // Single-formation drag: carry its frontage WIDTH and FACING into the order, so the
                // formation forms the line you stretched, facing the way it pointed (what the soldier
                // ghost shows is what executes). A short drag (a click) keeps default width/facing.
                var planned = FormationClassMap.ToPlanned(plannable[0].FormationIndex).Value;
                MapVec? facing = null;
                var w = (end.AsVec2 - _gestureStart.AsVec2).Length;
                if (w >= MinDragWidthMeters)
                {
                    width = w;
                    facing = ForwardFacing(plannable[0], _gestureStart.AsVec2, end.AsVec2, center);
                }
                var id = $"fw{++_fieldWaypointCounter}";
                MapAuthoring.AppendMarchStage(draft, planned, center, id, width, facing);
                anchorIds = new List<string> { id };
            }
            else
            {
                // Multi-select: spread the formations evenly along the drag line so they don't stack at
                // one point (the same Core arraying the parchment map's drag-to-line uses).
                var classes = new List<PlannedFormationClass>();
                foreach (var f in plannable)
                    classes.Add(FormationClassMap.ToPlanned(f.FormationIndex).Value);
                anchorIds = new List<string>(MapAuthoring.AppendLineFormation(
                    draft, classes, lineA, lineB, _ => $"fw{++_fieldWaypointCounter}"));
            }

            logic.ApplyPlan(draft.Build());

            // Freeze the soldier ghost so it persists at the waypoint for the rest of deployment,
            // keyed to the anchor(s) it created so deleting that stage clears exactly its soldiers.
            // Single-formation: the whole drag line (carries the authored width/facing). Multi-select:
            // each formation forms a default-width block at its OWN spot along the line, so simulate
            // and freeze each one separately (one anchor each → each deletes independently). Per
            // formation we reuse the order controller's existing simulation clone (populated for every
            // selected formation), so no selection mutation is needed; if a clone is somehow missing
            // we skip that one and its fallback marker still shows.
            var froze = 0;
            if (plannable.Count == 1)
            {
                // Single-formation: freeze along the whole drag line (carries the authored width/facing).
                froze += TryFreezeGhost(oc, plannable[0], _gestureStart, end, anchorIds);
            }
            else
            {
                var spots = MapAuthoring.LinePositions(lineA, lineB, plannable.Count);
                for (var i = 0; i < plannable.Count && i < anchorIds.Count && i < spots.Count; i++)
                {
                    var spot = new WorldPosition(Mission.Scene, UIntPtr.Zero, new Vec3(spots[i].X, spots[i].Y, 0f), hasValidZ: false);
                    froze += TryFreezeGhost(oc, plannable[i], spot, spot, new List<string> { anchorIds[i] });
                }
            }

            RbpLog.Info($"[FIELD] committed move waypoint at ({center.X:0},{center.Y:0}) for {anchorIds.Count} formation(s), {froze} soldier ghost(s) frozen, width={(width.HasValue ? $"{width.Value:0.0}m" : "default")}; plan now has {draft.Formations.Count} planned formation(s).");
        }

        // Simulate ONE formation's soldier line via the static overload (reusing the order
        // controller's existing per-formation clone) and freeze it, keyed to its anchor(s). The
        // clone's presence is guarded: the instance overload resolves it as simulationFormations[f]
        // and throws KeyNotFoundException when the dict is populated but missing this formation (a
        // selection the vanilla placer hasn't simulated yet) — which, after ApplyPlan already ran,
        // would silently abort the commit (no ghost, no log). Skips cleanly (0 frozen) if missing,
        // so the waypoint still shows its fallback marker. One formation only, so a co-selected
        // non-plannable slot never bloats a single placement's ghost.
        private int TryFreezeGhost(OrderController oc, Formation formation, WorldPosition begin, WorldPosition end, List<string> anchorIds)
        {
            if (oc?.simulationFormations == null || !oc.simulationFormations.ContainsKey(formation))
                return 0;
            OrderController.SimulateNewOrderWithPositionAndDirection(
                new List<Formation> { formation }, oc.simulationFormations,
                begin, end, out var frames, isFormationLayoutVertical: true);
            return FreezeGhost(anchorIds, frames);
        }

        // Freeze a simulated soldier line as a persistent placement, keyed to the anchor(s) it
        // belongs to (rendered while any survives in the plan). Returns the soldier count frozen.
        private int FreezeGhost(List<string> anchorIds, List<WorldPosition> frames)
        {
            var positions = new List<Vec3>(frames?.Count ?? 0);
            if (frames != null)
                foreach (var f in frames)
                    positions.Add(f.GetGroundVec3());
            _placements.Add(new PlacedPreview { AnchorIds = anchorIds, Frames = positions });
            return positions.Count;
        }

        // Right-click removal: drop the field waypoint nearest the click and re-link the march
        // (the same Core edit the parchment planner's right-click uses), then apply — so the ghost
        // clears. Tolerant radius because the deploy camera projects the field at a distance.
        private void TryRemoveFieldWaypoint(WorldPosition world)
        {
            var logic = Mission?.GetMissionBehavior<PlanMissionLogic>();
            var plan = logic?.ActivePlan;
            if (plan?.Anchors == null)
                return;

            var click = world.AsVec2;
            string nearest = null;
            var best = RemoveWaypointToleranceMeters;
            foreach (var a in plan.Anchors)
            {
                if (a == null || a.Basis != AnchorBasis.Scene || !IsAutoWaypointAnchorId(a.Id))
                    continue;
                var dx = a.X - click.x;
                var dy = a.Y - click.y;
                var d = (float)Math.Sqrt(dx * dx + dy * dy);
                if (d < best) { best = d; nearest = a.Id; }
            }
            if (nearest == null)
                return;

            var draft = PlanDraft.EditingCopyOf(plan);
            if (MapAuthoring.RemoveMarchWaypoint(draft, nearest))
            {
                draft.RemoveUnreferencedAnchors(IsAutoWaypointAnchorId);
                logic.ApplyPlan(draft.Build());
                RbpLog.Info($"[FIELD] removed waypoint '{nearest}' (field right-click).");
            }
        }

        // An auto-authored waypoint anchor the editor owns: a field gesture ("fwN") or a map click
        // ("wpN"). Mirrors the planner's IsAutoWaypointAnchor so the two surfaces prune the same set.
        private static bool IsAutoWaypointAnchorId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length <= 2
                || !(id[0] == 'f' && id[1] == 'w') && !(id[0] == 'w' && id[1] == 'p'))
                return false;
            for (var i = 2; i < id.Length; i++)
                if (!char.IsDigit(id[i]))
                    return false;
            return true;
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
            if (oc?.simulationFormations == null)
            {
                HidePreview();
                return;
            }
            // The instance simulate resolves each selected formation as simulationFormations[f] and
            // throws KeyNotFoundException if one isn't cloned yet (a selection the vanilla placer
            // hasn't simulated this tick). Skip the preview this tick if so — it draws next tick once
            // the clone exists, rather than throwing out of the gesture handler.
            foreach (var f in oc.SelectedFormations)
                if (!oc.simulationFormations.ContainsKey(f))
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
                if (mesh != null) e.AddComponent(mesh);
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

        // Persist the placed soldier ghosts, mirrored to the plan: each placement's frozen frames
        // render while any of its anchors survives, and a stage deleted in the planner drops both the
        // anchor and (once all its anchors are gone) its soldier flags. Plan anchors with no frozen
        // frames (a carried plan) fall back to a single marker so they still show.
        private readonly HashSet<string> _liveAnchorIds = new HashSet<string>();   // ids, for placement liveness
        private readonly HashSet<string> _coveredAnchorIds = new HashSet<string>();
        private readonly HashSet<string> _liveAnchorSig = new HashSet<string>();   // id|x|y, for the dirty check
        private readonly HashSet<string> _renderedAnchorSig = new HashSet<string>();
        private bool _committedRendered;
        private void RenderCommitted()
        {
            var anchors = Mission?.GetMissionBehavior<PlanMissionLogic>()?.ActivePlan?.Anchors;
            _liveAnchorIds.Clear();
            _liveAnchorSig.Clear();
            if (anchors != null)
                foreach (var a in anchors)
                    if (a != null && a.Basis == AnchorBasis.Scene && a.Id != null)
                    {
                        _liveAnchorIds.Add(a.Id);
                        _liveAnchorSig.Add(AnchorSig(a));
                    }

            // Drop placements whose every anchor is gone (their stage was deleted).
            _placements.RemoveAll(p => !AnyAlive(p.AnchorIds));

            // The fallback markers' positions depend on anchor (id, x, y), so the dirty key includes
            // coordinates — a re-applied plan that moved an anchor in place (same id) still re-renders.
            // Skip the per-soldier entity churn on idle ticks when nothing changed (exact set compare).
            if (_committedRendered && _liveAnchorSig.SetEquals(_renderedAnchorSig))
                return;
            _committedRendered = true;
            _renderedAnchorSig.Clear();
            foreach (var s in _liveAnchorSig)
                _renderedAnchorSig.Add(s);

            var shown = 0;
            _coveredAnchorIds.Clear();
            foreach (var p in _placements)
            {
                foreach (var id in p.AnchorIds)
                    _coveredAnchorIds.Add(id);
                foreach (var pos in p.Frames)
                {
                    EnsureCommittedEntity(shown);
                    var frame = new MatrixFrame(Mat3.Identity, pos);
                    var e = _committedEntities[shown];
                    e.SetFrame(ref frame);
                    e.SetVisibilityExcludeParents(visible: true);
                    shown++;
                }
            }

            // Fallback marker for any live Scene anchor no placement froze frames for.
            if (anchors != null)
                foreach (var a in anchors)
                {
                    if (a == null || a.Basis != AnchorBasis.Scene || a.Id == null || _coveredAnchorIds.Contains(a.Id))
                        continue;
                    EnsureCommittedEntity(shown);
                    var frame = new MatrixFrame(Mat3.Identity, GroundFor(a));
                    var e = _committedEntities[shown];
                    e.SetFrame(ref frame);
                    e.SetVisibilityExcludeParents(visible: true);
                    shown++;
                }

            for (var i = shown; i < _committedEntities.Count; i++)
                _committedEntities[i].SetVisibilityExcludeParents(visible: false);

            // Fires only when the committed set changes (past the dirty-check), so it traces every
            // commit/delete/apply without per-tick spam — the window into "do the field flags clear?".
            RbpLog.Info($"[FIELD] committed render: {_placements.Count} placement(s), {shown} flag(s) shown, live scene anchors=[{string.Join(",", _liveAnchorIds)}].");
        }

        private bool AnyAlive(List<string> anchorIds)
        {
            foreach (var id in anchorIds)
                if (_liveAnchorIds.Contains(id))
                    return true;
            return false;
        }

        private void EnsureCommittedEntity(int index)
        {
            while (_committedEntities.Count <= index)
            {
                var e = GameEntity.CreateEmpty(Mission.Scene);
                e.EntityFlags |= EntityFlags.NotAffectedBySeason;
                var mesh = MetaMesh.GetCopy("order_flag_small");
                mesh?.SetFactor1(CommittedTint);
                if (mesh != null) e.AddComponent(mesh);
                e.SetVisibilityExcludeParents(visible: false);
                _committedEntities.Add(e);
            }
        }

        // Ground position for a Scene anchor, resolved off the terrain once and cached by (id, x, y)
        // so a moved anchor (same id, new coords) gets a fresh resolve rather than the stale position.
        private Vec3 GroundFor(MapAnchor anchor)
        {
            var key = AnchorSig(anchor);
            if (!_markerGroundCache.TryGetValue(key, out var pos))
            {
                pos = new WorldPosition(Mission.Scene, UIntPtr.Zero, new Vec3(anchor.X, anchor.Y, 0f), hasValidZ: false).GetGroundVec3();
                _markerGroundCache[key] = pos;
            }
            return pos;
        }

        private static string AnchorSig(MapAnchor a)
            => a.Id + "|" + a.X.ToString("0.0", CultureInfo.InvariantCulture) + "|" + a.Y.ToString("0.0", CultureInfo.InvariantCulture);

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

        // The perpendicular to the drag line, disambiguated to point the way the formation is heading
        // (toward the destination), so a dragged frontage executes facing forward — matching the ghost.
        private static MapVec? ForwardFacing(Formation formation, Vec2 lineStart, Vec2 lineEnd, MapVec center)
        {
            var line = lineEnd - lineStart;
            var perp = new Vec2(-line.y, line.x);
            if (perp.Length < 0.01f)
                return null;
            perp.Normalize();
            var moveDir = new Vec2(center.X - formation.CurrentPosition.x, center.Y - formation.CurrentPosition.y);
            if (Vec2.DotProduct(perp, moveDir) < 0f)
                perp = perp * -1f;
            return new MapVec(perp.x, perp.y);
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
                    case "select" when parts.Length >= 2:
                        DevSelect(parts);
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
                    case "rremove" when parts.Length >= 3 && TryF(parts[1], out var rx) && TryF(parts[2], out var ry):
                        if (TryProjectToWorld(new Vec2(rx, ry), out var rworld)) TryRemoveFieldWaypoint(rworld);
                        else RbpLog.Info("[FIELD] dev rremove: no ground under cursor.");
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

        // "select 1" or "select 1 3 4" — selects one or several formations (so multi-select arraying
        // is testable over the file channel).
        private void DevSelect(string[] parts)
        {
            var oc = Mission?.PlayerTeam?.PlayerOrderController;
            if (oc == null)
            {
                RbpLog.Info("[FIELD] dev select: no order controller.");
                return;
            }
            oc.ClearSelectedFormations();
            var picked = 0;
            for (var i = 1; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], out var num))
                    continue;
                var formation = Mission?.PlayerTeam?.GetFormation((FormationClass)(num - 1));
                if (formation == null)
                    continue;
                oc.SelectFormation(formation);
                picked++;
            }
            RbpLog.Info($"[FIELD] dev selected {picked} formation(s) (count={SelectedCount()}).");
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
