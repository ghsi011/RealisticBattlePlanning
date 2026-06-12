using System;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Execution
{
    /// <summary>
    /// Parses the string formation selectors used by trigger/directive
    /// parameters: a PlannedFormationClass name, "Player" (A3.10), or
    /// "Nearest"/null meaning "any/nearest enemy" depending on context.
    /// </summary>
    public static class FormationSelector
    {
        public static bool IsPlayer(string selector)
            => string.Equals(selector, "Player", StringComparison.OrdinalIgnoreCase);

        public static bool IsNearest(string selector)
            => selector == null || string.Equals(selector, "Nearest", StringComparison.OrdinalIgnoreCase);

        public static PlannedFormationClass? ParseClass(string selector)
        {
            if (selector == null || IsPlayer(selector) || IsNearest(selector))
                return null;
            return Enum.TryParse<PlannedFormationClass>(selector, ignoreCase: true, out var parsed)
                ? parsed
                : null;
        }

        /// <summary>
        /// Validator support: a typo'd selector ("Nearset", "Infntry") parses
        /// to null and silently means "nearest/any" at runtime — the
        /// validator rejects anything that isn't a recognized word.
        /// </summary>
        public static bool IsValidEnemySelector(string selector)
            => IsNearest(selector) || (!IsPlayer(selector) && ParseClass(selector) != null);

        public static bool IsValidFriendlySelector(string selector)
            => selector != null && (IsPlayer(selector) || ParseClass(selector) != null);
    }
}
