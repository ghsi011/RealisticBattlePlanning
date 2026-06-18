using System.Globalization;
using System.Linq;
using ModDebugKit.Commands;
using TaleWorlds.MountAndBlade;

namespace ModDebugKit.Determinism
{
    /// <summary>
    /// Time and AI control so a battle can be inspected deterministically:
    /// pause/resume/step the mission clock and freeze a side's AI. With these an
    /// agent can stop the battle, snapshot, step a beat, and snapshot again —
    /// reading exactly what changed.
    ///
    /// Mission time is request-based: the engine sets <c>Scene.TimeSpeed</c>
    /// each tick to the MINIMUM of 1 and all <c>AddTimeSpeedRequest</c> values
    /// (so a direct <c>Scene.TimeSpeed = 0</c> is overwritten — that's why this
    /// uses a time-speed request). Requests can only slow/pause (≤1); faster
    /// than normal is the separate <c>SetFastForwardingFromUI</c> path.
    /// </summary>
    public static class DeterminismControls
    {
        // Distinctive request id ("MDK") so it doesn't collide with the game's own requests.
        private const int RequestId = 0x4D_44_4B;
        private const float DefaultStepSeconds = 0.5f;

        // dbg.step runs the clock at normal speed until a target mission time, then re-pauses.
        private static bool _stepping;
        private static float _stepUntilMissionTime;

        // Mission.RemoveTimeSpeedRequest does RemoveAt(-1) when the id is absent (no guard of its
        // own), so we track whether OUR request is live and only remove it when it is. Reset when
        // the mission changes — a new mission carries none of our requests.
        private static Mission _trackedMission;
        private static bool _requestActive;

        public static void RegisterAll(CommandDispatcher dispatcher)
        {
            dispatcher.Register("dbg.pause", "dbg.pause - freeze mission time", Pause);
            dispatcher.Register("dbg.resume", "dbg.resume - resume normal mission time", Resume);
            dispatcher.Register("dbg.timescale", "dbg.timescale <x> - mission time speed (0=pause, <1 slow-mo, 1 normal, >1 fast-forward)", Timescale);
            dispatcher.Register("dbg.step", "dbg.step [seconds] - advance ~N mission-seconds then pause (default 0.5)", Step);
            dispatcher.Register("dbg.freeze", "dbg.freeze <enemy|all|player|none> - pause/unpause AI for a side (none = unpause all)", Freeze);
        }

        /// <summary>Polled from OnApplicationTick to end a step once the target mission time is reached.</summary>
        public static void Tick()
        {
            if (!_stepping)
                return;
            var mission = Mission.Current;
            if (mission?.Scene == null)
            {
                _stepping = false;
                return;
            }
            // A mission swap cancels an in-progress step explicitly, rather than relying on the
            // teardown null-Scene window to clear it (SyncMission no-ops when the mission is the same).
            SyncMission(mission);
            if (!_stepping)
                return;
            if (mission.CurrentTime >= _stepUntilMissionTime)
            {
                ApplyRequest(mission, 0f); // re-pause
                _stepping = false;
            }
        }

        private static DbgOutcome Pause(DbgCommand command)
        {
            var mission = Mission.Current;
            if (mission?.Scene == null)
                return DbgOutcome.Failure("no active mission");
            _stepping = false;
            ApplyRequest(mission, 0f);
            return DbgOutcome.Success("paused");
        }

        private static DbgOutcome Resume(DbgCommand command)
        {
            var mission = Mission.Current;
            if (mission?.Scene == null)
                return DbgOutcome.Failure("no active mission");
            _stepping = false;
            ClearRequest(mission);
            mission.SetFastForwardingFromUI(false);
            return DbgOutcome.Success("resumed (normal speed)");
        }

