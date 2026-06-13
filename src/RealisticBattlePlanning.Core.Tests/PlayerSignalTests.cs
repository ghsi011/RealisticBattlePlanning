using System.Collections.Generic;
using System.Linq;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Planning;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// I8 (spec A4 player signal, B9): a palette-fired signal releases a
    /// gated stage exactly like a stage-emitted signal — same latched bus,
    /// same next-tick visibility — and the validator keeps the declared
    /// palette honest.
    /// </summary>
    public class PlayerSignalTests
    {
        [Fact]
        public void APaletteSignalReleasesAGatedStageOnTheNextTick()
        {
            var monitor = new PlanMonitor(HammerGatedCharge());

            monitor.Tick(Infantry(0f));
            Assert.Empty(monitor.Tick(Infantry(60f)).OfType<StageActivated>());

            monitor.RaiseExternalSignal("hammer");
            // Latched: visible the tick after it is raised, like any signal.
            var fired = monitor.Tick(Infantry(61f));
            var activated = Assert.Single(fired.OfType<StageActivated>());
            Assert.Equal(DirectiveType.Charge, activated.Directive.Spec.Type);
        }

        [Fact]
        public void PaletteAndStageEmittedSignalsBehaveIdentically()
        {
            // Two formations gated on the same name, one via PlayerSignal,
            // one via SignalReceived: a palette fire releases both together.
            var plan = HammerGatedCharge();
            plan.Formations.Add(new FormationPlan
            {
                Formation = PlannedFormationClass.Ranged,
                Stages =
                {
                    new Stage { Do = new DirectiveSpec { Type = DirectiveType.Hold } },
                    new Stage
                    {
                        When = { new TriggerSpec { Type = TriggerType.SignalReceived, Signal = "hammer" } },
                        Do = new DirectiveSpec { Type = DirectiveType.Charge },
                    },
                },
            });
            var monitor = new PlanMonitor(plan);

            monitor.Tick(Snapshot(0f));
            monitor.RaiseExternalSignal("hammer");

            var fired = monitor.Tick(Snapshot(1f)).OfType<StageActivated>().ToList();
            Assert.Equal(2, fired.Count);
            Assert.All(fired, e => Assert.Equal(DirectiveType.Charge, e.Directive.Spec.Type));
        }

        [Fact]
        public void DuplicateDeclaredPlayerSignalsAreAnError()
        {
            var plan = HammerGatedCharge();
            plan.PlayerSignals.Add("HAMMER");

            Assert.Contains(PlanValidator.Validate(plan).Errors, e => e.Contains("declared twice"));
        }

        [Fact]
        public void BlankDeclaredPlayerSignalIsAnError()
        {
            var plan = HammerGatedCharge();
            plan.PlayerSignals.Add("  ");

            Assert.Contains(PlanValidator.Validate(plan).Errors, e => e.Contains("blank"));
        }

        [Fact]
        public void AnUndeclaredPlayerSignalGateIsAnError()
        {
            // The palette only carries declared signals; a gate on anything
            // else could never fire.
            var plan = HammerGatedCharge();
            plan.PlayerSignals.Clear();

            Assert.Contains(PlanValidator.Validate(plan).Errors,
                e => e.Contains("'hammer'") && e.Contains("not declared"));
        }

        // ---- builders ----

        /// <summary>The H8-core shape: infantry charge gated solely on player signal "hammer".</summary>
        private static BattlePlan HammerGatedCharge() => new()
        {
            PlayerSignals = { "hammer" },
            Formations =
            {
                new FormationPlan
                {
                    Formation = PlannedFormationClass.Infantry,
                    Stages =
                    {
                        new Stage { Do = new DirectiveSpec { Type = DirectiveType.Hold, Arrangement = Arrangement.ShieldWall } },
                        new Stage
                        {
                            Name = "hammer falls",
                            When = { new TriggerSpec { Type = TriggerType.PlayerSignal, Signal = "hammer" } },
                            Do = new DirectiveSpec { Type = DirectiveType.Charge },
                        },
                    },
                },
            },
        };

        private static FakeBattlefield Infantry(float time)
            => new FakeBattlefield(time).WithOwn(PlannedFormationClass.Infantry, 0, 0);

        private static FakeBattlefield Snapshot(float time)
            => new FakeBattlefield(time)
                .WithOwn(PlannedFormationClass.Infantry, 0, 0)
                .WithOwn(PlannedFormationClass.Ranged, 15, 0);
    }
}
