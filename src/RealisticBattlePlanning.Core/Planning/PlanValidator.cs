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

        public static PlanValidationResult Validate(BattlePlan plan)
        {
            var result = new PlanValidationResult();

            if (plan.Formations.Count == 0)
                result.Warnings.Add("Plan contains no formations.");

            if (plan.PlayerSignals.Count > MaxPlayerSignals)
                result.Errors.Add($"Plan declares {plan.PlayerSignals.Count} player signals; the maximum is {MaxPlayerSignals}.");

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

            foreach (var formation in plan.Formations)
                ValidateFormationPlan(formation, anchorIds, emittedSignals, result);

            return result;
        }

        private static void ValidateFormationPlan(
            FormationPlan formation, HashSet<string> anchorIds, HashSet<string> emittedSignals, PlanValidationResult result)
        {
            string Where(int stageIndex) => $"[{formation.Formation}] stage {stageIndex + 1}";

            if (formation.Stages.Count == 0)
                result.Warnings.Add($"[{formation.Formation}] has no stages; it will hold under its default order.");

            if (formation.Abort.CasualtiesAbovePercent <= 0 || formation.Abort.CasualtiesAbovePercent > 100)
                result.Errors.Add($"[{formation.Formation}] abort casualties threshold must be in (0, 100]; got {formation.Abort.CasualtiesAbovePercent}.");

            for (var i = 0; i < formation.Stages.Count; i++)
            {
                var stage = formation.Stages[i];

                if (stage.When.Count > MaxConditionsPerStage)
                    result.Errors.Add($"{Where(i)} has {stage.When.Count} trigger conditions; the maximum is {MaxConditionsPerStage} (A3.5).");

                if (stage.When.Count == 0 && i > 0)
                    result.Errors.Add($"{Where(i)} has no trigger. Only the first stage may omit it (defaults to battle start).");

                foreach (var trigger in stage.When)
                    ValidateTrigger(trigger, Where(i), anchorIds, emittedSignals, result);

                if (stage.Do == null)
                    result.Errors.Add($"{Where(i)} has no directive.");
                else
                    ValidateDirective(stage.Do, Where(i), anchorIds, result);
            }
        }

        private static void ValidateTrigger(
            TriggerSpec trigger, string where, HashSet<string> anchorIds, HashSet<string> emittedSignals, PlanValidationResult result)
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
                    break;

                case TriggerType.CasualtiesAbove:
                    if (trigger.Percent is not (> 0 and <= 100))
                        result.Errors.Add($"{where}: CasualtiesAbove needs percent in (0, 100].");
                    break;

                case TriggerType.EnemyBroken:
                    if (trigger.Formation != null)
                        RequireEnemySelector(trigger.Formation);
                    break;

                case TriggerType.SignalReceived:
                case TriggerType.PlayerSignal:
                    if (string.IsNullOrWhiteSpace(trigger.Signal))
                        result.Errors.Add($"{where}: {trigger.Type} needs a signal name.");
                    else if (!emittedSignals.Contains(trigger.Signal))
                        result.Warnings.Add($"{where}: listens for signal '{trigger.Signal}', but nothing emits it and it is not a declared player signal.");
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
