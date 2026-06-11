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
    }
}
