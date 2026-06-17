using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using RealisticBattlePlanning.Diagnostics;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Planning.Model;
using RealisticBattlePlanning.Progression;

namespace RealisticBattlePlanning.Execution
{
    /// <summary>
    /// Dev-console window into the D4 commander-progression system (spec D4/G1),
    /// the half of the mod that only exists in a campaign and so can't be reached
    /// from the Custom-Battle harness. Lets a campaign session inspect each
    /// commander's earned familiarity and competence tier, simulate completed
    /// stages against a real hero to watch the tier climb, and forget a commander
    /// (the death-loss path) — exercising the exact <see cref="ProgressionService"/>
    /// calls the mission layer makes, so the otherwise battle-only loop is testable
    /// by loading a save and typing a command.
    /// </summary>
    public static class ProgressionCommands
    {
        private static readonly PlannedFormationClass Slot = PlannedFormationClass.Infantry;

        [CommandLineFunctionality.CommandLineArgumentFunction("progression", "rbp")]
        public static string Progression(List<string> args)
        {
            try
            {
                if (Campaign.Current == null)
                    return "not in a campaign — D4 progression is campaign-only (load a save first).";
                var behavior = CommanderProgressionBehavior.Current;
                if (behavior == null)
                    return "progression behavior is not registered for this campaign.";
                var svc = behavior.Service;

                var sub = args != null && args.Count > 0 ? args[0].ToLowerInvariant() : "status";
                switch (sub)
                {
                    case "status": return Status(svc);
                    case "award": return Award(svc, args);
                    case "forget": return Forget(svc, args);
                    default:
                        return "usage: rbp.progression [status | award <stages> [hero] | forget [hero]]";
                }
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] rbp.progression failed.", e);
                return $"progression command failed: {e.Message}";
            }
        }

        private static string Status(ProgressionService svc)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Commander records in the book: {svc.Book.Count}.");
            foreach (var hero in RelevantHeroes())
            {
                var (tac, led) = Stats(hero);
                var profile = svc.ProfileFor(new CommanderKey(hero.StringId), tac, led);
                var has = svc.Book.TryGet(hero.StringId, out var rec);
                var fam = has ? $", planXP {N1(rec.PlanFamiliarityXp)}, drillXP {N1(rec.DrillFamiliarityXp)}, battles {rec.BattlesUnderCommand}, stages {rec.StagesExecuted}/{rec.StagesAbortedOrFailed}" : ", no record yet";
                sb.AppendLine($"  {hero.Name}: Tac {tac}, Led {led} -> {profile.Competence} ({N1(profile.CompetenceScore)}){fam}");
            }
            return sb.ToString().TrimEnd();
        }

        private static string Award(ProgressionService svc, List<string> args)
        {
            if (args.Count < 2 || !int.TryParse(args[1], out var count) || count <= 0)
                return "usage: rbp.progression award <stages> [hero]   (stages > 0)";

            var hero = ResolveHero(args, 2, out var note);
            if (hero == null)
                return note;

            var key = new CommanderKey(hero.StringId);
            var (tac, led) = Stats(hero);
            var before = svc.ProfileFor(key, tac, led);

            // Exactly the mission-layer call: feed StageCompleted events for this
            // commander's slot, so the path under test is the real one (D4).
            var map = new Dictionary<PlannedFormationClass, CommanderKey> { [Slot] = key };
            var events = Enumerable.Range(0, count).Select(_ => (PlanEvent)new StageCompleted(Slot, 0, null)).ToList();
            svc.OnBattleEvents(events, map);

            var after = svc.ProfileFor(key, tac, led);
            svc.Book.TryGet(hero.StringId, out var rec);
            return $"{hero.Name}: +{count} completed stages -> {before.Competence} ({N1(before.CompetenceScore)}) => " +
                   $"{after.Competence} ({N1(after.CompetenceScore)}); planXP now {N1(rec?.PlanFamiliarityXp ?? 0f)}. " +
                   "Save and reload to confirm it persists (G1).";
        }

        private static string Forget(ProgressionService svc, List<string> args)
        {
            var hero = ResolveHero(args, 1, out var note);
            if (hero == null)
                return note;
            var had = svc.Book.TryGet(hero.StringId, out _);
            svc.OnCommanderLost(new CommanderKey(hero.StringId));
            return had
                ? $"{hero.Name}: record forgotten (death-loss path). A replacement captain would start green."
                : $"{hero.Name}: had no record to forget.";
        }

        /// <summary>The player and clan heroes — the commanders the player actually fields.</summary>
        private static IEnumerable<Hero> RelevantHeroes()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (Hero.MainHero is { } main && seen.Add(main.StringId))
                yield return main;
            var clan = Clan.PlayerClan;
            if (clan != null)
            {
                foreach (var hero in clan.Heroes)
                {
                    if (hero != null && hero.IsAlive && seen.Add(hero.StringId))
                        yield return hero;
                }
            }
        }

        private static Hero ResolveHero(List<string> args, int nameStartIndex, out string note)
        {
            note = null;
            if (args.Count <= nameStartIndex)
            {
                if (Hero.MainHero != null)
                    return Hero.MainHero;
                note = "no main hero in this campaign.";
                return null;
            }

            var query = string.Join(" ", args.Skip(nameStartIndex));
            var match = RelevantHeroes().FirstOrDefault(h =>
                h.Name?.ToString().IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            if (match == null)
                note = $"no clan hero matching '{query}' (try a name from rbp.progression status).";
            return match;
        }

        private static (int Tactics, int Leadership) Stats(Hero hero)
            => (hero.GetSkillValue(DefaultSkills.Tactics), hero.GetSkillValue(DefaultSkills.Leadership));

        private static string N1(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
