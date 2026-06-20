using System;
using System.Collections.Generic;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Planning
{
    /// <summary>
    /// The signals the in-battle numpad palette can fire. Built from the union of the plan's declared
    /// <see cref="BattlePlan.PlayerSignals"/> and every signal a stage trigger actually references
    /// (PlayerSignal or SignalReceived) — so a signal you wired into a stage is pressable even if you
    /// never added it to playerSignals. Without this, a referenced-but-undeclared signal (e.g. the
    /// "advance" default of a SignalReceived trigger) had no key, so the numpad couldn't fire it and
    /// only the dev console could. Deduplicated case-insensitively, declared signals first (stable key
    /// order), then referenced signals in plan/stage order.
    /// </summary>
    public static class SignalPalette
    {
        /// <summary>The ordered, de-duplicated signal names this plan exposes to the palette (uncapped;
        /// the caller binds the first N to its N keys and reports any overflow).</summary>
        public static IReadOnlyList<string> Resolve(BattlePlan plan)
        {
            var result = new List<string>();
            if (plan == null)
                return result;

            void TryAdd(string signal)
            {
                if (string.IsNullOrWhiteSpace(signal))
                    return;
                foreach (var existing in result)
                    if (string.Equals(existing, signal, StringComparison.OrdinalIgnoreCase))
                        return;
                result.Add(signal);
            }

            foreach (var declared in plan.PlayerSignals)
                TryAdd(declared);

            foreach (var formation in plan.Formations)
            {
                if (formation?.Stages == null)
                    continue;
                foreach (var stage in formation.Stages)
                {
                    if (stage?.When == null)
                        continue;
                    foreach (var trigger in stage.When)
                        if (trigger != null
                            && (trigger.Type == TriggerType.PlayerSignal || trigger.Type == TriggerType.SignalReceived))
                            TryAdd(trigger.Signal);
                }
            }

            return result;
        }
    }
}
