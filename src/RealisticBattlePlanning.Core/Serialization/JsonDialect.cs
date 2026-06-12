using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace RealisticBattlePlanning.Serialization
{
    /// <summary>
    /// The mod's one JSON dialect (enums as strings, comments allowed, nulls
    /// omitted, indented), shared by every serializer so plan and harness
    /// files can't drift apart. Strict (unknown properties are errors) for
    /// hand-authored files; lenient for machine-written ones that must stay
    /// readable across versions.
    /// </summary>
    public static class JsonDialect
    {
        public static readonly JsonSerializerSettings Strict = Create(MissingMemberHandling.Error);

        public static readonly JsonSerializerSettings Lenient = Create(MissingMemberHandling.Ignore);

        /// <summary>
        /// Engine-friendly deserialization: no exceptions cross this
        /// boundary; on failure returns false with a human-readable error.
        /// </summary>
        public static bool TryDeserialize<T>(string json, JsonSerializerSettings settings, out T value, out string error)
            where T : class
        {
            try
            {
                value = JsonConvert.DeserializeObject<T>(json, settings);
                if (value == null)
                {
                    error = "JSON parsed to nothing (empty file?).";
                    return false;
                }

                error = null;
                return true;
            }
            catch (Exception e)
            {
                value = null;
                error = e is JsonException ? e.Message : e.ToString();
                return false;
            }
        }

        private static JsonSerializerSettings Create(MissingMemberHandling missingMembers) => new()
        {
            Converters = { new StringEnumConverter() },
            MissingMemberHandling = missingMembers,
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented,
        };
    }
}
