using System.Collections.Generic;

namespace RealisticBattlePlanning.Planning.Model
{
    /// <summary>
    /// The full battle plan: one FormationPlan per planned formation plus
    /// shared anchors and declared player signals. Mission-scoped, never
    /// saved (spec G1). Plain data — no engine types — so plans can be
    /// authored from JSON and unit-tested without the game.
    /// </summary>
    public sealed class BattlePlan
    {
        public List<FormationPlan> Formations { get; set; } = new();

        public List<MapAnchor> Anchors { get; set; } = new();

        /// <summary>Manual "Go!" signals fireable from the Signal Palette (spec B9). Max 4.</summary>
        public List<string> PlayerSignals { get; set; } = new();
    }

    /// <summary>
    /// The plannable formation slots — the eight vanilla regular
    /// order-of-battle groups (spec §2: a Formation is a Bannerlord battle
    /// formation). The extra four exist so multi-formation maneuvers can
    /// address two formations of the same troop type (A6 fields two
    /// horse-archer and two infantry formations).
    /// </summary>
    public enum PlannedFormationClass
    {
        Infantry,
        Ranged,
        Cavalry,
        HorseArcher,
        Skirmisher,
        HeavyInfantry,
        LightCavalry,
        HeavyCavalry,
    }

    public sealed class FormationPlan
    {
        public PlannedFormationClass Formation { get; set; }

        /// <summary>Stages execute strictly in order (spec A3.3).</summary>
        public List<Stage> Stages { get; set; } = new();

        public AbortConditions Abort { get; set; } = new();
    }

    /// <summary>Per-formation abort conditions with spec defaults (A3.7).</summary>
    public sealed class AbortConditions
    {
        public float CasualtiesAbovePercent { get; set; } = 60f;
        public bool OnCommanderIncapacitated { get; set; } = true;
        public bool OnFormationBroken { get; set; } = true;
    }
}
