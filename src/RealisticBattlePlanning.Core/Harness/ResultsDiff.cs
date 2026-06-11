using System;
using System.Collections.Generic;
using System.Linq;

namespace RealisticBattlePlanning.Harness
{
    /// <summary>One full scenario-pack run, written to Logs\Harness\last-run.results.json.</summary>
    public sealed class PackResult
    {
        /// <summary>When the pack ran (informational, excluded from diffing).</summary>
        public string RunAt { get; set; }

        public List<ScenarioResult> Scenarios { get; set; } = new();
    }

    public sealed class DiffResult
    {
        /// <summary>Regression gate: every current scenario passes and none from the baseline is missing.</summary>
        public bool Clean { get; set; }

        public List<string> Lines { get; set; } = new();

        public string Summary()
            => (Clean ? "DIFF CLEAN" : "DIFF NOT CLEAN") +
               (Lines.Count == 0 ? "" : Environment.NewLine + string.Join(Environment.NewLine, Lines));
    }

    /// <summary>
    /// Regression check between two pack runs: pass/fail transitions plus
    /// informational drift of measured values that moved noticeably while
    /// still passing (an early warning that a tolerance band is eroding).
    /// </summary>
    public static class ResultsDiff
    {
        /// <summary>Measured drift is reported when it exceeds this fraction of the baseline value...</summary>
        private const float DriftFraction = 0.25f;

        /// <summary>...and this absolute floor (so tiny values don't spam the diff).</summary>
        private const float DriftFloor = 2f;

        public static DiffResult Diff(PackResult baseline, PackResult current)
        {
            var diff = new DiffResult { Clean = true };
            var baselineByName = (baseline?.Scenarios ?? new List<ScenarioResult>()).ToDictionary(s => s.Scenario);
            var currentNames = new HashSet<string>(current.Scenarios.Select(s => s.Scenario));

            foreach (var scenario in current.Scenarios)
            {
                baselineByName.TryGetValue(scenario.Scenario, out var before);

                if (!scenario.Pass)
                {
                    diff.Clean = false;
                    var transition = before == null ? "NEW+FAIL" : before.Pass ? "PASS->FAIL" : "still FAIL";
                    diff.Lines.Add($"{transition} {scenario.Scenario}:");
                    diff.Lines.AddRange(scenario.Assertions
                        .Where(a => !a.Pass)
                        .Select(a => $"    {a.Description}: {a.Message}"));
                    continue;
                }

                if (before == null)
                    diff.Lines.Add($"NEW {scenario.Scenario}: PASS");
                else if (!before.Pass)
                    diff.Lines.Add($"FIXED {scenario.Scenario}: FAIL->PASS");
                else
                    diff.Lines.AddRange(MeasuredDrift(before, scenario));
            }

            foreach (var name in baselineByName.Keys.Where(n => !currentNames.Contains(n)))
            {
                diff.Clean = false;
                diff.Lines.Add($"MISSING {name}: in the known-good baseline but not in this run");
            }

            return diff;
        }

        private static IEnumerable<string> MeasuredDrift(ScenarioResult before, ScenarioResult current)
        {
            var baselineByDescription = before.Assertions
                .Where(a => a.Measured != null)
                .GroupBy(a => a.Description)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var assertion in current.Assertions)
            {
                if (assertion.Measured is not { } measured ||
                    !baselineByDescription.TryGetValue(assertion.Description, out var was) ||
                    was.Measured is not { } baseline)
                {
                    continue;
                }

                var delta = Math.Abs(measured - baseline);
                if (delta > DriftFloor && delta > Math.Abs(baseline) * DriftFraction)
                    yield return $"drift {current.Scenario}: {assertion.Description} measured {measured:0.#} (was {baseline:0.#})";
            }
        }
    }
}
