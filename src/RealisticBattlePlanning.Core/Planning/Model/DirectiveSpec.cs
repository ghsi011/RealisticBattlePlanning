using System.Collections.Generic;

namespace RealisticBattlePlanning.Planning.Model
{
    /// <summary>Directive vocabulary, v1 minimum (spec A5).</summary>
    public enum DirectiveType
    {
        Hold,
        MoveTo,
        Skirmish,
        FeignRetreat,
        Charge,
        FlankArc,
        PullBack,
        Screen,
        Follow,
        FireControl,
    }

    public enum Arrangement
    {
        Line,
        ShieldWall,
        Loose,
        Square,
        Circle,
    }

    public enum MoveSpeed
    {
        Walk,
        Run,
    }

    public enum FlankSide
    {
        Left,
        Right,
    }

    public enum FireMode
    {
        Hold,
        Free,
    }

    /// <summary>
    /// One order the formation carries out while its stage is active. Which
    /// parameters apply depends on <see cref="Type"/>; PlanValidator enforces
    /// the required ones.
    /// </summary>
    public sealed class DirectiveSpec
    {
        public DirectiveType Type { get; set; }

        /// <summary>MoveTo / FeignRetreat / PullBack destination anchor.</summary>
        public string Anchor { get; set; }

        /// <summary>MoveTo waypoint path (anchor ids, in order). Overrides Anchor.</summary>
        public List<string> Path { get; set; }

        public Arrangement? Arrangement { get; set; }
        public float? WidthMeters { get; set; }

        /// <summary>
        /// Target selector: enemy ("Nearest" or class name) for Skirmish /
        /// Charge / FlankArc; friendly formation for Screen / Follow.
        /// </summary>
        public string Target { get; set; }

        public float? StandoffMeters { get; set; }
        public MoveSpeed? Speed { get; set; }

        /// <summary>FeignRetreat: keep shooting while withdrawing.</summary>
        public bool? FireWhileWithdrawing { get; set; }

        /// <summary>FlankArc: charge forbidden (spec A5).</summary>
        public bool? MissileOnly { get; set; }

        public FlankSide? Side { get; set; }

        /// <summary>Screen: distance kept between rear guard and protected formation.</summary>
        public float? GapMeters { get; set; }

        /// <summary>Follow: offset relative to the followed formation.</summary>
        public float? OffsetForwardMeters { get; set; }
        public float? OffsetRightMeters { get; set; }

        /// <summary>PullBack: keep facing the enemy while withdrawing.</summary>
        public bool? MaintainFacing { get; set; }

        /// <summary>FireControl directive, or fire policy attached to another directive.</summary>
        public FireMode? Fire { get; set; }
    }
}
