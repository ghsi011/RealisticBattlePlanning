using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ModDebugKit.Battles;
using ModDebugKit.Diagnostics;
using ModDebugKit.Io;
using TaleWorlds.MountAndBlade;

namespace ModDebugKit.Observability
{
    /// <summary>
    /// Per-mission anchor for the kit's observability. For M0/M1 it records each
    /// formation's unit count at deployment so <c>dbg.snapshot</c> can report
    /// casualties, and exposes whether deployment has finished. Attached to
    /// every mission by the SubModule; this is also where the M2 telemetry
    /// hooks will hang off. Engine reads only — must never throw into the game.
    /// </summary>
    public sealed class DebugMissionObserver : MissionLogic
    {
        // Resolve from the live mission rather than tracking a static across the behavior
        // lifecycle: OnBehaviorInitialize isn't reliably called for an added behavior, so a
        // field-assignment there silently never fires (it bit us once). GetMissionBehavior
        // is the robust pattern (cf. RBP's PlanMissionLogic.Active).
        public static DebugMissionObserver Active => Mission.Current?.GetMissionBehavior<DebugMissionObserver>();

        // Keyed by team index * 16 + formation slot index (a team has at most NumberOfAllFormations slots).
        private readonly Dictionary<int, int> _initialCounts = new();

        /// <summary>True once deployment has finished and the battle proper is running.</summary>
        public bool Deployed { get; private set; }

        // Mission-scoped queue of formation assignments requested during the deployment
        // phase, where the engine's auto-sort reverts an immediate reassignment. They are
        // applied here at OnDeploymentFinished (before the baseline capture, so casualties
        // resolve against the laid-out slots), so dbg.assign/dbg.layout "just work" without
        // the caller having to sequence them after dbg.ready.
        private readonly List<(AgentSelector Selector, int Number)> _pendingLayout = new();

        public void EnqueueLayout(AgentSelector selector, int number) => _pendingLayout.Add((selector, number));

        public override void OnDeploymentFinished()
        {
            base.OnDeploymentFinished();
            Deployed = true;
            ApplyPendingLayout();
            CaptureBaseline();
        }

        private void ApplyPendingLayout()
        {
            if (_pendingLayout.Count == 0)
                return;
            try
            {
                var team = Mission.PlayerTeam;
                if (team != null)
                {
                    var moves = 0;
                    foreach (var (selector, number) in _pendingLayout)
                        moves += FormationCommands.MoveToFormation(team, selector, number);
                    DbgLog.Info($"Observer: applied {_pendingLayout.Count} deferred layout assignment(s) at deployment finish ({moves} unit-move(s)).");
                }
            }
            catch (Exception e)
            {
                DbgLog.Error("Observer: applying the deferred layout failed.", e);
            }
            finally
            {
                _pendingLayout.Clear();
            }
        }

        // dbg.track: sample formations into a time-series (track.jsonl) over a window, so a
        // whole maneuver (move -> flank -> charge, or an orbit) is captured in one command
        // instead of a manual wait+snapshot loop. Sampling is best-effort; any fault stops it.
        private bool _tracking;
        private float _trackEndsAt, _trackInterval, _trackNextSampleAt;
        private string _trackFilter = "player", _trackPath;

        public void StartTrack(float duration, float interval, string filter, string path)
        {
            _trackInterval = interval < 0.1f ? 0.1f : interval;
            _trackFilter = filter;
            _trackPath = path;
            _trackEndsAt = Mission.CurrentTime + duration;
            _trackNextSampleAt = Mission.CurrentTime;  // first sample on the next tick
            _tracking = true;
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (!_tracking)
                return;
            try
            {
                var now = Mission.CurrentTime;
                if (now >= _trackNextSampleAt)
                {
                    SampleTrack(now);
                    _trackNextSampleAt += _trackInterval;
                }
                if (now >= _trackEndsAt)
                    _tracking = false;
            }
            catch (Exception e)
            {
                _tracking = false;
                DbgLog.Error("Track: sampling failed; tracking stopped.", e);
            }
        }

        private void SampleTrack(float now)
        {
            var dto = BattleSnapshotReader.Capture(Mission);
            var sb = new StringBuilder();
            foreach (var f in dto.Formations)
            {
                if (_trackFilter != "all" && !string.Equals(f.Side, _trackFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
                sb.AppendLine(DbgJson.Line(new
                {
                    t = now,
                    n = f.Number,
                    side = f.Side,
                    comp = f.Composition?.Label,
                    order = f.Order?.Type,
                    x = f.Position?.X,
                    y = f.Position?.Y,
                    cas = f.CasualtiesPercent,
                }));
            }
            if (sb.Length > 0)
                File.AppendAllText(_trackPath, sb.ToString());
        }

        public int? InitialCount(Team team, Formation formation)
        {
            if (team == null || formation == null)
                return null;
            return _initialCounts.TryGetValue(Key(team, formation), out var count) ? count : (int?)null;
        }

        private void CaptureBaseline()
        {
            try
            {
                _initialCounts.Clear();
                foreach (var team in Mission.Teams)
                {
                    foreach (var formation in team.FormationsIncludingEmpty)
                    {
                        if (formation.CountOfUnits > 0)
                            _initialCounts[Key(team, formation)] = formation.CountOfUnits;
                    }
                }
                DbgLog.Info($"Observer: captured deployment baseline for {_initialCounts.Count} formation(s).");
            }
            catch (Exception e)
            {
                DbgLog.Error("Observer: capturing the deployment baseline failed; casualties will read as unknown.", e);
            }
        }

        private static int Key(Team team, Formation formation) =>
            team.TeamIndex * 16 + (int)formation.FormationIndex;
    }
}
