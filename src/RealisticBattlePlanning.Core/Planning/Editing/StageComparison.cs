using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using RealisticBattlePlanning.Planning.Model;
using RealisticBattlePlanning.Serialization;

namespace RealisticBattlePlanning.Planning.Editing
{
    /// <summary>
    /// Stage value-equality for the multi-select stage rail (spec A2.6.5): when several
    /// formations are selected, a rail row is shown in full color only where that stage
    /// is identical across all of them, so shared stages are edited once and divergences
    /// are visible. "Identical" means same function — trigger conditions + directive +
    /// emitted signals — ignoring the cosmetic stage Name. Engine-free + unit-tested.
    /// </summary>
    public static class StageComparison
    {
        /// <summary>True if two stages are functionally equivalent (same When + Do + Emit, Name ignored).</summary>
        public static bool AreEquivalent(Stage a, Stage b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return Key(a) == Key(b);
        }

        /// <summary>
        /// Aligns stages by index across the selected formations' stage lists and returns,
        /// per row, whether that stage exists and is equivalent in every list. A formation
        /// missing a stage at a row makes that row not shared. The list length is the
        /// longest selection's stage count, so the rail can render every row.
        /// </summary>
        public static IReadOnlyList<bool> SharedRows(IReadOnlyList<IReadOnlyList<Stage>> stageLists)
        {
            var result = new List<bool>();
            if (stageLists == null || stageLists.Count == 0)
                return result;

            var rows = stageLists.Max(s => s?.Count ?? 0);
            for (var i = 0; i < rows; i++)
            {
                if (stageLists.Any(s => s == null || i >= s.Count))
                {
                    result.Add(false);
                    continue;
                }
                var first = Key(stageLists[0][i]);
                result.Add(stageLists.All(s => Key(s[i]) == first));
            }
            return result;
        }

        // Canonical functional signature of a stage (Name excluded), via the shared JSON
        // dialect so enums/params serialize consistently — equal function => equal key.
        private static string Key(Stage s)
            => JsonConvert.SerializeObject(new { s.When, s.Do, s.Emit }, JsonDialect.Lenient);
    }
}
