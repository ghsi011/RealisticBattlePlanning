using RealisticBattlePlanning.Planning;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// The session plan store (Area G): empty on a fresh session, carries the applied plan to
    /// the next battle, and deep-copies so the live plan and the store never alias.
    /// </summary>
    public class SessionPlanStoreTests
    {
        [Fact]
        public void EmptyByDefaultCarriesAndIsolates()
        {
            SessionPlanStore.Clear();
            Assert.Null(SessionPlanStore.Current);   // first battle of a session starts blank
            Assert.False(SessionPlanStore.HasPlan);

            var plan = new BattlePlan();
            plan.Formations.Add(new FormationPlan
            {
                Formation = PlannedFormationClass.Infantry,
                Stages = { new Stage { Do = new DirectiveSpec { Type = DirectiveType.Hold } } },
            });
            SessionPlanStore.Current = plan;

            Assert.True(SessionPlanStore.HasPlan);
            var carried = SessionPlanStore.Current;
            Assert.Equal(PlannedFormationClass.Infantry, Assert.Single(carried.Formations).Formation);

            // Deep-copy isolation: mutating the source OR the retrieved copy must not change the store.
            plan.Formations.Add(new FormationPlan { Formation = PlannedFormationClass.Cavalry });
            carried.Formations.Clear();
            Assert.Equal(PlannedFormationClass.Infantry, Assert.Single(SessionPlanStore.Current.Formations).Formation);

            SessionPlanStore.Clear();
            Assert.Null(SessionPlanStore.Current);
            Assert.False(SessionPlanStore.HasPlan);
        }
    }
}
