using System.IO;
using ModDebugKit.Battles;
using ModDebugKit.Commands;
using ModDebugKit.Diagnostics;
using ModDebugKit.Io;

namespace ModDebugKit
{
    /// <summary>
    /// Process-wide handles set up once at module load: the resolved IO paths
    /// and the shared command dispatcher. Static so the console command
    /// methods (which the engine discovers and calls without an instance) reach
    /// the same dispatcher the file channel uses.
    /// </summary>
    public static class ModDebugKitRuntime
    {
        public static DbgPaths Paths { get; private set; }

        public static CommandDispatcher Dispatcher { get; private set; }

        public static bool Initialized { get; private set; }

        public static void Initialize(string outputRoot)
        {
            if (Initialized)
                return;

            Paths = new DbgPaths(outputRoot);
            Directory.CreateDirectory(Paths.Root);
            Directory.CreateDirectory(Paths.IoDir);
            DbgLog.Init(Paths.Log);
            DbgLog.ErrorSink = ErrorLog.FromLog; // every kit-caught fault also lands in errors.jsonl

            Dispatcher = new CommandDispatcher();
            CoreCommands.RegisterAll(Dispatcher);
            BattleCommands.RegisterAll(Dispatcher);
            FormationCommands.RegisterAll(Dispatcher);
            Telemetry.TelemetryCommands.RegisterAll(Dispatcher);
            ErrorLog.RegisterCommands(Dispatcher);

            Initialized = true;
        }
    }
}
