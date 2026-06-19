# SPEC — "The General's Table": Battle Planning, Commander Training & Drilled Maneuvers
## A Mount & Blade II: Bannerlord Singleplayer Mod

**Document type:** Feature specification (the WHAT). Implementation approach (the HOW) is left to the implementer, except where an engine constraint materially shapes a requirement — those appear as *Implementation notes* and are advisory.

**Version:** 1.1 — Draft for implementation (adds manual player triggers, vanilla-first principle, and playability-review revisions: After-Action Report, plan presets, commander voice feedback, pacing targets, onboarding)
**Scope:** Singleplayer only. Field battles first; sieges explicitly out of scope for v1.

---

## 1. Vision

The player is a general, not a superhuman micromanager. Before battle, the player gathers their sub-commanders around a stylized battle map (in the spirit of historical staff maps) and lays out a plan: where each formation deploys, what it does, what it reacts to, and when. During the battle, sub-commanders execute that plan autonomously — *to the best of their ability*. Ability is earned: commanders who train with the player, or who survive many battles under the player's command, follow plans faster, more precisely, and with better discipline. Over a campaign, the player builds a trusted officer corps and a repertoire of named, drilled maneuvers (e.g., the Feigned Retreat) that can be stamped into plans or called mid-battle like a football play.

Three pillars, in priority order:

1. **Battle Planning Phase** — author a multi-formation, trigger-driven plan on a battle map before combat starts.
2. **Autonomous Plan Execution** — commanders carry out the plan during battle, with degradation, aborts, and player override.
3. **Commander Progression & Drilled Maneuvers** — a persistent training/experience system that improves execution fidelity and unlocks reusable maneuver templates.

### 1.1 Design principle — vanilla-first
**Wherever a feature can be built on, derived from, or expressed through an existing game mechanism, it must be — new systems are a last resort.** This is a binding principle for the implementer, for three reasons: compatibility, player familiarity, and maintenance cost across game patches. Concrete applications (non-exhaustive):
- **Commander competence derives from existing hero stats.** Vanilla Tactics and Leadership skills (plus relevant captain perks) form the *base* of Command Competence; the mod adds only a plan-following experience layer on top (see D1). A renowned lord is immediately a capable plan-executor; the mod tracks what vanilla doesn't (familiarity with *your* way of fighting and with specific maneuvers).
- **Planning Mode extends the vanilla deployment / order-of-battle phase**, its captain-assignment system, and its formation placement — it does not replace them.
- **Directives reuse or subclass vanilla formation behaviors** wherever a close relative exists (skirmish, flank, pull back, charge, square/circle arrangements).
- **Mid-battle interactions extend the vanilla order menu/radial** rather than introducing a parallel command UI.
- **Notifications, encyclopedia pages, save system, and settings** use the game's existing channels (battle messages, hero encyclopedia tab for the Dossier where feasible, standard save-data registration, MCM/config conventions).
- **Drill troop XP, food consumption, and time passage** use the vanilla campaign systems for those resources.

When the spec below names a capability with a clear vanilla relative, the implementer should treat reuse as the default and document any deliberate divergence.

---

## 2. Definitions

| Term | Meaning |
|---|---|
| **Commander** | A hero (companion, clan member, or allied lord) assigned as captain of a formation in the player's army. |
| **Formation** | A Bannerlord battle formation (Infantry I, Archers II, Cavalry III, Horse Archers IV, etc.) with an assigned Commander. |
| **Plan** | The full battle plan: one Formation Plan per planned formation, plus shared signals. Lives for one battle. |
| **Formation Plan** | An ordered list of Stages for one formation, plus abort conditions. |
| **Stage** | A (Trigger → Directive) pair. A formation executes exactly one stage's directive at a time and advances when the next stage's trigger fires. |
| **Trigger** | A condition that activates a stage (e.g., "enemy commits to attack," "friendly formation within X meters," "signal received"). |
| **Directive** | An order the formation carries out while its stage is active (e.g., "skirmish," "feign retreat toward anchor," "hold in shieldwall"). |
| **Signal** | A named event a stage can broadcast on activation/completion, which other formations' triggers can listen for. The coordination glue. |
| **Maneuver Template** ("Maneuver") | A named, reusable multi-formation plan fragment with parameterized role slots (e.g., Feigned Retreat = 2× horse-archer slots + 2× infantry slots). |
| **Proficiency** | A per-commander, per-maneuver mastery level. |
| **Command Competence** | A per-commander general stat governing baseline execution fidelity of any plan. |
| **Drill** | A training session: a battle-map session with no enemy, used to raise Competence/Proficiency and teach Maneuvers. |
| **Fidelity** | How accurately a directive is executed (timing, positioning, discipline). Derived from Competence + Proficiency. |

