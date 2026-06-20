using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Planning
{
    /// <summary>
    /// Carries the player's battle plan from one battle to the next within a play session
    /// (spec Area G, session-scoped). It is empty on game launch, so the FIRST battle starts
    /// with a blank plan; it's set whenever a plan is applied, so the next battle loads the
    /// previous one. Deep copies are stored on set and returned on get, so the live plan and
    /// the store never alias (a battle mutating its plan can't corrupt the carried one). Static
    /// and not tied to a save — restart-persistent, save-scoped storage is a later Area-G step.
    /// </summary>
    public static class SessionPlanStore
    {
        private static BattlePlan _current;

        /// <summary>True when a non-empty plan has been carried from an earlier battle.</summary>
        public static bool HasPlan => _current != null && _current.Formations.Count > 0;

        /// <summary>The carried plan (a fresh deep copy each get, so callers can't mutate the
        /// store in place); null until the first plan is applied this session.</summary>
        public static BattlePlan Current
        {
            get => _current == null ? null : Copy(_current);
            set => _current = value == null ? null : Copy(value);
        }

        /// <summary>Forget the carried plan (e.g. a "new session"/reset).</summary>
        public static void Clear() => _current = null;

        private static BattlePlan Copy(BattlePlan plan)
        {
            try { return PlanSerializer.DeepCopy(plan); }
            catch { return plan; } // a serialization hiccup must not lose the plan outright
        }
    }
}
