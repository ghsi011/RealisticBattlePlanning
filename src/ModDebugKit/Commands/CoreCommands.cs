using System.IO;
using System.Linq;
using ModDebugKit.Io;
using ModDebugKit.Observability;
using TaleWorlds.Core;
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
        public static void RegisterAll(CommandDispatcher dispatcher)
        {
            dispatcher.Register("dbg.ping", "dbg.ping - liveness check; reports where the game is and the output root", Ping);
            dispatcher.Register("dbg.help", "dbg.help - list every registered command", _ => Help(dispatcher));
            dispatcher.Register("dbg.snapshot", "dbg.snapshot [path] - write the full battle state to JSON (default Debug/battle_state.json)", Snapshot);
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

            var dto = BattleSnapshotReader.Capture(mission);

            var target = command.Arg(0) != null
                ? ModDebugKitRuntime.Paths.Resolve(command.Arg(0))
                : ModDebugKitRuntime.Paths.BattleState;

            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(target, DbgJson.Pretty(dto));

            // The out.jsonl line is a small ack; the full picture is in the file the agent then reads.
            return DbgOutcome.Success(
                $"snapshot: {dto.Formations.Count} formation(s) @ t={dto.MissionTime:0.0}s -> {target}",
                new { path = target, formations = dto.Formations.Count, battleStarted = dto.BattleStarted });
        }
    }
}
