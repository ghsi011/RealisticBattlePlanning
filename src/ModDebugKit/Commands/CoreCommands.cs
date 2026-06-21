using System;
using System.Globalization;
using System.IO;
using System.Linq;
using ModDebugKit.Io;
using ModDebugKit.Observability;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ModDebugKit.Commands
{
    /// <summary>
    /// The M0 command set, registered into the shared dispatcher. Every
    /// command here is reachable identically from the file channel and the
    /// console (see <c>ConsoleCommands</c>).
    /// </summary>
    public static class CoreCommands
    {
        private static readonly string[] SideFilters = { "all", "player", "enemy" };

        public static void RegisterAll(CommandDispatcher dispatcher)
        {
            dispatcher.Register("dbg.ping", "dbg.ping - liveness check; reports where the game is and the output root", Ping);
            dispatcher.Register("dbg.help", "dbg.help - list every registered command", _ => Help(dispatcher));
            dispatcher.Register("dbg.snapshot", "dbg.snapshot [path] [all|player|enemy] - write battle state to JSON (default Debug/battle_state.json, all teams)", Snapshot);
            dispatcher.Register("dbg.track", "dbg.track <seconds> [interval=1] [all|player|enemy] - sample formations over time to Debug/track.jsonl", Track);
            dispatcher.Register("dbg.exec", "dbg.exec <console-command> [args...] - run any registered game console command (e.g. rbp.session) and capture its result", Exec);
        }

        /// <summary>
        /// Runs any registered game console command through the engine's own dispatcher
        /// (<see cref="CommandLineFunctionality.CallFunction(string, System.Collections.Generic.List{string}, out bool)"/>)
        /// and captures its return string. Lets the file channel reach another mod's
        /// <c>name.command</c> diagnostics (e.g. RBP's <c>rbp.session</c>/<c>rbp.plan_status</c>)
        /// without console keystrokes. Runs on the main thread like every dbg command.
        /// </summary>
        private static DbgOutcome Exec(DbgCommand command)
        {
            if (command.Args.Count < 1 || string.IsNullOrWhiteSpace(command.Arg(0)))
                return DbgOutcome.Failure("usage: dbg.exec <console-command> [args...]  e.g. dbg.exec rbp.session");

            var name = command.Arg(0);
            var args = command.Args.Skip(1).ToList();

            string result;
            bool found;
            try
            {
                result = CommandLineFunctionality.CallFunction(name, args, out found);
            }
            catch (Exception e)
            {
                return DbgOutcome.Failure($"{name} threw: {e.Message}");
            }

            if (!found)
                return DbgOutcome.Failure($"unknown console command '{name}' (the mod that owns it may not be loaded)");

            return DbgOutcome.Success($"{name} -> {result}", new { command = name, args, result });
        }

        private static DbgOutcome Ping(DbgCommand command)
        {
            var mission = Mission.Current;
            var where = mission != null
                ? $"in mission '{mission.SceneName}' (t={mission.CurrentTime:0.0}s)"
                : Game.Current != null ? "in game, no mission" : "at the menu";

            return DbgOutcome.Success(
                $"pong - ModDebugKit live, {where}",
                new
                {
                    pong = true,
                    outputRoot = ModDebugKitRuntime.Paths.Root,
                    inMission = mission != null,
                    scene = mission?.SceneName,
                });
        }

        private static DbgOutcome Help(CommandDispatcher dispatcher)
        {
            var usages = dispatcher.Commands.Select(c => c.Usage).ToList();
            return DbgOutcome.Success(
                $"{usages.Count} command(s):\n  " + string.Join("\n  ", usages),
                new { commands = usages });
        }

        private static DbgOutcome Snapshot(DbgCommand command)
        {
            var mission = Mission.Current;
            if (mission == null)
                return DbgOutcome.Failure("no active mission to snapshot");

            // Args are an optional path and an optional team filter (all|player|enemy),
            // in either order, so `dbg.snapshot player` and `dbg.snapshot foo.json enemy`
            // both work without a positional clash.
            string path = null, filter = "all";
            foreach (var arg in command.Args)
            {
                if (SideFilters.Contains(arg, StringComparer.OrdinalIgnoreCase))
                    filter = arg.ToLowerInvariant();
                else
                    path = path ?? arg;
            }

            var dto = BattleSnapshotReader.Capture(mission);
            if (filter != "all")
                dto.Formations.RemoveAll(f => !string.Equals(f.Side, filter, StringComparison.OrdinalIgnoreCase));

            var target = path != null
                ? ModDebugKitRuntime.Paths.Resolve(path)
                : ModDebugKitRuntime.Paths.BattleState;

            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(target, DbgJson.Pretty(dto));

            // The out.jsonl line is a small ack; the full picture is in the file the agent then reads.
            return DbgOutcome.Success(
                $"snapshot: {dto.Formations.Count} {filter} formation(s) @ t={dto.MissionTime:0.0}s -> {target}",
                new { path = target, formations = dto.Formations.Count, team = filter, battleStarted = dto.BattleStarted });
        }

        private static DbgOutcome Track(DbgCommand command)
        {
            if (Mission.Current == null)
                return DbgOutcome.Failure("no active mission to track");
            var observer = DebugMissionObserver.Active;
            if (observer == null)
                return DbgOutcome.Failure("no observer on this mission");
            if (command.Args.Count < 1 || !TryFloat(command.Arg(0), out var duration) || duration <= 0)
                return DbgOutcome.Failure("usage: dbg.track <seconds> [interval=1] [all|player|enemy]");

            var interval = 1f;
            var filter = "player";
            foreach (var arg in command.Args.Skip(1))
            {
                if (SideFilters.Contains(arg, StringComparer.OrdinalIgnoreCase))
                    filter = arg.ToLowerInvariant();
                else if (TryFloat(arg, out var iv) && iv > 0)
                    interval = iv;
            }

            var path = ModDebugKitRuntime.Paths.Resolve("track.jsonl");
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, string.Empty);   // truncate any previous track
            observer.StartTrack(duration, interval, filter, path);

            return DbgOutcome.Success(
                $"tracking {filter} formations every {interval:0.#}s for {duration:0.#}s -> {path}",
                new { durationSeconds = duration, intervalSeconds = interval, team = filter, path });
        }

        private static bool TryFloat(string text, out float value)
            => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
