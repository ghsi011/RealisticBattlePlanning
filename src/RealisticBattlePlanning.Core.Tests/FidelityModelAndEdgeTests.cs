using System;
using System.Collections.Generic;
using System.Linq;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Fidelity;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// Direct model contracts + the two P2/P3-review edge fixes: a stale drift
    /// must not leak into a steering skip-forward, and a fired trigger whose
    /// stages all skip to hold must not emit a phantom reaction delay.
    /// </summary>
    public class FidelityModelAndEdgeTests
    {
        [Fact]
        public void CompetenceModelRollsTheCommandersOwnTier()
        {
            var profile = new CompetenceFidelityModel().Roll(new CommanderProfile(FidelityTier.Veteran), new Random(1));
            Assert.Equal(FidelityTier.Veteran, profile.Tier);
        }

        [Fact]
        public void FixedTierModelIgnoresTheCommander()
        {
            var profile = new FixedTierFidelityModel(FidelityTier.Master).Roll(new CommanderProfile(FidelityTier.Untrained), new Random(1));
            Assert.Equal(FidelityTier.Master, profile.Tier);
        }

        [Fact]
        public void NullCommanderRollsAsDefault()
        {
            var profile = new CompetenceFidelityModel().Roll(null, new Random(1));
            Assert.Equal(CommanderProfile.Default.Competence, profile.Tier);
        }

        [Fact]
        public void NoPhantomReactionWhenTheTriggeredStagesAllSkipToHold()
        {
            // Stage 2 moves to an unresolvable anchor -> inevaluable, no later
            // stage -> the formation holds. No reaction delay should be rolled
            // or recorded for an activation that never happens.
            var plan = new BattlePlan
            {
                Formations =
                {
                    new FormationPlan
                    {
                        Formation = PlannedFormationClass.Infantry,
                        Stages =
                        {
                            new Stage { Do = new DirectiveSpec { Type = DirectiveType.Hold } },
                            new Stage { When = { Timer(3f) }, Do = new DirectiveSpec { Type = DirectiveType.MoveTo, Anchor = "ghost" } },
                        },
                    },
                },
            };
            var monitor = new PlanMonitor(plan, new FixedTierFidelityModel(FidelityTier.Untrained), seed: 1);

            monitor.Tick(Field(0f));
            var events = monitor.Tick(Field(3.1f));

            Assert.Empty(events.OfType<ReactionDelayed>());
            Assert.Single(events.OfType<PlanHolding>());
        }

        [Fact]
        public void SteeringSkipActivatesTheNextStageWithoutStaleDrift()
        {
            // Skirmish (steering, rolls Untrained fidelity) -> the enemy
            // vanishes -> the plan skips to a MoveTo, which must land exactly
            // on its anchor (clean activation), not drift by the skirmish roll.
            var plan = new BattlePlan
            {
                Anchors = { new MapAnchor { Id = "rally", Basis = AnchorBasis.OwnStart, Forward = 50f } },
                Formations =
                {
                    new FormationPlan
                    {
                        Formation = PlannedFormationClass.Infantry,
                        Stages =
                        {
                            new Stage { Do = new DirectiveSpec { Type = DirectiveType.Hold } },
                            new Stage { When = { Timer(1f) }, Do = new DirectiveSpec { Type = DirectiveType.Skirmish, Target = "Nearest" } },
                            new Stage { When = { Timer(999f) }, Do = new DirectiveSpec { Type = DirectiveType.MoveTo, Anchor = "rally" } },
                        },
                    },
                },
            };
            var monitor = new PlanMonitor(plan, new FixedTierFidelityModel(FidelityTier.Untrained), seed: 4);

            // Advance (enemy present) until the skirmish stage actually activates,
            // past its reaction delay.
            StageActivated skirmish = null;
            for (var t = 0f; t <= 30f && skirmish == null; t += 0.5f)
                skirmish = monitor.Tick(WithEnemy(t)).OfType<StageActivated>().FirstOrDefault(e => e.Directive.Spec.Type == DirectiveType.Skirmish);
            Assert.NotNull(skirmish);

            // Enemy gone: the steering reference vanishes, skip to MoveTo.
            StageActivated move = null;
            for (var t = 31f; t <= 36f && move == null; t += 0.5f)
                move = monitor.Tick(Field(t)).OfType<StageActivated>().FirstOrDefault(e => e.Directive.Spec.Type == DirectiveType.MoveTo);
            Assert.NotNull(move);

            // No stale drift: the resolved target is exactly the anchor.
            Assert.Equal(new MapVec(0f, 50f), move.Directive.FirstMoveTarget.Value);
        }

        [Fact]
        public void PendingReactionSkipForwardDropsTheStaleDrift()
        {
            // Sibling of the steering-skip case above, on the pending-reaction
            // path: a triggered Skirmish parks for its reaction delay, and the
            // enemy vanishes *during* that window. When the delay elapses the
            // stage is no longer evaluable, so the plan skips to the MoveTo —
            // which must land exactly on its anchor, never offset by the
            // skirmish's rolled drift.
            var plan = new BattlePlan
            {
                Anchors = { new MapAnchor { Id = "rally", Basis = AnchorBasis.OwnStart, Forward = 30f } },
                Formations =
                {
                    new FormationPlan
                    {
                        Formation = PlannedFormationClass.Infantry,
                        Stages =
                        {
                            new Stage { Do = new DirectiveSpec { Type = DirectiveType.Hold } },
                            new Stage { When = { Timer(1f) }, Do = new DirectiveSpec { Type = DirectiveType.Skirmish, Target = "Nearest" } },
                            new Stage { When = { Timer(999f) }, Do = new DirectiveSpec { Type = DirectiveType.MoveTo, Anchor = "rally" } },
                        },
                    },
                },
            };
            var monitor = new PlanMonitor(plan, new FixedTierFidelityModel(FidelityTier.Untrained), seed: 3);

            // t=0 opening hold; t=1.1 the timer fires and the skirmish parks
            // pending (Untrained delay 6-10s) with the enemy still present.
            monitor.Tick(WithEnemy(0f));
            var parked = monitor.Tick(WithEnemy(1.1f));
            Assert.Single(parked.OfType<ReactionDelayed>());
            Assert.DoesNotContain(parked.OfType<StageActivated>(), e => e.Directive.Spec.Type == DirectiveType.Skirmish);

            // Enemy gone from here: past the delay the pending stage finds
            // itself inevaluable and skips forward to the MoveTo.
            StageActivated move = null;
            for (var t = 1.6f; t <= 15f && move == null; t += 0.5f)
                move = monitor.Tick(Field(t)).OfType<StageActivated>().FirstOrDefault(e => e.Directive.Spec.Type == DirectiveType.MoveTo);
            Assert.NotNull(move);

            // No stale drift across the pending skip: exactly on the anchor.
            Assert.Equal(new MapVec(0f, 30f), move.Directive.FirstMoveTarget.Value);
        }

        private static TriggerSpec Timer(float s) => new() { Type = TriggerType.TimerElapsed, Seconds = s };

        private static FakeBattlefield Field(float time)
            => new FakeBattlefield(time).WithOwn(PlannedFormationClass.Infantry, 0f, 0f);

        private static FakeBattlefield WithEnemy(float time)
            => new FakeBattlefield(time).WithOwn(PlannedFormationClass.Infantry, 0f, 0f).WithEnemy(1, 0f, 80f);
    }
}
