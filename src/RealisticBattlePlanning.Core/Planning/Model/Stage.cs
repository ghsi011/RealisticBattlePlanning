using System.Collections.Generic;

namespace RealisticBattlePlanning.Planning.Model
{
    /// <summary>
    /// A (Trigger → Directive) pair. A formation executes exactly one stage's
    /// directive at a time and advances when the next stage's trigger fires
    /// (spec §2). <see cref="When"/> is an AND of up to 3 atomic conditions
    /// (A3.5); empty on the first stage means "on battle start" (A3.3).
    /// </summary>
    public sealed class Stage
    {
        /// <summary>Optional label used in logs and (later) the HUD/AAR.</summary>
        public string Name { get; set; }

        public List<TriggerSpec> When { get; set; } = new();

        public DirectiveSpec Do { get; set; }

        /// <summary>Signals broadcast when this stage activates (A3.4).</summary>
        public List<string> Emit { get; set; } = new();
    }
}
