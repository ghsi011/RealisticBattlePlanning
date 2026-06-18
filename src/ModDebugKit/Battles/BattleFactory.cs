using System.Collections.Generic;
using ModDebugKit.Diagnostics;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.CustomBattle.CustomBattle;
using TaleWorlds.ObjectSystem;

namespace ModDebugKit.Battles
{
    /// <summary>
    /// Builds a <see cref="CustomBattleData"/> from an engine-free
    /// <see cref="BattlePreset"/> (generalizing RBP's hard-coded factory).
    /// Both parties are assembled here rather than via
    /// CustomBattleHelper.GetCustomBattleParties, so a side can be an exact
    /// per-troop roster, per-class counts, or a mix — plus named heroes. The
    /// per-class distribution mirrors the vanilla helper (even split, same-
    /// culture banner recolour). Missing preset fields fall back to the
    /// known-good default; a missing object returns a message, not an
    /// exception.
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
            var playerSide = p.Player ?? def.Player;
            var enemySide = p.Enemy ?? def.Enemy;

            BattlePresetValidator.TryParseSide(p.PlayerSide ?? "Defender", out var sideKind);
            BattlePresetValidator.TryParseRole(p.PlayerType ?? "Commander", out var roleKind);

            var playerCommanderId = playerSide.Commander ?? def.Player.Commander;
            var enemyCommanderId = enemySide.Commander ?? def.Enemy.Commander;
            var playerCultureId = playerSide.Culture ?? def.Player.Culture;
            var enemyCultureId = enemySide.Culture ?? def.Enemy.Culture;

            var playerChar = om.GetObject<BasicCharacterObject>(playerCommanderId);
            var enemyChar = om.GetObject<BasicCharacterObject>(enemyCommanderId);
            var playerCulture = om.GetObject<BasicCultureObject>(playerCultureId);
            // The enemy culture may be gated out of the active content set; fall back to the player's
            // rather than NPE deep in party-building (RBP hit this with aserai).
            var enemyCulture = om.GetObject<BasicCultureObject>(enemyCultureId) ?? playerCulture;

            if (playerChar == null || enemyChar == null || playerCulture == null)
            {
                error = $"missing custom-battle objects (playerCommander='{playerCommanderId}'={playerChar != null}, " +
                        $"enemyCommander='{enemyCommanderId}'={enemyChar != null}, " +
                        $"playerCulture='{playerCultureId}'={playerCulture != null}); custom game not loaded?";
                return false;
            }

            var isPlayerAttacker = sideKind == PlayerSideKind.Attacker;
            var playerParty = BuildParty(om, "Player Party", playerCulture, enemyCulture,
                playerChar, setGeneral: true, isPlayerAttacker ? BattleSideEnum.Attacker : BattleSideEnum.Defender,
                playerSide, def.Player);
            var enemyParty = BuildParty(om, "Enemy Party", enemyCulture, playerCulture,
                enemyChar, setGeneral: false, isPlayerAttacker ? BattleSideEnum.Defender : BattleSideEnum.Attacker,
                enemySide, def.Enemy);

