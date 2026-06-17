using System;
using System.Collections.Generic;
using ModDebugKit.Diagnostics;
using TaleWorlds.MountAndBlade;

namespace ModDebugKit.Observability
{
    /// <summary>
    /// Per-mission anchor for the kit's observability. For M0 it records each
    /// formation's unit count at deployment so <c>dbg.snapshot</c> can report
    /// casualties, and exposes whether deployment has finished. Attached to
    /// every mission by the SubModule; this is also where the M2 telemetry
    /// hooks will hang off. Engine reads only — must never throw into the game.
    /// </summary>
    public sealed class DebugMissionObserver : MissionLogic
    {
        /// <summary>The observer for the mission currently running, or null between missions.</summary>
        public static DebugMissionObserver Active { get; private set; }

        // Keyed by team index * 16 + formation slot index (a team has at most NumberOfAllFormations slots).
        private readonly Dictionary<int, int> _initialCounts = new();

        /// <summary>True once deployment has finished and the battle proper is running.</summary>
        public bool Deployed { get; private set; }

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            Active = this;
        }

        public override void OnDeploymentFinished()
        {
            base.OnDeploymentFinished();
            Deployed = true;
            CaptureBaseline();
        }

        public int? InitialCount(Team team, Formation formation)
        {
            if (team == null || formation == null)
                return null;
            return _initialCounts.TryGetValue(Key(team, formation), out var count) ? count : (int?)null;
        }

        protected override void OnEndMission()
        {
            if (Active == this)
                Active = null;
            base.OnEndMission();
        }

        public override void OnRemoveBehavior()
        {
            if (Active == this)
                Active = null;
            base.OnRemoveBehavior();
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
