using System;
using System.Collections.Generic;

namespace ModDebugKit.Battles
{
    /// <summary>One distribution result: add <see cref="Count"/> of the troop at (class, troop-index).</summary>
    public sealed class TroopAssignment
    {
        public TroopAssignment(int classIndex, int troopIndex, int count)
        {
            ClassIndex = classIndex;
            TroopIndex = troopIndex;
            Count = count;
        }

        public int ClassIndex { get; }
        public int TroopIndex { get; }
        public int Count { get; }
    }

    /// <summary>A class whose count could not be placed because it had no resolvable troop.</summary>
    public sealed class DroppedClass
    {
        public DroppedClass(int classIndex, int count)
        {
            ClassIndex = classIndex;
            Count = count;
        }

        public int ClassIndex { get; }
        public int Count { get; }
    }

    public sealed class RosterDistributionResult
    {
        public List<TroopAssignment> Assignments { get; } = new();

        /// <summary>Classes with a positive count but no troop to place them in (surfaced, not silently dropped).</summary>
        public List<DroppedClass> Dropped { get; } = new();
    }

    /// <summary>
    /// The per-class roster distribution math, lifted out of the engine so it is
    /// unit-testable. Mirrors the vanilla CustomBattleHelper.PopulateListsWithDefaults:
    /// each class's count is split evenly (with carry) across that class's troops,
    /// the last troop absorbing the rounding remainder; and if the horse-archer
    /// class has no troop, its count is redistributed to the other three. A class
    /// with a positive count but zero troops is reported in
    /// <see cref="RosterDistributionResult.Dropped"/> rather than silently lost.
    /// </summary>
    public static class RosterDistribution
    {
        /// <param name="counts">[infantry, ranged, cavalry, horseArcher].</param>
        /// <param name="troopsPerClass">how many resolvable troop types each class has (same indexing).</param>
        public static RosterDistributionResult Distribute(int[] counts, int[] troopsPerClass)
        {
            var result = new RosterDistributionResult();
            if (counts == null || troopsPerClass == null)
                return result;

            var n = (int[])counts.Clone(); // never mutate the caller's array

            // Horse-archer redistribution when no HA troop exists (vanilla parity).
            if (n.Length > 3 && troopsPerClass.Length > 3 && troopsPerClass[3] == 0 && n[3] > 0)
            {
                n[2] += n[3] / 3;
                n[1] += n[3] / 3;
                n[0] += n[3] / 3;
                n[0] += n[3] - n[3] / 3 * 3; // remainder to infantry
                n[3] = 0;
            }

            var classes = Math.Min(n.Length, troopsPerClass.Length);
            for (var cls = 0; cls < classes; cls++)
            {
                var remaining = n[cls];
                if (remaining <= 0)
                    continue;

                var troopCount = troopsPerClass[cls];
                if (troopCount <= 0)
                {
                    result.Dropped.Add(new DroppedClass(cls, remaining));
                    continue;
                }

                var perTroop = (float)remaining / troopCount;
                var carry = 0f;
                for (var k = 0; k < troopCount; k++)
                {
                    var share = perTroop + carry;
                    var floored = (int)Math.Floor(share);
                    carry = share - floored;
                    var add = floored;
                    remaining -= floored;
                    if (k == troopCount - 1 && remaining > 0)
                    {
                        add += remaining; // last troop absorbs the rounding remainder
                        remaining = 0;
                    }
                    if (add > 0)
                        result.Assignments.Add(new TroopAssignment(cls, k, add));
                }
            }

            return result;
        }
    }
}
