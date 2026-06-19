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
            var name = args != null && args.Count > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : "shot";
            return CaptureNamed(name);
        }

        /// <summary>Captures a screenshot to Logs\Screenshots\&lt;name&gt;.bmp (the engine writes
        /// BMP regardless of extension; tools\view-screenshot.ps1 converts it to PNG). Shared by
        /// the rbp.screenshot console command and the dev sentinel loop. Returns a status string.</summary>
        public static string CaptureNamed(string name)
        {
            try
            {
                var safe = Sanitize(string.IsNullOrWhiteSpace(name) ? "shot" : name);
                var dir = Path.Combine(ModuleHelper.GetModuleFullPath(SubModule.ModId), "Logs", "Screenshots");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, safe + ".bmp");

                TaleWorlds.Engine.Utilities.TakeScreenshot(path);
                RbpLog.Info($"Screenshot requested -> {path}");
                return $"screenshot -> {path} (written on the next frame; convert with tools\\view-screenshot.ps1)";
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
