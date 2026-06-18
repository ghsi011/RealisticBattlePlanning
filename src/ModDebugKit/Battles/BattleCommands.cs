using System;
using System.IO;
using ModDebugKit.Commands;
using ModDebugKit.Diagnostics;
using ModDebugKit.Io;
using ModDebugKit.Observability;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.CustomBattle;
using TaleWorlds.MountAndBlade.CustomBattle.CustomBattle;

namespace ModDebugKit.Battles
{
    /// <summary>
    /// The battle-factory commands (M1). For now: <c>dbg.battle</c> launches a
    /// preset-driven custom field battle with no menu navigation, so an agent
    /// can put the game into an exact battle through the file channel.
    /// </summary>
    public static class BattleCommands
    {
        /// <summary>The last preset launched, for dbg.restart.</summary>
        public static BattlePreset LastPreset { get; private set; }

        // dbg.restart ends the current mission, then relaunches once the game is back at the
        // custom-battle menu — which takes a few frames. The pump polls TickRestart() until then.
        private static bool _restartPending;
        private static int _restartWaitedFrames;
        private const int RestartWaitFrameCap = 1800; // ~30s safety so a stuck restart can't wedge

        public static void RegisterAll(CommandDispatcher dispatcher)
        {
            dispatcher.Register("dbg.battle",
                "dbg.battle [preset|path] - launch a custom field battle from a preset (no arg = Empire vs Aserai default)",
                Battle);
            dispatcher.Register("dbg.ready",
                "dbg.ready - finish deployment and start the battle (no Ready click needed)",
                Ready);
            dispatcher.Register("dbg.leave",
                "dbg.leave - end the current mission and return to the menu",
                Leave);
            dispatcher.Register("dbg.restart",
                "dbg.restart - end the current battle and relaunch the same preset",
                Restart);
        }

        private static DbgOutcome Battle(DbgCommand command)
        {
            // Never push a second mission onto a live one (the channel runs in-battle too):
            // that corrupts the state stack.
            if (Mission.Current != null)
                return DbgOutcome.Failure("already in a mission; finish it first");

            BattlePreset preset;
            string source;
            if (command.Arg(0) != null)
            {
                if (!JsonFileLibrary.TryLoad<BattlePreset>("preset", command.Arg(0), ModDebugKitRuntime.Paths.PresetsDir, out preset, out _, out var loadError))
                    return DbgOutcome.Failure(loadError);
                source = command.Arg(0);
            }
            else
            {
                preset = BattlePreset.CreateDefault();
                source = "default";
            }

            var errors = BattlePresetValidator.Validate(preset);
            if (errors.Count > 0)
                return DbgOutcome.Failure("invalid preset: " + string.Join("; ", errors));

            LastPreset = preset;

            try
            {
                // If the Custom Battle menu is already active the custom game is loaded —
                // launch directly. Otherwise (main menu) load the custom game first; its
                // manager then launches the battle on load-finish.
                if (Game.Current?.GameStateManager?.ActiveState is CustomBattleState)
                {
                    if (!BattleFactory.TryBuild(preset, out var data, out var buildError))
                        return DbgOutcome.Failure($"battle build failed: {buildError}");
                    CustomBattleHelper.StartGame(data);
                    return DbgOutcome.Success($"launched custom battle (preset: {source})", new { preset = source, mode = "direct" });
                }

                MBGameManager.StartNewGame(new BattleGameManager(preset));
                return DbgOutcome.Success($"loading custom battle (preset: {source}); will launch a field battle on load",
                    new { preset = source, mode = "load" });
            }
            catch (Exception e)
            {
                DbgLog.Error("dbg.battle failed.", e);
                return DbgOutcome.Failure($"battle failed: {e.Message}");
            }
        }

        private static DbgOutcome Ready(DbgCommand command)
        {
            var mission = Mission.Current;
            if (mission == null)
                return DbgOutcome.Failure("no active mission");

            if (DebugMissionObserver.Active?.Deployed == true)
                return DbgOutcome.Success("deployment already finished; battle is running");

            var handler = mission.GetMissionBehavior<DeploymentHandler>();
            if (handler == null)
                return DbgOutcome.Failure($"no deployment handler (mission mode={mission.Mode}); can't ready");

            handler.FinishDeployment();
            return DbgOutcome.Success("deployment finished; battle starting");
        }

        private static DbgOutcome Leave(DbgCommand command)
        {
            var mission = Mission.Current;
            if (mission == null)
                return DbgOutcome.Failure("not in a mission");

            mission.EndMission();
            return DbgOutcome.Success("ending mission; returning to the menu");
        }

        private static DbgOutcome Restart(DbgCommand command)
        {
            if (LastPreset == null)
                return DbgOutcome.Failure("no battle launched yet this session; use dbg.battle first");

            if (Mission.Current != null)
            {
                // End now; TickRestart relaunches once the game is back at the custom-battle menu.
                Mission.Current.EndMission();
                _restartPending = true;
                _restartWaitedFrames = 0;
                return DbgOutcome.Success("ending the battle; will relaunch the same preset");
            }

            // Already out of a mission: relaunch straight away.
            try
            {
                if (Game.Current?.GameStateManager?.ActiveState is CustomBattleState)
                {
                    if (!BattleFactory.TryBuild(LastPreset, out var data, out var buildError))
                        return DbgOutcome.Failure($"restart build failed: {buildError}");
                    CustomBattleHelper.StartGame(data);
                    return DbgOutcome.Success("relaunched the same preset");
                }

                MBGameManager.StartNewGame(new BattleGameManager(LastPreset));
                return DbgOutcome.Success("loading the same preset");
            }
            catch (Exception e)
            {
                DbgLog.Error("dbg.restart failed.", e);
                return DbgOutcome.Failure($"restart failed: {e.Message}");
            }
        }

        /// <summary>
        /// Polled by the SubModule each frame. Once a dbg.restart's EndMission
        /// has torn the mission down and the game is back at the custom-battle
        /// menu, relaunch the same preset. Bounded so a restart that never
        /// reaches the menu (e.g. it returned to the main menu) gives up.
        /// </summary>
        public static void TickRestart()
        {
            if (!_restartPending)
                return;

            if (Mission.Current != null)
                return; // mission still tearing down

            if (++_restartWaitedFrames > RestartWaitFrameCap)
            {
                _restartPending = false;
                DbgLog.Warn("dbg.restart: gave up waiting to return to the custom-battle menu.");
                return;
            }

            if (Game.Current?.GameStateManager?.ActiveState is CustomBattleState)
            {
                _restartPending = false;
                try
                {
                    if (BattleFactory.TryBuild(LastPreset, out var data, out var error))
                    {
                        CustomBattleHelper.StartGame(data);
                        DbgLog.Info("dbg.restart: relaunched the same preset.");
                    }
                    else
                    {
                        DbgLog.Error($"dbg.restart: build failed: {error}");
                    }
                }
                catch (Exception e)
                {
                    DbgLog.Error("dbg.restart: relaunch threw.", e);
                }
            }
        }
    }
}
