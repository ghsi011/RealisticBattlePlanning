namespace RealisticBattlePlanning.Progression
{
    /// <summary>
    /// The mod-owned persistent data for one commander (spec D1/D4): the Plan
    /// Familiarity XP layer that adds to the vanilla-derived competence base,
    /// plus a small Service Record for the Dossier/AAR. Vanilla skills are NOT
    /// stored here — they live on the hero and are read at query time, so the
    /// mod only persists what vanilla cannot express. Engine-free; the engine
    /// keys these by hero id and save-persists them (G1).
    /// </summary>
    public sealed class CommanderRecord
    {
        /// <summary>Experience following THIS player's plans (0–300, D1). Feeds CompetenceModel.</summary>
        public float PlanFamiliarityXp { get; set; }

        // Service Record (D1 "for UI flavor and debugging").
        public int BattlesUnderCommand { get; set; }
        public int StagesExecuted { get; set; }
        public int StagesAbortedOrFailed { get; set; }
    }
}
