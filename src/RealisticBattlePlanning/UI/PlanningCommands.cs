using System.Collections.Generic;
using TaleWorlds.Library;

namespace RealisticBattlePlanning.UI
{
    /// <summary>
    /// Console toggle for the Planning Mode panel — opens/closes it regardless
    /// of input focus, so the panel can be verified independently of the
    /// keybind (and as a reliable fallback while the keybind is dev-only).
    /// </summary>
    public static class PlanningCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("plan", "rbp")]
        public static string TogglePlan(List<string> args)
        {
            var view = PlanningModeView.Active;
            if (view == null)
                return "no plannable mission is active (field battle you command)";
            view.Toggle();
            return "toggled the Planning Mode panel";
        }
    }
}
