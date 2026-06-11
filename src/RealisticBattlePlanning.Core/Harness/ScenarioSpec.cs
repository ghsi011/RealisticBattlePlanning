using System.Collections.Generic;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Harness
{
    /// <summary>
    /// One Layer-2 scenario: which plan to run and the tolerance-based
    /// assertions its record must satisfy. Authored as JSON in
    /// ModuleData\Harness\&lt;name&gt;.scenario.json. Assertions use ranges,
    /// never exact ticks — battles are non-deterministic.
    /// </summary>
    public sealed class ScenarioSpec
    {
        public string Name { get; set; }

        public string Description { get; set; }

        /// <summary>Plan file name, relative to the scenario file's directory.</summary>
        public string PlanFile { get; set; }

        /// <summary>When set, the run fails if the battle outlasts this.</summary>
        public float? TimeLimitSeconds { get; set; }

        public List<ScenarioAssertion> Assertions { get; set; } = new();
    }

    public enum AssertionType
    {
        /// <summary>Stage activates inside [MinSeconds, MaxSeconds] of battle start.</summary>
        StageActivatedBetween,

        /// <summary>Stage activates [MinSeconds, MaxSeconds] after the previous stage did (scene-robust timer checks).</summary>
        StageActivatedAfterPrevious,

        /// <summary>Signal is first emitted inside [MinSeconds, MaxSeconds] of battle start.</summary>
        SignalEmittedBetween,

        /// <summary>Stage activates at most MaxDelaySeconds after the signal is first emitted (bus latency checks).</summary>
        StageAfterSignal,

        /// <summary>Formation comes within WithinMeters of the anchor, optionally by BySeconds.</summary>
        ReachesAnchor,
    }

    /// <summary>
    /// One tolerance assertion. Which parameters apply depends on
    /// <see cref="Type"/> (same flat-parameter pattern as TriggerSpec).
    /// Stage numbers are 1-based, matching the log lines and record.
    /// </summary>
    public sealed class ScenarioAssertion
    {
        public AssertionType Type { get; set; }

        public PlannedFormationClass? Formation { get; set; }

        /// <summary>1-based stage number.</summary>
        public int? Stage { get; set; }

        public string Signal { get; set; }

        public string Anchor { get; set; }

        public float? MinSeconds { get; set; }
        public float? MaxSeconds { get; set; }

        public float? WithinMeters { get; set; }

        /// <summary>ReachesAnchor deadline; unset means any time during the battle.</summary>
        public float? BySeconds { get; set; }

        /// <summary>StageAfterSignal: maximum allowed signal-to-activation delay.</summary>
        public float? MaxDelaySeconds { get; set; }

        public string Describe() => Type switch
        {
            AssertionType.StageActivatedBetween =>
                $"{Formation} stage {Stage} activates between {MinSeconds:0.#}s and {MaxSeconds:0.#}s",
            AssertionType.StageActivatedAfterPrevious =>
                $"{Formation} stage {Stage} activates {MinSeconds:0.#}-{MaxSeconds:0.#}s after stage {Stage - 1}",
            AssertionType.SignalEmittedBetween =>
                $"signal '{Signal}' emitted between {MinSeconds:0.#}s and {MaxSeconds:0.#}s",
            AssertionType.StageAfterSignal =>
                $"{Formation} stage {Stage} activates within {MaxDelaySeconds:0.#}s of signal '{Signal}'",
            AssertionType.ReachesAnchor =>
                $"{Formation} reaches anchor '{Anchor}' within {WithinMeters:0.#}m" +
                (BySeconds is { } by ? $" by {by:0.#}s" : ""),
            _ => Type.ToString(),
        };
    }
}
