using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace RealisticBattlePlanning.Harness
{
    /// <summary>
    /// JSON for harness files. Scenario specs are hand-authored, so unknown
    /// properties are errors (catches typos, same dialect as PlanSerializer);
    /// records and results are machine-written and read leniently so an old
    /// baseline file still diffs after the model grows a field.
    /// </summary>
    public static class HarnessSerializer
    {
        private static readonly JsonSerializerSettings StrictSettings = new()
        {
            Converters = { new StringEnumConverter() },
            MissingMemberHandling = MissingMemberHandling.Error,
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented,
        };

        private static readonly JsonSerializerSettings LenientSettings = new()
        {
            Converters = { new StringEnumConverter() },
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented,
        };

        public static string Serialize(ScenarioSpec spec)
            => JsonConvert.SerializeObject(spec, StrictSettings);

        public static string Serialize(BattleRecord record)
            => JsonConvert.SerializeObject(record, LenientSettings);

        public static string Serialize(PackResult results)
            => JsonConvert.SerializeObject(results, LenientSettings);

        public static bool TryDeserializeScenario(string json, out ScenarioSpec spec, out string error)
            => TryDeserialize(json, StrictSettings, out spec, out error);

        public static bool TryDeserializeRecord(string json, out BattleRecord record, out string error)
            => TryDeserialize(json, LenientSettings, out record, out error);

        public static bool TryDeserializeResults(string json, out PackResult results, out string error)
            => TryDeserialize(json, LenientSettings, out results, out error);

        private static bool TryDeserialize<T>(string json, JsonSerializerSettings settings, out T value, out string error)
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
    }
}
