using Newtonsoft.Json;
using RealisticBattlePlanning.Serialization;

namespace RealisticBattlePlanning.Harness
{
    /// <summary>
    /// JSON for harness files, on the shared dialect. Scenario specs are
    /// hand-authored, so they parse strictly; records and results are
    /// machine-written and read leniently so an old baseline file still
    /// diffs after the model grows a field.
    /// </summary>
    public static class HarnessSerializer
    {
        public static string Serialize(ScenarioSpec spec)
            => JsonConvert.SerializeObject(spec, JsonDialect.Strict);

        public static string Serialize(BattleRecord record)
            => JsonConvert.SerializeObject(record, JsonDialect.Lenient);

        public static string Serialize(PackResult results)
            => JsonConvert.SerializeObject(results, JsonDialect.Lenient);

        public static bool TryDeserializeScenario(string json, out ScenarioSpec spec, out string error)
            => JsonDialect.TryDeserialize(json, JsonDialect.Strict, out spec, out error);

        public static bool TryDeserializeRecord(string json, out BattleRecord record, out string error)
            => JsonDialect.TryDeserialize(json, JsonDialect.Lenient, out record, out error);

        public static bool TryDeserializeResults(string json, out PackResult results, out string error)
            => JsonDialect.TryDeserialize(json, JsonDialect.Lenient, out results, out error);
    }
}
