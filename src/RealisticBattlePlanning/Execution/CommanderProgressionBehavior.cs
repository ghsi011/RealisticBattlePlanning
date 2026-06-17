using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using RealisticBattlePlanning.Diagnostics;
using RealisticBattlePlanning.Progression;
using RealisticBattlePlanning.Serialization;

namespace RealisticBattlePlanning.Execution
{
    /// <summary>
    /// The campaign-scoped home of D4 commander progression: it owns the
    /// engine-free <see cref="ProgressionService"/> across battles, save-persists
    /// its record book (G1), and forgets a commander when they die (D4 — knowledge
    /// lives in people). All the logic lives in Core and is unit-tested without the
    /// game; this behavior is the thin campaign feeder.
    ///
    /// Only present in a campaign. Custom Battle and the harness run with no such
    /// behavior, so <see cref="Current"/> is null there, every captain resolves to
    /// a None key, and nothing accrues or persists — the no-progression path needs
    /// no special-casing.
    /// </summary>
    public sealed class CommanderProgressionBehavior : CampaignBehaviorBase
    {
        // Versioned so a future record-shape change can migrate rather than
        // silently mis-deserialize an old save's blob.
        private const string SaveKey = "RBP_CommanderRecords_v1";

        private readonly ProgressionService _service = new ProgressionService();

        /// <summary>The progression service for the mission layer to feed and query.</summary>
        public ProgressionService Service => _service;

        /// <summary>
        /// The live behavior for this campaign, or null outside a campaign (Custom
        /// Battle / harness) — the mission layer treats null as "no progression".
        /// </summary>
        public static CommanderProgressionBehavior Current
            => Campaign.Current?.GetCampaignBehavior<CommanderProgressionBehavior>();

        public override void RegisterEvents()
        {
            // Death loses the record (D4). Non-serialized: the listener is re-added
            // every load, never itself saved.
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (victim == null)
                return;
            try
            {
                _service.OnCommanderLost(new CommanderKey(victim.StringId));
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] Commander-lost handler failed.", e);
            }
        }

        /// <summary>
        /// G1 persistence. The record book is a plain string-&gt;POCO dictionary, so
        /// it round-trips as a single JSON string — bulletproof through the save
        /// system (strings persist natively) and free of a SaveableTypeDefiner just
        /// to move a few floats per hero. A corrupt or unreadable blob degrades to
        /// an empty book rather than taking the save down.
        /// </summary>
        public override void SyncData(IDataStore dataStore)
        {
            try
            {
                if (dataStore.IsSaving)
                {
                    var json = SerializeBook();
                    dataStore.SyncData(SaveKey, ref json);
                }
                else if (dataStore.IsLoading)
                {
                    string json = null;
                    dataStore.SyncData(SaveKey, ref json);
                    LoadBook(json);
                }
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] Commander-progression SyncData failed; records reset for safety.", e);
            }
        }

        // Snapshot() is a shallow copy — the dictionary is fresh but the
        // CommanderRecord objects are shared with the live book. Safe because the
        // save runs synchronously on the main thread, the same thread the mission
        // tick mutates records on, so serialization never interleaves with a write.
        // If an async-save path is ever added, deep-copy here.
        private string SerializeBook()
            => JsonConvert.SerializeObject(_service.Book.Snapshot(), JsonDialect.Lenient);

        private void LoadBook(string json)
        {
            if (string.IsNullOrEmpty(json))
                return;
            if (!JsonDialect.TryDeserialize<Dictionary<string, CommanderRecord>>(json, JsonDialect.Lenient, out var records, out var error))
            {
                RbpLog.Warn($"Commander records could not be read ({error}); starting fresh.");
                return;
            }
            foreach (var kv in records)
                _service.Book.Load(kv.Key, kv.Value);
            RbpLog.Info($"Loaded {records.Count} commander record(s) from the save.");
        }
    }
}
