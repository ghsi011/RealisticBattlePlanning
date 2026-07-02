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
        /// <summary>Battle-time cap (fast path under fast-forward): end a run that never resolves and has no scenario time limit.</summary>
        private const float BattleTimeCapSeconds = 300f;

        /// <summary>Wall-clock backstop, independent of the monitor feed: ends the run even if that feed dies (a plan fault), so the cap can't be defeated by the failure it guards against.</summary>
        private const float RealTimeCapSeconds = 180f;

        private PlanMissionLogic _host;
        private RunRecorder _recorder;
        private ScenarioActionScheduler _scheduler;
        private ScenarioSpec _scenario;
        private string _result;
        private float? _battleStartSeconds;
        private float _battleTimeSeconds;
        // Genuinely wall-clock (Stopwatch): OnMissionTick's dt is MISSION time —
        // scaled ~9x under fast-forward and frozen on pause — so accumulating it
        // made the "wall-clock" cap fire at ~180 battle-seconds (truncating slow
        // scenarios whose assertion windows reach 240s) and never during a pause.
        private System.Diagnostics.Stopwatch _realClock;
        private bool _deployed;
        private bool _resolved;
        private bool _forceEnd;
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

            _deployed = true; // the wall-clock backstop runs from here (monitor-independent)
            _realClock = System.Diagnostics.Stopwatch.StartNew();
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
            // Only a genuinely resolved battle (victory/defeat) ends the run; a
            // transient/None result must not.
            _resolved = missionResult is { BattleResolved: true };
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
                // _scenario deliberately stays set: OnMissionTick early-returns on a
                // null scenario, so nulling it here would dead-code the _forceEnd
                // auto-leave below and wedge the run at fast-forward forever.
                _forceEnd = true; // a dead recorder must still let the auto-leave tear the mission down
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
        /// to be hands-off, so end the mission once the battle is decided, the
        /// scenario's clock runs out, the recorder faulted, or a wall-clock
        /// backstop trips — rather than waiting for a manual Victory -> Done.
        /// FinalizeRun then evaluates and writes results. The wall-clock
        /// backstop (RealTimeCapSeconds) is a real Stopwatch, independent of
        /// both the monitor feed and mission-time scaling, so a plan fault that
        /// kills the feed — or a pause — can't wedge the run forever.
        /// </summary>
        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (_ended || _scenario == null)
                return;
            var realSeconds = _deployed && _realClock != null ? (float)_realClock.Elapsed.TotalSeconds : 0f;

            var limit = _scenario.TimeLimitSeconds ?? 0f;
            var timeUp = (limit > 0f && _battleTimeSeconds >= limit)
                         || _battleTimeSeconds >= BattleTimeCapSeconds
                         || realSeconds >= RealTimeCapSeconds;
            if (!_resolved && !_forceEnd && !timeUp)
                return;

            _ended = true;
            var why = _resolved ? _result
                : _forceEnd ? "recorder faulted"
                : realSeconds >= RealTimeCapSeconds ? $"{realSeconds:0}s wall-clock cap"
                : $"{_battleTimeSeconds:0}s battle elapsed";
            RbpLog.Info($"Harness: '{_scenario.Name}' finished ({why}); ending the mission.");
            try
            {
                Mission.EndMission();
            }
            catch (Exception e)
            {
                RbpLog.Error("[FAULT] Harness auto-leave EndMission threw; flushing results directly.", e);
                FinalizeRun(); // belt-and-braces: a throw must still write what we have
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
            _forceEnd = true; // a faulted run is invalid; end promptly rather than wait out the cap
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
