using System;
using RealisticBattlePlanning.Planning;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    public class PlanFormatterTests
    {
        [Fact]
        public void SecondaryParametersAreRendered()
        {
            // Params that were previously dropped from the description.
            Assert.Equal("Enemy commits to attack on Cavalry (within 60m)",
                PlanFormatter.DescribeTrigger(new TriggerSpec { Type = TriggerType.EnemyCommits, Formation = "Cavalry", Meters = 60f }));

            // A fire policy attached to a non-FireControl directive.
            Assert.Contains(", free fire",
                PlanFormatter.DescribeDirective(new DirectiveSpec { Type = DirectiveType.Hold, Fire = FireMode.Free }));

            // Follow offset.
            Assert.Contains("offset 10m fwd, 5m right",
                PlanFormatter.DescribeDirective(new DirectiveSpec { Type = DirectiveType.Follow, Target = "Player", OffsetForwardMeters = 10f, OffsetRightMeters = 5f }));
        }

        [Fact]
        public void SimplePlanGoldenOutput()
        {
            var expected = string.Join("\n",
                "Plan: 2 formation(s), 1 anchor(s), player signals: [hammer]",
                "  anchor 'advance-50': OwnStart forward 50m, right 0m",
                "  [Infantry] abort: casualties > 50%, commander down, broken",
                "    1. \"hold the line\" On battle start -> Hold position (ShieldWall)",
                "    2. \"advance\" After 30s -> Move to 'advance-50', walk, emits [advancing]",
                "  [Ranged] abort: casualties > 60%, commander down, broken",
                "    1. On battle start -> Hold position (Loose)",
                "    2. \"follow the advance\" Signal 'advancing' -> Move to 'advance-50'");

            var actual = PlanFormatter.Describe(TestPlans.SimpleValid()).Replace("\r\n", "\n");

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void EveryDirectiveDescribesWithoutThrowing()
        {
            var text = PlanFormatter.Describe(TestPlans.EveryEnumValue());

            // Spot-check each directive family is represented in plain language.
            Assert.Contains("Skirmish", text);
            Assert.Contains("Feign retreat toward 'own', firing", text);
            Assert.Contains("Flank arc Left", text);
            Assert.Contains("missile-only", text);
            Assert.Contains("Charge", text);
            Assert.Contains("Pull back to 'scene', facing the enemy", text);
            Assert.Contains("Screen Infantry at 30m", text);
            Assert.Contains("Follow Infantry", text);
            Assert.Contains("Hold fire", text);
            Assert.Contains("Move along [own -> team]", text);
            Assert.Contains("Player signal 'go-1'", text);
            Assert.Contains("Enemy (Nearest) broken", text);
        }
    }
}
