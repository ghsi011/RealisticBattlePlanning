using System.Linq;
using System.Text;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Planning
{
    /// <summary>
    /// Plain-language plan dump for the log (and, later, stage summaries in
    /// the editor UI, spec R4).
    /// </summary>
    public static class PlanFormatter
    {
        public static string Describe(BattlePlan plan)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Plan: {plan.Formations.Count} formation(s), {plan.Anchors.Count} anchor(s), player signals: [{string.Join(", ", plan.PlayerSignals)}]");

            foreach (var anchor in plan.Anchors)
            {
                sb.AppendLine(anchor.Basis == AnchorBasis.Scene
                    ? $"  anchor '{anchor.Id}': scene ({anchor.X:0.#}, {anchor.Y:0.#})"
                    : $"  anchor '{anchor.Id}': {anchor.Basis} forward {anchor.Forward:0.#}m, right {anchor.Right:0.#}m");
            }

            foreach (var formation in plan.Formations)
            {
                var abort = formation.Abort;
                sb.AppendLine($"  [{formation.Formation}] abort: casualties > {abort.CasualtiesAbovePercent:0.#}%" +
                              (abort.OnCommanderIncapacitated ? ", commander down" : "") +
                              (abort.OnFormationBroken ? ", broken" : ""));

                for (var i = 0; i < formation.Stages.Count; i++)
                {
                    var stage = formation.Stages[i];
                    var name = string.IsNullOrEmpty(stage.Name) ? "" : $" \"{stage.Name}\"";
                    var emits = stage.Emit.Count > 0 ? $", emits [{string.Join(", ", stage.Emit)}]" : "";
                    sb.AppendLine($"    {i + 1}.{name} {DescribeWhen(stage, i)} -> {DescribeDirective(stage.Do)}{emits}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>The abort line for a formation, plain language (R4 / the editor panel).</summary>
        public static string DescribeAbort(AbortConditions abort)
            => $"Aborts above {abort.CasualtiesAbovePercent:0.#}% casualties"
               + (abort.OnFormationBroken ? ", or if broken" : "")
               + (abort.OnCommanderIncapacitated ? ", or commander down" : "");

        /// <summary>The trigger ("When …") for a stage, plain language. Public for the editor panel.</summary>
        public static string DescribeWhen(Stage stage, int index)
        {
            if (stage.When.Count == 0)
                return index == 0 ? "On battle start" : "(no trigger!)";
            return string.Join(" AND ", stage.When.Select(DescribeTrigger));
        }

        /// <summary>One trigger condition in plain language (public for the editor's
        /// per-condition rows; <see cref="DescribeWhen"/> ANDs them for a whole stage).</summary>
        public static string DescribeTrigger(TriggerSpec t)
        {
            switch (t.Type)
            {
                case TriggerType.BattleStart: return "On battle start";
                case TriggerType.EnemyCommits:
                    return $"Enemy commits to attack{OnFormation(t)}" + (t.Meters != null ? $" (within {t.Meters:0.#}m)" : "");
                case TriggerType.EnemyWithinDistance:
                    return $"Enemy{Selector(t)} within {t.Meters:0.#}m" + (t.Anchor != null ? $" of '{t.Anchor}'" : "");
                case TriggerType.FriendlyWithinDistance: return $"{t.Formation ?? "Friendly"} within {t.Meters:0.#}m";
                case TriggerType.PositionReached: return $"Reached '{t.Anchor}'" + (t.ToleranceMeters != null ? $" (±{t.ToleranceMeters:0.#}m)" : "");
                case TriggerType.CasualtiesAbove: return $"Casualties{OnFormation(t)} above {t.Percent:0.#}%";
                case TriggerType.TimerElapsed: return $"After {t.Seconds:0.#}s";
                case TriggerType.SignalReceived: return $"Signal '{t.Signal}'";
                case TriggerType.PlayerSignal: return $"Player signal '{t.Signal}'";
                case TriggerType.EnemyBroken: return $"Enemy{Selector(t)} broken";
                default: return t.Type.ToString();
            }

            string OnFormation(TriggerSpec spec) => spec.Formation == null ? "" : $" on {spec.Formation}";
            string Selector(TriggerSpec spec) => spec.Formation == null ? "" : $" ({spec.Formation})";
        }

        /// <summary>The directive ("Do …") for a stage, plain language. Public for the editor panel.</summary>
        public static string DescribeDirective(DirectiveSpec d)
        {
            if (d == null) return "(no directive!)";

            var details = new StringBuilder();
            switch (d.Type)
            {
                case DirectiveType.Hold:
                    details.Append("Hold position");
                    break;
                case DirectiveType.MoveTo:
                    details.Append(d.Path is { Count: > 0 }
                        ? $"Move along [{string.Join(" -> ", d.Path)}]"
                        : $"Move to '{d.Anchor}'");
                    break;
                case DirectiveType.Skirmish:
                    details.Append($"Skirmish {TargetOrNearest(d)}");
                    if (d.Circle == true) details.Append(", circling");
                    break;
                case DirectiveType.FeignRetreat:
                    details.Append($"Feign retreat toward '{d.Anchor}'");
                    if (d.FireWhileWithdrawing == true) details.Append(", firing");
                    break;
                case DirectiveType.Charge:
                    details.Append($"Charge {TargetOrNearest(d)}");
                    break;
                case DirectiveType.FlankArc:
                    details.Append($"Flank arc {d.Side} on {TargetOrNearest(d)}");
                    if (d.MissileOnly == true) details.Append(", missile-only");
                    break;
                case DirectiveType.PullBack:
                    details.Append($"Pull back to '{d.Anchor}'");
                    if (d.MaintainFacing == true) details.Append(", facing the enemy");
                    break;
                case DirectiveType.Screen:
                    details.Append($"Screen {d.Target}");
                    if (d.GapMeters != null) details.Append($" at {d.GapMeters:0.#}m");
                    break;
                case DirectiveType.Follow:
                    details.Append($"Follow {d.Target}");
                    if (d.OffsetForwardMeters != null || d.OffsetRightMeters != null)
                        details.Append($" (offset {d.OffsetForwardMeters ?? 0:0.#}m fwd, {d.OffsetRightMeters ?? 0:0.#}m right)");
                    break;
                case DirectiveType.FireControl:
                    details.Append(d.Fire == FireMode.Hold ? "Hold fire" : "Free fire");
                    break;
                default:
                    details.Append(d.Type.ToString());
                    break;
            }

            if (d.Arrangement != null) details.Append($" ({d.Arrangement})");
            if (d.WidthMeters != null) details.Append($", {d.WidthMeters:0.#}m wide");
            if (d.FacingX != null && d.FacingY != null) details.Append($", facing ({d.FacingX:0.0#},{d.FacingY:0.0#})");
            if (d.StandoffMeters != null) details.Append($", standoff {d.StandoffMeters:0.#}m");
            if (d.Speed != null) details.Append($", {d.Speed.ToString().ToLowerInvariant()}");
            // A fire policy attached to a non-FireControl directive (e.g. hold + free-fire).
            if (d.Fire != null && d.Type != DirectiveType.FireControl)
                details.Append(d.Fire == FireMode.Hold ? ", hold fire" : ", free fire");
            return details.ToString();

            string TargetOrNearest(DirectiveSpec spec) => spec.Target ?? "nearest enemy";
        }
    }
}
