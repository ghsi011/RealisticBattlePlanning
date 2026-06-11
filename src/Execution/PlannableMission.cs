using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.MountAndBlade;

namespace RealisticBattlePlanning.Execution
{
    /// <summary>
    /// Decides whether a mission can carry a battle plan: field battles only —
    /// no sieges, hideouts, or village fights (spec G5). Player-command checks
    /// happen later, in PlanMissionLogic.AfterStart, because teams aren't set
    /// up yet when behaviors are initialized.
    /// </summary>
    public static class PlannableMission
    {
        public static bool Check(Mission mission, out string reason)
        {
            if (!mission.IsFieldBattle)
            {
                reason = "not a field battle";
                return false;
            }

            if (Campaign.Current != null)
            {
                var mapEvent = MobileParty.MainParty?.MapEvent;
                if (mapEvent == null)
                {
                    reason = "campaign mission without a map event";
                    return false;
                }

                if (!mapEvent.IsFieldBattle)
                {
                    reason = $"map event is {mapEvent.EventType}, not a field battle";
                    return false;
                }
            }

            reason = null;
            return true;
        }
    }
}
