using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Planning
{
    /// <summary>
    /// The plan JSON dialect: enums as strings, unknown properties are errors
    /// (catches typos), comments allowed, nulls omitted on write. Used by the
    /// debug-plan file, the Layer-2 harness, and (later) presets/templates.
    /// </summary>
    public static class PlanSerializer
    {
        private static readonly JsonSerializerSettings Settings = new()
        {
            Converters = { new StringEnumConverter() },
            MissingMemberHandling = MissingMemberHandling.Error,
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented,
        };

        public static string Serialize(BattlePlan plan)
            => JsonConvert.SerializeObject(plan, Settings);

        /// <summary>
        /// Engine-friendly entry point: no exceptions cross this boundary.
        /// On failure returns false with a human-readable error.
        /// </summary>
        public static bool TryDeserialize(string json, out BattlePlan plan, out string error)
        {
            try
            {
                plan = JsonConvert.DeserializeObject<BattlePlan>(json, Settings);
                if (plan == null)
                {
                    error = "JSON parsed to nothing (empty file?).";
                    return false;
                }

                error = null;
                return true;
            }
            catch (Exception e)
            {
                plan = null;
                error = e is JsonException ? e.Message : e.ToString();
                return false;
            }
        }
    }
}
