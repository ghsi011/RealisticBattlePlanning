using System.Collections.Generic;
using RealisticBattlePlanning.Planning.Model;
using static System.FormattableString;

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

        /// <summary>
        /// Scripted player inputs the harness injects at battle-relative times,
        /// so player-conducted scenarios (signals, override/resume) run
        /// unattended — no human at the keyboard. Empty for autonomous plans.
        /// </summary>
        public List<ScenarioAction> Actions { get; set; } = new();
    }

    public enum ScenarioActionType
    {
        /// <summary>Fire a player signal through the bus (B9), like a palette press.</summary>
        Signal,

        /// <summary>Suspend a formation's plan as a manual override would (B5).</summary>
        Override,

        /// <summary>Resume a suspended formation's plan (B5).</summary>
        Resume,
    }

    /// <summary>One scripted harness input, fired once when the clock passes <see cref="AtSeconds"/>.</summary>
    public sealed class ScenarioAction
    {
        /// <summary>
        /// Battle-relative time (0 = battle start) to fire at. The effect lands
        /// on the next monitor tick (~0.25s later), so leave that slack when
        /// pairing an action with a tight assertion band.
        /// </summary>
        public float AtSeconds { get; set; }

        public ScenarioActionType Type { get; set; }

        /// <summary>Signal name (Signal actions).</summary>
        public string Signal { get; set; }

        /// <summary>Formation class name or "all" (Override / Resume actions).</summary>
        public string Formation { get; set; }

        public string Describe() => Type switch
        {
            ScenarioActionType.Signal => Invariant($"fire signal '{Signal}' at {AtSeconds:0.#}s"),
            ScenarioActionType.Override => Invariant($"override {Formation} at {AtSeconds:0.#}s"),
            ScenarioActionType.Resume => Invariant($"resume {Formation} at {AtSeconds:0.#}s"),
            _ => Type.ToString(),
        };
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

        /// <summary>A plan event (suspend/resume/abort/skip/hold) for the formation lands inside [MinSeconds, MaxSeconds].</summary>
        PlanEventBetween,
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

        /// <summary>PlanEventBetween: which recorded plan event to look for.</summary>
        public RecordedEventKind? Event { get; set; }

        /// <summary>
        /// Invariant-culture on purpose: descriptions are the join key when
        /// diffing runs against a baseline, so they must not vary with the
        /// machine's locale.
        /// </summary>
        public string Describe() => Type switch
        {
            AssertionType.StageActivatedBetween =>
                Invariant($"{Formation} stage {Stage} activates between {MinSeconds:0.#}s and {MaxSeconds:0.#}s"),
            AssertionType.StageActivatedAfterPrevious =>
                Invariant($"{Formation} stage {Stage} activates {MinSeconds:0.#}-{MaxSeconds:0.#}s after stage {Stage - 1}"),
            AssertionType.SignalEmittedBetween =>
                Invariant($"signal '{Signal}' emitted between {MinSeconds:0.#}s and {MaxSeconds:0.#}s"),
            AssertionType.StageAfterSignal =>
                Invariant($"{Formation} stage {Stage} activates within {MaxDelaySeconds:0.#}s of signal '{Signal}'"),
            AssertionType.ReachesAnchor =>
                Invariant($"{Formation} reaches anchor '{Anchor}' within {WithinMeters:0.#}m") +
                (BySeconds is { } by ? Invariant($" by {by:0.#}s") : ""),
            AssertionType.PlanEventBetween =>
                Invariant($"{Formation} {Event} between {MinSeconds:0.#}s and {MaxSeconds:0.#}s"),
            _ => Type.ToString(),
        };
    }
}
