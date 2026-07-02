using System;
using Bannerlord.UIExtenderEx;
using HarmonyLib;
using RealisticBattlePlanning.Diagnostics;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Harness;
using TaleWorlds.CampaignSystem;
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

            // Vanilla-first (§1.1): the mod has exactly ONE [HarmonyPatch] —
            // Patches/DeploymentCameraReachPatch, which relaxes the deployment-camera
            // leash so you can pan to field-planned waypoints (the camera clamp is a
            // private method with no event/extension point, the only justified patch).
            // Everything else rides vanilla events (e.g. order-override detection uses
            // OrderController.OnOrderIssued, not a patch).
            // Guarded: the patch is a camera-comfort feature the mod runs fine
            // without, so a game update that removes/renames the target must degrade
            // to a readable log line (the contract check's whole point), not a
            // startup crash.
            try
            {
                Harmony.PatchAll(typeof(SubModule).Assembly);
            }
            catch (Exception e)
            {
                RbpLog.Error("Harmony patching failed; deployment-camera reach stays vanilla this session.", e);
            }

            UIExtender.Register(typeof(SubModule).Assembly);
            UIExtender.Enable();
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            try
            {
                // D4 commander progression lives only in a campaign (it persists
                // hero records across battles). Custom Battle / the harness use a
                // CustomGameStarter and get no behavior, so progression stays inert
                // there — exactly the no-campaign-hero path.
                if (gameStarterObject is CampaignGameStarter campaignStarter)
                {
                    campaignStarter.AddBehavior(new CommanderProgressionBehavior());
                    RbpLog.Info("Commander progression behavior registered for this campaign.");
                }
            }
            catch (Exception e)
            {
                RbpLog.Error("OnGameStart: registering the progression behavior failed; D4 progression inert this campaign.", e);
            }
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
                        // Alternative authoring surface: plan move orders on the deployment
                        // field by extending the vanilla placement gesture (field-planning-design.md).
                        mission.AddMissionBehavior(new UI.FieldDeploymentPlanView());
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
