namespace RealisticBattlePlanning.Planning.Model
{
    public enum AnchorBasis
    {
        /// <summary>Relative to the owning formation's initial deployment position.</summary>
        OwnStart,

        /// <summary>Relative to the player team's deployment center.</summary>
        TeamCenter,

        /// <summary>Absolute scene coordinates (UI-authored anchors, later iterations).</summary>
        Scene,
    }

    /// <summary>
    /// A named map position referenced by triggers and directives. Debug-file
    /// plans use relative bases (Forward/Right in meters, axes aligned to the
    /// team's initial attack direction) so one plan works on any scene;
    /// resolution to world positions happens at execution time.
    /// </summary>
    public sealed class MapAnchor
    {
        public string Id { get; set; }

        public AnchorBasis Basis { get; set; } = AnchorBasis.OwnStart;

        /// <summary>Meters along the team's initial attack direction (relative bases).</summary>
        public float Forward { get; set; }

        /// <summary>Meters to the right of the attack direction (relative bases).</summary>
        public float Right { get; set; }

        /// <summary>Scene coordinates (Basis == Scene).</summary>
        public float X { get; set; }
        public float Y { get; set; }
    }
}
