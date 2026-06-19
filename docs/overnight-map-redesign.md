# Planning Map redesign — design decision + iteration log

Goal (user, 2026-06-19): make the Planning Mode map **look like a Crusader-Kings /
Total-War parchment staff map** (faction-coloured unit blocks with glyphs + number
badges, on aged terrain) and be **fully functional and intuitive for Bannerlord /
Total-War players**. Reference: the "Baldwin IV — Anjou / Belvoir" staff-map screenshot.

Autonomous overnight pass. Keyboard to the game is unreliable here, so all visual
iteration goes through the file-driven dev loop (`tools\map-iterate.ps1`, sentinel in
`PlanningModeView`). Each iteration: edit → `map-iterate.ps1 mapN` → Read the PNG.

## Decision

### Visual look — 3 options
- **V1 — Parchment background + stylised unit blocks (CHOSEN).** Map canvas brush uses a
  paper/parchment sprite (`paper_texture_tile` / `MapIncidents\paper_texture_tile_colored`,
  fallback `stone_texture_overlay` tinted, fallback flat tan). Markers = faction-coloured
  tile brush + unit-class glyph (`Order\FormationTypeIcons\*` or `General\compass\*`) +
  number badge, with a border + drop-shadow for the block look. Achievable with brushes +
  already-shipped game sprites; faithful to the reference; low risk.
- V2 — Real battle-terrain minimap (render scene/heightmap to a texture). Most "accurate"
  but needs deep engine work (no battle minimap is exposed); high risk. Rejected.
- V3 — Parchment + procedural terrain (contours sampled from the heightmap). Medium gain
  over V1, medium-high risk (Gauntlet isn't a draw canvas). Deferred as a later flourish.

### Framing ("find a better solution") — 3 options
- **F1 — Fixed real-world scale, team-centred, forward-up (CHOSEN).** The map always shows a
  fixed FOV (~configurable metres) centred on the player army, forward = toward the enemy =
  up. Consistent zoom every battle → players build intuition (like a Total-War minimap).
  Formations sit at true relative positions and never collapse. Enemy within the FOV shows
  at true position; an enemy beyond it is pinned to the forward edge with a direction
  indicator. Zoom (+/− / wheel) and a "fit all" toggle for flexibility.
- F2 — Smarter auto-fit (current: enemy folded in + forward cap). Adaptive but the scale
  changes per battle (less intuitive) and still crams the army low. Superseded.
- F3 — Fixed tactical view + small strategic inset of both armies. Best coverage, most UI
  complexity. Deferred; F1 can grow into it.

De-overlap (Inf+Ranged share a column) is handled on top of F1: overlapping markers are
nudged apart with a leader line to true position.

## Iteration log
- **Iter 1 (Phase A):** keyboard-free dev loop — sentinel in `PlanningModeView` +
  `tools\map-iterate.ps1`. Verified end-to-end. (a93e6ab)
- **Iter 2 (Phase B):** design decision V1 + F1 (this doc).
- **Iter 3 (Phase C):** parchment canvas (`paper_texture_tile`) + `brushes` hot-reload
  dev verb (`UIResourceManager.BrushFactory.LoadBrushFile`). (21f940f)
- **Iter 4-5 (Phase D/E):** faction-colour unit blocks (live `Team.Color`) + class
  glyphs (`General\compass\*`) + number badge + selection glow; enemy blocks +
  `respawn.ps1`/`crop-zoom.ps1`. (a40ae7b)
- **Iter 6-7 (Phase F):** `BuildTacticalView` bounded fixed-scale framing (the "better
  solution") + `MapLayout.SpreadOverlaps` de-overlap + tuning; 8 new Core tests. (191b5e5)
- **Iter 8-9 (Phase G):** `click`/`rightclick` verbs; right-click waypoint removal with
  chain re-link (`MapAuthoring.RemoveMarchWaypoint`). Verified select/place/remove. (da18112)
- **Iter 10 (review #1):** subagent review → fixed waypoint-id collision (seed
  `_waypointCounter`) + dead fields. (1e13f4e)
- **Iter 11 (Phase H):** KSP-style stage rail (per-selection, shared-stage colouring,
  click-to-edit). (1aa9954)
- **Iter 12 (coach run #3):** refreshed the stale AGENTS.md dev-loop section. (9ae3d1e)
- **Iter 13 (Phase G):** drag box-select + drag-to-line (`drag` verb;
  `AppendLineFormation`). Verified both modes. (43aa1f0)
- **Iter 14 (polish):** framed parchment + gesture hint. (65b1edc)
- **Iter 15 (review #2 + follow-up):** subagent review (no bugs; click→release verified
  safe) → narrowed the rail to reduce map occlusion. (ae8e1d0)

**Status: the map matches the reference and is fully functional.** Remaining
nice-to-haves (deferred): rail ▲▼ reorder binding (Gauntlet CoverChildren floated it),
banner/terrain flourishes, a tested Core extraction of the VM box-select. The physical
mouse click/drag (now release-based) wants a quick human confirm.
