using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RealisticBattlePlanning.Diagnostics
{
    /// <summary>
    /// Layer-3 patch-survival check (testing architecture): at mod load,
    /// verify every TaleWorlds member we depend on still exists with the
    /// expected shape, and report failures readably instead of crashing
    /// mid-battle after a game patch. Grow this list with every new engine
    /// dependency or Harmony patch.
    /// </summary>
    public static class EngineContract
    {
        public static void VerifyAtLoad()
        {
            var failures = new List<string>();

            void Property(Type type, string name)
            {
                if (type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) == null)
                    failures.Add($"{type.Name}.{name} (property)");
            }

            void Method(Type type, string name, params Type[] parameters)
            {
                if (type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, parameters, null) == null)
                    failures.Add($"{type.Name}.{name}({string.Join(", ", Array.ConvertAll(parameters, p => p.Name))})");
            }

            void StaticMember(Type type, string name)
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
                if (type.GetField(name, flags) == null && type.GetMethod(name, flags) == null)
                    failures.Add($"{type.Name}.{name} (static)");
            }

            // Mission lifecycle & gating (PlannableMission, PlanMissionLogic).
            Property(typeof(Mission), "CurrentTime");
            Property(typeof(Mission), "IsFieldBattle");
            Property(typeof(Mission), "MissionTeamAIType");
            Property(typeof(Mission), "PlayerTeam");
            Property(typeof(Mission), "PlayerEnemyTeam");
            Property(typeof(Mission), "Scene");
            Property(typeof(Mission), "SceneName");
            Property(typeof(Mission), "Mode");

            // Team / formation reads (MissionSnapshot).
            Property(typeof(Mission), "MainAgent");
            Property(typeof(Mission), "Teams");
            Property(typeof(Team), "IsPlayerGeneral");
            Property(typeof(Team), "QuerySystem");
            Property(typeof(Team), "FormationsIncludingEmpty");
            Property(typeof(Team), "TeamIndex");
            Method(typeof(Team), "GetFormation", typeof(TaleWorlds.Core.FormationClass));
            Method(typeof(Team), "IsEnemyOf", typeof(Team));
            Property(typeof(TeamQuerySystem), "AveragePosition");
            Property(typeof(Formation), "CurrentPosition");
            Property(typeof(Formation), "CountOfUnits");
            Property(typeof(Formation), "Captain");
            Method(typeof(Formation), "ApplyActionOnEachUnit", typeof(Action<Agent>), typeof(Agent));
            Property(typeof(Agent), "Position");
            Property(typeof(Agent), "IsRunningAway");

            // Order issuance (FormationOrderExecutor).
            Method(typeof(Formation), "SetMovementOrder", typeof(MovementOrder));
            Method(typeof(Formation), "SetArrangementOrder", typeof(ArrangementOrder));
            Method(typeof(Formation), "SetFacingOrder", typeof(FacingOrder));
            Method(typeof(Formation), "SetFormOrder", typeof(FormOrder), typeof(bool));
            Method(typeof(Formation), "SetControlledByAI", typeof(bool), typeof(bool));
            StaticMember(typeof(MovementOrder), "MovementOrderMove");
            StaticMember(typeof(MovementOrder), "MovementOrderCharge");
            StaticMember(typeof(ArrangementOrder), "ArrangementOrderLine");
            StaticMember(typeof(ArrangementOrder), "ArrangementOrderShieldWall");
            StaticMember(typeof(ArrangementOrder), "ArrangementOrderLoose");
            StaticMember(typeof(ArrangementOrder), "ArrangementOrderSquare");
            StaticMember(typeof(ArrangementOrder), "ArrangementOrderCircle");
            StaticMember(typeof(FacingOrder), "FacingOrderLookAtEnemy");
            StaticMember(typeof(FormOrder), "FormOrderCustom");

            if (failures.Count == 0)
            {
                RbpLog.Info("Engine contract verified.");
            }
            else
            {
                RbpLog.Error(
                    "Engine contract FAILED — the game update likely changed these APIs; " +
                    "plan execution may misbehave or crash:\n  " + string.Join("\n  ", failures));
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{SubModule.ModName}: engine API mismatch after game update — see Logs\\rbp.log.", Colors.Red));
            }
        }
    }
}
