using System;
using RealisticBattlePlanning.Fidelity;

namespace RealisticBattlePlanning.Execution
{
    /// <summary>
    /// Runtime master switch for the fidelity/progression system (spec F).
    /// The default is CONTEXTUAL ("auto" in Config\rbp.cfg): ON at competence
    /// in a campaign — the officer pillar (D) is the mod's second half and must
    /// not ship dark — and OFF (pass-through) everywhere else, so Custom Battle
    /// stays a clean tactics sandbox and the Layer-2 harness baseline is
    /// byte-for-byte unchanged. rbp.fidelity (console) sets an explicit
    /// override for the session; the config file can pin a mode permanently.
    /// The per-battle seed defaults to a varied value so a campaign never
    /// replays the identical fidelity rolls every battle (audit H2/H3); a
    /// harness run can pin it for reproducibility.
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

        private static FidelityMode? _sessionOverride;

        public static FidelityMode Mode
        {
            get => _sessionOverride ?? ConfiguredDefault();
            set => _sessionOverride = value;
        }

        private static FidelityMode ConfiguredDefault()
        {
            switch (Settings.RbpConfig.FidelityMode)
            {
                case "off": return FidelityMode.Off;
                case "competence": return FidelityMode.Competence;
                case "fixed": return FidelityMode.Fixed;
                default: // "auto"
                    return TaleWorlds.CampaignSystem.Campaign.Current != null
                        ? FidelityMode.Competence
                        : FidelityMode.Off;
            }
        }

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
