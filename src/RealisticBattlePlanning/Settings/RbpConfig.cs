using System;
using System.IO;
using RealisticBattlePlanning.Diagnostics;
using TaleWorlds.InputSystem;
using TaleWorlds.ModuleManager;

namespace RealisticBattlePlanning.Settings
{
    /// <summary>
    /// File-based settings (Area F lite): <c>Config\rbp.cfg</c>, created with
    /// commented defaults on first launch (so it is discoverable) and read once
    /// at module load. The numpad defaults exclude laptop/TKL keyboards
    /// entirely — this is the rebinding path until the MCM menu arrives. The
    /// rbp.* console commands stay as in-session dev overrides.
    /// </summary>
    public static class RbpConfig
    {
        public static InputKey PlanKey { get; private set; } = InputKey.Numpad0;
        public static InputKey ResumeKey { get; private set; } = InputKey.Numpad5;

        public static InputKey[] SignalKeys { get; private set; } =
            { InputKey.Numpad1, InputKey.Numpad2, InputKey.Numpad3, InputKey.Numpad4 };

        /// <summary>"auto" (campaign on, custom battle off) | "off" | "competence" | "fixed".</summary>
        public static string FidelityMode { get; private set; } = "auto";

        private const string DefaultFile =
@"# RealisticBattlePlanning settings. Edit and restart the game.
# Keys use TaleWorlds InputKey names, e.g. Numpad0, P, F5, LeftControl.

# Opens/closes the battle planner (during deployment and mid-battle).
planKey=Numpad0

# Resumes every player-overridden formation's plan mid-battle.
resumeKey=Numpad5

# Fire the plan's 1st-4th player signal.
signalKey1=Numpad1
signalKey2=Numpad2
signalKey3=Numpad3
signalKey4=Numpad4

# Officer fidelity/progression (spec Area D): auto = ON in campaigns (officers
# execute at their competence and grow with your plans), OFF in custom battles.
# Other values: off | competence | fixed.
fidelity=auto
";

        public static void Load()
        {
            try
            {
                var dir = Path.Combine(ModuleHelper.GetModuleFullPath(SubModule.ModId), "Config");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "rbp.cfg");
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, DefaultFile);
                    RbpLog.Info($"Config: wrote defaults to {path}.");
                    return;
                }

                foreach (var rawLine in File.ReadAllLines(path))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#"))
                        continue;
                    var eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    var key = line.Substring(0, eq).Trim();
                    var value = line.Substring(eq + 1).Trim();
                    Apply(key, value);
                }
                RbpLog.Info($"Config: plan={PlanKey}, resume={ResumeKey}, signals={string.Join("/", Array.ConvertAll(SignalKeys, k => k.ToString()))}, fidelity={FidelityMode}.");
            }
            catch (Exception e)
            {
                RbpLog.Error("Config load failed; using defaults.", e);
            }
        }

        private static void Apply(string key, string value)
        {
            switch (key.ToLowerInvariant())
            {
                case "plankey": PlanKey = ParseKey(value, PlanKey); break;
                case "resumekey": ResumeKey = ParseKey(value, ResumeKey); break;
                case "signalkey1": SignalKeys[0] = ParseKey(value, SignalKeys[0]); break;
                case "signalkey2": SignalKeys[1] = ParseKey(value, SignalKeys[1]); break;
                case "signalkey3": SignalKeys[2] = ParseKey(value, SignalKeys[2]); break;
                case "signalkey4": SignalKeys[3] = ParseKey(value, SignalKeys[3]); break;
                case "fidelity": FidelityMode = value.ToLowerInvariant(); break;
                default: RbpLog.Warn($"Config: unknown setting '{key}' ignored."); break;
            }
        }

        private static InputKey ParseKey(string value, InputKey fallback)
        {
            if (Enum.TryParse<InputKey>(value, ignoreCase: true, out var key))
                return key;
            RbpLog.Warn($"Config: '{value}' is not an InputKey name; keeping {fallback}.");
            return fallback;
        }
    }
}
