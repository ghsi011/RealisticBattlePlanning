using Newtonsoft.Json;

namespace ModDebugKit.Diagnostics
{
    /// <summary>
    /// One captured fault, appended to <c>errors.jsonl</c>: the kit's own caught
    /// faults and any unhandled AppDomain exception, with the stack trace. The
    /// agent reads this to see what went wrong without scraping the game log.
    /// </summary>
    public sealed class ErrorRecord
    {
        [JsonProperty("seq")] public long Seq { get; set; }

        [JsonProperty("ts")] public string TimestampUtc { get; set; }

        [JsonProperty("t", NullValueHandling = NullValueHandling.Ignore)]
        public float? MissionTime { get; set; }

        /// <summary>Where the fault came from: "modkit" (kit-caught) or "appdomain" (unhandled).</summary>
        [JsonProperty("source")] public string Source { get; set; }

        [JsonProperty("message")] public string Message { get; set; }

        [JsonProperty("exceptionType", NullValueHandling = NullValueHandling.Ignore)]
        public string ExceptionType { get; set; }

        [JsonProperty("stack", NullValueHandling = NullValueHandling.Ignore)]
        public string Stack { get; set; }

        /// <summary>True when the runtime reported the exception as process-terminating.</summary>
        [JsonProperty("terminating", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Terminating { get; set; }

        /// <summary>Path of the auto-snapshot written for the first fault, if any.</summary>
        [JsonProperty("snapshot", NullValueHandling = NullValueHandling.Ignore)]
        public string Snapshot { get; set; }
    }
}
