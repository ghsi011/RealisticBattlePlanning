using System;
using HarmonyLib;
using RealisticBattlePlanning.Diagnostics;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RealisticBattlePlanning.Patches
{
    /// <summary>
    /// Field-deployment planning lets you place move waypoints out in the field, but the vanilla
    /// deployment camera is hard-clamped to the deployment boundary, so you can't pan far enough
    /// forward to see/place them. The clamp lives in a private <c>MissionScreen.UpdateCamera</c>
    /// with no event or extension point — its only lever is the snap function it calls when the
    /// camera leaves the zone: <see cref="DefaultMissionDeploymentPlan.GetClosestDeploymentBoundaryPosition"/>,
    /// whose sole caller across the whole engine IS that camera clamp (verified in the decompiled
    /// sources). So a postfix here relaxes the camera leash by a fixed margin WITHOUT touching troop
    /// placement (which uses <c>IsPositionInsideDeploymentBoundaries</c>, untouched) — the boundary
    /// itself, and where you may deploy, are unchanged. This is the mod's one Harmony patch because
    /// it's the only place the camera leash can be reached.
    ///
    /// Gated on our <see cref="UI.FieldDeploymentPlanView"/> being attached, so missions without
    /// field planning keep the exact vanilla camera (zero-touch, spec G3).
    /// </summary>
    [HarmonyPatch(typeof(DefaultMissionDeploymentPlan), nameof(DefaultMissionDeploymentPlan.GetClosestDeploymentBoundaryPosition))]
    internal static class DeploymentCameraReachPatch
    {
        // How far past the deployment boundary the camera may roam (meters). "Twice as far into the
        // field" — a deployment zone is ~40-60 m deep, so this roughly doubles the forward reach
        // while the wider mission boundary (clamped first, in UpdateCamera) still stops a fly-off.
        private const float ReachBonusMeters = 80f;

        // ReSharper disable InconsistentNaming
        private static void Postfix(ref Vec2 position, ref Vec2 __result)
        {
            try
            {
                var mission = Mission.Current;
                if (mission == null || mission.GetMissionBehavior<UI.FieldDeploymentPlanView>() == null)
                    return; // field planning not active in this mission — leave the vanilla clamp alone

                // __result is the boundary point closest to the camera's desired spot (position);
                // the overshoot points outward from the zone. Allow it up to ReachBonusMeters, then
                // clamp to boundary + bonus beyond that.
                var outward = position - __result;
                var dist = outward.Length;
                if (dist <= ReachBonusMeters)
                    __result = position;
                else
                    __result = __result + outward * (ReachBonusMeters / dist);
            }
            catch (Exception e)
            {
                RbpLog.Error("[FIELD] deployment-camera reach patch failed.", e);
            }
        }
        // ReSharper restore InconsistentNaming
    }
}
