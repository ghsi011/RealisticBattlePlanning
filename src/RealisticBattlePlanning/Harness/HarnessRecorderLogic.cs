using System;
using RealisticBattlePlanning.Diagnostics;
using RealisticBattlePlanning.Execution;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace RealisticBattlePlanning.Harness
{
    /// <summary>
    /// Mission-side adapter for the Core RunRecorder: subscribes to the plan
    /// logic's monitor feed, fast-forwards the battle once deployment ends,
    /// and on mission end evaluates the armed scenario's assertions and hands
    /// the results to HarnessSession. Attached only when the harness is armed
    /// (dev-mode tool, no player-facing surface — G5).
    /// </summary>
    internal sealed class HarnessRecorderLogic : MissionLogic
    {
        /// <summary>Battle-time safety cap: end an armed run even if it never resolves and has no scenario time limit.</summary>
        private const float HardCapSeconds = 300f;

        private PlanMissionLogic _host;
        private RunRecorder _recorder;
        private ScenarioActionScheduler _scheduler;
        private ScenarioSpec _scenario;
        private string _result;
        private float? _battleStartSeconds;
        private float _battleTimeSeconds;
        private bool _finalized;
        private bool _ended;

        public override void AfterStart()
        {
            base.AfterStart();
            try
            {
                _scenario = HarnessSession.CurrentScenario;
                if (_scenario == null)
                    return;

                _host = Mission.GetMissionBehavior<PlanMissionLogic>();
                if (_host?.ActivePlan == null)
                {
                    RbpLog.Warn($"Harness: scenario '{_scenario.Name}' armed but no plan is active " +
                                "(mission not plannable or plan rejected); recorder stays inert, scenario stays armed.");
                    _scenario = null;
                    return;
                }

                _recorder = new RunRecorder(_scenario.Name, _host.ActivePlan);
                _scheduler = new ScenarioActionScheduler(_scenario.Actions);
                _host.MonitorTicked += OnMonitorTicked;
                _host.MonitorFaulted += OnMonitorFaulted;
                RbpLog.Info($"Harness: recording scenario '{_scenario.Name}'" +
                            (_scenario.Actions.Count > 0 ? $" ({_scenario.Actions.Count} scripted action(s))." : "."));
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] HarnessRecorderLogic.AfterStart failed; recorder inert.", e);
                _scenario = null;
            }
        }

        public override void OnDeploymentFinished()
        {
            base.OnDeploymentFinished();
            if (_recorder == null)
                return;

            try
            {
                Mission.SetFastForwardingFromUI(true);
                RbpLog.Info("Harness: fast-forwarding the battle.");
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] Harness fast-forward failed; battle runs at normal speed.", e);
            }
        }

        public override void OnMissionResultReady(MissionResult missionResult)
        {
            base.OnMissionResultReady(missionResult);
            _result = Describe(missionResult);
        }

        protected override void OnEndMission()
        {
            base.OnEndMission();
            FinalizeRun();
        }

        public override void OnRemoveBehavior()
        {
            FinalizeRun();
            base.OnRemoveBehavior();
        }

        private void OnMonitorTicked(IBattlefieldSnapshot snapshot, System.Collections.Generic.IReadOnlyList<PlanEvent> events)
        {
            try
            {
                _recorder?.Tick(snapshot, events);
                if (snapshot.BattleStarted)
                {
                    _battleStartSeconds ??= snapshot.TimeSeconds;
                    _battleTimeSeconds = snapshot.TimeSeconds - _battleStartSeconds.Value;
                }
                FireScriptedActions(snapshot);
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] Harness recorder tick failed; recording stops.", e);
                Unsubscribe();
                _recorder = null;
                _scenario = null;
            }
        }

        /// <summary>
        /// Injects the scenario's scripted player inputs at their
        /// battle-relative times — the same clock the recorder stamps events
        /// with, so an action authored "at 10s" lines up with the record.
        /// </summary>
        private void FireScriptedActions(IBattlefieldSnapshot snapshot)
        {
            if (_scheduler == null || _scheduler.Done || !snapshot.BattleStarted)
                return;

            foreach (var fired in _scheduler.Tick(_battleTimeSeconds, _host.Monitor))
                RbpLog.Info($"Harness: scripted action — {fired}.");
        }

        /// <summary>
        /// Auto-leave (dev-tool, armed runs only — G5): an armed run is meant
        /// to be hands-off, so end the mission once the battle is decided or
        /// the scenario's clock runs out, rather than waiting for a manual
        /// Victory -> Done. FinalizeRun then evaluates and writes results.
        /// </summary>
        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (_ended || _recorder is not { Started: true } || _scenario == null)
                return;

            var limit = _scenario.TimeLimitSeconds ?? 0f;
            var timeUp = (limit > 0f && _battleTimeSeconds >= limit) || _battleTimeSeconds >= HardCapSeconds;
            if (_result == null && !timeUp)
                return;

            _ended = true;
            RbpLog.Info($"Harness: '{_scenario.Name}' finished ({_result ?? $"{_battleTimeSeconds:0}s elapsed"}); ending the mission.");
            try
            {
                Mission.EndMission();
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] Harness auto-leave failed; finish the battle manually.", e);
            }
        }

        private void FinalizeRun()
        {
            if (_finalized)
                return;
            _finalized = true;
            Unsubscribe();

            if (_recorder == null || _scenario == null)
                return;

            try
            {
                if (!_recorder.Started)
                {
                    RbpLog.Warn($"Harness: battle ended before it started (deployment abandoned?); " +
                                $"scenario '{_scenario.Name}' stays armed for the next battle.");
                    return;
                }

                var record = _recorder.Complete(_result);
                var result = ScenarioEvaluator.Evaluate(_scenario, record);
                HarnessSession.OnScenarioCompleted(_scenario, record, result);
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] Harness failed finalizing the scenario run.", e);
            }
        }

        private void OnMonitorFaulted(string reason)
        {
            _recorder?.MarkFault(reason);
        }

        private void Unsubscribe()
        {
            if (_host != null)
            {
                _host.MonitorTicked -= OnMonitorTicked;
                _host.MonitorFaulted -= OnMonitorFaulted;
            }
        }

        private static string Describe(MissionResult missionResult)
        {
            if (missionResult == null)
                return null;
            if (missionResult.PlayerVictory)
                return "PlayerVictory";
            if (missionResult.PlayerDefeated)
                return "PlayerDefeated";
            return missionResult.BattleState.ToString();
        }
    }
}
