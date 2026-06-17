using RealisticBattlePlanning.Harness;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    public class AfterActionReportTests
    {
        [Fact]
        public void SummarizesPerFormationTimelineAndDeviations()
        {
            var record = new BattleRecord
            {
                Scenario = "a6_feigned_retreat",
                DurationSeconds = 95f,
                Result = "Victory",
                Events =
                {
                    new RecordedEvent { TimeSeconds = 0f, Formation = PlannedFormationClass.HorseArcher, Kind = RecordedEventKind.StageActivated, Stage = 1, Name = "skirmish" },
                    new RecordedEvent { TimeSeconds = 12f, Formation = PlannedFormationClass.HorseArcher, Kind = RecordedEventKind.ReactionDelayed, Stage = 2, DelaySeconds = 3.5f, Name = "Untrained" },
                    new RecordedEvent { TimeSeconds = 15f, Formation = PlannedFormationClass.HorseArcher, Kind = RecordedEventKind.StageActivated, Stage = 2, Name = "withdraw" },
                    new RecordedEvent { TimeSeconds = 40f, Formation = PlannedFormationClass.Infantry, Kind = RecordedEventKind.PlanAborted, Name = "casualties 65%" },
                },
            };

            var report = AfterActionReport.Build(record);

            Assert.Equal(2, report.Formations.Count);
            // Ordered by formation slot: Infantry (slot 1) before HorseArcher (slot 4).
            Assert.Equal(PlannedFormationClass.Infantry, report.Formations[0].Formation);
            Assert.Equal(PlannedFormationClass.HorseArcher, report.Formations[1].Formation);
            Assert.Equal(1, report.Deviations.ReactionDelays);
            Assert.Equal(3.5f, report.Deviations.TotalReactionDelaySeconds, 3);
            Assert.Equal(1, report.Deviations.Aborts);

            var text = report.Describe();
            Assert.Contains("After-Action Report — a6_feigned_retreat (95s, Victory)", text);
            Assert.Contains("began stage 1 \"skirmish\"", text);
            Assert.Contains("delayed 3.5s (commander reaction, Untrained)", text);
            Assert.Contains("ABORTED — casualties 65%", text);
            Assert.Contains("1 reaction delay(s) totalling 3.5s", text);
            Assert.Contains("1 abort(s)", text);
        }

        [Fact]
        public void CleanRunReportsNoDeviations()
        {
            var record = new BattleRecord { Scenario = "drill", DurationSeconds = 10f, Result = "Victory" };
            record.Events.Add(new RecordedEvent { TimeSeconds = 0f, Formation = PlannedFormationClass.Infantry, Kind = RecordedEventKind.StageActivated, Stage = 1, Name = "hold" });

            var report = AfterActionReport.Build(record);

            Assert.False(report.Deviations.Any);
            Assert.Contains("Deviations: none — the plan ran clean.", report.Describe());
        }
    }
}
