using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Execution
{
    /// <summary>
    /// Turns a plan event into the short, commander-attributed battle line the
    /// player hears (spec B11 / R2 / H9: every perceivable plan action gets a
    /// message, and no deviation is silent). Engine-free and deterministic — the
    /// phrasing varies by formation and stage so a battle isn't monotone, but the
    /// same stage always says the same thing (no per-tick rng, reproducible in
    /// tests and seeded replays). The engine resolves the speaker's name and the
    /// directive/orders; this only chooses words.
    ///
    /// Returns null for events that aren't a player-facing bark (internal steering,
    /// waypoint hops, the XP-only StageCompleted) — the caller skips those.
    /// </summary>
    public static class CommanderBarks
    {
        public static string Line(PlanEvent ev, string commanderName)
        {
            if (ev == null)
                return null;
            var who = string.IsNullOrWhiteSpace(commanderName) ? ev.Formation.ToString() : commanderName;
            var body = Body(ev);
            return body == null ? null : $"{who}: {body}";
        }

        private static string Body(PlanEvent ev)
        {
            switch (ev)
            {
                case StageActivated activated:
                    return ActivationLine(activated);

                case StageSkipped skipped:
                    // A skipped stage is a visible deviation from the written plan;
                    // it must be announced with its reason (R2: no silent deviation).
                    return $"{Pick(ev, "skipping ahead", "moving on", "next phase")} — {Humanize(skipped.Reason)}.";

                case PlanSuspended _:
                    return Pick(ev, "holding on your order.", "as you command — off plan.", "your call; I'll hold here.");

                case PlanResumed _:
                    return Pick(ev, "back on the plan.", "resuming as ordered.", "picking the plan back up.");

                case PlanAborted aborted:
                    return $"{Pick(ev, "falling back", "breaking off", "we can't hold")} — {Humanize(aborted.Reason)}.";

                case PlanHolding _:
                    return Pick(ev, "no orders left — holding position.", "nothing more to do; we hold.", "standing fast, awaiting orders.");

                case ChargeOrdered _:
                    return Pick(ev, "flank reached — charge!", "into their flank!", "now — hit them in the flank!");

                case SignalEmitted signal:
                    return $"signalling '{signal.Signal}'.";

                case ReactionDelayed delayed:
                    // Only a green commander's hesitation is perceptible enough to
                    // remark on; a sharp officer just acts (no bark).
                    return HesitationLine(delayed);

                // Internal/steering events and the XP-only StageCompleted carry no bark.
                default:
                    return null;
            }
        }

        private static string ActivationLine(StageActivated activated)
        {
            var spec = activated.Directive?.Spec;
            var type = spec?.Type ?? DirectiveType.Hold;
            switch (type)
            {
                case DirectiveType.Hold:
                    return Pick(activated, "holding position.", "forming up.", "standing fast.");
                case DirectiveType.MoveTo:
                    return Pick(activated, "advancing.", "moving up.", "on the move.");
                case DirectiveType.Skirmish:
                    return Pick(activated, "skirmishing — keeping our distance.", "harassing them.", "loosing and falling back.");
                case DirectiveType.FeignRetreat:
                    return Pick(activated, "feigning retreat — baiting them in.", "pulling back to draw them on.", "giving ground on purpose.");
                case DirectiveType.Charge:
                    return Pick(activated, "charging!", "into them!", "at the gallop — charge!");
                case DirectiveType.FlankArc:
                    var side = spec?.Side?.ToString().ToLowerInvariant() ?? "their";
                    return Pick(activated, $"swinging around the {side} flank.", $"taking the {side} flank.", $"arcing wide, {side} side.");
                case DirectiveType.PullBack:
                    return Pick(activated, "pulling back in order.", "withdrawing — keep it tidy.", "falling back to the line.");
                case DirectiveType.Screen:
                    return Pick(activated, $"screening {Target(spec)}.", $"covering {Target(spec)}.", $"shielding {Target(spec)}.");
                case DirectiveType.Follow:
                    return Pick(activated, $"falling in with {Target(spec)}.", $"following {Target(spec)}.", $"staying on {Target(spec)}.");
                case DirectiveType.FireControl:
                    return spec?.Fire == FireMode.Hold
                        ? Pick(activated, "hold fire!", "shafts down — hold.", "no loosing yet.")
                        : Pick(activated, "loose at will!", "fire as you bear!", "let them have it!");
                default:
                    return "executing orders.";
            }
        }

        private static string HesitationLine(ReactionDelayed delayed)
        {
            switch (delayed.Tier)
            {
                case Fidelity.FidelityTier.Untrained:
                    return Pick(delayed, "...wait, what was the order?", "uh — hold on, sorting it out.", "give me a moment, sir!");
                case Fidelity.FidelityTier.Drilled:
                    return Pick(delayed, "...on it, sir — a moment.", "reading the field — stand by.", "almost — getting them moving.");
                default:
                    return null; // proficient+ react crisply; nothing to remark on.
            }
        }

        private static string Target(DirectiveSpec spec)
            => string.IsNullOrWhiteSpace(spec?.Target) ? "the line" : spec.Target;

        /// <summary>Lowercases a terse reason tag into something a soldier would say.</summary>
        private static string Humanize(string reason)
            => string.IsNullOrWhiteSpace(reason) ? "orders changed" : reason.Trim();

        /// <summary>
        /// Deterministic phrasing pick: stable per (formation, stage) so a given
        /// stage always speaks the same line, while different stages and formations
        /// vary. No rng — keeps seeded replays and tests reproducible.
        /// </summary>
        private static string Pick(PlanEvent ev, params string[] options)
        {
            if (options == null || options.Length == 0)
                return null;
            var stageIndex = ev switch
            {
                StageActivated a => a.StageIndex,
                StageSkipped s => s.StageIndex,
                PlanResumed r => r.StageIndex,
                ReactionDelayed d => d.StageIndex,
                _ => 0,
            };
            var hash = ((int)ev.Formation * 31 + stageIndex) & 0x7fffffff;
            return options[hash % options.Length];
        }
    }
}
