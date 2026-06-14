using System;
using System.Collections.Generic;
using RealisticBattlePlanning.Diagnostics;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.CustomBattle.CustomBattle;

namespace RealisticBattlePlanning.Harness
{
    /// <summary>
    /// Dev-console auto-spawn for the harness loop: starts a land custom field
    /// battle programmatically, so a run needs no menu navigation. Arm a
    /// scenario first (rbp.harness_arm); the armed HarnessRecorderLogic then
    /// records, fast-forwards, and auto-leaves. From the main menu this loads
    /// the custom game and launches; from the Custom Battle menu (custom game
    /// already loaded, e.g. after a previous auto-battle) it launches directly.
    /// </summary>
    public static class AutoBattleCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("autobattle", "rbp")]
        public static string AutoBattle(List<string> args)
        {
            try
            {
                // commander_1 exists only once the custom game's objects are
                // loaded — the signal that we can launch a mission directly.
                if (Game.Current?.ObjectManager?.GetObject<BasicCharacterObject>("commander_1") != null)
                {
                    CustomBattleHelper.StartGame(RbpCustomBattleFactory.BuildFieldBattle());
                    return "launched a custom field battle (arm a scenario first with rbp.harness_arm)";
                }

                MBGameManager.StartNewGame(new RbpAutoBattleGameManager());
                return "loading the custom battle, then launching a field battle";
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] rbp.autobattle failed.", e);
                return $"autobattle failed: {e.Message}";
            }
        }
    }
}
