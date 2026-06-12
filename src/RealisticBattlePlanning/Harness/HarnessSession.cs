using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RealisticBattlePlanning.Diagnostics;
using RealisticBattlePlanning.Planning;
using RealisticBattlePlanning.Planning.Model;
using TaleWorlds.ModuleManager;

namespace RealisticBattlePlanning.Harness
{
    /// <summary>
    /// The armed scenario queue (Layer-2 harness, dev-mode only — no
    /// player-facing surface, G5). Scenarios live in ModuleData\Harness as
    /// &lt;name&gt;.scenario.json next to their plan files; arming validates
    /// everything up front so a typo fails in the console, not mid-battle.
    /// Results land in Logs\Harness: one record per scenario, the pack's
    /// last-run.results.json, and the accepted known-good.results.json
    /// baseline the diff gate compares against.
    /// </summary>
    internal static class HarnessSession
    {
        private sealed class ArmedScenario
        {
            public ScenarioSpec Spec;
            public BattlePlan Plan;
        }

        private static readonly List<ArmedScenario> Queue = new();
        private static readonly List<ScenarioResult> Completed = new();
        private static int _index;

        public static bool IsArmed => _index < Queue.Count;

        public static ScenarioSpec CurrentScenario => IsArmed ? Queue[_index].Spec : null;

        /// <summary>The armed scenario's plan, or null when the harness is idle. Pure peek — the queue advances on completion.</summary>
        public static BattlePlan PlanForNextBattle() => IsArmed ? Queue[_index].Plan : null;

        public static string ScenariosDir
            => Path.Combine(ModuleHelper.GetModuleFullPath(SubModule.ModId), "ModuleData", "Harness");

        public static string ResultsDir
            => Path.Combine(ModuleHelper.GetModuleFullPath(SubModule.ModId), "Logs", "Harness");

        public static string LastRunPath => Path.Combine(ResultsDir, "last-run.results.json");
        public static string KnownGoodPath => Path.Combine(ResultsDir, "known-good.results.json");

        public static IReadOnlyList<string> ListScenarioNames()
        {
            if (!Directory.Exists(ScenariosDir))
                return Array.Empty<string>();
            return Directory.GetFiles(ScenariosDir, "*.scenario.json")
                .Select(f => Path.GetFileName(f).Replace(".scenario.json", ""))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Arms the named scenarios (in order). Returns null on success, else a readable error; on error nothing is armed.</summary>
        public static string Arm(IReadOnlyList<string> names)
        {
            var armed = new List<ArmedScenario>();
            foreach (var name in names)
            {
                var error = LoadScenario(name, out var scenario);
                if (error != null)
                    return error;
                armed.Add(scenario);
            }

            if (armed.Count == 0)
                return "no scenarios to arm";

            Queue.Clear();
            Completed.Clear();
            Queue.AddRange(armed);
            _index = 0;
            RbpLog.Info($"Harness armed: {string.Join(", ", Queue.Select(s => s.Spec.Name))}. " +
                        "Start a field battle you command to run the next scenario.");
            return null;
        }

        public static void Disarm()
        {
            Queue.Clear();
            Completed.Clear();
            _index = 0;
        }

        public static string Status()
        {
            if (Queue.Count == 0)
                return "harness idle; nothing armed";
            if (!IsArmed)
                return $"pack finished ({Completed.Count(r => r.Pass)}/{Completed.Count} passed); see {LastRunPath}";
            return $"scenario {_index + 1}/{Queue.Count} armed: '{CurrentScenario.Name}' " +
                   $"({Completed.Count(r => r.Pass)}/{Completed.Count} passed so far)";
        }

        /// <summary>
        /// Called by the recorder behavior when a harness battle ends. Writes
        /// the record, advances the queue, and on the last scenario writes the
        /// pack results and logs the diff against the known-good baseline.
        /// The completing scenario must still be the armed one — console
        /// arm/disarm mid-battle otherwise advances the wrong queue and
        /// pollutes the pack results.
        /// </summary>
        public static void OnScenarioCompleted(ScenarioSpec scenario, BattleRecord record, ScenarioResult result)
        {
            try
            {
                if (!IsArmed || !ReferenceEquals(Queue[_index].Spec, scenario))
                {
                    RbpLog.Warn($"Harness: the armed pack changed mid-battle (arm/disarm from the console); " +
                                $"result for '{result.Scenario}' is discarded.");
                    return;
                }

                Directory.CreateDirectory(ResultsDir);
                File.WriteAllText(
                    Path.Combine(ResultsDir, $"{result.Scenario}.record.json"),
                    HarnessSerializer.Serialize(record));

                Completed.Add(result);
                _index++;
                RbpLog.Info($"Harness: {result.Summary()}");

                if (IsArmed)
                {
                    RbpLog.Info($"Harness: next scenario is '{CurrentScenario.Name}' ({_index + 1}/{Queue.Count}). " +
                                "Start the next field battle to run it.");
                }
                else
                {
                    var pack = new PackResult
                    {
                        RunAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"),
                        Scenarios = Completed.ToList(),
                    };
                    File.WriteAllText(LastRunPath, HarnessSerializer.Serialize(pack));
                    RbpLog.Info($"Harness: pack finished, results written to {LastRunPath}.");
                    RbpLog.Info("Harness: " + DiffLastRun());
                }
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] Harness failed writing scenario results.", e);
            }
        }

