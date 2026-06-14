using System;
using RealisticBattlePlanning.Diagnostics;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade.CustomBattle;
using TaleWorlds.MountAndBlade.CustomBattle.CustomBattle;

namespace RealisticBattlePlanning.Harness
{
    /// <summary>
    /// Loads the custom-battle game exactly like the menu does, but on load
    /// finish launches a field battle directly (the auto-spawn harness loop)
    /// instead of showing the Custom Battle config screen. It still lets the
    /// base push CustomBattleState first, so when the mission ends the game
    /// returns to a valid menu state (and a subsequent rbp.autobattle can
    /// launch the next scenario without reloading the custom game).
    /// </summary>
    public sealed class RbpAutoBattleGameManager : CustomGameManager
    {
        public override void OnLoadFinished()
        {
            base.OnLoadFinished(); // pushes CustomBattleState: a valid state to return to after the mission
            try
            {
                CustomBattleHelper.StartGame(RbpCustomBattleFactory.BuildFieldBattle());
                RbpLog.Info("Auto-battle: launched a custom field battle.");
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] Auto-battle launch failed; left at the Custom Battle menu.", e);
                try
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "rbp.autobattle: failed to launch — see Logs\\rbp.log.", Colors.Red));
                }
                catch (Exception)
                {
                    // A failed toast must not take the menu down.
                }
            }
        }
    }
}