        private static DbgOutcome Timescale(DbgCommand command)
        {
            var mission = Mission.Current;
            if (mission?.Scene == null)
                return DbgOutcome.Failure("no active mission");
            if (!float.TryParse(command.Arg(0), NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) || scale < 0f)
                return DbgOutcome.Failure("usage: dbg.timescale <x>  (x >= 0; 0=pause, <1 slow-mo, 1 normal, >1 fast-forward)");

            _stepping = false;
            if (scale > 1f)
            {
                // The request system caps at 1; faster-than-normal is the fast-forward path
                // (a fixed engine speed, not an exact multiplier).
                ClearRequest(mission);
                mission.SetFastForwardingFromUI(true);
                return DbgOutcome.Success("fast-forwarding (engine fast speed)", new { fastForward = true });
            }

            mission.SetFastForwardingFromUI(false);
            ApplyRequest(mission, scale);
            return DbgOutcome.Success($"timescale {scale:0.##}", new { timeSpeed = scale });
        }

        private static DbgOutcome Step(DbgCommand command)
        {
            var mission = Mission.Current;
            if (mission?.Scene == null)
                return DbgOutcome.Failure("no active mission");

            var seconds = DefaultStepSeconds;
            if (command.Arg(0) != null && (!float.TryParse(command.Arg(0), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) || seconds <= 0f))
                return DbgOutcome.Failure("usage: dbg.step [seconds > 0]");

            // Run at normal speed (clear any pause), then Tick re-pauses at the target time.
            ClearRequest(mission);
            mission.SetFastForwardingFromUI(false);
            _stepUntilMissionTime = mission.CurrentTime + seconds;
            _stepping = true;
            return DbgOutcome.Success($"stepping {seconds:0.##}s of mission time, then pausing", new { seconds });
        }

        private static DbgOutcome Freeze(DbgCommand command)
        {
            var mission = Mission.Current;
            if (mission == null)
                return DbgOutcome.Failure("no active mission");

            var who = command.Arg(0)?.Trim().ToLowerInvariant();
            bool pause;
            switch (who)
            {
                case "all":
                case "enemy":
                case "player":
                    pause = true;
                    break;
                case "none":
                    pause = false;
                    break;
                default:
                    return DbgOutcome.Failure("usage: dbg.freeze <enemy|all|player|none>");
            }

            // "all"/"none" flip the engine's global AI tick — the only thing that reliably stops
            // the enemy general from re-commanding. Per-side freeze can't use it (it's global), so
            // enemy/player are best-effort (the enemy AI may re-path; use "all" or dbg.pause for a
            // hard freeze).
            if (who == "all")
                mission.PauseAITick = true;
            else if (who == "none")
                mission.PauseAITick = false;

            var playerTeam = mission.PlayerTeam;
            var count = 0;
            foreach (var team in mission.Teams)
            {
                if (who == "enemy" && !(playerTeam != null && team.IsEnemyOf(playerTeam)))
                    continue;
                if (who == "player" && team != playerTeam)
                    continue;

                // Pausing AI alone leaves an in-progress move order running, so also stop the
                // formations and drop AI control; unfreeze hands them back to the AI.
                foreach (var formation in team.FormationsIncludingEmpty)
                {
                    if (formation.CountOfUnits <= 0)
                        continue;
                    if (pause)
                        formation.SetMovementOrder(MovementOrder.MovementOrderStop);
                    formation.SetControlledByAI(!pause);
                }

                foreach (var agent in team.ActiveAgents.ToList())
                {
                    if (agent == null || !agent.IsActive())
                        continue;
                    agent.SetIsAIPaused(pause);
                    count++;
                }
            }

            return DbgOutcome.Success($"{(pause ? "froze" : "unfroze")} {count} agent(s) [{who}]", new { frozen = pause, count, who });
        }

        private static void ApplyRequest(Mission mission, float speed)
        {
            SyncMission(mission);
            if (_requestActive)
                mission.RemoveTimeSpeedRequest(RequestId); // dedup: AddTimeSpeedRequest just appends
            mission.AddTimeSpeedRequest(new Mission.TimeSpeedRequest(speed, RequestId));
            _requestActive = true;
        }

        private static void ClearRequest(Mission mission)
        {
            SyncMission(mission);
            if (!_requestActive)
                return;
            mission.RemoveTimeSpeedRequest(RequestId);
            _requestActive = false;
        }

        private static void SyncMission(Mission mission)
        {
            if (ReferenceEquals(mission, _trackedMission))
                return;
            _trackedMission = mission;
            _requestActive = false; // a fresh mission holds none of our requests
            _stepping = false;
        }
    }
}
