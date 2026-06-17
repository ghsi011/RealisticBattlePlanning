using System.Collections.Generic;
using Newtonsoft.Json;

namespace ModDebugKit.Battles
{
    /// <summary>An exact roster line: this many of this troop id.</summary>
    public sealed class TroopEntry
    {
        public TroopEntry() { }
        public TroopEntry(string troop, int count) { Troop = troop; Count = count; }

        [JsonProperty("troop")] public string Troop { get; set; }
        [JsonProperty("count")] public int Count { get; set; }
    }

    /// <summary>
    /// One side of a battle: who leads it, what culture, and the roster.
    /// Two roster modes: <see cref="Troops"/> gives an <em>exact</em> per-troop
    /// roster ({troop, count}); otherwise <see cref="Counts"/> = [infantry,
    /// ranged, cavalry, horseArcher] fills each class with the culture's
    /// default troop (or the ids in <see cref="TroopsByClass"/>, split across
    /// the count). <see cref="Troops"/> wins when both are present.
    /// <see cref="Heroes"/> adds named characters (one each) on top.
    /// </summary>
    public sealed class SidePreset
    {
        /// <summary>Culture string id, e.g. "empire", "aserai", "battania".</summary>
        [JsonProperty("culture")] public string Culture { get; set; }

        /// <summary>Commander character id (e.g. "commander_1"); the side's general.</summary>
        [JsonProperty("commander")] public string Commander { get; set; }

        /// <summary>[infantry, ranged, cavalry, horseArcher] counts. The +1 commander is added on top.</summary>
        [JsonProperty("counts")] public int[] Counts { get; set; }

        /// <summary>
        /// Optional: troop ids to use for each class bucket (index 0..3 =
        /// inf/rng/cav/ha). The bucket's count is split across the listed ids.
        /// Omitted/empty buckets fall back to the culture's default troop.
        /// Ignored when <see cref="Troops"/> is set.
        /// </summary>
        [JsonProperty("troopsByClass", NullValueHandling = NullValueHandling.Ignore)]
        public List<string>[] TroopsByClass { get; set; }

        /// <summary>Exact roster: each entry adds <c>count</c> of <c>troop</c>. Overrides <see cref="Counts"/>.</summary>
        [JsonProperty("troops", NullValueHandling = NullValueHandling.Ignore)]
        public List<TroopEntry> Troops { get; set; }

        /// <summary>Named characters (companions, lords) added one each, on top of the roster.</summary>
        [JsonProperty("heroes", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Heroes { get; set; }

        /// <summary>True when an exact <see cref="Troops"/> roster is given.</summary>
        [JsonIgnore]
        public bool HasExplicitRoster => Troops != null && Troops.Count > 0;
    }

    /// <summary>
    /// A repeatable battle description: scene, time, player role, and both
    /// sides. Deserialized from a preset file by <c>dbg.battle</c> and turned
    /// into a custom battle by the engine-side BattleFactory. Every field has a
    /// sensible default, so an empty preset (<c>{}</c>) yields a known-good
    /// Empire-vs-Aserai field battle — the same baseline RBP's harness used.
    /// </summary>
    public sealed class BattlePreset
    {
        /// <summary>Scene id; null -> the core default battle terrain.</summary>
        [JsonProperty("scene")] public string Scene { get; set; }

        /// <summary>"summer" | "winter" | "spring" | "fall"; null -> summer.</summary>
        [JsonProperty("season")] public string Season { get; set; }

        /// <summary>Time of day in hours (0-24); null -> 6 (dawn).</summary>
        [JsonProperty("timeOfDay")] public float? TimeOfDay { get; set; }

        /// <summary>"Battle" (field) for now; null -> Battle.</summary>
        [JsonProperty("gameType")] public string GameType { get; set; }

        /// <summary>"Defender" | "Attacker"; null -> Defender.</summary>
        [JsonProperty("playerSide")] public string PlayerSide { get; set; }

        /// <summary>"Commander" | "Sergeant"; null -> Commander. (The engine has no spectator role.)</summary>
        [JsonProperty("playerType")] public string PlayerType { get; set; }

        [JsonProperty("player")] public SidePreset Player { get; set; }

        [JsonProperty("enemy")] public SidePreset Enemy { get; set; }

        /// <summary>
        /// The canonical known-good default: Empire (player, Defender Commander)
        /// vs Aserai, on the core battle terrain. Matches the baseline RBP's
        /// auto-battle used, so a bare <c>dbg.battle</c> always lands a battle.
        /// </summary>
        public static BattlePreset CreateDefault() => new()
        {
            Player = new SidePreset { Culture = "empire", Commander = "commander_1", Counts = new[] { 150, 49, 0, 0 } },
            Enemy = new SidePreset { Culture = "aserai", Commander = "commander_11", Counts = new[] { 120, 40, 40, 0 } },
        };
    }
}
