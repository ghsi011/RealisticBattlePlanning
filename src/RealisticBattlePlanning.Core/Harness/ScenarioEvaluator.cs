using System;
using System.Collections.Generic;
using System.Linq;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Harness
{
    public sealed class AssertionResult
    {
        public string Description { get; set; }
        public bool Pass { get; set; }

        /// <summary>The observed value (activation time, delay, distance) for drift tracking.</summary>
        public float? Measured { get; set; }

        /// <summary>Human-readable verdict, e.g. "activated at 34.2s, outside 10-30s".</summary>
        public string Message { get; set; }
    }

    public sealed class ScenarioResult
    {
        public string Scenario { get; set; }
        public bool Pass { get; set; }
        public List<AssertionResult> Assertions { get; set; } = new();

        public string Summary()
        {
            var lines = new List<string> { $"{(Pass ? "PASS" : "FAIL")} {Scenario}" };
            lines.AddRange(Assertions.Select(a => $"  [{(a.Pass ? "ok" : "FAIL")}] {a.Description}: {a.Message}"));
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>
    /// Checks a scenario's tolerance assertions against a recorded run.
    /// Pure Core logic so the same evaluation runs in-game (after a harness
    /// battle) and in Layer-1 tests (against simulated records).
    /// </summary>
    public static class ScenarioEvaluator
    {
        public static ScenarioResult Evaluate(ScenarioSpec spec, BattleRecord record)
        {
            var result = new ScenarioResult { Scenario = spec.Name };

            foreach (var assertion in spec.Assertions)
                result.Assertions.Add(Check(assertion, record));

            if (spec.TimeLimitSeconds is { } limit)
            {
                result.Assertions.Add(new AssertionResult
                {
                    Description = $"battle ends within {limit:0.#}s",
                    Pass = record.DurationSeconds <= limit,
                    Measured = record.DurationSeconds,
                    Message = $"battle ran {record.DurationSeconds:0.#}s",
                });
            }

            result.Pass = result.Assertions.All(a => a.Pass);
            return result;
        }

        private static AssertionResult Check(ScenarioAssertion assertion, BattleRecord record)
        {
            var result = new AssertionResult { Description = assertion.Describe() };
            switch (assertion.Type)
            {
                case AssertionType.StageActivatedBetween:
                {
                    var time = StageActivationTime(record, assertion.Formation, assertion.Stage);
                    if (time == null)
                        return Fail(result, $"{assertion.Formation} stage {assertion.Stage} never activated");
                    result.Measured = time;
                    return Verdict(result, time.Value >= assertion.MinSeconds && time.Value <= assertion.MaxSeconds,
                        $"activated at {time:0.#}s");
                }

                case AssertionType.StageActivatedAfterPrevious:
                {
                    var time = StageActivationTime(record, assertion.Formation, assertion.Stage);
                    var previous = StageActivationTime(record, assertion.Formation, assertion.Stage - 1);
                    if (time == null)
                        return Fail(result, $"{assertion.Formation} stage {assertion.Stage} never activated");
                    if (previous == null)
                        return Fail(result, $"{assertion.Formation} stage {assertion.Stage - 1} never activated");
                    var delay = time.Value - previous.Value;
                    result.Measured = delay;
                    return Verdict(result, delay >= assertion.MinSeconds && delay <= assertion.MaxSeconds,
                        $"activated {delay:0.#}s after the previous stage");
                }

                case AssertionType.SignalEmittedBetween:
                {
                    var time = SignalTime(record, assertion.Signal);
                    if (time == null)
                        return Fail(result, $"signal '{assertion.Signal}' was never emitted");
                    result.Measured = time;
                    return Verdict(result, time.Value >= assertion.MinSeconds && time.Value <= assertion.MaxSeconds,
                        $"emitted at {time:0.#}s");
                }

                case AssertionType.StageAfterSignal:
                {
                    var stageTime = StageActivationTime(record, assertion.Formation, assertion.Stage);
                    var signalTime = SignalTime(record, assertion.Signal);
                    if (signalTime == null)
                        return Fail(result, $"signal '{assertion.Signal}' was never emitted");
                    if (stageTime == null)
                        return Fail(result, $"{assertion.Formation} stage {assertion.Stage} never activated");
                    var delay = stageTime.Value - signalTime.Value;
                    result.Measured = delay;
                    return Verdict(result, delay >= 0f && delay <= assertion.MaxDelaySeconds,
                        $"activated {delay:0.#}s after the signal");
                }

                case AssertionType.ReachesAnchor:
                {
                    var anchor = record.Anchors.FirstOrDefault(a =>
                        a.Formation == assertion.Formation &&
                        string.Equals(a.Anchor, assertion.Anchor, StringComparison.OrdinalIgnoreCase));
                    if (anchor == null)
                        return Fail(result, $"anchor '{assertion.Anchor}' was not resolved for {assertion.Formation}");

                    var anchorPos = new MapVec(anchor.X, anchor.Y);
                    var samples = record.Samples
                        .Where(s => s.Formation == assertion.Formation)
                        .Where(s => assertion.BySeconds is not { } by || s.TimeSeconds <= by)
                        .ToList();
                    if (samples.Count == 0)
                        return Fail(result, $"no position samples for {assertion.Formation}");

                    var closest = samples.Min(s => new MapVec(s.X, s.Y).DistanceTo(anchorPos));
                    result.Measured = closest;
                    return Verdict(result, closest <= assertion.WithinMeters,
                        $"closest approach {closest:0.#}m");
                }

                default:
                    return Fail(result, $"assertion type {assertion.Type} is not implemented");
            }
        }

        private static float? StageActivationTime(BattleRecord record, PlannedFormationClass? formation, int? stage)
            => record.Events.FirstOrDefault(e =>
                    e.Kind == RecordedEventKind.StageActivated &&
                    e.Formation == formation &&
                    e.Stage == stage)
                ?.TimeSeconds;

        private static float? SignalTime(BattleRecord record, string signal)
            => record.Events.FirstOrDefault(e =>
                    e.Kind == RecordedEventKind.SignalEmitted &&
                    string.Equals(e.Name, signal, StringComparison.OrdinalIgnoreCase))
                ?.TimeSeconds;

        private static AssertionResult Fail(AssertionResult result, string message)
        {
            result.Pass = false;
            result.Message = message;
            return result;
        }

        private static AssertionResult Verdict(AssertionResult result, bool pass, string message)
        {
            result.Pass = pass;
            result.Message = message;
            return result;
        }
    }
}