            data = CustomBattleHelper.PrepareBattleData(
                playerChar, playerSideGeneralCharacter: null, playerParty, enemyParty,
                isPlayerAttacker ? CustomBattlePlayerSide.Attacker : CustomBattlePlayerSide.Defender,
                roleKind == PlayerRoleKind.Sergeant ? CustomBattlePlayerType.Sergeant : CustomBattlePlayerType.Commander,
                p.GameType ?? CustomBattleHelper.DefaultBattleGameTypeStringId,
                p.Scene ?? CustomBattleData.CoreContentDefaultSceneName,
                p.Season ?? "summer", p.TimeOfDay ?? 6f,
                attackerMachines: null, defenderMachines: null, wallHitPointsPercentages: null,
                sceneUpgradeLevel: 1, isSallyOut: false, forcedSceneLevel: "");
            return true;
        }

        private static CustomBattleCombatant BuildParty(
            MBObjectManager om, string name, BasicCultureObject culture, BasicCultureObject otherCulture,
            BasicCharacterObject commander, bool setGeneral, BattleSideEnum side, SidePreset preset, SidePreset defaults)
        {
            var banner = new Banner(culture.Banner, culture.Color, culture.Color2);
            if (culture.StringId == otherCulture.StringId)
            {
                // Same-culture battle: recolour one banner so the sides are distinguishable
                // (mirrors CustomBattleHelper.GetCustomBattleParties).
                var primary = banner.GetPrimaryColor();
                banner.ChangePrimaryColor(banner.GetFirstIconColor());
                banner.ChangeIconColors(primary);
            }

            var party = new CustomBattleCombatant(new TextObject(name), culture, banner) { Side = side };
            party.AddCharacter(commander, 1);
            if (setGeneral)
                party.SetGeneral(commander);

            if (preset.HasExplicitRoster)
                AddExplicitRoster(om, party, preset.Troops);
            else
                AddByCounts(om, party, preset.Counts ?? defaults.Counts, preset.TroopsByClass, culture);

            AddHeroes(om, party, preset.Heroes);
            return party;
        }

        private static void AddExplicitRoster(MBObjectManager om, CustomBattleCombatant party, List<TroopEntry> troops)
        {
            foreach (var entry in troops)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Troop) || entry.Count <= 0)
                    continue;
                var troop = om.GetObject<BasicCharacterObject>(entry.Troop);
                if (troop != null)
                    party.AddCharacter(troop, entry.Count);
                else
                    DbgLog.Warn($"dbg.battle: troop id '{entry.Troop}' not found; skipped {entry.Count} unit(s).");
            }
        }

        private static void AddHeroes(MBObjectManager om, CustomBattleCombatant party, List<string> heroes)
        {
            if (heroes == null)
                return;
            foreach (var heroId in heroes)
            {
                if (string.IsNullOrWhiteSpace(heroId))
                    continue;
                var hero = om.GetObject<BasicCharacterObject>(heroId);
                if (hero != null)
                    party.AddCharacter(hero, 1);
                else
                    DbgLog.Warn($"dbg.battle: hero id '{heroId}' not found; skipped.");
            }
        }

        /// <summary>
        /// Per-class roster: resolve each class's troop list, then place the
        /// counts via the engine-free <see cref="RosterDistribution"/> (even
        /// split + horse-archer redistribution, mirroring vanilla
        /// PopulateListsWithDefaults). A class with a count but no resolvable
        /// troop is warned about rather than silently dropped.
        /// </summary>
        private static void AddByCounts(MBObjectManager om, CustomBattleCombatant party, int[] counts, List<string>[] troopsByClass, BasicCultureObject culture)
        {
            var lists = new List<BasicCharacterObject>[4];
            var troopsPerClass = new int[4];
            for (var cls = 0; cls < 4; cls++)
            {
                lists[cls] = ResolveClassList(om, troopsByClass, cls, culture);
                troopsPerClass[cls] = lists[cls].Count;
            }

            var distribution = RosterDistribution.Distribute(counts, troopsPerClass);
            foreach (var assignment in distribution.Assignments)
                party.AddCharacter(lists[assignment.ClassIndex][assignment.TroopIndex], assignment.Count);
            foreach (var dropped in distribution.Dropped)
                DbgLog.Warn($"dbg.battle: no troop resolved for class {dropped.ClassIndex} ({(FormationClass)dropped.ClassIndex}) of culture '{culture.StringId}'; {dropped.Count} unit(s) dropped.");
        }

        private static List<BasicCharacterObject> ResolveClassList(MBObjectManager om, List<string>[] troopsByClass, int cls, BasicCultureObject culture)
        {
            var list = new List<BasicCharacterObject>();
            if (troopsByClass != null && cls < troopsByClass.Length && troopsByClass[cls] != null)
            {
                foreach (var id in troopsByClass[cls])
                {
                    if (string.IsNullOrWhiteSpace(id))
                        continue;
                    var troop = om.GetObject<BasicCharacterObject>(id);
                    if (troop != null)
                        list.Add(troop);
                    else
                        DbgLog.Warn($"dbg.battle: troop id '{id}' not found; ignored for class {cls}.");
                }
            }

            if (list.Count == 0)
            {
                var def = CustomBattleHelper.GetDefaultTroopOfFormationForFaction(culture, (FormationClass)cls);
                if (def != null)
                    list.Add(def);
            }

            return list;
        }
    }
}
