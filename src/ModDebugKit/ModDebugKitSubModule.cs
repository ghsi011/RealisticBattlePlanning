using System;
using System.IO;
using HarmonyLib;
using ModDebugKit.Diagnostics;
using ModDebugKit.Io;
using ModDebugKit.Observability;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ModuleManager;
using TaleWorlds.MountAndBlade;

namespace ModDebugKit
{
    /// <summary>
    /// Module entry point. Sets up the output directory and the file command
    /// channel at load, pumps the channel on the application tick (which runs
    /// every frame regardless of game state, so file commands work at the menu
    /// too), and attaches the per-mission observer to every mission.
    /// </summary>
    public class ModDebugKitSubModule : MBSubModuleBase
    {
        public const string ModuleId = "ModDebugKit";
        public const string ModuleName = "Mod Debug Kit";

        private static readonly Harmony Harmony = new(ModuleId);

        private FileCommandChannel _channel;
        private bool _loadedToastShown;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            try
            {
                DbgLog.MirrorSink = message => Debug.Print(message);
                ModDebugKitRuntime.Initialize(ResolveOutputRoot());

                // Capture any unhandled exception (process-fatal) to errors.jsonl with its stack.
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

                _channel = new FileCommandChannel(ModDebugKitRuntime.Paths, ModDebugKitRuntime.Dispatcher);
                _channel.Start();

                // No [HarmonyPatch] classes yet; the hook is registered ready for the
                // M2 cross-mod order/exception capture. PatchAll over an empty set is a no-op.
                Harmony.PatchAll(typeof(ModDebugKitSubModule).Assembly);

                DbgLog.Info($"{ModuleName} loaded. Output root: {ModDebugKitRuntime.Paths.Root}");
            }
            catch (Exception e)
            {
                DbgLog.Error("OnSubModuleLoad failed; the kit may be partially inert this session.", e);
            }
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            _channel?.Tick(dt);
            Battles.BattleCommands.TickRestart();
            Determinism.DeterminismControls.Tick();
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);
            try
            {
                mission.AddMissionBehavior(new DebugMissionObserver());
                mission.AddMissionBehavior(new TelemetryRecorder());
            }
            catch (Exception e)
            {
                DbgLog.Error("Attaching the mission observer/recorder failed; snapshots/telemetry may be degraded this mission.", e);
            }
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            if (_loadedToastShown)
                return;
            _loadedToastShown = true;
            try
            {
                InformationManager.DisplayMessage(new InformationMessage($"{ModuleName} loaded.", Color.FromUint(0xFF00FFFF)));
            }
            catch (Exception)
            {
                // A failed toast must never take the menu down.
            }
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            ErrorLog.Capture("appdomain", exception?.Message ?? "unhandled exception", exception, e.IsTerminating);
        }

        /// <summary>
        /// Output root: the <c>MODDEBUGKIT_OUT</c> env var if set, else
        /// <c>Modules/ModDebugKit/Debug</c> under the deployed module. The env
        /// override lets the agent point output anywhere (e.g. a scratch dir).
        /// </summary>
        private static string ResolveOutputRoot()
        {
            var overridePath = Environment.GetEnvironmentVariable("MODDEBUGKIT_OUT");
            if (!string.IsNullOrWhiteSpace(overridePath))
                return overridePath;
            return Path.Combine(ModuleHelper.GetModuleFullPath(ModuleId), "Debug");
        }
    }
}
