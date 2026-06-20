using RealisticBattlePlanning.Planning;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// The in-battle numpad palette must be able to fire any signal a stage actually references, not
    /// just the declared ones — otherwise a referenced-but-undeclared signal (the "advance" default of
    /// a SignalReceived trigger) has no key and only the dev console can fire it.
    /// </summary>
    public class SignalPaletteTests
    {
        [Fact]
        public void ResolveUnionsDeclaredAndReferencedSignalsDeclaredFirstDeduped()
        {
            var plan = new BattlePlan
            {
                PlayerSignals = { "advance", "charge" },
                Formations =
                {
                    new FormationPlan
                    {
                        Formation = PlannedFormationClass.Infantry,
                        Stages =
                        {
                            new Stage { When = { new TriggerSpec { Type = TriggerType.PlayerSignal, Signal = "charge" } } },    // already declared
                            new Stage { When = { new TriggerSpec { Type = TriggerType.SignalReceived, Signal = "flank" } } },   // referenced, not declared
                            new Stage { When = { new TriggerSpec { Type = TriggerType.SignalReceived, Signal = "ADVANCE" } } }, // dup of declared, case-insensitive
                        },
                    },
                },
            };

            // Declared first (stable key order), then referenced-not-declared, deduped case-insensitively.
            Assert.Equal(new[] { "advance", "charge", "flank" }, SignalPalette.Resolve(plan));
        }

        [Fact]
        public void ResolveIgnoresNonSignalTriggersBlanksAndNullPlan()
        {
            Assert.Empty(SignalPalette.Resolve(null));

            var plan = new BattlePlan
            {
                Formations =
                {
                    new FormationPlan
                    {
                        Formation = PlannedFormationClass.Cavalry,
                        Stages =
                        {
                            new Stage { When = { new TriggerSpec { Type = TriggerType.BattleStart } } },              // not a signal trigger
                            new Stage { When = { new TriggerSpec { Type = TriggerType.PlayerSignal, Signal = "  " } } }, // blank signal
                        },
                    },
                },
            };
            Assert.Empty(SignalPalette.Resolve(plan));
        }
    }
}
