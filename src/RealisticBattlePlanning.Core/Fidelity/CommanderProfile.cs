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

        /// <summary>
        /// A profile from a commander's vanilla stats alone — the zero-XP base
        /// (D1): a renowned lord arrives competent, a green companion does not.
        /// Deliberately stats-only: familiarity-bearing profiles come from
        /// ProgressionModel.ProfileFor, the single path that applies the
        /// battle/drill split and the C5 drill cap. There is no uncapped
        /// familiarity door here, so the engine can't accidentally grant
        /// Veteran/Master from drilling alone.
        /// </summary>
        public static CommanderProfile FromStats(int tactics, int leadership)
        {
            var score = CompetenceModel.Score(tactics, leadership, 0);
            return new CommanderProfile(CompetenceModel.TierFor(score), score);
        }

        /// <summary>Stand-in until a commander is known (treated as green).</summary>
        public static readonly CommanderProfile Default = new(FidelityTier.Untrained);
    }
}
