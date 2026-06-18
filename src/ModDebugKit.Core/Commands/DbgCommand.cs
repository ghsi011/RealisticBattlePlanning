using System.Collections.Generic;

namespace ModDebugKit.Commands
{
    /// <summary>
    /// One parsed command line: <c>namespace.name arg1 arg2 …</c>. The same
    /// shape backs both front-ends — the file channel and the in-game console —
    /// so a command behaves identically whichever way it is issued.
    /// </summary>
    public sealed class DbgCommand
    {
        public DbgCommand(string @namespace, string name, IReadOnlyList<string> args, string raw)
        {
            Namespace = @namespace;
            Name = name;
            Args = args ?? new List<string>();
            Raw = raw;
        }

        /// <summary>Text before the first dot in the head token (e.g. <c>dbg</c>); empty if none.</summary>
        public string Namespace { get; }

        /// <summary>Text after the first dot (e.g. <c>snapshot</c>), or the whole head when there is no dot.</summary>
        public string Name { get; }

        public IReadOnlyList<string> Args { get; }

        /// <summary>The original line, verbatim.</summary>
        public string Raw { get; }

        /// <summary>The dispatch key, e.g. <c>dbg.snapshot</c>.</summary>
        public string Full => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";

        public string Arg(int index) => index >= 0 && index < Args.Count ? Args[index] : null;
    }
}
