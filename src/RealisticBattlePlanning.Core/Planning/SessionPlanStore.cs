using System;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Planning
{
    /// <summary>
    /// Carries the player's battle plan from one battle to the next within a play session
    /// (spec Area G, session-scoped). The carry is keyed to a <c>sessionKey</c> the engine
    /// derives from the game identity (a campaign's unique id, or one bucket for custom
    /// battles): plans carry across consecutive battles of the SAME game, but a battle in a
    /// DIFFERENT game (a campaign plan leaking into a later custom battle, or vice-versa)
    /// reads as no plan and starts blank. Deep copies are stored on set and returned on get,
    /// so the live plan and the store never alias. Static and not tied to a save — restart-
    /// persistent, save-scoped storage is a later Area-G step.
    /// </summary>
    public static class SessionPlanStore
    {
        private static BattlePlan _current;
        private static string _sessionKey;

        /// <summary>True when a non-empty plan is carried for <paramref name="sessionKey"/>.</summary>
        public static bool HasPlanFor(string sessionKey)
            => _current != null && _current.Formations.Count > 0 && KeyMatches(sessionKey);

        /// <summary>The plan carried for <paramref name="sessionKey"/> (a fresh deep copy each
        /// get), or null when this session has none — so a different game/campaign starts blank
        /// instead of inheriting another's plan.</summary>
        public static BattlePlan CurrentFor(string sessionKey)
            => _current != null && KeyMatches(sessionKey) ? Copy(_current) : null;

        /// <summary>Carry <paramref name="plan"/> as this session's plan. A plan applied under a
        /// new key supersedes any prior one, so plans never cross sessions.</summary>
        public static void Set(string sessionKey, BattlePlan plan)
        {
            _sessionKey = sessionKey;
            _current = plan == null ? null : Copy(plan);
        }

        /// <summary>Forget the carried plan (e.g. a "new session"/reset).</summary>
        public static void Clear()
        {
            _current = null;
            _sessionKey = null;
        }

        private static bool KeyMatches(string sessionKey)
            => string.Equals(_sessionKey, sessionKey, StringComparison.Ordinal);

        private static BattlePlan Copy(BattlePlan plan)
        {
            try { return PlanSerializer.DeepCopy(plan); }
            catch { return plan; } // a serialization hiccup must not lose the plan outright
        }
    }
}
