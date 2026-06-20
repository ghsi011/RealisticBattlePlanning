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

## Our approach — a parallel MissionView, no Harmony patches

`FieldDeploymentPlanView : MissionView` runs during deployment beside the vanilla
placer and mirrors its mouse handling, but acts **only when the gesture starts
beyond the deployment boundary** (where vanilla is inert, so they never conflict):
- Project the mouse to the ground with `MissionScreen.ScreenPointToWorldRay` +
  `Mission.Scene.RayCastForClosestEntityOrTerrain` (same as vanilla, ~line 197).
- Down beyond the boundary, with a formation selected → begin a waypoint gesture.
- Drag → `SimulateNewOrderWithPositionAndDirection(start, end, …)` for the soldier
  positions; render them as **blue** ghost dots (reuse `order_flag_small`, tinted).
- Up → commit a **MoveTo waypoint** (line begin/end) into the active plan, so it
  shows in the KSP stage rail. Reuses `MapAuthoring.AppendMarchStage` (+ a line
  variant to carry the width/facing for a "move to line segment" execution).

Everything heavy (soldier-line math, projection, boundary test) is **reused**
from the engine; we only add the thin input/preview/commit wrapper.

The list-based Planning Mode UI stays for the richer plan components (triggers,
abort, signals) the field gesture doesn't cover.

## Build order (iterations)
1. **Detect** — view ticks in deployment, reads selection, projects mouse,
   tells in/out of boundary. (log-only) ← current
2. **Preview** — blue ghost dots at the simulated soldier positions on the drag.
3. **Commit** — mouse-up appends a MoveTo waypoint to the plan + stage rail.
4. **Line** — carry the drag's width/facing so the formation arrays along it
   (MoveToLineSegment), and persistent blue markers for placed waypoints.
5. **Polish** — multi-formation, march chains, integration with the rail/list.
