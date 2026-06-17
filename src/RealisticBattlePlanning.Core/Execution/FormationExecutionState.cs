using RealisticBattlePlanning.Fidelity;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Execution
{
    /// <summary>
    /// One formation's place in its plan (spec B2/B4/B5): which stage is
    /// active, the per-stage timing/waypoint/hold scratch, the suspend/abort
    /// mode, and the fidelity rolled for the current (or pending) activation.
    /// Extracted from <see cref="PlanMonitor"/> so the state machine's field
    /// invariants live in one guarded type rather than scattered across the
    /// tick loop, and so a later HUD can read a formation's status without
    /// reaching into the monitor.
    ///
    /// The state-machine fields mutate only through the transition methods
    /// below; each bundles the fields that must change together, so a caller
    /// can't, say, suspend without clearing a queued reaction, or resume
    /// without dropping a stale one. Two invariants the bundling protects:
    ///   * beginning a stage (or entering a hold) clears the per-stage scratch
    ///     — waypoint index, steering target, pending reaction;
    ///   * <see cref="ActiveFidelity"/> is the roll for the stage being
    ///     activated. Resume and hold drop it back to <see cref="FidelityProfile.Perfect"/>,
    ///     and the monitor drops it whenever an activation skips to a different
    ///     stage than the one it was rolled for — so one stage's drift never
    ///     leaks onto another's destination.
    /// <see cref="WaypointIndex"/> and <see cref="LastSteeringTarget"/> are
    /// transient per-tick steering details the monitor manages directly.
    /// </summary>
    internal sealed class FormationExecutionState
    {
        public FormationExecutionState(FormationPlan plan)
        {
            Plan = plan;
        }

        public FormationPlan Plan { get; }

        public int ActiveStageIndex { get; private set; } = -1;
        public float ActivatedAtSeconds { get; private set; }
        public ResolvedDirective ActiveDirective { get; private set; }
        public FormationPlanMode Mode { get; private set; } = FormationPlanMode.Active;

        /// <summary>Set when no evaluable stage remained (B6 hold).</summary>
        public bool Holding { get; private set; }

        /// <summary>Stage whose trigger fired but whose reaction delay (D3) hasn't elapsed; -1 when none.</summary>
        public int PendingStageIndex { get; private set; } = -1;
        public float PendingActivateAt { get; private set; }

        /// <summary>Fidelity rolled for the current/pending activation (reaction delay + drift), carried from trigger to activation.</summary>
        public FidelityProfile ActiveFidelity { get; private set; } = FidelityProfile.Perfect;

        // Transient per-tick steering scratch (not state-machine transitions).
        public int WaypointIndex { get; set; }
        public MapVec? LastSteeringTarget { get; set; }

        /// <summary>
        /// A steering directive that has committed to its decisive action and
        /// should stop repositioning — e.g. a FlankArc that reached its station and
        /// charged home (A5). Cleared when a new stage begins.
        /// </summary>
        public bool SteeringCommitted { get; set; }

        /// <summary>Activate a stage: adopt its resolved directive and clear the per-stage scratch.</summary>
        public void BeginStage(int stageIndex, float atSeconds, ResolvedDirective directive)
        {
            ResetScratch(stageIndex, atSeconds);
            Holding = false;
            ActiveDirective = directive;
        }

        /// <summary>
        /// No evaluable stage remained: hold in place at the given (clamped)
        /// index. Nothing to drift and the rolled reaction no longer applies,
        /// so fidelity resets.
        /// </summary>
        public void EnterHold(int stageIndex, float atSeconds, ResolvedDirective holdDirective)
        {
            ResetScratch(stageIndex, atSeconds);
            Holding = true;
            ActiveDirective = holdDirective;
            ActiveFidelity = FidelityProfile.Perfect;
        }

        /// <summary>Player override (B5): stop governing until a resume; a queued reaction is the plan's, not the player's.</summary>
        public void Suspend()
        {
            Mode = FormationPlanMode.Suspended;
            PendingStageIndex = -1;
        }

        /// <summary>Resume (B5): the player's clean re-adoption — no carried reaction.</summary>
        public void Resume()
        {
            Mode = FormationPlanMode.Active;
            ActiveFidelity = FidelityProfile.Perfect;
        }

        /// <summary>Abort (B4): the formation leaves the plan for good; any queued reaction is dropped.</summary>
        public void Abort()
        {
            Mode = FormationPlanMode.Aborted;
            PendingStageIndex = -1;
        }

        /// <summary>Park a triggered stage to wait out the commander's reaction delay (D3), carrying its roll.</summary>
        public void ParkReaction(int stageIndex, float activateAt, FidelityProfile fidelity)
        {
            PendingStageIndex = stageIndex;
            PendingActivateAt = activateAt;
            ActiveFidelity = fidelity;
        }

        /// <summary>Consume the pending stage index (clearing it) once its reaction delay has elapsed.</summary>
        public int TakePendingStage()
        {
            var index = PendingStageIndex;
            PendingStageIndex = -1;
            return index;
        }

        /// <summary>Set the fidelity for the activation about to happen — the roll, or Perfect for a clean (drift-free) one.</summary>
        public void CarryFidelity(FidelityProfile fidelity)
        {
            ActiveFidelity = fidelity ?? FidelityProfile.Perfect;
        }

        private void ResetScratch(int stageIndex, float atSeconds)
        {
            ActiveStageIndex = stageIndex;
            ActivatedAtSeconds = atSeconds;
            WaypointIndex = 0;
            LastSteeringTarget = null;
            SteeringCommitted = false;
            PendingStageIndex = -1;
        }
    }
}
