using System.IO;

namespace ModDebugKit.Io
{
    /// <summary>
    /// The on-disk IO protocol: every path the kit reads or writes, derived
    /// from one output root. Engine-free and pure so the layout is defined in
    /// exactly one place and the agent (and tests) can rely on it. Schemas are
    /// documented in <c>docs/mod-debug-kit-io.md</c>.
    /// </summary>
    public sealed class DbgPaths
    {
        public DbgPaths(string root)
        {
            Root = root;
        }

        /// <summary>The Debug output root, e.g. <c>Modules/ModDebugKit/Debug</c>.</summary>
        public string Root { get; }

        public string IoDir => Path.Combine(Root, "io");

        /// <summary>Append-only command input: each line is one <c>ns.command args…</c>.</summary>
        public string CommandIn => Path.Combine(IoDir, "in.txt");

        /// <summary>One JSON result object per line (JSONL), one per executed command.</summary>
        public string CommandOut => Path.Combine(IoDir, "out.jsonl");

        /// <summary>Default destination for <c>dbg.snapshot</c>.</summary>
        public string BattleState => Path.Combine(Root, "battle_state.json");

        /// <summary>Structured end-of-battle outcome, overwritten at every mission end:
        /// result + per-formation final counts/casualties. One Read replaces parsing
        /// telemetry.jsonl for "who won and at what cost".</summary>
        public string BattleResult => Path.Combine(Root, "battle_result.json");

        /// <summary>Continuous flight-recorder stream (M2).</summary>
        public string Telemetry => Path.Combine(Root, "telemetry.jsonl");

        /// <summary>Captured exceptions with stack traces (M2).</summary>
        public string Errors => Path.Combine(Root, "errors.jsonl");

        /// <summary>Battle snapshot auto-written on the first captured fault.</summary>
        public string ErrorSnapshot => Path.Combine(Root, "error_snapshot.json");

        /// <summary>Default destination for <c>dbg.camp.status</c>.</summary>
        public string CampaignState => Path.Combine(Root, "campaign_state.json");

        public string Log => Path.Combine(Root, "moddebugkit.log");

        public string ShotsDir => Path.Combine(Root, "shots");

        /// <summary>Battle/scenario preset library; <c>dbg.battle &lt;name&gt;</c> reads <c>presets/&lt;name&gt;.json</c>.</summary>
        public string PresetsDir => Path.Combine(Root, "presets");

        /// <summary>Script library; <c>dbg.run &lt;name&gt;</c> reads <c>scripts/&lt;name&gt;.json</c>.</summary>
        public string ScriptsDir => Path.Combine(Root, "scripts");

        /// <summary>Resolve a caller-supplied path: absolute as-is, otherwise relative to the root.</summary>
        public string Resolve(string maybeRelative)
        {
            if (string.IsNullOrWhiteSpace(maybeRelative))
                return null;
            return Path.IsPathRooted(maybeRelative)
                ? maybeRelative
                : Path.Combine(Root, maybeRelative);
        }
    }
}
