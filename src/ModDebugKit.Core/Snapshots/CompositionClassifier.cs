namespace ModDebugKit.Snapshots
{
    /// <summary>
    /// Labels a formation by what it actually holds, from the four unit buckets
    /// (infantry / ranged / cavalry / horse-archer). One bucket holding at
    /// least <see cref="DominantFraction"/> of the units names the formation
    /// ("Infantry", "Mostly Ranged"); otherwise it reads "Mixed" with the
    /// dominant bucket noted. Engine-free and unit-tested so the labelling rule
    /// can't drift; the engine only supplies the counts.
    /// </summary>
    public static class CompositionClassifier
    {
        /// <summary>A bucket at or above this share of the total names the formation outright.</summary>
        public const float DominantFraction = 0.70f;

        public static CompositionDto Classify(int infantry, int ranged, int cavalry, int horseArcher)
        {
            var total = infantry + ranged + cavalry + horseArcher;
            return new CompositionDto
            {
                Infantry = infantry,
                Ranged = ranged,
                Cavalry = cavalry,
                HorseArcher = horseArcher,
                Total = total,
                Label = Label(infantry, ranged, cavalry, horseArcher, total),
            };
        }

        private static string Label(int infantry, int ranged, int cavalry, int horseArcher, int total)
        {
            if (total <= 0)
                return "Empty";

            var max = infantry;
            var name = "Infantry";
            if (ranged > max) { max = ranged; name = "Ranged"; }
            if (cavalry > max) { max = cavalry; name = "Cavalry"; }
            if (horseArcher > max) { max = horseArcher; name = "Horse Archer"; }

            var share = (float)max / total;
            if (max == total)
                return name;
            if (share >= DominantFraction)
                return $"Mostly {name}";
            return $"Mixed (mostly {name})";
        }
    }
}
