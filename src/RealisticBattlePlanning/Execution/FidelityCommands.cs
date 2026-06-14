using System;
using System.Collections.Generic;
using RealisticBattlePlanning.Fidelity;
using TaleWorlds.Library;

namespace RealisticBattlePlanning.Execution
{
    /// <summary>
    /// Dev-console control of the fidelity master switch (spec F, until MCM).
    /// The change applies to the NEXT battle, since the monitor captures the
    /// model + seed at mission start.
    /// </summary>
    public static class FidelityCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("fidelity", "rbp")]
        public static string Fidelity(List<string> args)
        {
            if (args.Count == 0)
                return $"fidelity is {FidelityConfig.Describe()}.\n" +
                       "usage: rbp.fidelity <off | on | fixed <tier> | seed <n|clear>>";

            switch (args[0].ToLowerInvariant())
            {
                case "off":
                    FidelityConfig.Mode = FidelityConfig.FidelityMode.Off;
                    break;

                case "on":
                case "competence":
                    FidelityConfig.Mode = FidelityConfig.FidelityMode.Competence;
                    break;

                case "fixed":
                    if (args.Count < 2 || !Enum.TryParse<FidelityTier>(args[1], ignoreCase: true, out var tier))
                        return "usage: rbp.fidelity fixed <Untrained|Drilled|Proficient|Veteran|Master>";
                    FidelityConfig.Mode = FidelityConfig.FidelityMode.Fixed;
                    FidelityConfig.FixedTier = tier;
                    break;

                case "seed":
                    if (args.Count < 2)
                        return "usage: rbp.fidelity seed <n|clear>";
                    if (string.Equals(args[1], "clear", StringComparison.OrdinalIgnoreCase))
                        FidelityConfig.Seed = null;
                    else if (int.TryParse(args[1], out var seed))
                        FidelityConfig.Seed = seed;
                    else
                        return "seed must be an integer or 'clear'";
                    break;

                default:
                    return $"unknown option '{args[0]}'. usage: rbp.fidelity <off | on | fixed <tier> | seed <n|clear>>";
            }

            return $"fidelity is now {FidelityConfig.Describe()} (applies to the next battle)";
        }
    }
}
