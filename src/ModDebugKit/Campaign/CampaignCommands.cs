using System;
using System.IO;
using System.Linq;
using ModDebugKit.Commands;
using ModDebugKit.Diagnostics;
using ModDebugKit.Io;
using SandBox;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using Path = System.IO.Path;

// Namespace deliberately NOT ModDebugKit.Campaign — that would shadow the
// engine's Campaign type and break `Campaign.Current`.
namespace ModDebugKit.CampaignControl
{
    /// <summary>
    /// Campaign control (M4): drive the campaign from the file channel instead
    /// of the mouse — load a save, read player/party state, and adjust gold,
    /// roster, and time. All changes are in-memory for the session; nothing
    /// here ever writes a save file, so the player's saves are safe.
    /// </summary>
    public static class CampaignCommands
    {
        public static void RegisterAll(CommandDispatcher dispatcher)
        {
            dispatcher.Register("dbg.camp.saves", "dbg.camp.saves - list save names (newest first)", Saves);
            dispatcher.Register("dbg.camp.load", "dbg.camp.load [save] - load a campaign save (no arg = newest, i.e. Continue)", Load);
            dispatcher.Register("dbg.camp.status", "dbg.camp.status [path] - write campaign state to campaign_state.json", Status);
            dispatcher.Register("dbg.camp.gold", "dbg.camp.gold <n|+n|-n> - set or adjust the player's gold", Gold);
            dispatcher.Register("dbg.camp.party", "dbg.camp.party <troopId> <count> - add (negative removes) troops in the main party", Party);
            dispatcher.Register("dbg.camp.time", "dbg.camp.time <stop|play|fast> - set the campaign time control mode", Time);
        }

        private static DbgOutcome Saves(DbgCommand command)
        {
            var names = MBSaveLoad.GetSaveFiles().Select(f => f.Name).ToList();
            return DbgOutcome.Success($"{names.Count} save(s): {string.Join(", ", names.Take(10))}", new { saves = names });
        }

        private static DbgOutcome Load(DbgCommand command)
        {
            if (Mission.Current != null)
                return DbgOutcome.Failure("in a mission; leave it first");

            var saves = MBSaveLoad.GetSaveFiles();
            if (saves.Length == 0)
                return DbgOutcome.Failure("no save files found");

            var name = command.Arg(0) ?? saves[0].Name; // GetSaveFiles is newest-first
            var info = saves.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (info == null)
                return DbgOutcome.Failure($"save '{name}' not found (try dbg.camp.saves)");

            try
            {
                var loadResult = MBSaveLoad.LoadSaveGameData(info.Name);
                if (loadResult == null)
                    return DbgOutcome.Failure($"could not load '{info.Name}'");
                MBGameManager.StartNewGame(new SandBoxGameManager(loadResult));
                return DbgOutcome.Success($"loading campaign save '{info.Name}'", new { save = info.Name });
            }
            catch (Exception e)
            {
                DbgLog.Error("dbg.camp.load failed.", e);
                return DbgOutcome.Failure($"load failed: {e.Message}");
            }
        }

        private static DbgOutcome Status(DbgCommand command)
        {
            if (Campaign.Current == null)
                return DbgOutcome.Failure("no active campaign");

            var hero = Hero.MainHero;
            var party = MobileParty.MainParty;
            var members = party?.MemberRoster?.GetTroopRoster()
                .Select(e => new { troop = e.Character?.StringId, name = e.Character?.Name?.ToString(), count = e.Number })
                .ToList();

            var state = new
            {
                capturedAtUtc = DateTime.UtcNow.ToString("o"),
                day = (int)CampaignTime.Now.ToDays,
                gold = hero?.Gold ?? 0,
                hero = hero?.Name?.ToString(),
                position = party != null ? new { x = party.GetPosition2D.x, y = party.GetPosition2D.y } : null,
                currentSettlement = party?.CurrentSettlement?.Name?.ToString(),
                partySize = party?.MemberRoster?.TotalManCount ?? 0,
                members,
            };

            var target = command.Arg(0) != null
                ? ModDebugKitRuntime.Paths.Resolve(command.Arg(0))
                : Path.Combine(ModDebugKitRuntime.Paths.Root, "campaign_state.json");
            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(target, DbgJson.Pretty(state));

            return DbgOutcome.Success($"campaign: day {state.day}, {state.gold} gold, party {state.partySize} -> {target}",
                new { path = target, state.day, state.gold, state.partySize });
        }

        private static DbgOutcome Gold(DbgCommand command)
        {
            if (Campaign.Current == null || Hero.MainHero == null)
                return DbgOutcome.Failure("no active campaign");
            var arg = command.Arg(0);
            if (string.IsNullOrWhiteSpace(arg) || !int.TryParse(arg, out var value))
                return DbgOutcome.Failure("usage: dbg.camp.gold <n|+n|-n>");

            var relative = arg[0] == '+' || arg[0] == '-';
            var current = Hero.MainHero.Gold;
            var delta = relative ? value : value - current;
            Hero.MainHero.ChangeHeroGold(delta);
            return DbgOutcome.Success($"gold {current} -> {Hero.MainHero.Gold}", new { from = current, to = Hero.MainHero.Gold });
        }

        private static DbgOutcome Party(DbgCommand command)
        {
            if (Campaign.Current == null || MobileParty.MainParty == null)
                return DbgOutcome.Failure("no active campaign");
            if (command.Args.Count < 2 || !int.TryParse(command.Arg(1), out var count) || count == 0)
                return DbgOutcome.Failure("usage: dbg.camp.party <troopId> <count!=0>");

            var troop = MBObjectManager.Instance.GetObject<CharacterObject>(command.Arg(0));
            if (troop == null)
                return DbgOutcome.Failure($"troop '{command.Arg(0)}' not found");

            MobileParty.MainParty.MemberRoster.AddToCounts(troop, count);
            return DbgOutcome.Success(
                $"{(count > 0 ? "added" : "removed")} {Math.Abs(count)} {troop.StringId}; party now {MobileParty.MainParty.MemberRoster.TotalManCount}",
                new { troop = troop.StringId, count, partySize = MobileParty.MainParty.MemberRoster.TotalManCount });
        }

        private static DbgOutcome Time(DbgCommand command)
        {
            if (Campaign.Current == null)
                return DbgOutcome.Failure("no active campaign");
            // The engine's TimeControlMode setter silently no-ops while locked (camera move, popups,
            // menus), so reporting success would be misleading.
            if (Campaign.Current.TimeControlModeLock)
                return DbgOutcome.Failure("campaign time control is locked (popup/camera/menu active); cannot change mode now");
            switch (command.Arg(0)?.Trim().ToLowerInvariant())
            {
                case "stop":
                    Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
                    return DbgOutcome.Success("campaign time: stopped");
                case "play":
                    Campaign.Current.TimeControlMode = CampaignTimeControlMode.StoppablePlay;
                    return DbgOutcome.Success("campaign time: play");
                case "fast":
                    Campaign.Current.TimeControlMode = CampaignTimeControlMode.StoppableFastForward;
                    return DbgOutcome.Success("campaign time: fast-forward");
                default:
                    return DbgOutcome.Failure("usage: dbg.camp.time <stop|play|fast>");
            }
        }
    }
}
