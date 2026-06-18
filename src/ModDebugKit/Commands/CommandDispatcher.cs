using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ModDebugKit.Diagnostics;
using TaleWorlds.MountAndBlade;

namespace ModDebugKit.Commands
{
    /// <summary>
    /// The single dispatcher both front-ends share — the file channel and the
    /// in-game console both build a <see cref="DbgCommand"/> and call
    /// <see cref="Execute"/>, so a command behaves identically either way
    /// (console parity). Runs handlers on the calling (main) thread, catches
    /// everything, and stamps each result with a sequence number, wall-clock
    /// time, and the mission time when a mission is live.
    /// </summary>
    public sealed class CommandDispatcher
    {
        private readonly Dictionary<string, Registration> _handlers =
            new(StringComparer.OrdinalIgnoreCase);

        private long _seq;

        public void Register(string fullName, string usage, Func<DbgCommand, DbgOutcome> handler)
        {
            _handlers[fullName] = new Registration(fullName, usage, handler);
        }

        public IReadOnlyList<(string Name, string Usage)> Commands =>
            _handlers.Values.Select(r => (r.Name, r.Usage)).OrderBy(c => c.Name).ToList();

        public DbgResult Execute(DbgCommand command)
        {
            var result = new DbgResult
            {
                Seq = Interlocked.Increment(ref _seq),
                TimestampUtc = DateTime.UtcNow.ToString("o"),
                Command = command.Full,
                Raw = command.Raw,
            };

            try
            {
                var mission = Mission.Current;
                if (mission != null)
                    result.MissionTime = mission.CurrentTime;
            }
            catch (Exception)
            {
                // Time-stamping is best-effort; never let it fail a command.
            }

            if (!_handlers.TryGetValue(command.Full, out var registration))
            {
                result.Ok = false;
                result.Message = $"unknown command '{command.Full}' (try dbg.help)";
                result.Error = result.Message;
                return result;
            }

            try
            {
                var outcome = registration.Handler(command) ?? DbgOutcome.Failure("handler returned null");
                result.Ok = outcome.Ok;
                result.Message = outcome.Message;
                result.Data = outcome.Data;
                if (!outcome.Ok)
                    result.Error = outcome.Message;
            }
            catch (Exception e)
            {
                result.Ok = false;
                result.Message = $"{command.Full} threw: {e.Message}";
                result.Error = e.ToString();
                DbgLog.Error($"Command '{command.Full}' threw.", e);
            }

            return result;
        }

        /// <summary>Build a command from a known full name + args (the console path) and execute it.</summary>
        public DbgResult ExecuteRaw(string fullName, IReadOnlyList<string> args)
        {
            var dot = fullName.IndexOf('.');
            var ns = dot < 0 ? string.Empty : fullName.Substring(0, dot);
            var name = dot < 0 ? fullName : fullName.Substring(dot + 1);
            args ??= new List<string>();
            var raw = args.Count > 0 ? $"{fullName} {string.Join(" ", args)}" : fullName;
            return Execute(new DbgCommand(ns, name, args, raw));
        }

        private readonly struct Registration
        {
            public Registration(string name, string usage, Func<DbgCommand, DbgOutcome> handler)
            {
                Name = name;
                Usage = usage;
                Handler = handler;
            }

            public string Name { get; }
            public string Usage { get; }
            public Func<DbgCommand, DbgOutcome> Handler { get; }
        }
    }
}
