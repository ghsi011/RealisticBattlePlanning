using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Harness
{
    /// <summary>
    /// After-Action Report (spec B10): a plain-language summary of a battle from
    /// its recorded events — the per-formation timeline plus a tally of the
    /// deviations (reaction delays, skips, aborts, overrides, holds) that made it
    /// memorable. Built from the same <see cref="BattleRecord"/> the harness
    /// captures, so every plannable battle can end with "here's what your
    /// commanders actually did." Engine-free and unit-tested; the end-of-battle
    /// screen (engine) is a thin view over this.
    /// </summary>
    public sealed class AfterActionReport
    {
        private AfterActionReport(BattleRecord record, IReadOnlyList<FormationStory> formations, DeviationTally deviations)
        {
            Scenario = record.Scenario;
            DurationSeconds = record.DurationSeconds;
            Result = record.Result;
            Fault = record.Fault;
            Formations = formations;
            Deviations = deviations;
        }

        public string Scenario { get; }
        public float DurationSeconds { get; }
        public string Result { get; }
        public string Fault { get; }
        public IReadOnlyList<FormationStory> Formations { get; }
        public DeviationTally Deviations { get; }

        public static AfterActionReport Build(BattleRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var events = record.Events ?? new List<RecordedEvent>();

            var stories = events
                .GroupBy(e => e.Formation)
                .OrderBy(g => (int)g.Key)
                .Select(g => new FormationStory(g.Key, g.OrderBy(e => e.TimeSeconds).Select(Line).ToList()))
                .ToList();

            int Count(RecordedEventKind kind) => events.Count(e => e.Kind == kind);
            var deviations = new DeviationTally(
                reactionDelays: Count(RecordedEventKind.ReactionDelayed),
                totalReactionDelaySeconds: events.Where(e => e.Kind == RecordedEventKind.ReactionDelayed).Sum(e => e.DelaySeconds ?? 0f),
                skips: Count(RecordedEventKind.StageSkipped),
                aborts: Count(RecordedEventKind.PlanAborted),
                overrides: Count(RecordedEventKind.PlanSuspended),
                holds: Count(RecordedEventKind.PlanHolding));

            return new AfterActionReport(record, stories, deviations);
        }

        private static string Line(RecordedEvent e)
        {
            var t = $"{N0(e.TimeSeconds)}s";
            var name = string.IsNullOrEmpty(e.Name) ? "" : $" \"{e.Name}\"";
            switch (e.Kind)
            {
                case RecordedEventKind.StageActivated: return $"{t}  began stage {e.Stage}{name}";
                case RecordedEventKind.SignalEmitted: return $"{t}  emitted signal '{e.Name}'";
                case RecordedEventKind.WaypointReached: return $"{t}  reached waypoint {e.Waypoint}";
                case RecordedEventKind.ReactionDelayed: return $"{t}  stage {e.Stage} delayed {N1(e.DelaySeconds ?? 0f)}s (commander reaction, {e.Name})";
                case RecordedEventKind.PlanSuspended: return $"{t}  you took command (override)";
                case RecordedEventKind.PlanResumed: return $"{t}  resumed the plan at stage {e.Stage}";
                case RecordedEventKind.PlanAborted: return $"{t}  ABORTED — {e.Name}";
                case RecordedEventKind.StageSkipped: return $"{t}  skipped stage {e.Stage} — {e.Name}";
                case RecordedEventKind.PlanHolding: return $"{t}  holding — {e.Name}";
                default: return $"{t}  {e.Kind}";
            }
        }

        /// <summary>Plain-language report (for the log / the end-of-battle screen).</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"After-Action Report — {Scenario ?? "battle"} ({N1(DurationSeconds)}s, {Result ?? "?"})");
            if (!string.IsNullOrEmpty(Fault))
                sb.AppendLine($"  FAULT: {Fault}");
            foreach (var formation in Formations)
            {
                sb.AppendLine($"  [{formation.Formation}]");
                foreach (var line in formation.Lines)
                    sb.AppendLine($"    {line}");
            }
            sb.Append(Deviations.Describe());
            return sb.ToString().TrimEnd();
        }

        internal static string N0(float v) => v.ToString("0", CultureInfo.InvariantCulture);
        internal static string N1(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    }

    /// <summary>One formation's ordered timeline of what it actually did.</summary>
    public sealed class FormationStory
    {
        public FormationStory(PlannedFormationClass formation, IReadOnlyList<string> lines)
        {
            Formation = formation;
            Lines = lines;
        }

        public PlannedFormationClass Formation { get; }
        public IReadOnlyList<string> Lines { get; }
    }

    /// <summary>How much the battle deviated from a clean run — the "wobbles".</summary>
    public sealed class DeviationTally
    {
        public DeviationTally(int reactionDelays, float totalReactionDelaySeconds, int skips, int aborts, int overrides, int holds)
        {
            ReactionDelays = reactionDelays;
            TotalReactionDelaySeconds = totalReactionDelaySeconds;
            Skips = skips;
            Aborts = aborts;
            Overrides = overrides;
            Holds = holds;
        }

        public int ReactionDelays { get; }
        public float TotalReactionDelaySeconds { get; }
        public int Skips { get; }
        public int Aborts { get; }
        public int Overrides { get; }
        public int Holds { get; }
        public bool Any => ReactionDelays + Skips + Aborts + Overrides + Holds > 0;

        public string Describe()
        {
            if (!Any)
                return "  Deviations: none — the plan ran clean.";
            var parts = new List<string>();
            if (ReactionDelays > 0) parts.Add($"{ReactionDelays} reaction delay(s) totalling {AfterActionReport.N1(TotalReactionDelaySeconds)}s");
            if (Skips > 0) parts.Add($"{Skips} skipped stage(s)");
            if (Aborts > 0) parts.Add($"{Aborts} abort(s)");
            if (Overrides > 0) parts.Add($"{Overrides} override(s)");
            if (Holds > 0) parts.Add($"{Holds} hold(s)");
            return "  Deviations: " + string.Join(", ", parts) + ".";
        }
    }
}
