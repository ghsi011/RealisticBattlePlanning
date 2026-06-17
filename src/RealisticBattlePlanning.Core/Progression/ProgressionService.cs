using System;
using System.Collections.Generic;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Fidelity;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Progression
{
    /// <summary>
    /// Engine-free commander identity: a hero's stable StringId, or <see cref="None"/>
    /// for a captain with no persistent hero (a custom-battle basic troop, or a
    /// captain-less formation). A None key flows through to a throwaway record, so it
    /// accrues nothing and persists nothing — exactly the no-progression custom-battle path.
    /// </summary>
    public readonly struct CommanderKey : IEquatable<CommanderKey>
    {
        public static readonly CommanderKey None = default;

        public CommanderKey(string id) => Id = string.IsNullOrEmpty(id) ? null : id;

        public string Id { get; } // null == None
        public bool IsPersistent => Id != null;

        public bool Equals(CommanderKey other) => string.Equals(Id, other.Id, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is CommanderKey k && Equals(k);
        public override int GetHashCode() => Id == null ? 0 : StringComparer.Ordinal.GetHashCode(Id);
        public override string ToString() => Id ?? "(none)";
    }

    /// <summary>
    /// The D4 progression service: consumes a battle's plan events and turns them into
    /// Plan Familiarity XP per commander, and produces the familiarity-bearing
    /// <see cref="CommanderProfile"/> (the only door that applies the C5 drill cap —
    /// <see cref="ProgressionModel.ProfileFor"/>, never <c>FromStats</c>). Owns a
    /// <see cref="CommanderRecordBook"/>; the engine adapter (PlanMissionLogic) is a
    /// thin feeder — it maps each formation to its captain's hero id, pipes the
    /// monitor's per-tick events in, and reads profiles out. All the logic lives here
    /// and is unit-tested without the game, so the otherwise campaign-only progression
    /// loop is verifiable.
    /// </summary>
    public sealed class ProgressionService
    {
        private readonly CommanderRecordBook _book;

        public ProgressionService(CommanderRecordBook book = null) => _book = book ?? new CommanderRecordBook();

        /// <summary>The live record book, for the persistence layer's Snapshot()/Load() (G1).</summary>
        public CommanderRecordBook Book => _book;

        /// <summary>
        /// Awards/penalizes XP from a batch of monitor events (call per tick or per
        /// battle). Each event's formation is resolved to a commander via
        /// <paramref name="commanders"/> (captured at battle start). A completed stage
        /// earns familiarity; a skipped/aborted stage grants the reduced "lesson
        /// learned" trickle (D4); everything else is inert. <paramref name="inDrill"/>
        /// routes to the accelerated, Proficient-capped drill layer (C4/C5).
        /// </summary>
        public void OnBattleEvents(
            IEnumerable<PlanEvent> events,
            IReadOnlyDictionary<PlannedFormationClass, CommanderKey> commanders,
            bool inDrill = false)
        {
            if (events == null || commanders == null)
                return;

            foreach (var ev in events)
            {
                if (ev == null || !commanders.TryGetValue(ev.Formation, out var key))
                    continue;
                switch (ev)
                {
                    case StageCompleted _:
                        ProgressionModel.OnStageCompleted(_book.GetOrCreate(key.Id), inDrill);
                        break;
                    case StageSkipped _:
                    case PlanAborted _:
                        ProgressionModel.OnStageFailed(_book.GetOrCreate(key.Id));
                        break;
                    // All other events (activated, reaction delay, signal, suspend/
                    // resume, hold, waypoint, steering) carry no XP effect — only a
                    // completed vs skipped/aborted stage moves familiarity (D4).
                }
            }
        }

        /// <summary>The familiarity-bearing profile for a commander (replaces
        /// CommanderProfile.FromStats at the call site). A read, never a write: a
        /// commander with no record yet (or a None key) reads a transient blank, so
        /// it equals the stats-only base WITHOUT inserting an empty record. Records
        /// are born only when something is actually earned (OnBattleEvents /
        /// OnBattleConcluded), so the book stays free of blank entries for every
        /// officer who was ever merely queried.</summary>
        public CommanderProfile ProfileFor(CommanderKey key, int tactics, int leadership)
        {
            var record = _book.TryGet(key.Id, out var existing) ? existing : new CommanderRecord();
            return ProgressionModel.ProfileFor(record, tactics, leadership);
        }

        /// <summary>Commander death loses everything (D4): the record is forgotten, so a
        /// replacement captain starts green. No-op for a None key.</summary>
        public void OnCommanderLost(CommanderKey key) => _book.Forget(key.Id);

        /// <summary>Bumps each persistent commander's service record (D1) once per battle.</summary>
        public void OnBattleConcluded(IReadOnlyDictionary<PlannedFormationClass, CommanderKey> commanders)
        {
            if (commanders == null)
                return;
            var counted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in commanders.Values)
            {
                if (key.Id != null && counted.Add(key.Id))
                    ProgressionModel.OnBattleUnderCommand(_book.GetOrCreate(key.Id));
            }
        }
    }
}
