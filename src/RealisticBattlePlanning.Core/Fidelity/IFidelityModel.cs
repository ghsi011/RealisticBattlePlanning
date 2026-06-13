using System;

namespace RealisticBattlePlanning.Fidelity
{
    /// <summary>
    /// Turns a commander's competence into the concrete execution deviations
    /// the Plan Monitor applies (spec D3). The seam the spec's "progression
    /// on/off" master toggle (F) and the whole fidelity system hang on: swap
    /// the implementation, the monitor is untouched. All randomness comes from
    /// the supplied seedable <see cref="Random"/> so a battle replays
    /// identically and tests can assert exact-per-seed outcomes.
    /// </summary>
    public interface IFidelityModel
    {
        FidelityProfile Roll(CommanderProfile commander, Random rng);
    }

    /// <summary>Perfect execution — no deviation, ignores competence (spec "progression off" / Phase-1 default).</summary>
    public sealed class PassThroughFidelityModel : IFidelityModel
    {
        public FidelityProfile Roll(CommanderProfile commander, Random rng) => FidelityProfile.Perfect;
    }

    /// <summary>
    /// Every commander executes at one fixed tier, regardless of their stats
    /// (spec F: "progression-off = all commanders behave at a fixed
    /// configurable tier — a pure tactics sandbox"). Also the building block
    /// the competence-derived model (P2) specialises.
    /// </summary>
    public sealed class FixedTierFidelityModel : IFidelityModel
    {
        private readonly FidelityTier _tier;

        public FixedTierFidelityModel(FidelityTier tier)
        {
            _tier = tier;
        }

        public FidelityProfile Roll(CommanderProfile commander, Random rng)
            => FidelityRolls.ForTier(_tier, rng);
    }

    /// <summary>Shared rolling so every model draws the same way (one rng draw per dimension, in a fixed order).</summary>
    public static class FidelityRolls
    {
        public static FidelityProfile ForTier(FidelityTier tier, Random rng)
        {
            var (min, max) = FidelityDefaults.ReactionDelaySeconds(tier);
            var delay = min + (float)rng.NextDouble() * (max - min);
            return new FidelityProfile(tier, delay);
        }
    }
}
