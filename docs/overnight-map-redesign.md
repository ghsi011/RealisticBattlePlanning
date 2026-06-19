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
- **Iter 1 (Phase A, done, committed):** keyboard-free dev loop — sentinel in
  `PlanningModeView` + `tools\map-iterate.ps1`. Verified end-to-end.
- **Iter 2 (Phase B, this doc):** design decision V1 + F1.
