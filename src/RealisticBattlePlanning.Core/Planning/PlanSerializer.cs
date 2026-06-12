using Newtonsoft.Json;
using RealisticBattlePlanning.Planning.Model;
using RealisticBattlePlanning.Serialization;

namespace RealisticBattlePlanning.Planning
{
    /// <summary>
    /// Plan JSON: the shared strict dialect (unknown properties are errors —
    /// catches typos). Used by the debug-plan file, the Layer-2 harness, and
    /// (later) presets/templates.
    /// </summary>
    public static class PlanSerializer
    {
        public static string Serialize(BattlePlan plan)
            => JsonConvert.SerializeObject(plan, JsonDialect.Strict);

        public static bool TryDeserialize(string json, out BattlePlan plan, out string error)
            => JsonDialect.TryDeserialize(json, JsonDialect.Strict, out plan, out error);
    }
}
