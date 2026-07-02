using System;
using System.Collections.Generic;
using RealisticBattlePlanning.Diagnostics;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.CustomBattle;
using TaleWorlds.MountAndBlade.CustomBattle.CustomBattle;
using TaleWorlds.MountAndBlade.View.Tableaus;

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
                // Never push a second mission onto a live one (the console is
                // available in-battle): that corrupts the state stack.
                if (Mission.Current != null)
                    return "already in a mission; finish it first";

                // Cold-launch guard (same race ModDebugKit's dbg.battle guards):
                // pushing a game state before the view layer is up NREs deep in
                // GameStateScreenManager.OnPushState (null ThumbnailCacheManager
                // .Current) and WEDGES the game. Reachable file-first via
                // dbg.exec rbp.autobattle right after the menu appears — fail
                // cleanly + retryably instead.
                if (ThumbnailCacheManager.Current == null)
                    return "engine view layer still loading; retry rbp.autobattle in a few seconds";

                // If the Custom Battle menu is the active state, the custom game
                // is loaded — launch the mission directly. Otherwise (main menu),
                // load the custom game first; its manager then launches.
                if (Game.Current?.GameStateManager?.ActiveState is CustomBattleState)
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
