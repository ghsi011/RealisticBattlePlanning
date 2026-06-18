using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ModDebugKit.Io
{
    /// <summary>
    /// The kit's one JSON dialect. Two shapes from the same settings:
    /// <see cref="Line"/> serializes a single object onto one line (for JSONL
    /// streams like <c>out.jsonl</c>), and <see cref="Pretty"/> indents
    /// (for human-read documents like <c>battle_state.json</c>). Enums are
    /// strings; nulls are omitted.
    /// </summary>
    public static class DbgJson
    {
        private static readonly JsonSerializerSettings LineSettings = Create(Formatting.None);
        private static readonly JsonSerializerSettings PrettySettings = Create(Formatting.Indented);

        /// <summary>One-line JSON (no embedded newlines) — safe to append as a JSONL record.</summary>
        public static string Line(object value) => JsonConvert.SerializeObject(value, LineSettings);

        /// <summary>Indented JSON for a standalone document.</summary>
        public static string Pretty(object value) => JsonConvert.SerializeObject(value, PrettySettings);

        public static bool TryDeserialize<T>(string json, out T value, out string error) where T : class
        {
            try
            {
                value = JsonConvert.DeserializeObject<T>(json, PrettySettings);
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

        private static JsonSerializerSettings Create(Formatting formatting) => new()
        {
            Converters = { new StringEnumConverter() },
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = formatting,
        };
    }
}
