using System;
using System.IO;
using ModDebugKit.Commands;
using ModDebugKit.Diagnostics;
using ModDebugKit.Io;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.CustomBattle;
using TaleWorlds.MountAndBlade.CustomBattle.CustomBattle;

namespace ModDebugKit.Battles
{
    /// <summary>
    /// The battle-factory commands (M1). For now: <c>dbg.battle</c> launches a
    /// preset-driven custom field battle with no menu navigation, so an agent
    /// can put the game into an exact battle through the file channel.
    /// </summary>
    public static class BattleCommands
    {
        /// <summary>The last preset launched, for dbg.restart (M1.3).</summary>
        public static BattlePreset LastPreset { get; private set; }

        public static void RegisterAll(CommandDispatcher dispatcher)
        {
            dispatcher.Register("dbg.battle",
                "dbg.battle [preset|path] - launch a custom field battle from a preset (no arg = Empire vs Aserai default)",
                Battle);
        }

        private static DbgOutcome Battle(DbgCommand command)
        {
            // Never push a second mission onto a live one (the channel runs in-battle too):
            // that corrupts the state stack.
            if (Mission.Current != null)
                return DbgOutcome.Failure("already in a mission; finish it first");

            BattlePreset preset;
            string source;
            if (command.Arg(0) != null)
            {
                if (!TryLoadPreset(command.Arg(0), out preset, out var loadError))
                    return DbgOutcome.Failure(loadError);
                source = command.Arg(0);
            }
            else
            {
                preset = BattlePreset.CreateDefault();
                source = "default";
            }

            var errors = BattlePresetValidator.Validate(preset);
            if (errors.Count > 0)
                return DbgOutcome.Failure("invalid preset: " + string.Join("; ", errors));

            LastPreset = preset;

            try
            {
                // If the Custom Battle menu is already active the custom game is loaded —
                // launch directly. Otherwise (main menu) load the custom game first; its
                // manager then launches the battle on load-finish.
                if (Game.Current?.GameStateManager?.ActiveState is CustomBattleState)
                {
                    if (!BattleFactory.TryBuild(preset, out var data, out var buildError))
                        return DbgOutcome.Failure($"battle build failed: {buildError}");
                    CustomBattleHelper.StartGame(data);
                    return DbgOutcome.Success($"launched custom battle (preset: {source})", new { preset = source, mode = "direct" });
                }

                MBGameManager.StartNewGame(new BattleGameManager(preset));
                return DbgOutcome.Success($"loading custom battle (preset: {source}); will launch a field battle on load",
                    new { preset = source, mode = "load" });
            }
            catch (Exception e)
            {
                DbgLog.Error("dbg.battle failed.", e);
                return DbgOutcome.Failure($"battle failed: {e.Message}");
            }
        }

        /// <summary>
        /// Resolve a preset argument: a bare name -> <c>presets/&lt;name&gt;.json</c>;
        /// anything with a separator or a .json suffix -> a path relative to the
        /// output root (or absolute).
        /// </summary>
        private static bool TryLoadPreset(string arg, out BattlePreset preset, out string error)
        {
            preset = null;
            error = null;

            var paths = ModDebugKitRuntime.Paths;
            string path;
            var looksLikePath = arg.IndexOf('/') >= 0 || arg.IndexOf('\\') >= 0 ||
                                arg.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            path = looksLikePath ? paths.Resolve(arg) : Path.Combine(paths.PresetsDir, arg + ".json");

            if (!File.Exists(path))
            {
                error = $"preset not found: {path}";
                return false;
            }

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                error = $"could not read preset '{path}': {e.Message}";
                return false;
            }

            if (!DbgJson.TryDeserialize<BattlePreset>(json, out preset, out var parseError))
            {
                error = $"preset parse error in '{path}': {parseError}";
                return false;
            }

            return true;
        }
    }
}
