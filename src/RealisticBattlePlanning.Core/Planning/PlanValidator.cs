using System;
using System.Collections.Generic;
using System.Linq;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Planning
{
    public sealed class PlanValidationResult
    {
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();
        public bool IsValid => Errors.Count == 0;
    }

    /// <summary>
    /// Structural validation of a plan. Errors mean the plan (or a stage of
    /// it) can't be executed as written; warnings inform but never block
    /// (spec A3.8: warnings inform, they never prevent saving).
    /// </summary>
    public static class PlanValidator
    {
        public const int MaxConditionsPerStage = 3;  // A3.5
        public const int MaxPlayerSignals = 4;       // B9

        /// <summary>Standoff/gap beyond any vanilla weapon range — a missile formation set this far back may never engage (A3.8 warning, not an error).</summary>
        public const float MaxSensibleStandoffMeters = 150f;

        public static PlanValidationResult Validate(BattlePlan plan)
        {
            var result = new PlanValidationResult();

            if (plan.Formations.Count == 0)
                result.Warnings.Add("Plan contains no formations.");

            if (plan.PlayerSignals.Count > MaxPlayerSignals)
                result.Errors.Add($"Plan declares {plan.PlayerSignals.Count} player signals; the maximum is {MaxPlayerSignals}.");

            var declaredPlayerSignals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var signal in plan.PlayerSignals)
            {
                if (string.IsNullOrWhiteSpace(signal))
                    result.Errors.Add("A declared player signal is blank.");
                else if (!declaredPlayerSignals.Add(signal))
                    result.Errors.Add($"Player signal '{signal}' is declared twice.");
            }

            var duplicateFormations = plan.Formations.GroupBy(f => f.Formation).Where(g => g.Count() > 1);
            foreach (var dup in duplicateFormations)
                result.Errors.Add($"Formation '{dup.Key}' has more than one plan.");

            var anchorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var anchor in plan.Anchors)
            {
                if (string.IsNullOrWhiteSpace(anchor.Id))
                    result.Errors.Add("An anchor is missing its id.");
                else if (!anchorIds.Add(anchor.Id))
                    result.Errors.Add($"Duplicate anchor id '{anchor.Id}'.");
            }

            var emittedSignals = new HashSet<string>(plan.PlayerSignals, StringComparer.OrdinalIgnoreCase);
            foreach (var formation in plan.Formations)
            {
                for (var i = 0; i < formation.Stages.Count; i++)
                {
                    foreach (var signal in formation.Stages[i].Emit)
                    {
                        if (string.IsNullOrWhiteSpace(signal))
                            result.Errors.Add($"[{formation.Formation}] stage {i + 1} emits a blank signal name.");
                        else
                            emittedSignals.Add(signal);
                    }
                }
            }

            // Dead coordination glue (A3.8, non-blocking): a signal emitted by
            // some stage that no stage reacts to (no SignalReceived/PlayerSignal
            // trigger anywhere). The inverse — listening for a signal nothing
            // emits — is warned per-trigger in ValidateTrigger.
            var consumedSignals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in plan.Formations)
                foreach (var stage in f.Stages)
                    foreach (var trigger in stage.When)
                        if (trigger.Type is TriggerType.SignalReceived or TriggerType.PlayerSignal
                            && !string.IsNullOrWhiteSpace(trigger.Signal))
                            consumedSignals.Add(trigger.Signal);

            foreach (var f in plan.Formations)
                for (var i = 0; i < f.Stages.Count; i++)
                    foreach (var signal in f.Stages[i].Emit)
                        if (!string.IsNullOrWhiteSpace(signal) && !consumedSignals.Contains(signal))
                            result.Warnings.Add($"[{f.Formation}] stage {i + 1} emits '{signal}', but no stage reacts to it.");

            // Dead anchors (A3.8, non-blocking): a declared anchor that no trigger
            // or directive references is harmless, but usually a rename or a deleted
            // stage left it stranded. Symmetric with the dead-signal warning above;
            // the inverse (referencing an undefined anchor) is an error per-site.
            var referencedAnchors = CollectReferencedAnchors(plan);
            foreach (var anchor in plan.Anchors)
                if (!string.IsNullOrWhiteSpace(anchor.Id) && !referencedAnchors.Contains(anchor.Id))
                    result.Warnings.Add($"Anchor '{anchor.Id}' is declared but never used by a trigger or directive.");

            foreach (var formation in plan.Formations)
                ValidateFormationPlan(formation, anchorIds, emittedSignals, declaredPlayerSignals, result);

            return result;
        }

        /// <summary>Every anchor id a trigger or directive points at, across the whole plan.</summary>
        private static HashSet<string> CollectReferencedAnchors(BattlePlan plan)
        {
            var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var formation in plan.Formations)
                foreach (var stage in formation.Stages)
                {
                    foreach (var trigger in stage.When)
                        if (!string.IsNullOrWhiteSpace(trigger.Anchor))
                            refs.Add(trigger.Anchor);

                    var directive = stage.Do;
                    if (directive == null)
                        continue;
                    if (!string.IsNullOrWhiteSpace(directive.Anchor))
                        refs.Add(directive.Anchor);
                    if (directive.Path != null)
                        foreach (var waypoint in directive.Path)
                            if (!string.IsNullOrWhiteSpace(waypoint))
                                refs.Add(waypoint);
                }
            return refs;
        }

        private static void ValidateFormationPlan(
            FormationPlan formation, HashSet<string> anchorIds, HashSet<string> emittedSignals,
            HashSet<string> declaredPlayerSignals, PlanValidationResult result)
        {
            string Where(int stageIndex) => $"[{formation.Formation}] stage {stageIndex + 1}";

            if (formation.Stages.Count == 0)
                result.Warnings.Add($"[{formation.Formation}] has no stages; it will hold under its default order.");

            if (formation.Abort.CasualtiesAbovePercent <= 0 || formation.Abort.CasualtiesAbovePercent > 100)
                result.Errors.Add($"[{formation.Formation}] abort casualties threshold must be in (0, 100]; got {formation.Abort.CasualtiesAbovePercent}.");

            if (!formation.Abort.OnCommanderIncapacitated)
                result.Warnings.Add($"[{formation.Formation}] onCommanderIncapacitated=false has no effect yet: commander death always aborts in Phase 1 (the flag is reserved for Phase 2's incapacitated-but-alive distinction).");

            for (var i = 0; i < formation.Stages.Count; i++)
            {
                var stage = formation.Stages[i];

                if (stage.When.Count > MaxConditionsPerStage)
                    result.Errors.Add($"{Where(i)} has {stage.When.Count} trigger conditions; the maximum is {MaxConditionsPerStage} (A3.5).");

                if (stage.When.Count == 0 && i > 0)
                    result.Errors.Add($"{Where(i)} has no trigger. Only the first stage may omit it (defaults to battle start).");

                foreach (var trigger in stage.When)
                    ValidateTrigger(trigger, Where(i), anchorIds, emittedSignals, declaredPlayerSignals, result);

                WarnRedundantConditions(stage, Where(i), result);
                WarnUnreachablePastAbort(stage, formation.Abort.CasualtiesAbovePercent, Where(i), result);

                if (stage.Do == null)
                    result.Errors.Add($"{Where(i)} has no directive.");
                else
                    ValidateDirective(stage.Do, Where(i), anchorIds, result);
            }
        }

        /// <summary>
        /// A stage's conditions are ANDed (A3.5), so two conditions that test the
        /// same thing leave only the stricter one with any effect. Flags a repeated
        /// condition (same kind, same selector/anchor/signal) — almost always a
        /// duplicated trigger left over from editing. Warning, never blocking.
        /// </summary>
        private static void WarnRedundantConditions(Stage stage, string where, PlanValidationResult result)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var reported = new HashSet<string>(StringComparer.Ordinal);
            foreach (var trigger in stage.When)
            {
                var key = RedundancyKey(trigger);
                if (key == null)
                    continue;
                if (!seen.Add(key) && reported.Add(key))
                    result.Warnings.Add($"{where} repeats a {trigger.Type} condition; conditions are ANDed, so the looser one has no effect.");
            }
        }

        /// <summary>
        /// The key two conditions collide on when one makes the other redundant.
        /// Distance/timer/casualty thresholds collapse to the strictest, so the
        /// selector (not the magnitude) is the key; signals key on the name, so an
        /// AND of two *different* signals is left alone. Returns null for kinds we
        /// never flag (e.g. BattleStart).
        /// </summary>
        private static string RedundancyKey(TriggerSpec trigger)
        {
            string Selector() => (trigger.Formation ?? "").Trim().ToLowerInvariant();
            string Sig() => (trigger.Signal ?? "").Trim().ToLowerInvariant();
            string Anc() => (trigger.Anchor ?? "").Trim().ToLowerInvariant();
            return trigger.Type switch
            {
                TriggerType.TimerElapsed => "TimerElapsed",
                TriggerType.CasualtiesAbove => "CasualtiesAbove",
                TriggerType.PositionReached => $"PositionReached|{Anc()}",
                TriggerType.EnemyWithinDistance => $"EnemyWithinDistance|{Selector()}|{Anc()}",
                TriggerType.FriendlyWithinDistance => $"FriendlyWithinDistance|{Selector()}",
                TriggerType.EnemyCommits => $"EnemyCommits|{Selector()}",
                TriggerType.EnemyBroken => $"EnemyBroken|{Selector()}",
                TriggerType.SignalReceived => $"SignalReceived|{Sig()}",
                TriggerType.PlayerSignal => $"PlayerSignal|{Sig()}",
                _ => null,
            };
        }

        /// <summary>
        /// A stage gated on "casualties above X%" can never fire when X is at or
        /// past this formation's abort threshold: the formation reverts to the AI
        /// (B4) before the stage is reachable. Dead stage — warn (A3.8).
        /// </summary>
        private static void WarnUnreachablePastAbort(Stage stage, float abortPercent, string where, PlanValidationResult result)
        {
            if (abortPercent <= 0 || abortPercent > 100)
                return; // an out-of-range abort threshold is already an error; don't pile on.
            foreach (var trigger in stage.When)
                if (trigger.Type == TriggerType.CasualtiesAbove && trigger.Percent is { } p && p >= abortPercent)
                {
                    result.Warnings.Add($"{where} triggers at casualties ≥ {p:0}%, but the formation aborts at {abortPercent:0}%; the stage may never be reached.");
                    break; // one note per stage is enough.
                }
        }

        private static void ValidateTrigger(
            TriggerSpec trigger, string where, HashSet<string> anchorIds, HashSet<string> emittedSignals,
            HashSet<string> declaredPlayerSignals, PlanValidationResult result)
        {
            void RequireEnemySelector(string selector)
            {
                if (!FormationSelector.IsValidEnemySelector(selector))
                    result.Errors.Add($"{where}: '{selector}' is not a valid enemy selector (Nearest or a formation class).");
            }

            switch (trigger.Type)
            {
                case TriggerType.TimerElapsed:
                    if (trigger.Seconds is not > 0)
                        result.Errors.Add($"{where}: TimerElapsed needs seconds > 0.");
                    break;

                case TriggerType.EnemyCommits:
                    if (trigger.Meters is <= 0)
                        result.Errors.Add($"{where}: EnemyCommits engagement range (meters) must be > 0 when given.");
                    if (trigger.SustainSeconds is < 0)
                        result.Errors.Add($"{where}: EnemyCommits sustainSeconds cannot be negative.");
                    if (trigger.SpeedThreshold is <= 0)
                        result.Errors.Add($"{where}: EnemyCommits speedThreshold must be > 0 when given.");
                    if (trigger.Formation != null
                        && !FormationSelector.IsPlayer(trigger.Formation)
                        && FormationSelector.ParseClass(trigger.Formation) == null)
                        result.Errors.Add($"{where}: '{trigger.Formation}' is not a valid reference formation (Player or a formation class).");
                    break;

                case TriggerType.EnemyWithinDistance:
                    if (trigger.Meters is not > 0)
                        result.Errors.Add($"{where}: {trigger.Type} needs meters > 0.");
                    if (trigger.Anchor != null && !anchorIds.Contains(trigger.Anchor))
                        result.Errors.Add($"{where}: anchor '{trigger.Anchor}' is not defined.");
                    if (trigger.Formation != null)
                        RequireEnemySelector(trigger.Formation);
                    break;

                case TriggerType.FriendlyWithinDistance:
                    if (trigger.Meters is not > 0)
                        result.Errors.Add($"{where}: {trigger.Type} needs meters > 0.");
                    // A null/typo'd formation would make the trigger silently
                    // never fire (there is no sensible self-distance default).
                    if (!FormationSelector.IsValidFriendlySelector(trigger.Formation))
                        result.Errors.Add($"{where}: FriendlyWithinDistance needs a formation (Player or a formation class).");
                    break;

                case TriggerType.PositionReached:
                    if (string.IsNullOrWhiteSpace(trigger.Anchor))
                        result.Errors.Add($"{where}: PositionReached needs an anchor.");
                    else if (!anchorIds.Contains(trigger.Anchor))
                        result.Errors.Add($"{where}: anchor '{trigger.Anchor}' is not defined.");
                    if (trigger.ToleranceMeters is <= 0)
                        result.Errors.Add($"{where}: PositionReached toleranceMeters must be > 0 when given.");
                    break;

                case TriggerType.CasualtiesAbove:
                    if (trigger.Percent is not (> 0 and <= 100))
                        result.Errors.Add($"{where}: CasualtiesAbove needs percent in (0, 100].");
                    // null = "this formation"; otherwise it must be an own-team formation
                    // class. "Player"/"Nearest"/a typo parse to no class and never fire.
                    if (trigger.Formation != null && FormationSelector.ParseClass(trigger.Formation) == null)
                        result.Errors.Add($"{where}: CasualtiesAbove formation '{trigger.Formation}' must be a formation class (or omitted to watch this formation).");
                    break;

                case TriggerType.EnemyBroken:
                    if (trigger.Formation != null)
                        RequireEnemySelector(trigger.Formation);
                    break;

                case TriggerType.SignalReceived:
                    if (string.IsNullOrWhiteSpace(trigger.Signal))
                        result.Errors.Add($"{where}: SignalReceived needs a signal name.");
                    else if (!emittedSignals.Contains(trigger.Signal))
                        result.Warnings.Add($"{where}: listens for signal '{trigger.Signal}', but nothing emits it and it is not a declared player signal.");
                    break;

                case TriggerType.PlayerSignal:
                    // A PlayerSignal gate must be fireable from the palette,
                    // which only carries the declared signals (B9).
                    if (string.IsNullOrWhiteSpace(trigger.Signal))
                        result.Errors.Add($"{where}: PlayerSignal needs a signal name.");
                    else if (!declaredPlayerSignals.Contains(trigger.Signal))
                        result.Errors.Add($"{where}: player signal '{trigger.Signal}' is not declared in playerSignals; the palette could never fire it.");
                    break;
            }
        }

        private static void ValidateDirective(
            DirectiveSpec directive, string where, HashSet<string> anchorIds, PlanValidationResult result)
        {
            void RequireAnchor(string anchor, string role)
            {
                if (string.IsNullOrWhiteSpace(anchor))
                    result.Errors.Add($"{where}: {directive.Type} needs {role}.");
                else if (!anchorIds.Contains(anchor))
                    result.Errors.Add($"{where}: anchor '{anchor}' is not defined.");
            }

            if (directive.StandoffMeters is <= 0)
                result.Errors.Add($"{where}: standoffMeters must be > 0 when given.");
            if (directive.GapMeters is <= 0)
                result.Errors.Add($"{where}: gapMeters must be > 0 when given.");
            if (directive.WidthMeters is <= 0)
                result.Errors.Add($"{where}: widthMeters must be > 0 when given.");

            // Walk/Run speed round-trips through the model and prints in the summary,
            // but the executor drops it (vanilla movement orders don't expose a march
            // speed) — warn so the author isn't surprised it has no effect (A3.8).
            if (directive.Speed is { } speed)
                result.Warnings.Add($"{where}: {speed.ToString().ToLowerInvariant()} speed is recorded but not yet applied; the formation moves at the vanilla default.");

            // Type-specific sub-parameters that round-trip but the executor ignores on the
            // wrong directive — usually a leftover after a stage's directive type was changed
            // in the editor (A3.8). Only flag parameters exclusive to one directive type;
            // shared ones (anchor, target, standoff, arrangement, fire) are validated per type.
            void StrayOn(bool isSet, string name, DirectiveType owner)
            {
                if (isSet && directive.Type != owner)
                    result.Warnings.Add($"{where}: {name} is set but only applies to {owner}; it is ignored on {directive.Type}.");
            }
            StrayOn(directive.Circle == true, "circle", DirectiveType.Skirmish);
            StrayOn(directive.FireWhileWithdrawing == true, "fireWhileWithdrawing", DirectiveType.FeignRetreat);
            StrayOn(directive.MissileOnly == true, "missileOnly", DirectiveType.FlankArc);
            StrayOn(directive.MaintainFacing == true, "maintainFacing", DirectiveType.PullBack);
            StrayOn(directive.Side != null, "side", DirectiveType.FlankArc);
            StrayOn(directive.GapMeters != null, "gapMeters", DirectiveType.Screen);
            StrayOn(directive.OffsetForwardMeters != null || directive.OffsetRightMeters != null, "offset", DirectiveType.Follow);

            // Contradictory-but-executable parameters (A3.8 warnings): a standoff
            // beyond any weapon range means the formation holds too far back to
            // ever engage.
            if (directive.StandoffMeters is { } standoff && standoff > MaxSensibleStandoffMeters)
                result.Warnings.Add($"{where}: standoffMeters {standoff:0} m is beyond any weapon range; the formation may never engage.");
            if (directive.GapMeters is { } gap && gap > MaxSensibleStandoffMeters)
                result.Warnings.Add($"{where}: gapMeters {gap:0} m is very large; the screen may sit far from what it guards.");

            switch (directive.Type)
            {
                case DirectiveType.MoveTo:
                    if (directive.Path is { Count: > 0 })
                    {
                        foreach (var waypoint in directive.Path.Where(w => !anchorIds.Contains(w)))
                            result.Errors.Add($"{where}: path waypoint '{waypoint}' is not a defined anchor.");
                        if (!string.IsNullOrWhiteSpace(directive.Anchor))
                            result.Warnings.Add($"{where}: MoveTo has both an anchor and a path; the path wins.");
                    }
                    else
                    {
                        RequireAnchor(directive.Anchor, "a destination anchor or path");
                    }
                    break;

                case DirectiveType.Skirmish:
                case DirectiveType.Charge:
                    if (directive.Target != null && !FormationSelector.IsValidEnemySelector(directive.Target))
                        result.Errors.Add($"{where}: '{directive.Target}' is not a valid enemy selector (Nearest or a formation class).");
                    break;

                case DirectiveType.FeignRetreat:
                case DirectiveType.PullBack:
                    RequireAnchor(directive.Anchor, "a destination anchor");
                    break;

                case DirectiveType.FlankArc:
                    if (directive.Side == null)
                        result.Errors.Add($"{where}: FlankArc needs a side (Left/Right).");
                    if (directive.Target != null && !FormationSelector.IsValidEnemySelector(directive.Target))
                        result.Errors.Add($"{where}: '{directive.Target}' is not a valid enemy selector (Nearest or a formation class).");
                    break;

                case DirectiveType.Screen:
                case DirectiveType.Follow:
                    if (!FormationSelector.IsValidFriendlySelector(directive.Target))
                        result.Errors.Add($"{where}: {directive.Type} needs a target formation (Player or a formation class).");
                    break;

                case DirectiveType.FireControl:
                    if (directive.Fire == null)
                        result.Errors.Add($"{where}: FireControl needs fire = Hold or Free.");
                    break;
            }
        }
    }
}
