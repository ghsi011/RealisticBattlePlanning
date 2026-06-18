using System;
using ModDebugKit.Diagnostics;
using ModDebugKit.Snapshots;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace ModDebugKit.Observability
{
    /// <summary>
    /// Reads <see cref="Mission.Current"/> into an engine-free
    /// <see cref="BattleStateDto"/>: the whole battlefield, one capture, so the
    /// agent reads JSON instead of a screenshot. All engine access lives here.
    /// Records the three things the kit exists to disambiguate — slot number,
    /// the engine's representative class, and the live composition — and, for a
    /// Move order, whether the target resolves to a nav-mesh face (the
    /// silent-ignore move bug, made visible).
    /// </summary>
    public static class BattleSnapshotReader
    {
        /// <summary>Broken = at least half the units routing (matches the vanilla rout semantic RBP used).</summary>
        private const float BrokenRunningAwayFraction = 0.5f;

        public static BattleStateDto Capture(Mission mission)
        {
            var observer = DebugMissionObserver.Active;
            var playerTeam = mission.PlayerTeam;

            var dto = new BattleStateDto
            {
                CapturedAtUtc = DateTime.UtcNow.ToString("o"),
                SceneName = mission.SceneName,
                MissionTime = mission.CurrentTime,
                BattleStarted = observer?.Deployed ?? (mission.Mode == MissionMode.Battle),
                PlayerTeamIndex = playerTeam?.TeamIndex ?? -1,
            };

            foreach (var team in mission.Teams)
            {
                var side = playerTeam == null ? "other"
                    : team == playerTeam ? "player"
                    : team.IsEnemyOf(playerTeam) ? "enemy"
                    : "other";

                foreach (var formation in team.FormationsIncludingEmpty)
                {
                    if (formation.CountOfUnits == 0)
                        continue;

                    try
                    {
                        dto.Formations.Add(ReadFormation(team, formation, side, observer));
                    }
                    catch (Exception e)
                    {
                        DbgLog.Error($"Snapshot: reading formation {(int)formation.FormationIndex} on team {team.TeamIndex} failed; skipped.", e);
                    }
                }
            }

            return dto;
        }

        private static FormationStateDto ReadFormation(Team team, Formation formation, string side, DebugMissionObserver observer)
        {
            int infantry = 0, ranged = 0, cavalry = 0, horseArcher = 0, runningAway = 0;
            Agent captain = formation.Captain;
            formation.ApplyActionOnEachUnit(agent =>
            {
                if (agent == null)
                    return;
                if (agent.IsRunningAway)
                    runningAway++;

                var mounted = agent.HasMount;
                // IsRangedCached is true only for a real ranged weapon with ammo (bow/crossbow),
                // so javelin/throwing infantry stay "Infantry" rather than counting as "Ranged".
                var shoots = agent.IsRangedCached;
                if (mounted && shoots) horseArcher++;
                else if (mounted) cavalry++;
                else if (shoots) ranged++;
                else infantry++;
            });

            var count = formation.CountOfUnits;

            float? casualties = null;
            var initial = observer?.InitialCount(team, formation);
            if (initial.HasValue && initial.Value > 0)
                casualties = 100f * (initial.Value - count) / initial.Value;

            return new FormationStateDto
            {
                Side = side,
                TeamIndex = team.TeamIndex,
                Number = (int)formation.FormationIndex + 1,
                SlotClass = formation.FormationIndex.ToString(),
                RepresentativeClass = formation.RepresentativeClass.ToString(),
                Count = count,
                Composition = CompositionClassifier.Classify(infantry, ranged, cavalry, horseArcher),
                Position = new Vec2Dto(formation.CurrentPosition.x, formation.CurrentPosition.y),
                Facing = new Vec2Dto(formation.Direction.x, formation.Direction.y),
                Order = OrderInspector.Describe(formation),
                Captain = captain != null
                    ? new CaptainDto { Name = captain.Name, AgentIndex = captain.Index, Active = captain.IsActive() }
                    : null,
                CasualtiesPercent = casualties,
                Broken = count > 0 && runningAway >= count * BrokenRunningAwayFraction,
            };
        }
    }
}
