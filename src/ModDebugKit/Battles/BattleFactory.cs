using System.Collections.Generic;
using System.Linq;
using ModDebugKit.Diagnostics;
using TaleWorlds.Core;
using TaleWorlds.Library;
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
                AddByCounts(om, party, (int[])(preset.Counts ?? defaults.Counts).Clone(), preset.TroopsByClass, culture);

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
        /// Per-class roster, mirroring CustomBattleHelper.PopulateListsWithDefaults:
        /// each class bucket is the listed troop ids (or the culture default),
        /// and the count is split evenly across that bucket's troops. If a
        /// culture has no horse-archer troop, its HA count is redistributed to
        /// the other classes (as the vanilla helper does).
        /// </summary>
        private static void AddByCounts(MBObjectManager om, CustomBattleCombatant party, int[] counts, List<string>[] troopsByClass, BasicCultureObject culture)
        {
            var lists = new List<BasicCharacterObject>[4];
            for (var cls = 0; cls < 4; cls++)
            {
                lists[cls] = ResolveClassList(om, troopsByClass, cls, culture);
            }

            if (lists[3].Count == 0 || lists[3].All(t => t == null))
            {
                counts[2] += counts[3] / 3;
                counts[1] += counts[3] / 3;
                counts[0] += counts[3] / 3;
                counts[0] += counts[3] - counts[3] / 3 * 3;
                counts[3] = 0;
            }

            for (var cls = 0; cls < 4; cls++)
            {
                var list = lists[cls];
                var remaining = counts[cls];
                if (remaining <= 0 || list.Count == 0)
                    continue;

                var perTroop = (float)remaining / list.Count;
                var carry = 0f;
                for (var k = 0; k < list.Count; k++)
                {
                    var share = perTroop + carry;
                    var floored = MathF.Floor(share);
                    carry = share - floored;
                    if (list[k] != null && floored > 0)
                        party.AddCharacter(list[k], floored);
                    remaining -= floored;
                    if (k == list.Count - 1 && remaining > 0 && list[k] != null)
                    {
                        party.AddCharacter(list[k], remaining);
                        remaining = 0;
                    }
                }
            }
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
