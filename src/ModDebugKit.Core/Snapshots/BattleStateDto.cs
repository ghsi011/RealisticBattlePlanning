using System.Collections.Generic;
using Newtonsoft.Json;

namespace ModDebugKit.Snapshots
{
    /// <summary>A 2D map point (engine X/Y; Z is ground height, irrelevant here).</summary>
    public sealed class Vec2Dto
    {
        public Vec2Dto() { }
        public Vec2Dto(float x, float y) { X = x; Y = y; }

        [JsonProperty("x")] public float X { get; set; }
        [JsonProperty("y")] public float Y { get; set; }
    }

    /// <summary>
    /// Live troop composition of a formation — what it actually <em>holds</em>,
    /// independent of its slot's class name. Units are bucketed by mounted ×
    /// shoots; <see cref="Label"/> is the human summary (see
    /// <c>CompositionClassifier</c>). This is the readout that would have
    /// caught the "wrong formation moved" bug.
    /// </summary>
    public sealed class CompositionDto
    {
        [JsonProperty("infantry")] public int Infantry { get; set; }
        [JsonProperty("ranged")] public int Ranged { get; set; }
        [JsonProperty("cavalry")] public int Cavalry { get; set; }
        [JsonProperty("horseArcher")] public int HorseArcher { get; set; }
        [JsonProperty("total")] public int Total { get; set; }
        [JsonProperty("label")] public string Label { get; set; }
    }

    /// <summary>
    /// A formation's current movement order and (for a Move order) its target,
    /// with the nav-mesh-face verdict that makes the silent-ignore bug visible:
    /// <see cref="TargetHasNavMeshFace"/> false means the engine will discard
    /// the move. <see cref="TargetIsValid"/> is the weaker scene+position check.
    /// </summary>
    public sealed class OrderDto
    {
        /// <summary>Movement order enum name (Move, Charge, Stop, Advance, …).</summary>
        [JsonProperty("type")] public string Type { get; set; }

        /// <summary>Resolved move destination; null when the order has no positional target.</summary>
        [JsonProperty("moveTarget", NullValueHandling = NullValueHandling.Ignore)]
        public Vec2Dto MoveTarget { get; set; }

        /// <summary>WorldPosition.IsValid (scene set and position finite).</summary>
        [JsonProperty("targetIsValid", NullValueHandling = NullValueHandling.Ignore)]
        public bool? TargetIsValid { get; set; }

        /// <summary>True when the target resolves to a nav-mesh face the engine can path to.</summary>
        [JsonProperty("targetHasNavMeshFace", NullValueHandling = NullValueHandling.Ignore)]
        public bool? TargetHasNavMeshFace { get; set; }
    }

    public sealed class CaptainDto
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("agentIndex")] public int AgentIndex { get; set; }
        [JsonProperty("active")] public bool Active { get; set; }
    }

    /// <summary>
    /// One formation as seen at capture. Deliberately records the three things
    /// that are <em>not</em> the same (the lesson the kit exists to surface):
    /// <see cref="Number"/>/<see cref="SlotClass"/> (the slot),
    /// <see cref="RepresentativeClass"/> (the engine's dominant-class guess),
    /// and <see cref="Composition"/> (what it actually holds).
    /// </summary>
    public sealed class FormationStateDto
    {
        /// <summary>"player" or "enemy", relative to the player's team.</summary>
        [JsonProperty("side")] public string Side { get; set; }

        [JsonProperty("teamIndex")] public int TeamIndex { get; set; }

        /// <summary>Player-visible formation number 1–8 (FormationClass index + 1).</summary>
        [JsonProperty("number")] public int Number { get; set; }

        /// <summary>The slot's class name (its FormationClass slot, e.g. "Infantry").</summary>
        [JsonProperty("slotClass")] public string SlotClass { get; set; }

        /// <summary>The engine's representative class for the formation's current contents.</summary>
        [JsonProperty("representativeClass")] public string RepresentativeClass { get; set; }

        [JsonProperty("count")] public int Count { get; set; }

        [JsonProperty("composition")] public CompositionDto Composition { get; set; }

        [JsonProperty("position")] public Vec2Dto Position { get; set; }

        /// <summary>Unit facing direction (normalized).</summary>
        [JsonProperty("facing")] public Vec2Dto Facing { get; set; }

        [JsonProperty("order")] public OrderDto Order { get; set; }

        [JsonProperty("captain", NullValueHandling = NullValueHandling.Ignore)]
        public CaptainDto Captain { get; set; }

        /// <summary>Losses since deployment, 0–100; null when no baseline was captured.</summary>
        [JsonProperty("casualtiesPercent", NullValueHandling = NullValueHandling.Ignore)]
        public float? CasualtiesPercent { get; set; }

        /// <summary>Majority of units routing/fleeing.</summary>
        [JsonProperty("broken")] public bool Broken { get; set; }
    }

    /// <summary>
    /// One capture of the whole battlefield, written to <c>battle_state.json</c>
    /// by <c>dbg.snapshot</c>. The agent reads this instead of a screenshot.
    /// </summary>
    public sealed class BattleStateDto
    {
        [JsonProperty("capturedAtUtc")] public string CapturedAtUtc { get; set; }

        [JsonProperty("sceneName")] public string SceneName { get; set; }

        [JsonProperty("missionTime")] public float MissionTime { get; set; }

        /// <summary>True once deployment is over and the battle is running.</summary>
        [JsonProperty("battleStarted")] public bool BattleStarted { get; set; }

        [JsonProperty("playerTeamIndex")] public int PlayerTeamIndex { get; set; }

        [JsonProperty("formations")] public List<FormationStateDto> Formations { get; set; } = new();
    }
}
