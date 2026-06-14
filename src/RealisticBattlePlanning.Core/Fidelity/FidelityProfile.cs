namespace RealisticBattlePlanning.Fidelity
{
    /// <summary>
    /// One rolled set of execution-fidelity parameters for a commander
    /// carrying out a stage (spec D3). Phase 1 (P1) populates reaction delay;
    /// positional error, trigger-threshold jitter, discipline-break and
    /// signal-miss chances join in later P-iterations. All magnitudes are
    /// data-driven (FidelityDefaults), so balance is config, not code.
    /// </summary>
    public sealed class FidelityProfile
    {
        public FidelityProfile(
            FidelityTier tier,
            float reactionDelaySeconds,
            float positionErrorMeters = 0f,
            float positionErrorX = 0f,
            float positionErrorY = 0f)
        {
            Tier = tier;
            ReactionDelaySeconds = reactionDelaySeconds;
            PositionErrorMeters = positionErrorMeters;
            PositionErrorX = positionErrorX;
            PositionErrorY = positionErrorY;
        }

        /// <summary>
        /// The tier these deviations were rolled for — reporting only (e.g. the
        /// ReactionDelayed event). NOT a commander's identity: <see cref="Perfect"/>
        /// carries Master as a "no-deviation" sentinel even when the commander
        /// is green (its opening posture, a resume, a clean skip). Read a
        /// commander's competence from their CommanderProfile / record, never
        /// off a rolled profile.
        /// </summary>
        public FidelityTier Tier { get; }

        /// <summary>True when this roll actually imposes a deviation; false for <see cref="Perfect"/>. The honest check for "did fidelity change anything", rather than inspecting <see cref="Tier"/>.</summary>
        public bool Deviates => ReactionDelaySeconds > 0f || PositionErrorMeters > 0f;

        /// <summary>Lag between a stage's trigger firing and the commander acting on it (D3).</summary>
        public float ReactionDelaySeconds { get; }

        /// <summary>How far the formation drifts off its anchors/paths (D3); the magnitude of the offset.</summary>
        public float PositionErrorMeters { get; }

        /// <summary>The rolled drift offset, in meters (kept as components so Fidelity needn't know MapVec).</summary>
        public float PositionErrorX { get; }
        public float PositionErrorY { get; }

        /// <summary>
        /// No deviation — the pass-through model and the monitor's clean
        /// transitions (opening posture, resume, skip-forward) return this. Its
        /// Master <see cref="Tier"/> means "flawless execution," a sentinel, not
        /// that the commander is a Master (see <see cref="Tier"/>); test it with
        /// <see cref="Deviates"/>.
        /// </summary>
        public static readonly FidelityProfile Perfect = new(FidelityTier.Master, 0f);
    }
}