---

## 3. Feature Area A — Battle Planning Phase

### A1. Entry & flow
- **A1.1** On entering any field battle where the player commands, after the vanilla deployment/order-of-battle phase begins, the player may open **Planning Mode** (default keybind + on-screen button). Time remains frozen.
- **A1.2** Planning Mode is optional. Skipping it yields vanilla behavior exactly (zero behavioral change for unplanned formations — this is a hard compatibility requirement).
- **A1.3** Exiting Planning Mode and confirming the plan starts the battle. The plan is immutable as a document once battle starts, but the player can override per formation at any time (see B5) and can invoke known Maneuvers mid-battle (see E5).

### A2. The Battle Map
- **A2.1** Planning Mode presents a top-down stylized map of the actual battlefield: terrain relief (contours or shading), major features (forest, water, roads, elevation), deployment zones, and known enemy starting positions (what vanilla deployment already reveals — no extra intel).
- **A2.2** Each player formation appears as a draggable unit block (historical map aesthetic: rectangles with type glyphs — infantry, archers, cavalry, horse archers) showing commander name/portrait, troop count, and a facing indicator.
- **A2.3** Dragging a block sets the formation's initial deployment position and facing (mirrored to the vanilla deployment system).
- **A2.4** The player can draw movement paths (waypoint arrows) on the map; paths attach to stages as movement directives.
- **A2.5 (v1 fallback permitted):** If the stylized map render is high-risk, v1 may reuse the existing top-down deployment camera with the planning UI overlaid. The stylized map is the target presentation, fallback is acceptable for first release.
- **A2.6 Map-first interactive authoring (target editor UX).** The map is the primary authoring surface — the player does as much as possible by direct manipulation on it, not through nested menus. The reference aesthetic is a historical staff map: terrain-matched relief, faction-colored formation blocks with troop-type glyphs and a number badge (cf. the Marj Ayyun reference).
  - **A2.6.1 Select by number/block.** Clicking a formation's number badge (or its block) highlights and selects it; a box-drag or modifier-click selects several.
  - **A2.6.2 Point-and-click march.** With a formation selected, clicking a map point appends a *move* stage to that point whose trigger defaults to "previous stage completed," so a chain of clicks builds a waypoint march with no menu trips.
  - **A2.6.3 Multi-select drag-to-line.** With several formations selected, click-dragging forms a line: the drag sets the line's span and facing and the formations array along it and move together (one shared move stage cloned per formation, cf. A3.6).
  - **A2.6.4 The stage rail (KSP-style staging).** A vertical stack of stage boxes pinned to the right edge of the map shows the selected formation's stages in execution order and follows the current selection. Drag a box to reorder; click a box to expand it in place, revealing only the controls its command needs (command, trigger, target, distance, arrangement…).
  - **A2.6.5 Multi-formation rail.** When several formations are selected, the rail aligns their stages; a box renders in full color only where that stage is *identical* across all selected formations (same command + trigger + parameters) and dimmed/striped where they diverge — so shared stages are edited once and divergences are visible at a glance.
  - **A2.6.6** Every map/rail action maps onto the engine-free `PlanDraft` + `PlanValidator`; the map and rail are a thin, tested-logic-backed direct-manipulation front-end (A2.5's overlay fallback stays acceptable for the first cut; the stylized art of A2.1/A2.2 is the visual target).

### A3. The Plan Editor
- **A3.1** Selecting a unit block opens that formation's Stage List: an ordered, editable list of Stages.
- **A3.2** Each Stage is authored by picking one Trigger and one Directive from the vocabularies below, with parameters (distances, target formations, map anchors, formation arrangement/spacing, weapon rules).
- **A3.3** Stages execute strictly in order. Stage 1's trigger defaults to "On battle start." A formation with an exhausted stage list holds its last directive until plan end or abort.
- **A3.4** Any stage may, on activation, **emit a named Signal**. Any trigger may include **"Signal received: \<name\>"** as a condition. Signals are the mechanism for cross-formation coordination and must be supported in v1.
- **A3.5** Triggers support simple composition: AND of up to 3 atomic conditions (v1). OR composition is out of scope for v1 (author it as two stages if needed).
- **A3.6** Multi-select: the player can select multiple unit blocks and author a shared stage applied to all (cloned per formation), matching the user story "select the 2 horse archer commanders and instruct them together."
- **A3.7** Per-formation **Abort Conditions** (editable, with sensible defaults): casualties above X%, commander incapacitated, formation broken/retreating. On abort, the formation reverts to vanilla team AI control and the player is notified.
- **A3.8** The editor must surface **feasibility warnings** at author time (non-blocking): commander doesn't know an assigned Maneuver, commander Competence below a stage's recommended tier, contradictory parameters (e.g., flank standoff larger than weapon range). Warnings inform; they never prevent saving the plan — players may knowingly assign plans above their commanders' ability (and watch them fumble, per Section D).
- **A3.9 Planning friction reducers (required for v1 — this is what keeps the mod fun on the 40th battle, not just the 4th):**
  - **Repeat last plan:** one click re-applies the previous battle's plan, re-mapped to current formations by class and adapted to the new map's deployment origin/facing, with un-mappable stages flagged for review.
  - **Battle Plan Presets:** the player can save an entire battle plan (all formations) as a named preset keyed to formation classes, and load it onto any future battle in one click, then adjust. (Maneuver Templates cover fragments; presets cover the whole table.)
  - **Suggested opening stages:** new formations start with a sensible default Stage 1 (hold position, current facing) so an empty plan is never invalid, and common stage patterns are offered as one-click inserts ("skirmish then withdraw on contact," "hold then charge on signal").
  - Planning a simple 4-formation battle from a preset must take under 30 seconds; a from-scratch simple plan under 3 minutes. Trivial battles (looters): the player just skips Planning Mode — zero added friction is the requirement (A1.2).
- **A3.10 Player's own formation:** the formation the player personally fights in may be included in the plan like any other (its "commander" is the player at effective Master fidelity, executing only movement/arrangement directives — the player can always act freely, which simply counts as override per B5). Other formations' triggers and anchors may reference the player's formation; "Friendly formation within distance: player" is an expected idiom (e.g., "charge when I reach the hill").

### A4. Trigger vocabulary (v1 minimum)
| Trigger | Parameters |
|---|---|
| On battle start | — |
| Enemy commits to attack (on us / on formation F) | sustained-approach speed threshold, sustain seconds (defaults provided) |
| Enemy formation within distance | enemy selector (nearest / type), meters |
| Friendly formation within distance | formation F, meters |
| Own position reached | map anchor, tolerance |
| Casualties above | percent (own formation or named formation) |
| Timer elapsed | seconds since previous stage activated |
| Signal received | signal name |
| **Player signal (manual trigger)** | signal name; fired by the player mid-battle via the Signal Palette (B9). The plan's "Go!" button. |
| Enemy formation broken/fleeing | enemy selector |

### A5. Directive vocabulary (v1 minimum)
| Directive | Parameters / notes |
|---|---|
| Hold position | arrangement (line/shieldwall/loose/square/circle), facing, width |
| Move to | map anchor or path of waypoints, speed (walk/run), arrangement |
| Skirmish / Harass | target enemy formation, standoff distance, fire-at-will |
| **Feign retreat toward** | anchor (typically behind a friendly line), retreat speed, "fire while withdrawing" flag |
| Charge | target enemy formation or nearest |
| **Flank arc** | side (left/right), target, standoff distance, **missile-only flag (charge forbidden)** |
| Pull back / Fall back | anchor, maintain facing toward enemy |
| Screen / Rear guard | protected formation F, gap distance, delaying posture (engage briefly, withdraw, repeat) |
| Follow / Escort | formation F, relative offset |
| Hold fire / Free fire | toggle |

*Implementation note:* most directives have close vanilla `BehaviorComponent` relatives (skirmish, flank, pull back, charge); Feign Retreat, Flank Arc with missile-only, and Rear Guard will need custom behaviors. The vocabulary above is the product requirement; behavior reuse is the implementer's choice.

### A6. Canonical worked example (must be expressible end-to-end in the shipped UI)
The **Feigned Retreat** scenario, 2 horse-archer (HA) formations + 2 infantry (INF) formations:
1. Deployment: HA-1, HA-2 in a front line; INF-1, INF-2 in a second line behind.
2. HA-1 & HA-2 — Stage 1: *On battle start* → **Skirmish** (nearest enemy). Stage 2: *Enemy commits to attack* → **Feign retreat toward** anchor behind the infantry line, firing while withdrawing. Stage 3: *Signal received "spring-trap"* → **Flank arc** (HA-1 left, HA-2 right), missile-only, standoff ~50 m.
3. INF-1 & INF-2 — Stage 1: *On battle start* → **Hold position** (shieldwall). Stage 2: *Enemy within 40 m of the HA retreat anchor* → **Charge**, and **emit signal "spring-trap"** on activation.

This exact plan is the primary acceptance scenario (Section H).

---

## 4. Feature Area B — Plan Execution During Battle

- **B1** When battle starts, each planned formation is governed by its Formation Plan. The game's team-level general AI must not override planned formations' orders for as long as the plan governs them. Unplanned formations behave exactly as vanilla.
- **B2** A lightweight Plan Monitor evaluates triggers a few times per second and advances stages. Trigger evaluation must be cheap (target: no measurable frame cost in a 1000-agent battle).
- **B3 Execution is mediated by the commander's Fidelity** (Section D). The plan says what should happen; Fidelity determines how well it happens. A plan executed by green commanders should *visibly* differ from the same plan executed by veterans.
- **B4 Aborts:** when a formation's abort condition fires, it drops out of the plan, reverts to vanilla AI, and the player receives a clear notification ("Derthert's line has broken — he no longer follows the plan"). Commander death/incapacitation always aborts that formation's plan (a new captain may be auto-assigned by vanilla rules but does not inherit the plan).
- **B5 Player override:** any manual order issued to a planned formation immediately suspends that formation's plan ("override mode") and is obeyed via normal vanilla ordering. The player can return the formation to its plan via a "Resume plan" command (radial/menu entry); on resume, the Plan Monitor picks the most appropriate stage (most recent stage whose trigger conditions currently hold; otherwise current stage).
- **B6 Situational invalidation (v1 minimal):** if a stage's directive references a target that no longer exists (enemy formation destroyed, anchor unreachable), the formation skips to the next stage whose trigger can still be evaluated; if none, it holds and notifies the player. v1 does **not** attempt clever re-planning — "conditions totally changed" is handled by aborts, skips, and the player's override hand.
- **B7 Battlefield feedback:** the player must be able to see plan state at a glance mid-battle: a compact HUD element per planned formation showing current stage, pending trigger, and override/abort status. A toggleable mini-map or order overlay showing planned paths/anchors is a v1.x stretch goal.
- **B8 Comms realism toggle (option, default off):** when enabled, signals and stage activations incur a courier/horn delay scaled by distance from the player (flat configurable seconds in v1). Off = instantaneous, game-y, reliable.
- **B9 Signal Palette (manual triggers):** any plan may declare up to 4 player signals. Mid-battle, these appear as a compact palette (order-menu entries plus optional direct keybinds) so the player can fire a named "Go!" with one input. This converts plans from fully autonomous scripts into semi-conducted ones — the player decides *when* the trap springs even if the plan decides *how*. Player signals propagate exactly like stage-emitted signals (including B8 delays and low-Fidelity miss chances per D3 — a green commander can miss even your direct order, with a "re-signal" prompt offered). The same mechanism implements drill cues (C7).
- **B10 After-Action Report (AAR):** on battle end, before or alongside the vanilla results screen, the player gets a plan debrief: per formation, a timeline of stages (planned trigger vs. actual firing time), maneuvers attempted and their outcome (completed / aborted / overridden), notable Fidelity events ("Wulfric sprang the charge 9 s early — misread an enemy probe"), and XP/tier progress earned. The AAR is essential to playability: it converts execution noise into legible character ("he's still green") instead of perceived bugs, and it is where progression is *felt*. Skippable in one click; data also logged to file for debugging.
- **B11 Commander voice (event feedback):** Fidelity events and stage transitions surface as in-battle text barks attributed to the commander ("Forming square, my lord!", "They're breaking — shall we pursue?", "We cannot hold!"), using the vanilla battle-message channel. Every visible deviation from the plan must produce a bark or notification naming the commander — silent deviation is the single biggest trust-killer for this kind of system. Audio barks are a stretch goal; text is required.

---

## 5. Feature Area C — Training Mode (Drills)

- **C1** A new campaign action, **"Drill the troops"** (available from the party/clan menu or while waiting in a settlement/camp), launches a **Drill Session**: a battle-map mission on a nearby scene with **no enemy present**, the player's party deployed, and Planning Mode tooling available.
- **C2** In a Drill Session the player can: lay out plans and watch commanders execute them against empty field conditions; run any known or built-in Maneuver Template at "demonstration" level; end the session at will.
- **C3 Costs & pacing:** a Drill Session consumes campaign time (configurable; default 4 in-game hours per session, max 1 session/day) and party resources (food consumption as normal; optional small denar cost representing wear, configurable). Drilling is not free XP printing — see C5 caps.
- **C4 Gains:**
  - Participating commanders gain **Command Competence XP** for time spent executing stages.
  - Running a Maneuver Template in a drill grants the participating commanders **Proficiency XP for that specific maneuver** at an accelerated rate vs. battle (default 2×), because repetition is safe and supervised.
  - Regular troops gain a small amount of vanilla troop XP (much less than real combat; configurable, default ~10–15% of equivalent combat time) so drilling feels holistic but never replaces fighting.
  - The player gains a trickle of vanilla **Tactics/Leadership** skill XP.
- **C5 Anti-grind caps:** Proficiency gained from drills alone is capped at the **"Proficient"** tier (see D2). Tiers above Proficient ("Veteran," "Master") require executing the maneuver in real battles. Rationale: drills teach the choreography; only battle teaches the judgment.
- **C6 Teaching new Maneuvers:** to teach a commander a Maneuver they don't know, the player runs that Maneuver in a Drill Session (or completes it in a real battle — see E4) with that commander filling one of its role slots. After a configurable number of successful run-throughs (default 2 drill runs or 1 battle execution), the commander **learns** the maneuver at base Proficiency.
- **C7** Triggers that depend on enemies (e.g., "enemy commits") are simulated in drills via the same Signal Palette mechanism as B9: a "fire next pending trigger" drill cue, letting full plans be rehearsed without enemies. (Optional v1.x stretch: mock OPFOR formed from the player's own troops with blunted weapons.)
- **C8 Drill pacing controls:** drills must respect the player's time — sessions support an in-mission time-scale control (1×/2×/4×) and an "auto-run remaining stages" button, so rehearsing a 3-minute maneuver doesn't require 3 real minutes of watching troops walk. Watching the execution is optional flavor, not a tax.

---

## 6. Feature Area D — Commander Competence & Fidelity

### D1. The stats
**Vanilla-first (per 1.1): Command Competence is *derived*, not invented.** Its base value is computed from the commander's existing vanilla stats — primarily **Tactics**, secondarily **Leadership**, with relevant captain perks contributing modifiers. The mod stores only what vanilla cannot express: a **Plan Familiarity XP** layer (experience following *this player's* plans, earned per D4) that adds to the derived base, and per-maneuver data. Practical consequences the design depends on:
- A famous lord (high Tactics/Leadership) joining the player's army is immediately Drilled-or-better at general plan execution with zero mod XP — no "great general acts like a recruit" dissonance.
- Companions level toward competence through their normal vanilla skill growth *and* through Plan Familiarity, so the mod's progression compounds with, rather than duplicates, vanilla progression.
- Per-Maneuver Proficiency remains fully mod-owned (vanilla has no concept of it), but its XP gain rate is accelerated by vanilla Tactics (smart officers learn plays faster).

Each hero who has ever captained a formation in the player's battles carries persistent mod data:
- **Plan Familiarity XP** (0–300 scale): the mod-owned layer on top of the vanilla-derived base. Effective **Command Competence** = derived base + familiarity layer (exact formula data-driven).
- **Per-Maneuver Proficiency** (per learned maneuver, 0–300): mastery of that specific maneuver.
- A small **Service Record** (battles under player command, drills attended, maneuvers executed/failed) for UI flavor and debugging.

The mod must not write to vanilla skills except the small player-XP trickles named in C4 (and a modest Tactics XP trickle to *commanders* who execute plan stages — using the vanilla skill system as the carrier of general growth, per 1.1).

### D2. Tiers
Both Competence and Proficiency map to five tiers: **Untrained → Drilled → Proficient → Veteran → Master.** Tier thresholds configurable.

### D3. Fidelity effects (what the player actually sees)
Fidelity for a given stage = function of Command Competence and, if the stage came from a Maneuver Template, that maneuver's Proficiency (use the lower-weighted blend so an unfamiliar maneuver feels unfamiliar even to a competent officer). Fidelity manifests as concrete, observable execution differences:

| Dimension | Untrained end | Master end |
|---|---|---|
| **Reaction delay** | 6–10 s lag between trigger firing and movement | ≤1 s, near-instant |
| **Positional accuracy** | drifts 15–25 m off anchors/paths; ragged facing | within 2–3 m; crisp facing & spacing |
| **Trigger judgment** | misreads conditions: fires "enemy commits" on a probe, or late on a real attack (random ± error on thresholds) | reads the field correctly; thresholds honored tightly |
| **Discipline** | risk of breaking script under pressure — e.g., a feigning unit panics into a real rout, or charging infantry overpursues past its stage | holds the script; feign stays a feign; halts on cue |
| **Signal handling** | may miss a signal (chance to require re-broadcast / extra delay) | never misses |
| **Abort composure** | aborts early (treats configured thresholds as ~0.7×) | fights to the configured letter |

All error magnitudes are data-driven (config/XML) so balance iteration doesn't require code changes.

### D4. XP sources & loss
- Battle execution: each completed stage grants Competence XP; each completed Maneuver grants its Proficiency XP (more than drills — default 1× battle vs 2× drill *rate* but battles are where the caps unlock, per C5). Failed/aborted maneuvers grant a reduced "lesson learned" trickle.
- Drills per Section C.
- **No decay** in v1 (configurable hook reserved). Commander **death loses everything** — knowledge lives in people. This is intentional: protecting trained officers becomes a campaign-level incentive.
- Commanders who leave the clan retain their data (it returns if they come back); data is save-persistent (Section G).

**Pacing targets (default tuning, all configurable):** a fresh companion with mediocre Tactics should reach **Drilled** within ~3–5 battles or ~4 drill sessions; **Proficient** in a specific maneuver within ~6–10 executions mixing drills and battles; **Veteran** after ~10–15 battlefield executions; **Master** is a campaign-long achievement (~30+). The fantasy is "my officers grow with me over a war," not "grind 200 drills" and not "everyone is Master by week 6." These targets are acceptance-relevant (H4) and must be reachable without the player optimizing for them.

### D5. UI
- A **Commander Dossier** screen (accessible from the clan/party screens and from Planning Mode by clicking a commander's portrait): Competence tier & progress, known Maneuvers with Proficiency tiers, Service Record highlights.
- Planning Mode unit blocks show a tier pip so the player sees at a glance who they're trusting with what.

---

## 7. Feature Area E — Maneuver Templates

### E1. What a template is
A named plan fragment spanning 1–6 **role slots**, each slot specifying a formation class requirement (infantry / ranged / cavalry / horse archers / any) and that slot's Stage List (with triggers, directives, signals, and **relative** spatial anchors — positions defined relative to the template's placement origin/facing, not absolute map points).

### E2. Built-in templates (ship with v1)
1. **Feigned Retreat** — 2× horse archer (or light cavalry) slots + 1–2× infantry slots; exactly the canonical example in A6.
2. **Hedgehog** (infantry square, archers center, cavalry harass) — 1–2× infantry slots forming a square, 1× ranged slot held inside it (hold-fire until enemy within X), 1–2× cavalry slots on Skirmish/Harass with leashed return-to-square trigger on casualty threshold.
3. **Organized Withdrawal with Rear Guard** — 1× rear-guard slot (Screen/Rear guard directive, delaying posture) + remaining slots executing staged Fall Back along a waypoint path toward a map-edge anchor, leapfrogging on distance triggers, rear guard breaking contact on signal.

Each built-in ships with: name, description, role-slot definitions, default parameters, a diagram/illustration for the UI, and recommended minimum tiers.

### E3. Player-authored templates
- **E3.1** In Planning Mode (or a Drill), the player can select a set of formations whose plans form a working pattern and **"Save as Maneuver"**: the mod converts their absolute anchors to relative ones, asks the player to confirm role-slot classes, and names it.
- **E3.2** Custom templates are save-persistent and appear alongside built-ins. (Export/import to file is a v1.x stretch goal.)

### E4. Learning & knowledge
- A Maneuver is **known per-commander** (not globally). Built-ins are not known by default — they must be taught (C6) or learned by doing: a commander who fills a slot in a maneuver that completes in a real battle learns it immediately at base Proficiency.
- The player (as a commander entity) always "knows" all templates for authoring purposes; knowledge gates *execution quality and mid-battle invocation*, not plan authoring (see A3.8 — you may assign the ignorant, at your peril: unknown maneuver executes at Untrained-equivalent fidelity with elevated discipline-break risk).

### E5. Stamping & mid-battle invocation
- **E5.1 Stamping (planning time):** in Planning Mode, pick a template, assign formations to its role slots (UI validates class requirements and shows each candidate's Proficiency tier), place its origin and facing on the map, optionally tweak parameters; its stages merge into the assigned formations' plans. This is the "speed up planning" path.
- **E5.2 Mid-battle invocation:** during battle, via an order-menu/radial entry **"Execute Maneuver,"** the player picks a known template, assigns currently available formations to slots (only formations whose commanders **know** the maneuver are eligible), and places origin/facing via a quick ground-target pick. The slot picker must **auto-suggest** the best valid assignment (matching class, highest Proficiency, nearest to origin) so that in the common case invocation is: pick maneuver → confirm suggestion → place origin — three inputs, a few seconds, usable under pressure. The maneuver overrides those formations' current plans/orders (treated as a player override per B5; "Resume plan" returns them afterward). Mid-battle invocation incurs a setup delay scaled by the worst participant's Proficiency tier (Master ≈ seconds; Drilled ≈ noticeably slow) and by the comms-realism option if enabled.
- **E5.3** Invocation must work even if the formation was never part of the pre-battle plan ("even if the plan didn't involve them" — explicit user requirement).

---

## 8. Feature Area F — Options & Configuration

All of the following exposed via in-game settings (MCM if available; config file fallback mandatory):
- Master toggles: planning phase on/off, progression on/off (progression-off = all commanders behave at a fixed configurable tier — lets players use planning as a pure tactics sandbox).
- Fidelity error magnitudes per tier (data table).
- XP rates, drill costs/caps, tier thresholds.
- Comms realism toggle & delay scale (B8).
- HUD verbosity (full / minimal / off).
- Keybinds.

---

## 9. Feature Area G — Persistence & Compatibility

- **G1** Commander stats, known maneuvers, proficiencies, service records, and custom templates **persist in campaign saves**. Battle plans themselves are mission-scoped and never saved.
- **G2** Removing the mod must not corrupt saves (standard Bannerlord save-data hygiene; orphaned data acceptable, crashes not).
- **G3** Zero-touch principle: with the mod installed but unused (no planning, no drills), gameplay must be indistinguishable from vanilla.
- **G4** Compatibility targets: must function alongside RBM (including its AI module — plan-governed formations must keep their plan governance even with modified team AI present) and RTS Camera. Conflicts beyond these are best-effort.
- **G5** Singleplayer only; multiplayer code paths untouched. Field battles only in v1; the planning entry point must cleanly not appear in sieges, hideouts, and village fights.
- **G6** Allied/AI-army formations not under the player's command are out of scope for planning in v1 (player's own party formations + formations the player is given command over in army battles are in scope).

---

## 10. Playability Requirements & Known Design Risks

This section is the output of a playability review of the whole design; the hard requirements it produced are already embedded above (B9–B11, A3.9–A3.10, C8, E5.2 auto-suggest, D4 pacing). What follows binds the loop-level qualities and names the risks the implementer must design and test against.

### 10.1 The three loops (each must independently be fun)
- **Battle loop (minutes):** plan → watch/conduct → adapt. Fun hinges on (a) low planning friction in repeat play (A3.9), (b) the player feeling like a *conductor* — the Signal Palette (B9) is the keystone here, turning "watch the script run" into "call the moment," and (c) legibility — the player can always tell what each formation believes it is doing (B7) and is told, in character, when reality diverges (B11).
- **Progression loop (sessions):** fight/drill → AAR shows growth → commanders visibly improve. The AAR (B10) and tier-up notifications are where progression becomes felt; the same plan must *play differently* at different tiers (D3, verified by H1). Risk: if fidelity errors are too subtle, progression feels fake; too punishing, low-tier play feels broken. The error tables (D3) ship at magnitudes where an Untrained commander is *usably unreliable* — plans mostly work, with one memorable wobble per battle, not constant failure.
- **Campaign loop (the long game):** building an officer corps becomes a strategic concern — keeping veterans alive (D4 death rule), choosing whom to train in what, and lords arriving pre-competent via vanilla stats (D1) so army battles aren't a fidelity slum.

### 10.2 Named risks and required mitigations
- **R1 — Planning fatigue.** The #1 way this mod dies in week two. Mitigations are mandatory: A1.2 (fully skippable), A3.9 (presets, repeat, suggestions, time budgets). Test: a player who plans only "important" battles must never feel punished in the rest.
- **R2 — Fidelity reads as bugs.** Any silent deviation will be reported as a defect and erode all trust in the system. Mitigation: B11's "every deviation barks" rule + AAR attribution (B10) + a debug log distinguishing intended Fidelity noise from genuine execution faults. This distinction must exist *in the code*, not just the fiction.
- **R3 — The enemy won't take the bait.** Feigned Retreat is only fun if the vanilla (and RBM) AI commits often enough to spring traps. The mod cannot rewrite enemy AI (out of scope, Section 12), so: directive parameters must let bait formations present convincingly (closing to provoke, casualty-tolerance before withdrawing), the "enemy commits" trigger defaults must be tuned against both vanilla and RBM AI, and the Signal Palette gives the player a manual escape — if the AI never commits, the player can still spring the plan on their own judgment. Acceptance H1 must be run against both AI configurations.
- **R4 — Trigger-authoring overwhelm.** A 10-trigger × 10-directive vocabulary with parameters can intimidate. Mitigations: defaults everywhere (a stage is valid the moment it's created), plain-language stage summaries ("When the enemy charges us → fall back behind Derthert, firing"), tooltips with small diagrams for every trigger/directive, and the one-click pattern inserts (A3.9). Templates (Section E) are themselves the main complexity shield — most players should mostly stamp.
- **R5 — Onboarding cliff.** Required: a guided first-use flow — the first time Planning Mode opens, a short interactive walkthrough builds a two-formation plan ("hold" + "charge on your signal") and points at the Signal Palette in the following battle; Dossier and Drill features get one-time callout hints; an in-game concept page (encyclopedia or help panel) documents triggers, directives, tiers, and maneuvers. No external manual may be required to reach the H1 scenario.
- **R6 — Drill tedium.** Mitigated by C8 (time scale, auto-run) and C3/C5 (drills are short, capped, and never the *only* path). Drills should be a deliberate 2-minute real-time activity with a clear payoff, not a daily chore. If a player never drills, the battle-learning path (E4, D4) must keep progression alive.
- **R7 — Mid-battle UI under pressure.** Invocation and the Signal Palette compete with combat attention. Requirements: pausable/slow-time-compatible (respect whatever pause-on-order settings the player uses, including RTS Camera's), three-input invocation (E5.2), palette signals fireable in ≤2 inputs, and everything reachable from the existing order UI (1.1).

---

## 11. Acceptance Scenarios (definition of done)

**H1 — Canonical Feigned Retreat (the gold test).** Author the A6 plan in Planning Mode in under 3 minutes using stamping (template) or under 6 minutes manually. In battle against an aggressive AI army: HA units skirmish, withdraw on enemy commitment while shooting, infantry charges when the enemy nears the anchor, signal fires, HA wheel out left/right and pour arrows without melee-charging. With all four commanders at Veteran+, the sequence executes with ≤2 s stage lag and units within ~5 m of intended geometry. With all four Untrained, at least one visible failure mode occurs per battle on average (late trigger, drifting arc, discipline break) — and the battle is still playable via overrides.

**H2 — Override & resume.** Mid-maneuver, manually order one HA formation to charge; it obeys instantly and its plan suspends with clear HUD indication. "Resume plan" returns it to a sensible stage.

**H3 — Abort.** Kill a planned commander (console/dev tool acceptable); their formation reverts to vanilla AI with a notification; remaining formations continue their plans.

**H4 — Drill loop.** From a settlement: run a Drill Session, rehearse Feigned Retreat with two commanders who don't know it using drill cues, end session. Both commanders now know the maneuver; campaign clock advanced; Dossier reflects gains. Repeat-drilling to Proficient works; Veteran is unreachable by drilling alone.

**H5 — Mid-battle invocation.** In a battle with no pre-plan, invoke Hedgehog with eligible commanders; formations assemble the square/center/harass pattern at the placed origin within Fidelity-appropriate time. Attempting it with a commander who doesn't know it: that formation is ineligible in the slot picker.

**H6 — Persistence.** Save, quit, reload: all Dossier data and custom templates intact. Remove mod, load save: game loads.

**H7 — Zero-touch.** Fresh battle, never open Planning Mode: behavior and performance match vanilla.

**H8 — Conducted battle (Signal Palette).** Author a plan where the infantry charge is gated solely on player signal "hammer." In battle, fire "hammer" from the palette in ≤2 inputs; the infantry charges within the Fidelity-appropriate delay; the AAR timeline shows the signal and the response. With an Untrained commander and comms realism on, at least occasionally observe a missed signal with a re-signal prompt.

**H9 — Legibility loop.** Play one battle with deliberately under-tiered commanders: every visible plan deviation produces an attributed bark/notification (B11), and the AAR (B10) correctly attributes each deviation to a Fidelity event with the commander named. No silent deviations across 5 test battles.

**H10 — Friction budget.** Load a saved Battle Plan Preset onto a new battle and confirm in under 30 seconds; "Repeat last plan" produces a valid plan in one click against a same-composition army. H1 plan authored from the Feigned Retreat template in under 3 minutes (per H1).

*All battle scenarios above (esp. H1) must pass on vanilla AI and with RBM's AI module active (R3, G4).*

---

## 12. Out of Scope (v1) — explicit
Sieges; naval/hideouts; multiplayer; enemy AI using this planning system against the player (tempting v2 feature — note the architecture should not preclude it); plan re-planning AI beyond skips/aborts; voice/courier visualizations; formation-internal micro (sub-unit detachments); mock-OPFOR drills (stretch); template file export (stretch); mini-map order overlay (stretch).

## 13. Suggested Milestones (advisory)
1. **M1 — Execution core:** plan model, triggers/directives/signals (incl. player signals), plan monitor, vanilla-AI suppression for planned formations, debug authoring via config file. H2/H3/H8(core) testable.
2. **M2 — Planning UI:** deployment-phase editor (fallback camera acceptable), stage editor, multi-select, warnings, friction reducers (presets/repeat/suggestions). H1 (fixed-fidelity) + H10 testable.
3. **M3 — Fidelity & progression:** vanilla-derived stats, tiers, error model, barks, AAR, dossier UI, save persistence. H1 (tiered) + H6 + H9.
4. **M4 — Templates & drills:** built-ins, stamping, mid-battle invocation, drill sessions, learning rules. H4/H5.
5. **M5 — Polish:** stylized map, HUD, onboarding flow, options, comms realism, compatibility passes (incl. RBM runs of all battle scenarios). H7 + perf.
