using Newtonsoft.Json;

namespace ModDebugKit.Telemetry
{
    /// <summary>
    /// One line of the flight recorder (<c>telemetry.jsonl</c>): a timestamped,
    /// typed event the agent can replay to see what happened over a battle —
    /// phase changes, deaths, orders (M2.2), and more. One object per line.
    /// </summary>
    public sealed class TelemetryEvent
    {
        /// <summary>Monotonic per-session sequence number.</summary>
        [JsonProperty("seq")] public long Seq { get; set; }

        /// <summary>Wall-clock time, ISO-8601 UTC.</summary>
        [JsonProperty("ts")] public string TimestampUtc { get; set; }

        /// <summary>Mission time in seconds when a mission is active; omitted otherwise.</summary>
        [JsonProperty("t", NullValueHandling = NullValueHandling.Ignore)]
        public float? MissionTime { get; set; }

        /// <summary>Event kind, e.g. mission_start, deployment_finished, agent_removed, order, mission_result.</summary>
        [JsonProperty("kind")] public string Kind { get; set; }

        [JsonProperty("msg", NullValueHandling = NullValueHandling.Ignore)]
        public string Message { get; set; }

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public object Data { get; set; }
    }

    /// <summary>The telemetry event kinds the recorder emits (stringly-typed on the wire, named here).</summary>
    public static class TelemetryKinds
    {
        public const string MissionStart = "mission_start";
        public const string DeploymentFinished = "deployment_finished";
        public const string ModeChange = "mode_change";
        public const string AgentRemoved = "agent_removed";
        public const string Order = "order";
        public const string MissionResult = "mission_result";
        public const string MissionEnd = "mission_end";
        public const string Note = "note";
    }
}
