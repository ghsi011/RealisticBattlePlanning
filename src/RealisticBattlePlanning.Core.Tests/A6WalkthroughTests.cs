using System.Collections.Generic;
using System.Linq;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Planning;
using RealisticBattlePlanning.Planning.Editing;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// The spec's canonical worked example (A6), as a scripted timeline:
    /// two horse-archer formations (HorseArcher + LightCavalry slots) skirmish,
    /// feign-retreat behind the infantry on enemy commitment, and wheel into
    /// mirrored missile-only flank arcs on "spring-trap" — which the two
    /// infantry formations (Infantry + HeavyInfantry slots) emit when they
    /// charge as the enemy nears the retreat anchor.
    /// </summary>
    public class A6WalkthroughTests
    {
        private const string Trap = "trap";
        private const string SpringTrap = "spring-trap";

        [Fact]
        public void ThePlanValidatesClean()
        {
            var validation = PlanValidator.Validate(BuildPlan());
            Assert.True(validation.IsValid, string.Join("; ", validation.Errors));
        }

        [Fact]
        public void AllFourFormationsAdvanceThroughAllStagesInOrder()
        {
            var monitor = new PlanMonitor(BuildPlan());
            var log = RunTimeline(monitor);

            string[] StagesOf(PlannedFormationClass cls) => log
                .OfType<StageActivated>()
                .Where(e => e.Formation == cls)
                .Select(e => e.Directive.Spec.Type.ToString())
                .ToArray();

            Assert.Equal(new[] { "Skirmish", "FeignRetreat", "FlankArc" }, StagesOf(PlannedFormationClass.HorseArcher));
            Assert.Equal(new[] { "Skirmish", "FeignRetreat", "FlankArc" }, StagesOf(PlannedFormationClass.LightCavalry));
            Assert.Equal(new[] { "Hold", "Charge" }, StagesOf(PlannedFormationClass.Infantry));
            Assert.Equal(new[] { "Hold", "Charge" }, StagesOf(PlannedFormationClass.HeavyInfantry));
        }

        [Fact]
        public void SpringTrapIsEmittedByTheChargeAndReceivedNextTick()
        {
            var monitor = new PlanMonitor(BuildPlan());
            var log = RunTimelineRaw(monitor);

            var emission = log.First(e => e.Event is SignalEmitted s && s.Signal == SpringTrap);
            var infantryCharge = log.First(e => e.Event is StageActivated { StageIndex: 1, Formation: PlannedFormationClass.Infantry });
            Assert.Equal(infantryCharge.At, emission.At);

            // Latched bus: both HA formations wheel out on the next tick.
            foreach (var cls in new[] { PlannedFormationClass.HorseArcher, PlannedFormationClass.LightCavalry })
            {
                var flank = log.First(e => e.Event is StageActivated { StageIndex: 2 } a && a.Formation == cls);
                Assert.True(flank.At > emission.At,
                    $"{cls} flank arc at {flank.At}s, signal at {emission.At}s");
                Assert.True(flank.At - emission.At <= 1.01f,
                    $"{cls} took {flank.At - emission.At}s to react to the signal");
            }
        }

        [Fact]
        public void FeignRetreatCarriesTheFireWhileWithdrawingFlag()
        {
            var monitor = new PlanMonitor(BuildPlan());
            var log = RunTimeline(monitor);

            foreach (var cls in new[] { PlannedFormationClass.HorseArcher, PlannedFormationClass.LightCavalry })
            {
                var feign = log.OfType<StageActivated>().First(e => e.Formation == cls && e.StageIndex == 1);
                Assert.Equal(DirectiveType.FeignRetreat, feign.Directive.Spec.Type);
                Assert.True(feign.Directive.Spec.FireWhileWithdrawing);
                Assert.Equal(new MapVec(0f, -30f), feign.Directive.Target); // trap, TeamCenter -30
            }
        }

        [Fact]
        public void MissileOnlyFlankArcsNeverProduceAChargeAndMirrorSides()
        {
            var monitor = new PlanMonitor(BuildPlan());
            var log = RunTimeline(monitor);

            foreach (var cls in new[] { PlannedFormationClass.HorseArcher, PlannedFormationClass.LightCavalry })
            {
                Assert.DoesNotContain(log.OfType<StageActivated>(),
                    e => e.Formation == cls && e.Directive.Spec.Type == DirectiveType.Charge);
            }

            // After the signal: HA-1 steers left of the axis (-X), HA-2 right (+X).
            var leftArc = log.OfType<SteeringTargetChanged>()
                .Where(e => e.Formation == PlannedFormationClass.HorseArcher).Last();
            var rightArc = log.OfType<SteeringTargetChanged>()
                .Where(e => e.Formation == PlannedFormationClass.LightCavalry).Last();
            Assert.True(leftArc.Target.X < -25f, $"HA-1 final steering at {leftArc.Target}");
            Assert.True(rightArc.Target.X > 25f, $"HA-2 final steering at {rightArc.Target}");
        }

        [Fact]
        public void TheDraftAuthoredPlanValidatesClean()
        {
            // I9 acceptance, Core form: the editor's logic layer (PlanDraft)
            // can author the canonical A6 plan, and it is valid.
            Assert.True(BuildPlanViaDraft().Validate().IsValid);
        }

        [Fact]
        public void DraftAuthoredA6ExecutesIdenticallyToTheModelAuthoredOne()
        {
            // The I9 "Verify" goal ("author A6 in the UI, run the A6 scenario,
            // diff clean against the file-authored run") at the logic layer:
            // the PlanDraft-authored plan produces the exact same execution
            // timeline as the model-authored one (stage names aside, which
            // don't affect execution).
            var fromModel = Signature(RunTimeline(new PlanMonitor(BuildPlan()))).ToList();
            var fromDraft = Signature(RunTimeline(new PlanMonitor(BuildPlanViaDraft().Build()))).ToList();

            Assert.Equal(fromModel, fromDraft);
        }

        /// <summary>An execution-only event signature (excludes cosmetic stage names).</summary>
        private static IEnumerable<string> Signature(IEnumerable<PlanEvent> log) => log.Select(e => e switch
        {
            StageActivated s =>
                $"{s.Formation} stage{s.StageIndex} {s.Directive.Spec.Type} target={s.Directive.Target} " +
                $"side={s.Directive.Spec.Side} missileOnly={s.Directive.Spec.MissileOnly} fire={s.Directive.Spec.FireWhileWithdrawing}",
            SignalEmitted s => $"{s.Formation} emit {s.Signal}",
            SteeringTargetChanged s => $"{s.Formation} steer {s.Target}",
            _ => e.GetType().Name,
        });

        // ---- the timeline ----

        /// <summary>
        /// Scripted battle: a single enemy blob advances from 200 m out,
        /// pursues the feigning horse archers past the infantry line toward
        /// the trap anchor, 1 s per tick. Events are tagged with their tick
        /// time via <see cref="TimedEvent"/> wrappers.
        /// </summary>
        private static List<TimedEvent> RunTimelineRaw(PlanMonitor monitor)
        {
            var log = new List<TimedEvent>();
            var enemyY = 200f;
            var haY = 20f;

            for (var t = 0f; t <= 120f; t += 1f)
            {
                // Enemy: steady 6 m/s advance until it reaches the trap line.
                enemyY = System.Math.Max(enemyY - 6f, -25f);
                // HA line gives ground ahead of the enemy (close to what the
                // skirmish/feign steering produces; positions are scripted —
                // this timeline tests trigger/stage flow, not kinematics).
                haY = System.Math.Min(haY, enemyY - 60f);
                if (haY < -30f) haY = -30f;

                var snapshot = new FakeBattlefield(t, started: true)
                    .WithOwn(PlannedFormationClass.HorseArcher, -20, haY)
                    .WithOwn(PlannedFormationClass.LightCavalry, 20, haY)
                    .WithOwn(PlannedFormationClass.Infantry, -20, 0)
                    .WithOwn(PlannedFormationClass.HeavyInfantry, 20, 0)
                    .WithEnemy(1, 0, enemyY, cls: PlannedFormationClass.Infantry);

                foreach (var planEvent in monitor.Tick(snapshot))
                    log.Add(new TimedEvent(t, planEvent));
            }

            return log;
        }

        private static List<PlanEvent> RunTimeline(PlanMonitor monitor)
            => RunTimelineRaw(monitor).Select(e => e.Event).ToList();

        private sealed class TimedEvent
        {
            public TimedEvent(float at, PlanEvent planEvent)
            {
                At = at;
                Event = planEvent;
            }

            public float At { get; }
            public PlanEvent Event { get; }
        }

        // ---- the A6 plan ----

        private static BattlePlan BuildPlan()
        {
            var plan = new BattlePlan
            {
                Anchors =
                {
                    // Shared trap point behind the infantry line: TeamCenter
                    // basis so the HA retreat target and the infantry's
                    // "enemy within 40 m of the anchor" measure the same spot.
                    new MapAnchor { Id = Trap, Basis = AnchorBasis.TeamCenter, Forward = -30f },
                },
            };

            plan.Formations.Add(HorseArchers(PlannedFormationClass.HorseArcher, FlankSide.Left));
            plan.Formations.Add(HorseArchers(PlannedFormationClass.LightCavalry, FlankSide.Right));
            plan.Formations.Add(InfantryLine(PlannedFormationClass.Infantry));
            plan.Formations.Add(InfantryLine(PlannedFormationClass.HeavyInfantry));
            return plan;
        }

        private static FormationPlan HorseArchers(PlannedFormationClass cls, FlankSide side) => new()
        {
            Formation = cls,
            Stages =
            {
                new Stage
                {
                    Name = "skirmish",
                    Do = new DirectiveSpec { Type = DirectiveType.Skirmish, StandoffMeters = 60f },
                },
                new Stage
                {
                    Name = "feign retreat",
                    When = { new TriggerSpec { Type = TriggerType.EnemyCommits, SpeedThreshold = 2f, SustainSeconds = 4f } },
                    Do = new DirectiveSpec { Type = DirectiveType.FeignRetreat, Anchor = Trap, FireWhileWithdrawing = true },
                },
                new Stage
                {
                    Name = "wheel out",
                    When = { new TriggerSpec { Type = TriggerType.SignalReceived, Signal = SpringTrap } },
                    Do = new DirectiveSpec { Type = DirectiveType.FlankArc, Side = side, StandoffMeters = 50f, MissileOnly = true },
                },
            },
        };

        private static FormationPlan InfantryLine(PlannedFormationClass cls) => new()
        {
            Formation = cls,
            Stages =
            {
                new Stage
                {
                    Name = "shieldwall",
                    Do = new DirectiveSpec { Type = DirectiveType.Hold, Arrangement = Arrangement.ShieldWall },
                },
                new Stage
                {
                    Name = "spring the trap",
                    When = { new TriggerSpec { Type = TriggerType.EnemyWithinDistance, Meters = 40f, Anchor = Trap } },
                    Do = new DirectiveSpec { Type = DirectiveType.Charge },
                    Emit = { SpringTrap },
                },
            },
        };

        /// <summary>
        /// The same A6 plan authored through the editor's logic layer
        /// (PlanDraft) — the operations a Gauntlet view would drive. Built with
        /// AddStage (not AddFormation) so the horse archers open on skirmish,
        /// not the seeded hold (A3.9). Same parameters as the model plan, so
        /// the two execute identically.
        /// </summary>
        private static PlanDraft BuildPlanViaDraft()
        {
            var draft = new PlanDraft();
            draft.AddAnchor(new MapAnchor { Id = Trap, Basis = AnchorBasis.TeamCenter, Forward = -30f });

            foreach (var (cls, side) in new[]
                     {
                         (PlannedFormationClass.HorseArcher, FlankSide.Left),
                         (PlannedFormationClass.LightCavalry, FlankSide.Right),
                     })
            {
                draft.AddStage(cls, new Stage { Do = new DirectiveSpec { Type = DirectiveType.Skirmish, StandoffMeters = 60f } });
                draft.AddStage(cls);
                draft.SetTrigger(cls, 1, new TriggerSpec { Type = TriggerType.EnemyCommits, SpeedThreshold = 2f, SustainSeconds = 4f });
                draft.SetDirective(cls, 1, new DirectiveSpec { Type = DirectiveType.FeignRetreat, Anchor = Trap, FireWhileWithdrawing = true });
                draft.AddStage(cls);
                draft.SetTrigger(cls, 2, new TriggerSpec { Type = TriggerType.SignalReceived, Signal = SpringTrap });
                draft.SetDirective(cls, 2, new DirectiveSpec { Type = DirectiveType.FlankArc, Side = side, StandoffMeters = 50f, MissileOnly = true });
            }

            foreach (var cls in new[] { PlannedFormationClass.Infantry, PlannedFormationClass.HeavyInfantry })
            {
                draft.AddStage(cls, new Stage { Do = new DirectiveSpec { Type = DirectiveType.Hold, Arrangement = Arrangement.ShieldWall } });
                draft.AddStage(cls);
                draft.SetTrigger(cls, 1, new TriggerSpec { Type = TriggerType.EnemyWithinDistance, Meters = 40f, Anchor = Trap });
                draft.SetDirective(cls, 1, new DirectiveSpec { Type = DirectiveType.Charge });
                // Both infantry formations emit the latched spring-trap signal,
                // matching the model plan (the second emit is deduped on the bus).
                draft.EmitSignal(cls, 1, SpringTrap);
            }

            return draft;
        }
    }
}
