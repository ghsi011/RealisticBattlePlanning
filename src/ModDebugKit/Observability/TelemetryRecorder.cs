using System.Collections.Generic;
using System.IO;
using ModDebugKit.Diagnostics;
using ModDebugKit.Io;
using ModDebugKit.Telemetry;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace ModDebugKit.Observability
{
    /// <summary>
    /// Records the per-mission event stream to <c>telemetry.jsonl</c> via vanilla
    /// mission hooks: mission/deployment/mode transitions, deaths and routs, and
    /// the final result. Attached to every mission by the SubModule. Order
    /// capture (cross-mod, via Harmony) is layered on in M2.2. Engine reads
    /// only — never throws into the game.
    /// </summary>
    public sealed class TelemetryRecorder : MissionLogic
    {
        private const float OrderPollIntervalSeconds = 0.25f;
        private float _orderPollAccum;

        public override void AfterStart()
        {
            base.AfterStart();
            OrderTelemetry.Reset(); // fresh per-formation order-dedup cache for this mission
            TelemetryLog.Write(TelemetryKinds.MissionStart, Mission.CurrentTime, Mission.SceneName);
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            _orderPollAccum += dt;
            if (_orderPollAccum < OrderPollIntervalSeconds)
                return;
            _orderPollAccum = 0f;
            OrderTelemetry.Poll(Mission); // main-thread sampling of formation orders -> "order" events on change
        }

        public override void OnDeploymentFinished()
        {
            base.OnDeploymentFinished();
            TelemetryLog.Write(TelemetryKinds.DeploymentFinished, Mission.CurrentTime);
        }

        public override void OnMissionModeChange(MissionMode oldMissionMode, bool atStart)
        {
            base.OnMissionModeChange(oldMissionMode, atStart);
            TelemetryLog.Write(TelemetryKinds.ModeChange, Mission.CurrentTime, $"{oldMissionMode} -> {Mission.Mode}");
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow blow)
        {
            base.OnAgentRemoved(affectedAgent, affectorAgent, agentState, blow);
            if (affectedAgent == null || !affectedAgent.IsHuman)
                return; // skip mounts; humans are the interesting casualties

            TelemetryLog.Write(TelemetryKinds.AgentRemoved, Mission.CurrentTime, null, new
            {
                agent = affectedAgent.Name,
                team = affectedAgent.Team?.TeamIndex ?? -1,
                formation = affectedAgent.Formation != null ? (int)affectedAgent.Formation.FormationIndex + 1 : (int?)null,
                state = agentState.ToString(),
                killer = affectorAgent?.Name,
                killerTeam = affectorAgent?.Team?.TeamIndex ?? -1,
            });
        }

        public override void OnMissionResultReady(MissionResult missionResult)
        {
            base.OnMissionResultReady(missionResult);
            _result = Describe(missionResult);
            TelemetryLog.Write(TelemetryKinds.MissionResult, Mission.CurrentTime, _result);
        }

        protected override void OnEndMission()
        {
            WriteBattleResult();
            TelemetryLog.Write(TelemetryKinds.MissionEnd, Mission?.CurrentTime);
            base.OnEndMission();
        }

        private string _result;

        /// <summary>
        /// Structured end-of-battle outcome (battle_result.json, overwritten per
        /// mission): result + duration + each surviving formation's side/number/
        /// composition/count/casualties. An agent Reads one file instead of
        /// aggregating telemetry.jsonl's agent_removed stream by hand.
        /// </summary>
        private void WriteBattleResult()
        {
            try
            {
                if (ModDebugKitRuntime.Paths == null || Mission == null)
                    return;
                var dto = BattleSnapshotReader.Capture(Mission);
                var formations = new List<object>();
                foreach (var f in dto.Formations)
                    formations.Add(new
                    {
                        n = f.Number,
                        side = f.Side,
                        comp = f.Composition?.Label,
                        count = f.Count,
                        cas = f.CasualtiesPercent,
                    });
                File.WriteAllText(ModDebugKitRuntime.Paths.BattleResult, DbgJson.Line(new
                {
                    result = _result,   // null when the mission ended unresolved (player left early)
                    t = Mission.CurrentTime,
                    scene = Mission.SceneName,
                    formations,
                }));
            }
            catch (System.Exception e)
            {
                DbgLog.Error("Writing battle_result.json failed.", e);
            }
        }

        private static string Describe(MissionResult result)
        {
            if (result == null)
                return null;
            if (result.PlayerVictory)
                return "PlayerVictory";
            if (result.PlayerDefeated)
                return "PlayerDefeated";
            return result.BattleState.ToString();
        }
    }
}
