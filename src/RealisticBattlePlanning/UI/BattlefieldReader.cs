using System;
using System.Collections.Generic;
using RealisticBattlePlanning.Diagnostics;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Planning.Model;
using TaleWorlds.MountAndBlade;

namespace RealisticBattlePlanning.UI
{
    /// <summary>
    /// Deployment-phase battlefield geometry for the map view: each commanded
    /// slot's position, the team centre, the attack direction (toward the enemy),
    /// and the enemy formation positions — all as engine-free <see cref="MapVec"/>
    /// so Core's <see cref="Planning.PlanMapProjection"/> can place them. The
    /// position source matches <see cref="MissionSnapshot"/> (Formation.CurrentPosition)
    /// so the map and the executor agree on where things are.
    /// </summary>
    public sealed class BattlefieldGeometry
    {
        public MapVec TeamCenter;
        public MapVec AttackDirection = new(0f, 1f);
        public readonly Dictionary<PlannedFormationClass, MapVec> FormationPositions = new();
        public readonly List<MapVec> EnemyPositions = new();

        public bool HasFormations => FormationPositions.Count > 0;
    }

    internal static class BattlefieldReader
    {
        /// <summary>Reads the live geometry. Never throws — on any fault returns
        /// what it has so the map degrades to empty instead of taking the editor down.</summary>
        public static BattlefieldGeometry Read(Team team)
        {
            var geo = new BattlefieldGeometry();
            if (team == null)
                return geo;

            try
            {
                var sum = new MapVec(0f, 0f);
                var count = 0;
                foreach (var (planned, engine) in FormationClassMap.All)
                {
                    var formation = team.GetFormation(engine);
                    if (formation is not { CountOfUnits: > 0 })
                        continue;
                    var position = ToMapVec(formation.CurrentPosition);
                    geo.FormationPositions[planned] = position;
                    sum += position;
                    count++;
                }
                if (count > 0)
                    geo.TeamCenter = sum * (1f / count);

                var enemySum = new MapVec(0f, 0f);
                var enemyCount = 0;
                var mission = Mission.Current;
                if (mission != null)
                {
                    foreach (var other in mission.Teams)
                    {
                        if (!other.IsEnemyOf(team))
                            continue;
                        foreach (var formation in other.FormationsIncludingEmpty)
                        {
                            if (formation.CountOfUnits == 0)
                                continue;
                            var position = ToMapVec(formation.CurrentPosition);
                            geo.EnemyPositions.Add(position);
                            enemySum += position;
                            enemyCount++;
                        }
                    }
                }
                if (enemyCount > 0)
                {
                    var enemyCenter = enemySum * (1f / enemyCount);
                    var direction = enemyCenter - geo.TeamCenter;
                    if (direction.Length > 1e-3f)
                        geo.AttackDirection = direction.Normalized();
                }
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] Reading battlefield geometry failed; the map will be empty.", e);
            }

            return geo;
        }

        private static MapVec ToMapVec(TaleWorlds.Library.Vec2 v) => new(v.x, v.y);
    }
}
