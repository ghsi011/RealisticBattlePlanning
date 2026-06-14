using System;
using RealisticBattlePlanning.Fidelity;

namespace RealisticBattlePlanning.Execution
{
    /// <summary>
    /// Runtime master switch for the fidelity/progression system (spec F).
    /// Default OFF = pass-through, so normal play and the Layer-2 harness
    /// baseline stay byte-for-byte unchanged until the system is deliberately
    /// switched on. Toggled via the rbp.fidelity console command (a dev
    /// affordance until the MCM menu arrives in Area F). The per-battle seed
    /// defaults to a varied value so a campaign never replays the identical
    /// fidelity rolls every battle (audit H2/H3); a harness run can pin it for
    /// reproducibility.
    /// </summary>
    public static class FidelityConfig
    {
        public enum FidelityMode
        {
            /// <summary>Pass-through: no deviation (the Phase-1 default).</summary>
            Off,
            /// <summary>Each commander executes at their own vanilla-derived competence tier (the real progression model).</summary>
            Competence,
            /// <summary>Every commander executes at one fixed tier (a tactics sandbox; also the easiest in-game verification).</summary>
            Fixed,
        }

        public static FidelityMode Mode { get; set; } = FidelityMode.Off;

        public static FidelityTier FixedTier { get; set; } = FidelityTier.Untrained;

        /// <summary>Set for reproducible (harness) runs; null = a different stream each battle.</summary>
        public static int? Seed { get; set; }

        public static bool Enabled => Mode != FidelityMode.Off;

        /// <summary>The model the monitor rolls against this battle (captured at mission start).</summary>
        public static IFidelityModel CreateModel() => Mode switch
        {
            FidelityMode.Competence => new CompetenceFidelityModel(),
            FidelityMode.Fixed => new FixedTierFidelityModel(FixedTier),
            _ => new PassThroughFidelityModel(),
        };

        public static int NextBattleSeed() => Seed ?? Environment.TickCount;

        public static string Describe() => Mode switch
        {
            FidelityMode.Competence => $"competence-derived (seed {(Seed?.ToString() ?? "varied")})",
            FidelityMode.Fixed => $"fixed {FixedTier} (seed {(Seed?.ToString() ?? "varied")})",
            _ => "off (pass-through)",
        };
    }
}
