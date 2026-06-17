using System;
using ModDebugKit.Diagnostics;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade.CustomBattle;
using TaleWorlds.MountAndBlade.CustomBattle.CustomBattle;

namespace ModDebugKit.Battles
{
    /// <summary>
    /// Loads the custom-battle game like the menu does, then on load-finish
    /// launches the preset's field battle directly instead of showing the
    /// config screen (generalizing RBP's RbpAutoBattleGameManager). Lets the
    /// base push CustomBattleState first, so when the mission ends the game
    /// returns to a valid menu and a later dbg.battle can launch again without
    /// reloading.
    /// </summary>
    public sealed class BattleGameManager : CustomGameManager
    {
        private readonly BattlePreset _preset;

        public BattleGameManager(BattlePreset preset)
        {
            _preset = preset;
        }

        public override void OnLoadFinished()
        {
            base.OnLoadFinished(); // pushes CustomBattleState: a valid state to return to after the mission
            try
            {
                if (!BattleFactory.TryBuild(_preset, out var data, out var error))
                {
                    DbgLog.Error($"dbg.battle: could not build the battle ({error}); left at the Custom Battle menu.");
                    Toast($"dbg.battle: build failed - {error}");
                    return;
                }

                CustomBattleHelper.StartGame(data);
                DbgLog.Info("dbg.battle: launched a custom field battle.");
            }
            catch (Exception e)
            {
                DbgLog.Error("dbg.battle: launch failed; left at the Custom Battle menu.", e);
                Toast("dbg.battle: launch failed - see moddebugkit.log.");
            }
        }

        private static void Toast(string message)
        {
            try
            {
                InformationManager.DisplayMessage(new InformationMessage(message, Colors.Red));
            }
            catch (Exception)
            {
                // A failed toast must not take the menu down.
            }
        }
    }
}
