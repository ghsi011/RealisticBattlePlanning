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
  (anchor id `fwN`); a chain of out-of-bounds clicks builds a march. A single-formation
  drag carries its frontage **width** into the order (so the formation forms the line
  you drew — what the ghost shows is what executes); a click keeps the default width.
- **Right-click** (not a camera drag) near a field waypoint **removes** it and re-links
  the march (`MapAuthoring.RemoveMarchWaypoint` + prune), so the whole place/remove loop
  lives on the field with no parchment-planner trip. The vanilla camera only enters
  drag-mode past a movement threshold, so a quick click doesn't pan it.
- **Placed soldier ghosts persist, mirrored to the plan:** on commit the FULL
  per-soldier ghost the live preview was drawing is frozen and re-rendered each
  deployment tick (so you can read how the formation will array for the rest of
  deployment), keyed to the anchor(s) it created. It renders only while its anchor
  survives, so a stage removed in the parchment planner clears its soldier flags —
  the planner's orphan-prune now owns `fwN` anchors too (alongside its `wpN`). The
  per-tick render is skipped when the live Scene-anchor set is unchanged. NOTE: the
  full per-soldier freeze is for **single-formation** placement; a multi-select
  placement (one combined frame set across N anchors that can't be split) instead
  drops a single marker per anchor, each independently deletable — per-formation
  multi-select ghosts are a follow-up needing per-formation frame slicing.

Everything heavy (soldier-line math, projection, boundary test) is **reused**
from the engine; we only add the thin input/preview/commit wrapper.

### The one Harmony patch — deployment camera reach
The vanilla deployment camera is hard-clamped to the deployment boundary, so you
can't pan far enough forward to see/place out-of-field waypoints. That clamp lives
in a private `MissionScreen.UpdateCamera` with no hook; its only lever is the snap
function it calls, `DefaultMissionDeploymentPlan.GetClosestDeploymentBoundaryPosition`,
whose **only vanilla caller is that camera clamp** (verified in the decompiled
sources). So `Patches/DeploymentCameraReachPatch` postfixes it to let the camera roam
a fixed margin (~80 m) past the boundary — touching **only** the camera, never troop
placement (which uses `IsPositionInsideDeploymentBoundaries`, untouched). Gated on our
view being attached, so other missions keep the exact vanilla camera. (RTSCamera, a
reference mod, also calls this from its own camera override — the same relaxation
applies there and is equally wanted, and its camera is mission-clamped first too, so
the effect stays bounded. The patch target is registered in `EngineContract`.)

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
   rail and the monitor marches it in battle; the full per-soldier ghost is frozen
   and persists for the rest of deployment, clearing when the stage is deleted.
4. **Width + remove** ✅ — a single-formation drag carries its frontage width into
   the order (the ghost is what executes); right-click on the field removes the
   nearest waypoint (re-link + prune), closing the place/remove loop on the field.
5. **Facing + multi-select** ✅ — a single-formation drag also carries the line's
   **facing** (perpendicular to the drag, disambiguated forward) so the arrived
   formation looks the way the ghost showed; a **multi-select** drag spreads the
   formations evenly along the line (reusing `MapAuthoring.AppendLineFormation`)
   instead of stacking them at one point.
6. **Per-formation multi-select ghosts** ✅ — a multi-select placement now freezes EACH
   formation's full soldier block at its own spot along the line (keyed to its own anchor,
   so each deletes independently), instead of dropping one marker per formation. Done by
   simulating each formation alone via the static
   `OrderController.SimulateNewOrderWithPositionAndDirection(formations, simulationFormations, …)`
   overload, reusing the order controller's existing per-formation simulation clones (no
   selection mutation). Verified in-game: a 3-formation drag froze 200 ghosts (150+49+1)
   across fw1/fw2/fw3 and all three marched to distinct spots, no faults.
7. **Remaining polish** — a custom planning banner mesh (the ghosts reuse the vanilla
   `order_flag_small` tinted blue, which reads dark). Deferred by the user (doc-for-later).

## Follow-ups (not blocking)
- ~~Per-formation soldier ghosts for a multi-select placement~~ — done (iteration 6 above).
- **Custom planning banner** (user request 2026-06-20): the ghost/committed
  markers currently reuse the vanilla `order_flag_small` mesh tinted blue (reads
  dark, not blue). Author our own flag/banner mesh or sprite for planning orders
  so a planned move is visually distinct from a vanilla deploy flag — not
  critical, a polish pass.