        /// <summary>Diffs last-run against the known-good baseline. Returns the readable summary.</summary>
        public static string DiffLastRun()
        {
            if (!TryReadResults(LastRunPath, out var current, out var error))
                return $"no last run to diff ({error})";

            if (!TryReadResults(KnownGoodPath, out var baseline, out _))
                return "no known-good baseline yet; run rbp.harness_accept to promote this run.\n" +
                       string.Join("\n", current.Scenarios.Select(s => s.Summary()));

            return ResultsDiff.Diff(baseline, current).Summary();
        }

        /// <summary>Promotes the last run to the known-good baseline.</summary>
        public static string AcceptLastRun()
        {
            if (!File.Exists(LastRunPath))
                return "no last run to accept";
            File.Copy(LastRunPath, KnownGoodPath, overwrite: true);
            return $"known-good baseline updated from {LastRunPath}";
        }

        private static string LoadScenario(string name, out ArmedScenario scenario)
        {
            scenario = null;
            var specPath = Path.Combine(ScenariosDir, $"{name}.scenario.json");
            if (!File.Exists(specPath))
                return $"no scenario file at {specPath}";

            if (!HarnessSerializer.TryDeserializeScenario(File.ReadAllText(specPath), out var spec, out var error))
                return $"{specPath}: {error}";
            spec.Name = name; // The file name is authoritative.

            var specErrors = ScenarioValidator.Validate(spec);
            if (specErrors.Count > 0)
                return $"{specPath}: {string.Join("; ", specErrors)}";

            var planPath = Path.Combine(ScenariosDir, spec.PlanFile);
            if (!File.Exists(planPath))
                return $"{specPath}: plan file {planPath} does not exist";

            if (!PlanSerializer.TryDeserialize(File.ReadAllText(planPath), out var plan, out error))
                return $"{planPath}: {error}";

            var validation = PlanValidator.Validate(plan);
            if (!validation.IsValid)
                return $"{planPath}: invalid plan: {string.Join("; ", validation.Errors)}";

            scenario = new ArmedScenario { Spec = spec, Plan = plan };
            return null;
        }

        private static bool TryReadResults(string path, out PackResult results, out string error)
        {
            results = null;
            if (!File.Exists(path))
            {
                error = $"{path} does not exist";
                return false;
            }

            return HarnessSerializer.TryDeserializeResults(File.ReadAllText(path), out results, out error);
        }
    }
}
