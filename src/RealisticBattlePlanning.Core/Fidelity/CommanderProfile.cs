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
        public CommanderProfile(FidelityTier competence, float competenceScore = 0f)
        {
            Competence = competence;
            CompetenceScore = competenceScore;
        }

        public FidelityTier Competence { get; }

        /// <summary>The raw effective-competence score behind the tier (for the Dossier/AAR; 0 when tier-only).</summary>
        public float CompetenceScore { get; }

        /// <summary>Builds a profile from vanilla stats + the mod's familiarity layer (D1).</summary>
        public static CommanderProfile FromStats(int tactics, int leadership, int planFamiliarity = 0)
        {
            var score = CompetenceModel.Score(tactics, leadership, planFamiliarity);
            return new CommanderProfile(CompetenceModel.TierFor(score), score);
        }

        /// <summary>Stand-in until a commander is known (treated as green).</summary>
        public static readonly CommanderProfile Default = new(FidelityTier.Untrained);
    }
}
