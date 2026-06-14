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
        // Must not touch rng: pass-through draws nothing, or it would desync
        // seeded replays and break the byte-for-byte backward-compat guarantee.
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

    /// <summary>
    /// The real progression model: each commander executes at their own
    /// derived Command Competence tier (D1-D3). This is what "progression on"
    /// installs; turning it off swaps in <see cref="FixedTierFidelityModel"/>
    /// or <see cref="PassThroughFidelityModel"/>.
    /// </summary>
    public sealed class CompetenceFidelityModel : IFidelityModel
    {
        public FidelityProfile Roll(CommanderProfile commander, Random rng)
            => FidelityRolls.ForTier((commander ?? CommanderProfile.Default).Competence, rng);
    }

    /// <summary>Shared rolling so every model draws the same way (one rng draw per dimension, in a fixed order).</summary>
    public static class FidelityRolls
    {
        public static FidelityProfile ForTier(FidelityTier tier, Random rng)
        {
            // Fixed draw order keeps a seeded battle reproducible:
            // reaction delay, then drift magnitude, then drift direction.
            var (delayMin, delayMax) = FidelityDefaults.ReactionDelaySeconds(tier);
            var delay = delayMin + (float)rng.NextDouble() * (delayMax - delayMin);

            var (errMin, errMax) = FidelityDefaults.PositionErrorMeters(tier);
            var magnitude = errMin + (float)rng.NextDouble() * (errMax - errMin);
            var angle = (float)(rng.NextDouble() * 2.0 * Math.PI);

            return new FidelityProfile(
                tier,
                delay,
                magnitude,
                magnitude * (float)Math.Cos(angle),
                magnitude * (float)Math.Sin(angle));
        }
    }
}
