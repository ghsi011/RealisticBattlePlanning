using System;
using TaleWorlds.GauntletUI;            // UIContext, EventManager
using TaleWorlds.GauntletUI.BaseTypes;  // Widget
using TaleWorlds.Library;

namespace RealisticBattlePlanning.UI
{
    /// <summary>
    /// The interactive battle-map canvas (spec A2.6.2): a full-bounds widget that turns a
    /// click on empty map into a normalized [0,1] point and hands it to the VM, so the
    /// editor can append a point-and-click move stage. Marker buttons sit on top and consume
    /// their own clicks; clicks that land on the bare canvas reach here. The VM wires
    /// <see cref="MapClicked"/> after the movie loads (Command.Click is parameterless and
    /// cannot carry the position — the 2026-06-12 review's open question, solved by a direct
    /// delegate rather than a two-way binding). Guarded — a click handler fault must never
    /// take the mission down.
    /// </summary>
    public class MapCanvasWidget : Widget
    {
        public MapCanvasWidget(UIContext context) : base(context) { }

        /// <summary>Invoked with the click's normalized canvas coordinates: x right in [0,1],
        /// y DOWN in [0,1] (screen sense; the VM un-flips to map-forward). The active planning
        /// view sets this on open and clears it on close — a static hook avoids hunting the
        /// widget out of the loaded movie tree (only one planner is open at a time).</summary>
        public static Action<float, float> Clicked { get; set; }

        protected override void OnMousePressed()
        {
            base.OnMousePressed();
            try
            {
                var size = Size;
                if (Clicked == null || size.X <= 0f || size.Y <= 0f)
                    return;
                var local = EventManager.MousePosition - GlobalPosition;
                var nx = Clamp01(local.X / size.X);
                var ny = Clamp01(local.Y / size.Y);
                Clicked(nx, ny);
            }
            catch
            {
                // Never let a UI click handler fault propagate into the mission tick.
            }
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
