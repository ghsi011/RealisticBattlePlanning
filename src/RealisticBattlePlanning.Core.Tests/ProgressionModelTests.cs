using RealisticBattlePlanning.Fidelity;
using RealisticBattlePlanning.Progression;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// P5: Plan Familiarity XP, tier-up, and death loss (D4). Pacing is tuned
    /// to the spec targets (a fresh companion reaches Drilled in a handful of
    /// battles, Master is campaign-long); death removes the record entirely.
    /// </summary>
    public class ProgressionModelTests
    {
        [Fact]
        public void CompletedStagesRaiseFamiliarityAndCount()
        {
            var record = new CommanderRecord();
            ProgressionModel.OnStageCompleted(record);
            ProgressionModel.OnStageCompleted(record);

            Assert.Equal(2, record.StagesExecuted);
            Assert.Equal(2 * ProgressionModel.XpPerCompletedStage, record.PlanFamiliarityXp);
        }

        [Fact]
        public void FailedStagesGrantOnlyTheLessonLearnedTrickle()
        {
            var record = new CommanderRecord();
            ProgressionModel.OnStageFailed(record);

            Assert.Equal(1, record.StagesAbortedOrFailed);
            Assert.Equal(ProgressionModel.XpPerFailedStage, record.PlanFamiliarityXp);
            Assert.True(ProgressionModel.XpPerFailedStage < ProgressionModel.XpPerCompletedStage);
        }

        [Fact]
        public void FamiliarityCapsAtTheScaleMaximum()
        {
            var record = new CommanderRecord();
            for (var i = 0; i < 1000; i++)
                ProgressionModel.OnStageCompleted(record);

            Assert.Equal(ProgressionModel.MaxFamiliarityXp, record.PlanFamiliarityXp);
        }

        [Fact]
        public void DrillsAccrueFasterButThatIsTheCallersCapToEnforce()
        {
            var battle = new CommanderRecord();
            var drill = new CommanderRecord();
            ProgressionModel.OnStageCompleted(battle, inDrill: false);
            ProgressionModel.OnStageCompleted(drill, inDrill: true);

            Assert.Equal(ProgressionModel.DrillXpMultiplier, drill.PlanFamiliarityXp / battle.PlanFamiliarityXp);
        }

        [Fact]
        public void AFreshCompanionReachesDrilledInAHandfulOfBattles()
        {
            // Mediocre Tactics/Leadership 30 -> competence base 30 (Untrained).
            // Drilled is 60, so +30 familiarity is needed. At ~4 completed
            // stages/battle, that is ~3 battles (D4 pacing target: 3-5).
            var record = new CommanderRecord();
            Assert.Equal(FidelityTier.Untrained, ProgressionModel.TierFor(record, 30, 30));

            const int stagesPerBattle = 4;
            var battles = 0;
            while (ProgressionModel.TierFor(record, 30, 30) == FidelityTier.Untrained)
            {
                for (var s = 0; s < stagesPerBattle; s++)
                    ProgressionModel.OnStageCompleted(record);
                ProgressionModel.OnBattleUnderCommand(record);
                battles++;
                Assert.True(battles <= 8, "should not take more than a handful of battles to reach Drilled");
            }

            Assert.InRange(battles, 2, 6);
            Assert.Equal(FidelityTier.Drilled, ProgressionModel.TierFor(record, 30, 30));
        }

        [Fact]
        public void MasterIsCampaignLongForAModestCompanion()
        {
            // Tactics/Leadership 30 (base 30): Master is 240, so +210
            // familiarity — dozens of battles, not a week-six default (D4).
            var record = new CommanderRecord();
            var stages = 0;
            while (ProgressionModel.TierFor(record, 30, 30) != FidelityTier.Master && stages < 1000)
            {
                ProgressionModel.OnStageCompleted(record);
                stages++;
            }

            Assert.Equal(FidelityTier.Master, ProgressionModel.TierFor(record, 30, 30));
            // ~84 completed stages => ~20+ battles at 4 stages each.
            Assert.True(stages >= 60, $"Master should be a long grind, took {stages} stages");
        }

        [Fact]
        public void ARenownedLordIsCompetentImmediately()
        {
            // Zero familiarity, high vanilla stats -> already Veteran+ (D1).
            var record = new CommanderRecord();
            Assert.True(ProgressionModel.TierFor(record, 230, 190) >= FidelityTier.Veteran);
        }

        [Fact]
        public void DeathLosesEverything()
        {
            var book = new CommanderRecordBook();
            var record = book.GetOrCreate("hero_1");
            ProgressionModel.OnStageCompleted(record);
            Assert.True(book.TryGet("hero_1", out _));

            book.Forget("hero_1");

            Assert.False(book.TryGet("hero_1", out _));
            Assert.Equal(0f, book.GetOrCreate("hero_1").PlanFamiliarityXp); // a new captain starts green
        }

        [Fact]
        public void LeavingAndReturningKeepsTheRecord()
        {
            // Not death, just absence: the record persists in the book.
            var book = new CommanderRecordBook();
            ProgressionModel.OnStageCompleted(book.GetOrCreate("hero_2"));
            var xp = book.GetOrCreate("hero_2").PlanFamiliarityXp;

            // Re-fetched later (e.g. rejoins the clan): same data.
            Assert.Equal(xp, book.GetOrCreate("hero_2").PlanFamiliarityXp);
            Assert.Equal(1, book.Count);
        }
    }
}
