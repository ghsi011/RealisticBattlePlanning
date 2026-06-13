namespace RealisticBattlePlanning.Fidelity
{
    /// <summary>
    /// What the fidelity model needs to know about the commander executing a
    /// stage (spec D1/D3). Phase 1 carries only the derived Command Competence
    /// tier; P2 fills it from vanilla Tactics/Leadership + the Plan Familiarity
    /// layer, and per-maneuver Proficiency joins it when templates land
    /// (Phase 3). Engine-free: the engine maps a hero to this.
    /// </summary>
    public sealed class CommanderProfile
    {
        public CommanderProfile(FidelityTier competence)
        {
            Competence = competence;
        }

        public FidelityTier Competence { get; }

        /// <summary>Stand-in until a commander is known (treated as green).</summary>
        public static readonly CommanderProfile Default = new(FidelityTier.Untrained);
    }
}
