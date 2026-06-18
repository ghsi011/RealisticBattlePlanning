using System;
using System.IO;
using ModDebugKit.Commands;
using ModDebugKit.Diagnostics;
using ModDebugKit.Io;
using ModDebugKit.Observability;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using Path = System.IO.Path;

namespace ModDebugKit.Capture
{
    /// <summary>
    /// Screenshot capture (M3): a clean, game-only frame plus a JSON sidecar of
    /// the battle state at capture, so a screenshot is self-describing — the
    /// agent reads the sidecar for the facts and the image only when it needs to
    /// look. The engine writes the frame as a BMP on the next frame regardless
    /// of extension; <c>tools/mdk-shot.ps1</c> converts it to a PNG to read.
    /// </summary>
    public static class CaptureCommands
    {
        public static void RegisterAll(CommandDispatcher dispatcher)
        {
            dispatcher.Register("dbg.shot",
                "dbg.shot [name] - clean game screenshot + battle-state sidecar JSON in shots/",
                Shot);
        }

        private static DbgOutcome Shot(DbgCommand command)
        {
            var paths = ModDebugKitRuntime.Paths;
            try
            {
                Directory.CreateDirectory(paths.ShotsDir);
                var name = Sanitize(command.Arg(0));
                var image = Path.Combine(paths.ShotsDir, name + ".bmp");
                var sidecar = Path.Combine(paths.ShotsDir, name + ".json");

                // Sidecar first (synchronous): the full battle state when in a mission, else context.
                var mission = Mission.Current;
                object state = mission != null
                    ? BattleSnapshotReader.Capture(mission)
                    : new { capturedAtUtc = DateTime.UtcNow.ToString("o"), inMission = false, note = "no active mission" };
                File.WriteAllText(sidecar, DbgJson.Pretty(state));

                // The engine renders the frame and writes the file on the NEXT frame.
                Utilities.TakeScreenshot(image);

                return DbgOutcome.Success(
                    $"shot '{name}': {image} (written next frame) + sidecar {sidecar}",
                    new { image, sidecar, inMission = mission != null });
            }
            catch (Exception e)
            {
                DbgLog.Error("dbg.shot failed.", e);
                return DbgOutcome.Failure($"shot failed: {e.Message}");
            }
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "shot";
            foreach (var invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');
            return name;
        }
    }
}
