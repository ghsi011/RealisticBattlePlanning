using System;
using System.Collections.Generic;

namespace RealisticBattlePlanning.Progression
{
    /// <summary>
    /// The campaign's commander records, keyed by hero id (spec D4/G1). The
    /// engine save-persists this; commanders who leave the clan keep their
    /// data (it returns with them), but death removes it — knowledge lives in
    /// people, so protecting trained officers is a campaign-level incentive.
    /// Engine-free: the engine supplies a stable string id per hero.
    /// </summary>
    public sealed class CommanderRecordBook
    {
        private readonly Dictionary<string, CommanderRecord> _records = new(StringComparer.Ordinal);

        public int Count => _records.Count;

        /// <summary>The record for a hero, created blank on first sight.</summary>
        public CommanderRecord GetOrCreate(string heroId)
        {
            if (heroId == null)
                return new CommanderRecord();
            if (!_records.TryGetValue(heroId, out var record))
                _records[heroId] = record = new CommanderRecord();
            return record;
        }

        public bool TryGet(string heroId, out CommanderRecord record)
        {
            if (heroId != null)
                return _records.TryGetValue(heroId, out record);
            record = null;
            return false;
        }

        /// <summary>Death loses everything (D4): the record is gone, so a new captain starts green.</summary>
        public void Forget(string heroId)
        {
            if (heroId != null)
                _records.Remove(heroId);
        }

        /// <summary>For save round-tripping (G1): the engine reads/writes these.</summary>
        public IReadOnlyDictionary<string, CommanderRecord> All => _records;

        public void Load(string heroId, CommanderRecord record)
        {
            if (heroId != null && record != null)
                _records[heroId] = record;
        }
    }
}
