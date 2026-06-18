using Newtonsoft.Json;

namespace ModDebugKit.Commands
{
    /// <summary>
    /// The structured outcome of one command, appended as a single line to
    /// <c>out.jsonl</c> and (its <see cref="Message"/>) returned to the
    /// console. The agent reads this back to learn what happened — it is the
    /// other half of the file channel.
    /// </summary>
    public sealed class DbgResult
    {
        /// <summary>Monotonic per-session sequence number (matches the order commands were dispatched).</summary>
        [JsonProperty("seq")]
        public long Seq { get; set; }

        /// <summary>Wall-clock capture time, ISO-8601 UTC.</summary>
        [JsonProperty("ts")]
        public string TimestampUtc { get; set; }

        /// <summary>Mission time in seconds when a mission is active; null otherwise.</summary>
        [JsonProperty("missionTime", NullValueHandling = NullValueHandling.Ignore)]
        public float? MissionTime { get; set; }

        [JsonProperty("ok")]
        public bool Ok { get; set; }

        /// <summary>The dispatch key, e.g. <c>dbg.snapshot</c>.</summary>
        [JsonProperty("cmd")]
        public string Command { get; set; }

        /// <summary>The original input line, verbatim.</summary>
        [JsonProperty("raw")]
        public string Raw { get; set; }

        /// <summary>Human-readable one-liner (also the console return value).</summary>
        [JsonProperty("msg")]
        public string Message { get; set; }

        /// <summary>Set only when <see cref="Ok"/> is false.</summary>
        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public string Error { get; set; }

        /// <summary>Optional structured payload (a DTO or an inline object).</summary>
        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public object Data { get; set; }
    }
}
