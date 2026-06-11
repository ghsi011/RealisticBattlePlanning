using System;
using System.Collections.Generic;
using System.Linq;
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
                foreach (var stage in formation.Stages)
                    foreach (var signal in stage.Emit)
                        emittedSignals.Add(signal);

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
            switch (trigger.Type)
            {
                case TriggerType.TimerElapsed:
                    if (trigger.Seconds is not > 0)
                        result.Errors.Add($"{where}: TimerElapsed needs seconds > 0.");
                    break;

                case TriggerType.EnemyWithinDistance:
                case TriggerType.FriendlyWithinDistance:
                    if (trigger.Meters is not > 0)
                        result.Errors.Add($"{where}: {trigger.Type} needs meters > 0.");
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

            switch (directive.Type)
            {
                case DirectiveType.MoveTo:
                    if (directive.Path is { Count: > 0 })
                    {
                        foreach (var waypoint in directive.Path.Where(w => !anchorIds.Contains(w)))
                            result.Errors.Add($"{where}: path waypoint '{waypoint}' is not a defined anchor.");
                    }
                    else
                    {
                        RequireAnchor(directive.Anchor, "a destination anchor or path");
                    }
                    break;

                case DirectiveType.FeignRetreat:
                case DirectiveType.PullBack:
                    RequireAnchor(directive.Anchor, "a destination anchor");
                    break;

                case DirectiveType.FlankArc:
                    if (directive.Side == null)
                        result.Errors.Add($"{where}: FlankArc needs a side (Left/Right).");
                    break;

                case DirectiveType.Screen:
                case DirectiveType.Follow:
                    if (string.IsNullOrWhiteSpace(directive.Target))
                        result.Errors.Add($"{where}: {directive.Type} needs a target formation.");
                    break;

                case DirectiveType.FireControl:
                    if (directive.Fire == null)
                        result.Errors.Add($"{where}: FireControl needs fire = Hold or Free.");
                    break;
            }
        }
    }
}
