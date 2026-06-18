using System;
using System.Collections.Generic;
using ModDebugKit.Battles;
using ModDebugKit.Diagnostics;
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
