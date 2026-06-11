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

        public float TimeSeconds { get; private set; }
        public bool BattleStarted { get; private set; }
        public MapVec AttackDirection { get; private set; }
        public MapVec TeamCenter { get; private set; }

        public IFormationSnapshot GetOwn(PlannedFormationClass formationClass)
            => _own.TryGetValue(formationClass, out var snapshot) ? snapshot : null;

        public static MissionSnapshot Capture(Mission mission, bool battleStarted)
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

            var enemy = mission.PlayerEnemyTeam;
            snapshot.AttackDirection = enemy != null
                ? (ToMapVec(enemy.QuerySystem.AveragePosition) - snapshot.TeamCenter).Normalized()
                : new MapVec(0f, 1f);

            foreach (var (planned, engine) in FormationClassMap.All)
            {
                var formation = team.GetFormation(engine);
                if (formation is { CountOfUnits: > 0 })
                    snapshot._own[planned] = new FormationSnapshot(planned, ToMapVec(formation.CurrentPosition));
            }

            return snapshot;
        }

        private static MapVec ToMapVec(TaleWorlds.Library.Vec2 v) => new(v.x, v.y);

        private sealed class FormationSnapshot : IFormationSnapshot
        {
            public FormationSnapshot(PlannedFormationClass formationClass, MapVec position)
            {
                Class = formationClass;
                Position = position;
            }

            public PlannedFormationClass Class { get; }
            public bool Exists => true;
            public MapVec Position { get; }
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
