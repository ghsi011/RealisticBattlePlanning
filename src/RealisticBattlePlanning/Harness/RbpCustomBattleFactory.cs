using TaleWorlds.Core;
using TaleWorlds.MountAndBlade.CustomBattle.CustomBattle;

namespace RealisticBattlePlanning.Harness
{
    /// <summary>
    /// Builds a default land field-battle <see cref="CustomBattleData"/> the
    /// same way the Custom Battle menu's "Start" does (CustomBattleVM.
    /// PrepareBattleData), but with fixed sensible defaults so the auto-spawn
    /// harness loop needs no UI: Empire vs Aserai on the core default battle
    /// scene, the player a Defender Commander (so IsPlayerGeneral is true and
    /// the mod attaches). Troop selections are null -> faction defaults; the
    /// harness auto-split redistributes the player's troops into the plan's
    /// formations regardless, so composition only needs to be a valid army.
    /// </summary>
    internal static class RbpCustomBattleFactory
    {
        public static CustomBattleData BuildFieldBattle()
        {
            var om = Game.Current.ObjectManager;
            var playerChar = om.GetObject<BasicCharacterObject>("commander_1");
            var enemyChar = om.GetObject<BasicCharacterObject>("commander_11");
            var playerFaction = om.GetObject<BasicCultureObject>("empire");
            var enemyFaction = om.GetObject<BasicCultureObject>("aserai");

            // [infantry, ranged, cavalry, horse-archer]; the +1 commander char
            // is added by GetCustomBattleParties. Player is infantry-heavy
            // (the harness plans drive the Infantry slot).
            var playerNumbers = new[] { 150, 49, 0, 0 };
            var enemyNumbers = new[] { 120, 40, 40, 0 };

            var parties = CustomBattleHelper.GetCustomBattleParties(
                playerChar, playerSideGeneralCharacter: null, enemyChar,
                playerFaction, playerNumbers, playerTroopSelections: null,
                enemyFaction, enemyNumbers, enemyTroopSelections: null,
                isPlayerAttacker: false);

            return CustomBattleHelper.PrepareBattleData(
                playerChar, playerSideGeneralCharacter: null, parties[0], parties[1],
                CustomBattlePlayerSide.Defender, CustomBattlePlayerType.Commander,
                CustomBattleHelper.DefaultBattleGameTypeStringId,   // "Battle"
                CustomBattleData.CoreContentDefaultSceneName,       // "battle_terrain_029"
                season: "summer", timeOfDay: 6f,
                attackerMachines: null, defenderMachines: null, wallHitPointsPercentages: null,
                sceneUpgradeLevel: 1, isSallyOut: false, forcedSceneLevel: "");
        }
    }
}
