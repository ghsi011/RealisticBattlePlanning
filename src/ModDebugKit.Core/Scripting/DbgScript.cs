using System.Collections.Generic;
using Newtonsoft.Json;

namespace ModDebugKit.Scripting
{
    /// <summary>
    /// One scripted step: run <see cref="Do"/> (a command line) once
    /// <see cref="At"/> seconds have elapsed since the script started. This is
    /// the timed front-end to the whole command set, so a scenario — build a
    /// battle, ready it, wait, snapshot, leave — is one file.
    /// </summary>
    public sealed class DbgScriptStep
    {
        public DbgScriptStep() { }
        public DbgScriptStep(float at, string @do) { At = at; Do = @do; }

        /// <summary>Seconds since the script started before this step fires (0 = immediately).</summary>
        [JsonProperty("at")] public float At { get; set; }

        /// <summary>A command line, e.g. <c>dbg.snapshot t10.json</c>.</summary>
        [JsonProperty("do")] public string Do { get; set; }
    }

    /// <summary>A named, timed sequence of commands, loaded by <c>dbg.run</c> from <c>scripts/*.json</c>.</summary>
    public sealed class DbgScript
    {
        [JsonProperty("name")] public string Name { get; set; }

        [JsonProperty("steps")] public List<DbgScriptStep> Steps { get; set; } = new();
    }
}
