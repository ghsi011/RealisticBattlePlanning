# Field-deployment planning (alternative authoring surface)

Plan move orders **directly on the deployment field**, by extending the vanilla
deployment-placement gesture, instead of (or alongside) the custom parchment map.

## The vanilla gesture we extend

During deployment, `OrderTroopPlacer` (a `MissionView`, in
`TaleWorlds.MountAndBlade.View.MissionViews.Order`) drives placement:
- Select a formation (number key) → `OrderController.SelectedFormations`.
- Mouse-down + drag on the ground → it draws the per-soldier **ghost dots**
  (`order_flag_small` mesh) and, on mouse-up, commits the deployment position.
- The drag from start→end defines the **line** (width + facing); the per-soldier
  positions come from `OrderController.SimulateNewOrderWithPositionAndDirection(
  lineBegin, lineEnd, out List<WorldPosition> frames, isVertical)` (public).
- A short/quick drag is treated as a click (a point at the default width).
- **The boundary gate** (`OrderTroopPlacer.UpdateFormationDrawing`, ~line 389):
  it only draws/commits when the drag's *start* position passes
  `Mission.DeploymentPlan.IsPositionInsideDeploymentBoundaries(team, pos)`. A
  gesture that **starts beyond the boundary makes vanilla do nothing** — our seam.

## Our approach — a parallel MissionView (+ one tiny camera patch)

`FieldDeploymentPlanView : MissionView` runs during deployment beside the vanilla
placer and mirrors its mouse handling, but acts **only when the gesture starts
beyond the deployment boundary** (where vanilla is inert, so they never conflict):
- Project the mouse to the ground with `MissionScreen.ScreenPointToWorldRay` +
  `Mission.Scene.RayCastForClosestEntityOrTerrain` (same as vanilla, ~line 197).
- Down beyond the boundary, with a formation selected → begin a waypoint gesture.
- Drag → `SimulateNewOrderWithPositionAndDirection(start, end, …)` for the soldier
  positions; render them as **blue** ghost dots (reuse `order_flag_small`, tinted).
- Up → commit a **MoveTo waypoint** into the active plan, so it shows in the KSP
  stage rail and the monitor marches it in battle. Reuses `MapAuthoring.AppendMarchStage`
  (anchor id `fwN`); a chain of out-of-bounds clicks builds a march.
- **Markers mirror the plan, not a private list:** every tick the view renders one
  flag per Scene anchor in the active plan, so a placed waypoint persists after the
  drag releases AND a waypoint removed in the parchment planner makes its flag
  vanish. The planner's orphan-prune now owns `fwN` anchors too (alongside its `wpN`).

Everything heavy (soldier-line math, projection, boundary test) is **reused**
from the engine; we only add the thin input/preview/commit wrapper.

### The one Harmony patch — deployment camera reach
The vanilla deployment camera is hard-clamped to the deployment boundary, so you
can't pan far enough forward to see/place out-of-field waypoints. That clamp lives
in a private `MissionScreen.UpdateCamera` with no hook; its only lever is the snap
function it calls, `DefaultMissionDeploymentPlan.GetClosestDeploymentBoundaryPosition`,
whose **sole caller across the whole engine is that camera clamp** (verified in the
decompiled sources). So `Patches/DeploymentCameraReachPatch` postfixes it to let the
camera roam a fixed margin (~80 m) past the boundary — touching **only** the camera,
never troop placement (which uses `IsPositionInsideDeploymentBoundaries`, untouched).
Gated on our view being attached, so other missions keep the exact vanilla camera.

The list-based Planning Mode UI stays for the richer plan components (triggers,
abort, signals) the field gesture doesn't cover.

## Build order (iterations)
1. **Detect** ✅ — view ticks in deployment, reads selection, projects mouse,
   tells in/out of boundary. (log-only)
2. **Preview** ✅ — ghost flags at the simulated soldier positions on the drag,
   and the vanilla "invalid deploy" cursor is suppressed over a valid waypoint
   spot (user-confirmed: no banner, drag works).
3. **Commit** ✅ — mouse-up appends a MoveTo waypoint to the active plan (reusing
   `MapAuthoring.AppendMarchStage`, anchor `fwN`) so it shows in the KSP stage
   rail and the monitor marches it in battle; a persistent blue marker is dropped
   at the destination so placed waypoints stay visible after release.
4. **Line** — carry the drag's width/facing so the formation arrays along it
   (MoveToLineSegment) instead of only its centre point.
5. **Polish** — multi-formation, march chains (chained out-of-bounds clicks),
   integration with the rail/list.

## Follow-ups (not blocking)
- **Custom planning banner** (user request 2026-06-20): the ghost/committed
  markers currently reuse the vanilla `order_flag_small` mesh tinted blue (reads
  dark, not blue). Author our own flag/banner mesh or sprite for planning orders
  so a planned move is visually distinct from a vanilla deploy flag — not
  critical, a polish pass.
