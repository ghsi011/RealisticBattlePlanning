using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using RealisticBattlePlanning.Diagnostics;
using RealisticBattlePlanning.Planning.Model;
using TaleWorlds.ModuleManager;

namespace RealisticBattlePlanning.Planning
{
    /// <summary>
    /// Dev/test authoring path (plan I1, spec M1): reads a hand-written plan
    /// from the deployed module so it can be edited live between battles
    /// without rebuilding. Returns null — never throws — when the file is
    /// absent or broken; a bad debug plan must not affect the game (G3).
    /// </summary>
    public static class DebugPlanLoader
    {
        public const string FileName = "rbp_debug_plan.json";

        private static readonly JsonSerializerSettings Settings = new()
        {
            Converters = { new StringEnumConverter() },
            // Catches typo'd property names instead of silently ignoring them.
            MissingMemberHandling = MissingMemberHandling.Error,
        };

        public static BattlePlan TryLoad()
        {
            string path;
            try
            {
                path = Path.Combine(ModuleHelper.GetModuleFullPath(SubModule.ModId), "ModuleData", FileName);
            }
            catch (Exception e)
            {
                RbpLog.Error("Could not resolve the module path for the debug plan.", e);
                return null;
            }

            if (!File.Exists(path))
            {
                RbpLog.Info($"No debug plan at {path}; nothing to load.");
                return null;
            }

            try
            {
                var plan = JsonConvert.DeserializeObject<BattlePlan>(File.ReadAllText(path), Settings);
                if (plan == null)
                {
                    RbpLog.Warn($"Debug plan {path} parsed to nothing (empty file?).");
                    return null;
                }

                RbpLog.Info($"Loaded debug plan from {path}.");
                return plan;
            }
            catch (JsonException e)
            {
                RbpLog.Error($"Debug plan {path} is not valid: {e.Message}");
                return null;
            }
            catch (Exception e)
            {
                RbpLog.Error($"Failed reading debug plan {path}.", e);
                return null;
            }
        }
    }
}
