using System;
using Bannerlord.UIExtenderEx;
using HarmonyLib;
using RealisticBattlePlanning.Diagnostics;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Harness;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ModuleManager;
using TaleWorlds.MountAndBlade;

namespace RealisticBattlePlanning
{
    public class SubModule : MBSubModuleBase
    {
        public const string ModId = "RealisticBattlePlanning";
        public const string ModName = "Realistic Battle Planning";

        private static readonly UIExtender UIExtender = UIExtender.Create(ModId);
        private static readonly Harmony Harmony = new(ModId);

        private bool _loadedToastShown;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            RbpLog.MirrorSink = message => Debug.Print(message);
            RbpLog.Init(ModuleHelper.GetModuleFullPath(ModId));
            RbpLog.Info("OnSubModuleLoad");

            try
            {
                EngineContract.VerifyAtLoad();
            }
            catch (Exception e)
            {
                RbpLog.Error("Engine contract check itself failed.", e);
            }

            Harmony.PatchAll(typeof(SubModule).Assembly);

            UIExtender.Register(typeof(SubModule).Assembly);
            UIExtender.Enable();
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);
            try
            {
                if (PlannableMission.CheckOnAttach(mission, out var reason))
                {
                    mission.AddMissionBehavior(new PlanMissionLogic());
                    // Added after PlanMissionLogic so its AfterStart sees the
                    // plan logic already initialized.
                    if (HarnessSession.IsArmed)
                    {
                        mission.AddMissionBehavior(new HarnessRecorderLogic());
                    }
                    else
                    {
                        // The Planning Mode UI is for interactive play, not the
                        // fast-forwarded harness. A MissionView is a
                        // MissionBehavior; the screen registers it at
                        // OnMissionAfterStarting (verified in decompiled
                        // MissionScreen).
                        mission.AddMissionBehavior(new UI.PlanningModeView());
                    }
                }
                else
                {
                    RbpLog.Info($"Not attaching to mission (scene '{mission.SceneName}'): {reason}.");
                }
            }
            catch (Exception e)
            {
                RbpLog.Error("OnMissionBehaviorInitialize failed.", e);
            }
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            if (_loadedToastShown) return;
            _loadedToastShown = true;
            InformationManager.DisplayMessage(
                new InformationMessage($"{ModName} loaded.", Colors.Green));
        }
    }
}
