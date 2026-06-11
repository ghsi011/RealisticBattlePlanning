using System.Collections.Generic;
using RealisticBattlePlanning.Planning.Model;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace RealisticBattlePlanning.Execution
{
    /// <summary>
    /// Eagerly-captured implementation of the Core snapshot interfaces: all
    /// engine reads happen here, once per monitor tick, so Core never touches
    /// a live Mission object.
    /// </summary>
    internal sealed class MissionSnapshot : IBattlefieldSnapshot
    {
        private readonly Dictionary<PlannedFormationClass, FormationSnapshot> _own = new();
        private readonly List<IEnemyFormationSnapshot> _enemies = new();

        public float TimeSeconds { get; private set; }
        public bool BattleStarted { get; private set; }
        public MapVec AttackDirection { get; private set; }
        public MapVec TeamCenter { get; private set; }
        public MapVec? PlayerPosition { get; private set; }
        public IReadOnlyList<IEnemyFormationSnapshot> Enemies => _enemies;

        public IFormationSnapshot GetOwn(PlannedFormationClass formationClass)
            => _own.TryGetValue(formationClass, out var snapshot) ? snapshot : null;

        public static MissionSnapshot Capture(
            Mission mission,
            bool battleStarted,
            IReadOnlyDictionary<PlannedFormationClass, int> initialCounts)
        {
            var snapshot = new MissionSnapshot
            {
                TimeSeconds = mission.CurrentTime,
                BattleStarted = battleStarted,
            };

            var team = mission.PlayerTeam;
            if (team == null)
                return snapshot;

            snapshot.TeamCenter = ToMapVec(team.QuerySystem.AveragePosition);

            var mainAgent = mission.MainAgent;
            if (mainAgent != null)
                snapshot.PlayerPosition = new MapVec(mainAgent.Position.x, mainAgent.Position.y);

            var enemyTeam = mission.PlayerEnemyTeam;
            snapshot.AttackDirection = enemyTeam != null
                ? (ToMapVec(enemyTeam.QuerySystem.AveragePosition) - snapshot.TeamCenter).Normalized()
                : new MapVec(0f, 1f);

            foreach (var (planned, engine) in FormationClassMap.All)
            {
                var formation = team.GetFormation(engine);
                if (formation is not { CountOfUnits: > 0 })
                    continue;

                var casualties = 0f;
                if (initialCounts != null
                    && initialCounts.TryGetValue(planned, out var initial)
                    && initial > 0)
                {
                    casualties = 100f * (initial - formation.CountOfUnits) / initial;
                }

                snapshot._own[planned] = new FormationSnapshot(planned, ToMapVec(formation.CurrentPosition), casualties);
            }

            foreach (var otherTeam in mission.Teams)
            {
                if (!otherTeam.IsEnemyOf(team))
                    continue;

                foreach (var formation in otherTeam.FormationsIncludingEmpty)
                {
                    if (formation.CountOfUnits == 0)
                        continue;

                    snapshot._enemies.Add(new EnemyFormationSnapshot(
                        id: otherTeam.TeamIndex * 16 + (int)formation.FormationIndex,
                        formationClass: MapClass(formation.FormationIndex),
                        position: ToMapVec(formation.CurrentPosition),
                        isBroken: IsBroken(formation)));
                }
            }

            return snapshot;
        }

        /// <summary>Broken = majority of units routing (the BattleEndLogic victory semantic).</summary>
        private static bool IsBroken(Formation formation)
        {
            var runningAway = 0;
            formation.ApplyActionOnEachUnit(agent =>
            {
                if (agent.IsRunningAway)
                    runningAway++;
            });
            return runningAway >= formation.CountOfUnits * TriggerDefaults.BrokenRunningAwayFraction;
        }

        private static PlannedFormationClass? MapClass(FormationClass formationClass) => formationClass switch
        {
            FormationClass.Infantry => PlannedFormationClass.Infantry,
            FormationClass.Ranged => PlannedFormationClass.Ranged,
            FormationClass.Cavalry => PlannedFormationClass.Cavalry,
            FormationClass.HorseArcher => PlannedFormationClass.HorseArcher,
            _ => null,
        };

        private static MapVec ToMapVec(TaleWorlds.Library.Vec2 v) => new(v.x, v.y);

        private sealed class FormationSnapshot : IFormationSnapshot
        {
            public FormationSnapshot(PlannedFormationClass formationClass, MapVec position, float casualtiesPercent)
            {
                Class = formationClass;
                Position = position;
                CasualtiesPercent = casualtiesPercent;
            }

            public PlannedFormationClass Class { get; }
            public bool Exists => true;
            public MapVec Position { get; }
            public float CasualtiesPercent { get; }
        }

        private sealed class EnemyFormationSnapshot : IEnemyFormationSnapshot
        {
            public EnemyFormationSnapshot(int id, PlannedFormationClass? formationClass, MapVec position, bool isBroken)
            {
                Id = id;
                Class = formationClass;
                Position = position;
                IsBroken = isBroken;
            }

            public int Id { get; }
            public PlannedFormationClass? Class { get; }
            public MapVec Position { get; }
            public bool IsBroken { get; }
        }
    }

    /// <summary>The four plannable classes mapped to engine formation slots.</summary>
    internal static class FormationClassMap
    {
        public static readonly (PlannedFormationClass Planned, FormationClass Engine)[] All =
        {
            (PlannedFormationClass.Infantry, FormationClass.Infantry),
            (PlannedFormationClass.Ranged, FormationClass.Ranged),
            (PlannedFormationClass.Cavalry, FormationClass.Cavalry),
            (PlannedFormationClass.HorseArcher, FormationClass.HorseArcher),
        };

        public static FormationClass ToEngine(PlannedFormationClass planned)
        {
            foreach (var (p, e) in All)
            {
                if (p == planned)
                    return e;
            }
            return FormationClass.Infantry;
        }
    }
}
