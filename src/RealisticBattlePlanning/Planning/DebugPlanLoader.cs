using System;
using System.IO;
using RealisticBattlePlanning.Diagnostics;
using RealisticBattlePlanning.Planning.Model;
using TaleWorlds.ModuleManager;

namespace RealisticBattlePlanning.Planning
{
    /// <summary>
    /// Dev/test authoring path (plan I1, spec M1): reads a hand-written plan
    /// from the deployed module so it can be edited live between battles
    /// without rebuilding. Engine-side thin wrapper: path lookup + file IO;
    /// parsing lives in Core's PlanSerializer. Returns null — never throws —
    /// when the file is absent or broken; a bad debug plan must not affect
    /// the game (G3).
    /// </summary>
    public static class DebugPlanLoader
    {
        public const string FileName = "rbp_debug_plan.json";

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

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                RbpLog.Error($"Failed reading debug plan {path}.", e);
                return null;
            }

            if (!PlanSerializer.TryDeserialize(json, out var plan, out var error))
            {
                RbpLog.Error($"Debug plan {path} is not valid: {error}");
                return null;
            }

            RbpLog.Info($"Loaded debug plan from {path}.");
            return plan;
        }
    }
}
