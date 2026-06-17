using System.Collections.Generic;
using ModDebugKit.Diagnostics;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade.CustomBattle.CustomBattle;
using TaleWorlds.ObjectSystem;

namespace ModDebugKit.Battles
{
    /// <summary>
    /// Builds a <see cref="CustomBattleData"/> from an engine-free
    /// <see cref="BattlePreset"/>, the same way the Custom Battle menu's
    /// "Start" does (generalizing RBP's hard-coded RbpCustomBattleFactory).
    /// Missing preset fields fall back to the known-good default, so a partial
    /// preset only overrides what it sets. Returns null + a message on any
    /// missing object rather than throwing deep in the engine.
    /// </summary>
    public static class BattleFactory
    {
        // CustomBattleData is a struct, so failure can't be a null return: use the try-pattern.
        public static bool TryBuild(BattlePreset preset, out CustomBattleData data, out string error)
        {
            data = default;
            error = null;

            var om = Game.Current?.ObjectManager;
            if (om == null)
            {
                error = "no active game/object manager; is a game loaded?";
                return false;
            }

            var def = BattlePreset.CreateDefault();
            var p = preset ?? def;
            var playerSidePreset = p.Player ?? def.Player;
            var enemySidePreset = p.Enemy ?? def.Enemy;

            BattlePresetValidator.TryParseSide(p.PlayerSide ?? "Defender", out var sideKind);
            BattlePresetValidator.TryParseRole(p.PlayerType ?? "Commander", out var roleKind);

            var playerCommanderId = playerSidePreset.Commander ?? def.Player.Commander;
            var enemyCommanderId = enemySidePreset.Commander ?? def.Enemy.Commander;
            var playerCultureId = playerSidePreset.Culture ?? def.Player.Culture;
            var enemyCultureId = enemySidePreset.Culture ?? def.Enemy.Culture;

            var playerChar = om.GetObject<BasicCharacterObject>(playerCommanderId);
            var enemyChar = om.GetObject<BasicCharacterObject>(enemyCommanderId);
            var playerCulture = om.GetObject<BasicCultureObject>(playerCultureId);
            // The enemy culture may be gated out of the active content set; fall back to the player's
            // rather than NPE deep in GetCustomBattleParties (RBP hit this with aserai).
            var enemyCulture = om.GetObject<BasicCultureObject>(enemyCultureId) ?? playerCulture;

            if (playerChar == null || enemyChar == null || playerCulture == null)
            {
                error = $"missing custom-battle objects (playerCommander='{playerCommanderId}'={playerChar != null}, " +
                        $"enemyCommander='{enemyCommanderId}'={enemyChar != null}, " +
                        $"playerCulture='{playerCultureId}'={playerCulture != null}); custom game not loaded?";
                return false;
            }

            // GetCustomBattleParties mutates the counts arrays as it distributes troops, so clone
            // them — otherwise a second build (dbg.restart) would see zeroed counts.
            var playerCounts = (int[])(playerSidePreset.Counts ?? def.Player.Counts).Clone();
            var enemyCounts = (int[])(enemySidePreset.Counts ?? def.Enemy.Counts).Clone();

            var playerSelections = ResolveSelections(om, playerSidePreset.TroopsByClass);
            var enemySelections = ResolveSelections(om, enemySidePreset.TroopsByClass);

            var isPlayerAttacker = sideKind == PlayerSideKind.Attacker;
            var parties = CustomBattleHelper.GetCustomBattleParties(
                playerChar, playerSideGeneralCharacter: null, enemyChar,
                playerCulture, playerCounts, playerSelections,
                enemyCulture, enemyCounts, enemySelections,
                isPlayerAttacker);

            data = CustomBattleHelper.PrepareBattleData(
                playerChar, playerSideGeneralCharacter: null, parties[0], parties[1],
                isPlayerAttacker ? CustomBattlePlayerSide.Attacker : CustomBattlePlayerSide.Defender,
                roleKind == PlayerRoleKind.Sergeant ? CustomBattlePlayerType.Sergeant : CustomBattlePlayerType.Commander,
                p.GameType ?? CustomBattleHelper.DefaultBattleGameTypeStringId,
                p.Scene ?? CustomBattleData.CoreContentDefaultSceneName,
                p.Season ?? "summer", p.TimeOfDay ?? 6f,
                attackerMachines: null, defenderMachines: null, wallHitPointsPercentages: null,
                sceneUpgradeLevel: 1, isSallyOut: false, forcedSceneLevel: "");
            return true;
        }

        /// <summary>
        /// Maps per-class troop-id lists to the engine's
        /// <see cref="List{T}"/>[4] selections. Null/empty input -> null, so
        /// the helper fills every class with the culture's default troop.
        /// Unknown ids are dropped (logged); a class left empty also falls back
        /// to its default.
        /// </summary>
        private static List<BasicCharacterObject>[] ResolveSelections(MBObjectManager om, List<string>[] troopsByClass)
        {
            if (troopsByClass == null)
                return null;

            var selections = new List<BasicCharacterObject>[4];
            var any = false;
            for (var i = 0; i < 4; i++)
            {
                selections[i] = new List<BasicCharacterObject>();
                if (i >= troopsByClass.Length || troopsByClass[i] == null)
                    continue;
                foreach (var id in troopsByClass[i])
                {
                    if (string.IsNullOrWhiteSpace(id))
                        continue;
                    var troop = om.GetObject<BasicCharacterObject>(id);
                    if (troop != null)
                    {
                        selections[i].Add(troop);
                        any = true;
                    }
                    else
                    {
                        DbgLog.Warn($"dbg.battle: troop id '{id}' not found; that class falls back to the culture default.");
                    }
                }
            }

            return any ? selections : null;
        }
    }
}
