using RealisticBattlePlanning.Planning;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// The session plan store (Area G): empty on a fresh session, carries the applied plan to
    /// the next battle of the SAME game, deep-copies so the live plan and the store never alias,
    /// and is keyed so a plan never leaks into a different game.
    /// </summary>
    public class SessionPlanStoreTests
    {
        private const string GameA = "campaign:AAA";
        private const string GameB = "campaign:BBB";

        private static BattlePlan InfantryPlan()
        {
            var plan = new BattlePlan();
            plan.Formations.Add(new FormationPlan
            {
                Formation = PlannedFormationClass.Infantry,
                Stages = { new Stage { Do = new DirectiveSpec { Type = DirectiveType.Hold } } },
            });
            return plan;
        }

        [Fact]
        public void EmptyByDefaultCarriesAndIsolates()
        {
            SessionPlanStore.Clear();
            Assert.Null(SessionPlanStore.CurrentFor(GameA));   // first battle of a session starts blank
            Assert.False(SessionPlanStore.HasPlanFor(GameA));

            var plan = InfantryPlan();
            SessionPlanStore.Set(GameA, plan);

            Assert.True(SessionPlanStore.HasPlanFor(GameA));
            var carried = SessionPlanStore.CurrentFor(GameA);
            Assert.Equal(PlannedFormationClass.Infantry, Assert.Single(carried.Formations).Formation);

            // Deep-copy isolation: mutating the source OR the retrieved copy must not change the store.
            plan.Formations.Add(new FormationPlan { Formation = PlannedFormationClass.Cavalry });
            carried.Formations.Clear();
            Assert.Equal(PlannedFormationClass.Infantry, Assert.Single(SessionPlanStore.CurrentFor(GameA).Formations).Formation);

            SessionPlanStore.Clear();
            Assert.Null(SessionPlanStore.CurrentFor(GameA));
            Assert.False(SessionPlanStore.HasPlanFor(GameA));
        }

        [Fact]
        public void PlanDoesNotLeakIntoADifferentGame()
        {
            // The HIGH cross-session leak: a plan applied in one game must not surface in
            // the first plannable battle of another (e.g. a campaign plan in a later custom
            // battle). The store is keyed, so a different key reads as blank.
            SessionPlanStore.Clear();
            SessionPlanStore.Set(GameA, InfantryPlan());

            Assert.True(SessionPlanStore.HasPlanFor(GameA));
            Assert.Null(SessionPlanStore.CurrentFor(GameB));   // a different game starts blank
            Assert.False(SessionPlanStore.HasPlanFor(GameB));

            // Same game still carries.
            Assert.NotNull(SessionPlanStore.CurrentFor(GameA));

            SessionPlanStore.Clear();
        }

        [Fact]
        public void ApplyingUnderANewKeySupersedesThePriorPlan()
        {
            SessionPlanStore.Clear();
            SessionPlanStore.Set(GameA, InfantryPlan());
            SessionPlanStore.Set(GameB, InfantryPlan());

            // GameB now owns the carry; GameA's is gone (plans never coexist across games).
            Assert.True(SessionPlanStore.HasPlanFor(GameB));
            Assert.Null(SessionPlanStore.CurrentFor(GameA));

            SessionPlanStore.Clear();
        }
    }
}
