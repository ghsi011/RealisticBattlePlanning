using System;
using System.IO;
using RealisticBattlePlanning.Planning;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    public class PlanSerializerTests
    {
        [Fact]
        public void EveryEnumValueRoundTrips()
        {
            var plan = TestPlans.EveryEnumValue();

            var json = PlanSerializer.Serialize(plan);
            Assert.True(PlanSerializer.TryDeserialize(json, out var reloaded, out var error), error);

            // Canonical-form round trip: serializing the reloaded plan must
            // reproduce the original JSON exactly.
            Assert.Equal(json, PlanSerializer.Serialize(reloaded));
        }

        [Fact]
        public void RoundTripPreservesStructure()
        {
            var json = PlanSerializer.Serialize(TestPlans.EveryEnumValue());
            Assert.True(PlanSerializer.TryDeserialize(json, out var plan, out _));

            Assert.Equal(4, plan.Formations.Count);
            Assert.Equal(3, plan.Anchors.Count);
            Assert.Equal(2, plan.PlayerSignals.Count);
            Assert.Equal(3, plan.Formations[0].Stages[1].When.Count);
            Assert.Equal(35f, plan.Formations[0].Abort.CasualtiesAbovePercent);
            Assert.True(plan.Formations[1].Stages[2].Do.MissileOnly);
        }

        [Fact]
        public void TypoedPropertyFailsWithReadableError()
        {
            var ok = PlanSerializer.TryDeserialize(
                "{ \"formations\": [ { \"formation\": \"Infantry\", \"stagess\": [] } ] }",
                out var plan, out var error);

            Assert.False(ok);
            Assert.Null(plan);
            Assert.Contains("stagess", error);
        }

        [Fact]
        public void UnknownEnumValueFailsWithReadableError()
        {
            var ok = PlanSerializer.TryDeserialize(
                "{ \"formations\": [ { \"formation\": \"Imaginary\", \"stages\": [] } ] }",
                out _, out var error);

            Assert.False(ok);
            Assert.Contains("Imaginary", error);
        }

        [Fact]
        public void EmptyInputFailsGracefully()
        {
            var ok = PlanSerializer.TryDeserialize("", out var plan, out var error);
            Assert.False(ok);
            Assert.Null(plan);
            Assert.NotNull(error);
        }

        [Fact]
        public void CommentsAreAllowed()
        {
            var ok = PlanSerializer.TryDeserialize(
                "{ // a comment\n \"playerSignals\": [\"x\"] }",
                out var plan, out var error);

            Assert.True(ok, error);
            Assert.Single(plan.PlayerSignals);
        }

        [Fact]
        public void ShippedSamplePlanParsesAndValidatesClean()
        {
            var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rbp_debug_plan.json"));

            Assert.True(PlanSerializer.TryDeserialize(json, out var plan, out var error), error);

            var result = PlanValidator.Validate(plan);
            Assert.Empty(result.Errors);
            Assert.Empty(result.Warnings);
        }
    }
}
