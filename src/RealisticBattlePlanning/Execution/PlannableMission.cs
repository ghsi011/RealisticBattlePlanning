using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.MountAndBlade;

namespace RealisticBattlePlanning.Execution
{
    /// <summary>
    /// Decides whether a mission can carry a battle plan: field battles only —
    /// no sieges, hideouts, or village fights (spec G5).
    ///
    /// Engine ordering constraint (decompiled Mission.AfterStart): submodules'
    /// OnMissionBehaviorInitialize runs BEFORE behaviors' EarlyStart, and
    /// MissionCombatantsLogic.EarlyStart is what sets Mission.MissionTeamAIType
    /// — so Mission.IsFieldBattle is always false at attach time. The checks
    /// are therefore split: campaign MapEvent at attach time (already set up),
    /// IsFieldBattle and player-command at MissionLogic.AfterStart.
    /// </summary>
    public static class PlannableMission
    {
        /// <summary>Attach-time check. Permissive outside campaigns; AfterStartCheck decides.</summary>
        public static bool CheckOnAttach(Mission mission, out string reason)
        {
            if (Campaign.Current != null)
            {
                var mapEvent = MobileParty.MainParty?.MapEvent;
                if (mapEvent == null)
                {
                    reason = "campaign mission without a map event (town/arena/hideout)";
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

        /// <summary>Mission-state check, valid from MissionLogic.AfterStart onward.</summary>
        public static bool CheckAfterStart(Mission mission, out string reason)
        {
            if (!mission.IsFieldBattle)
            {
                reason = $"mission team AI type is {mission.MissionTeamAIType}, not FieldBattle";
                return false;
            }

            if (mission.PlayerTeam == null)
            {
                reason = "no player team";
                return false;
            }

            if (!mission.PlayerTeam.IsPlayerGeneral)
            {
                reason = "player does not command this battle (G6)";
                return false;
            }

            reason = null;
            return true;
        }
    }
}
