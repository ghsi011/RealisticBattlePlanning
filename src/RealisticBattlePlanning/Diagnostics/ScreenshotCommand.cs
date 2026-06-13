using System;
using System.Collections.Generic;
using System.IO;
using TaleWorlds.Library;
using TaleWorlds.ModuleManager;

namespace RealisticBattlePlanning.Diagnostics
{
    /// <summary>
    /// Saves a screenshot to Logs\Screenshots under the deployed module, so
    /// in-game UI/state can be inspected from outside the game (the dev/test
    /// loop reads the PNG directly). Engine captures on the next frame, so the
    /// file appears a moment after the command returns.
    /// </summary>
    public static class ScreenshotCommand
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("screenshot", "rbp")]
        public static string Capture(List<string> args)
        {
            try
            {
                var name = args != null && args.Count > 0 && !string.IsNullOrWhiteSpace(args[0])
                    ? Sanitize(args[0])
                    : "shot";

                var dir = Path.Combine(ModuleHelper.GetModuleFullPath(SubModule.ModId), "Logs", "Screenshots");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, name + ".png");

                TaleWorlds.Engine.Utilities.TakeScreenshot(path);
                RbpLog.Info($"Screenshot requested -> {path}");
                return $"screenshot -> {path} (written on the next frame)";
            }
            catch (Exception e)
            {
                RbpLog.Error("Screenshot failed.", e);
                return $"screenshot failed: {e.Message}";
            }
        }

        private static string Sanitize(string name)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');
            return name;
        }
    }
}
