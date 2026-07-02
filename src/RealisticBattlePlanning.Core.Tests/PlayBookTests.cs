using System.Linq;
using RealisticBattlePlanning.Planning;
using RealisticBattlePlanning.Planning.Editing;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>The A6 feigned-retreat play: role assignment by mounted-ness and the canonical stage structure.</summary>
    public class PlayBookTests
    {
        [Fact]
        public void AssignsMountedToBaitAndFootToAnvilAndBuildsTheA6Structure()
        {
            var draft = new PlanDraft();
            var result = PlayBook.FeignedRetreat(draft, new[]
            {
                (PlannedFormationClass.Infantry, false),
                (PlannedFormationClass.Cavalry, true),
                (PlannedFormationClass.HorseArcher, true),
                (PlannedFormationClass.HeavyInfantry, false),
            });

            Assert.True(result.Ok);
            Assert.Equal(new[] { PlannedFormationClass.Cavalry, PlannedFormationClass.HorseArcher }, result.Bait);
            Assert.Equal(new[] { PlannedFormationClass.Infantry, PlannedFormationClass.HeavyInfantry }, result.Anvil);

            var plan = draft.Build();
            Assert.Contains(plan.Anchors, a => a.Id == PlayBook.TrapAnchorId && a.Basis == AnchorBasis.TeamCenter);
            Assert.Contains(PlayBook.SpringTrapSignal, plan.PlayerSignals);

            // Bait: skirmish -> feign to the trap on EnemyCommits -> flank out on the signal.
            var bait = plan.Formations.First(f => f.Formation == PlannedFormationClass.Cavalry);
            Assert.Equal(3, bait.Stages.Count);
            Assert.Equal(DirectiveType.Skirmish, bait.Stages[0].Do.Type);
            Assert.Equal(TriggerType.EnemyCommits, bait.Stages[1].When.Single().Type);
            Assert.Equal(PlayBook.TrapAnchorId, bait.Stages[1].Do.Anchor);
            Assert.Equal(TriggerType.SignalReceived, bait.Stages[2].When.Single().Type);
            Assert.Equal(DirectiveType.FlankArc, bait.Stages[2].Do.Type);
            // Flanks alternate so the baits wheel out to opposite sides.
            var secondBait = plan.Formations.First(f => f.Formation == PlannedFormationClass.HorseArcher);
            Assert.NotEqual(bait.Stages[2].Do.Side, secondBait.Stages[2].Do.Side);

            // Anvil: shieldwall -> charge when pursuers reach the trap; exactly ONE broadcaster.
            var anvils = plan.Formations.Where(f => result.Anvil.Contains(f.Formation)).ToList();
            foreach (var anvil in anvils)
            {
                Assert.Equal(DirectiveType.Hold, anvil.Stages[0].Do.Type);
                var spring = anvil.Stages[1];
                Assert.Equal(TriggerType.EnemyWithinDistance, spring.When.Single().Type);
                Assert.Equal(PlayBook.TrapAnchorId, spring.When.Single().Anchor);
                Assert.Equal(DirectiveType.Charge, spring.Do.Type);
            }
            Assert.Equal(1, anvils.Count(a => a.Stages[1].Emit.Contains(PlayBook.SpringTrapSignal)));

            // Valid by construction (A3.9).
            Assert.True(PlanValidator.Validate(plan).IsValid);
        }

        [Fact]
        public void RefusesWithoutBothRoles()
        {
            var draft = new PlanDraft();
            var allFoot = PlayBook.FeignedRetreat(draft, new[] { (PlannedFormationClass.Infantry, false) });
            Assert.False(allFoot.Ok);
            Assert.Contains("mounted", allFoot.Problem);
            Assert.Empty(draft.Build().Formations); // nothing half-stamped

            var allMounted = PlayBook.FeignedRetreat(draft, new[] { (PlannedFormationClass.Cavalry, true) });
            Assert.False(allMounted.Ok);
        }
    }
}
