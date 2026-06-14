using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.CustomBattle.CustomBattle;

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

            // Some TaleWorlds members we read are public fields, not
            // properties (e.g. Formation.FormationIndex). Field/property access
            // is identical in C#, so accept either — checking only for a
            // property would cry wolf on a member that exists and works.
            void InstanceMember(Type type, string name)
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
                if (type.GetProperty(name, flags) == null && type.GetField(name, flags) == null)
                    failures.Add($"{type.Name}.{name} (instance field/property)");
            }

            void Method(Type type, string name, params Type[] parameters)
            {
                if (type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, parameters, null) == null)
                    failures.Add($"{type.Name}.{name}({string.Join(", ", Array.ConvertAll(parameters, p => p.Name))})");
            }

            void StaticMember(Type type, string name)
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
                // Field, method, OR property: accessors like DefaultSkills.Tactics
                // are static properties, whose getter is "get_Tactics" — a plain
                // GetMethod(name) misses them and reports a false failure.
                if (type.GetField(name, flags) == null
                    && type.GetMethod(name, flags) == null
                    && type.GetProperty(name, flags) == null)
                    failures.Add($"{type.Name}.{name} (static)");
            }

            // Like StaticMember but tolerant of overloads (GetMethod throws on
            // an ambiguous match; GetMethods + name search does not).
            void StaticByName(Type type, string name)
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
                if (type.GetField(name, flags) == null
                    && type.GetProperty(name, flags) == null
                    && !Array.Exists(type.GetMethods(flags), m => m.Name == name))
                    failures.Add($"{type.Name}.{name} (static)");
            }

            void Constructor(Type type, params Type[] parameters)
            {
                if (type.GetConstructor(parameters) == null)
                    failures.Add($"{type.Name}..ctor({string.Join(", ", Array.ConvertAll(parameters, p => p.Name))})");
            }

            // Mission lifecycle & gating (PlannableMission, PlanMissionLogic).
            Method(typeof(Mission), "AddMissionBehavior", typeof(MissionBehavior));
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
            InstanceMember(typeof(Formation), "FormationIndex");
            Method(typeof(Formation), "ApplyActionOnEachUnit", typeof(Action<Agent>), typeof(Agent));
            Property(typeof(Agent), "Position");
            Property(typeof(Agent), "IsRunningAway");
            // Harness troop redistribution (FormationSplitter).
            InstanceMember(typeof(Agent), "IsHuman");
            InstanceMember(typeof(Agent), "Formation");

            // Override detection & plan control (PlanMissionLogic, I7).
            Property(typeof(Team), "PlayerOrderController");
            if (typeof(OrderController).GetEvent("OnOrderIssued") == null)
                failures.Add("OrderController.OnOrderIssued (event)");
            Method(typeof(Agent), "IsActive");
            StaticMember(typeof(InformationManager), "DisplayMessage");

            // Signal Palette keybinds (PlanMissionLogic, I8).
            StaticMember(typeof(TaleWorlds.InputSystem.Input), "IsKeyReleased");

            // Harness recorder + auto-leave (HarnessRecorderLogic).
            Method(typeof(Mission), "SetFastForwardingFromUI", typeof(bool));
            Method(typeof(Mission), "EndMission");
            Property(typeof(MissionResult), "PlayerVictory");
            Property(typeof(MissionResult), "PlayerDefeated");
            Property(typeof(MissionResult), "BattleResolved");
            Property(typeof(MissionResult), "BattleState");
            if (!Array.Exists(typeof(Mission).GetMethods(BindingFlags.Public | BindingFlags.Instance),
                    m => m.Name == "GetMissionBehavior" && m.IsGenericMethod))
                failures.Add("Mission.GetMissionBehavior<T>() (generic method)");

            // Auto-spawn custom field battle (RbpAutoBattleFactory/GameManager,
            // AutoBattleCommands). The CustomBattle assembly ships in the
            // CustomBattle module; these are the exact members we call.
            StaticByName(typeof(MBGameManager), "StartNewGame");
            StaticByName(typeof(CustomBattleHelper), "StartGame");
            StaticByName(typeof(CustomBattleHelper), "PrepareBattleData");
            StaticByName(typeof(CustomBattleHelper), "GetCustomBattleParties");
            StaticMember(typeof(CustomBattleHelper), "DefaultBattleGameTypeStringId");
            StaticMember(typeof(CustomBattleData), "CoreContentDefaultSceneName");

            // Fidelity switch-on: read each captain's competence from vanilla
            // skills (PlanMissionLogic.SetCommanderProfiles).
            InstanceMember(typeof(Agent), "Character");
            Method(typeof(BasicCharacterObject), "GetSkillValue", typeof(SkillObject));
            StaticMember(typeof(DefaultSkills), "Tactics");
            StaticMember(typeof(DefaultSkills), "Leadership");

            // Order issuance (FormationOrderExecutor).
            Method(typeof(Formation), "SetMovementOrder", typeof(MovementOrder));
            Method(typeof(Formation), "SetArrangementOrder", typeof(ArrangementOrder));
            Method(typeof(Formation), "SetFacingOrder", typeof(FacingOrder));
            Method(typeof(Formation), "SetFormOrder", typeof(FormOrder), typeof(bool));
            Method(typeof(Formation), "SetFiringOrder", typeof(FiringOrder));
            Method(typeof(Formation), "SetControlledByAI", typeof(bool), typeof(bool));
            StaticMember(typeof(MovementOrder), "MovementOrderMove");
            StaticMember(typeof(MovementOrder), "MovementOrderCharge");
            StaticMember(typeof(FiringOrder), "FiringOrderFireAtWill");
            StaticMember(typeof(FiringOrder), "FiringOrderHoldYourFire");
            StaticMember(typeof(FacingOrder), "FacingOrderLookAtDirection");
            StaticMember(typeof(ArrangementOrder), "ArrangementOrderLine");
            StaticMember(typeof(ArrangementOrder), "ArrangementOrderShieldWall");
            StaticMember(typeof(ArrangementOrder), "ArrangementOrderLoose");
            StaticMember(typeof(ArrangementOrder), "ArrangementOrderSquare");
            StaticMember(typeof(ArrangementOrder), "ArrangementOrderCircle");
            StaticMember(typeof(FacingOrder), "FacingOrderLookAtEnemy");
            StaticMember(typeof(FormOrder), "FormOrderCustom");
            Constructor(typeof(TaleWorlds.Engine.WorldPosition),
                typeof(TaleWorlds.Engine.Scene), typeof(UIntPtr), typeof(Vec3), typeof(bool));

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
