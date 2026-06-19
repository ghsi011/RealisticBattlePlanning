using System;
using System.Reflection;
using TaleWorlds.GauntletUI;            // UIContext, EventManager
using TaleWorlds.GauntletUI.BaseTypes;  // ButtonWidget

namespace RealisticBattlePlanning.UI
{
    /// <summary>
    /// The interactive battle-map canvas (spec A2.6): captures every click in its bounds and
    /// hands the VM a normalized [0,1] point, so the editor can select a formation (marker
    /// hit-test, VM-side) or append a point-and-click move stage. The active planning view wires
    /// <see cref="Clicked"/> on open / clears it on close (Command.Click is parameterless and
    /// cannot carry the position — the 2026-06-12 review's open question, solved with a static
    /// delegate rather than a fragile two-way binding).
    ///
    /// Bases on <see cref="ButtonWidget"/>: a raw Widget's OnMousePressed never fired — the
    /// marker ListPanels filling the canvas swallowed the clicks; a ButtonWidget with
    /// DoNotPassEventsToChildren reliably captures the whole area.
    ///
    /// Geometry (Size / GlobalPosition / MousePosition) is read by REFLECTION: the compile-time
    /// reference assembly and the installed game disagree on the Vector2 type behind those
    /// members, so a direct call fails to JIT with a MissingMethodException (which a try/catch
    /// can't catch — the whole method won't compile). Reflection binds to the runtime members.
    /// </summary>
    public class MapCanvasWidget : ButtonWidget
    {
        public MapCanvasWidget(UIContext context) : base(context) { }

        /// <summary>Invoked with the click's normalized canvas coordinates: x right in [0,1],
        /// y DOWN in [0,1] (screen sense; the VM un-flips to map-forward).</summary>
        public static Action<float, float> Clicked { get; set; }

        /// <summary>Right-click (alternate) at the same normalized coordinates — the VM uses it
        /// to remove the nearest click-placed waypoint (A2.6.2).</summary>
        public static Action<float, float> RightClicked { get; set; }

        /// <summary>A drag from press→release (both normalized): box-select with nothing selected,
        /// or array the selected formations along the line (A2.6.3).</summary>
        public static Action<float, float, float, float> Dragged { get; set; }

        // A press+release within this normalized distance is a click; beyond it, a drag. Left
        // actions fire on RELEASE so a drag can be told from a click (right-click fires on press).
        private const float DragThreshold = 0.03f;
        private bool _pressed;
        private float _pressX, _pressY;

        protected override void OnMousePressed()
        {
            base.OnMousePressed();
            if (TryGetNormalized(out var nx, out var ny))
            {
                _pressed = true;
                _pressX = nx;
                _pressY = ny;
            }
        }

        protected override void OnMouseReleased(bool isFromInput)
        {
            base.OnMouseReleased(isFromInput);
            if (!_pressed)
                return;
            _pressed = false;
            try
            {
                if (!TryGetNormalized(out var nx, out var ny))
                {
                    Clicked?.Invoke(_pressX, _pressY); // fall back to the press point
                    return;
                }
                var dx = nx - _pressX;
                var dy = ny - _pressY;
                if (dx * dx + dy * dy < DragThreshold * DragThreshold)
                {
                    Diagnostics.RbpLog.Info($"[MAP] click n=({nx:0.00},{ny:0.00})");
                    Clicked?.Invoke(nx, ny);
                }
                else
                {
                    Diagnostics.RbpLog.Info($"[MAP] drag ({_pressX:0.00},{_pressY:0.00})->({nx:0.00},{ny:0.00})");
                    Dragged?.Invoke(_pressX, _pressY, nx, ny);
                }
            }
            catch (Exception e)
            {
                Diagnostics.RbpLog.Error("[MAP] canvas release handler failed.", e);
            }
        }

        protected override void OnMouseAlternatePressed()
        {
            base.OnMouseAlternatePressed();
            try
            {
                if (RightClicked != null && TryGetNormalized(out var nx, out var ny))
                {
                    Diagnostics.RbpLog.Info($"[MAP] rightclick n=({nx:0.00},{ny:0.00})");
                    RightClicked(nx, ny);
                }
            }
            catch (Exception e)
            {
                Diagnostics.RbpLog.Error("[MAP] canvas rightclick handler failed.", e);
            }
        }

        // Reads the canvas geometry (by reflection — see the class summary) into a normalized
        // [0,1] point. Returns false if any read failed (the runtime/compile Vector2 skew).
        private bool TryGetNormalized(out float nx, out float ny)
        {
            nx = 0f; ny = 0f;
            var okSize = TryXY(this, "Size", out var sw, out var sh);
            var okGp = TryXY(this, "GlobalPosition", out var gx, out var gy);
            var em = EventManager;
            float mx = 0f, my = 0f;
            var okMouse = em != null && TryXY(em, "MousePosition", out mx, out my);
            if (!okSize || !okGp || !okMouse || sw <= 0f || sh <= 0f)
                return false;
            nx = Clamp01((mx - gx) / sw);
            ny = Clamp01((my - gy) / sh);
            return true;
        }

        /// <summary>Reads the X/Y of a Vector2-like value fetched from <paramref name="prop"/> on
        /// <paramref name="obj"/> by reflection (X/Y may be a field or a property).</summary>
        private static bool TryXY(object obj, string prop, out float x, out float y)
        {
            x = 0f; y = 0f;
            var value = obj?.GetType().GetProperty(prop)?.GetValue(obj);
            return value != null && TryComponent(value, "X", out x) && TryComponent(value, "Y", out y);
        }

        private static bool TryComponent(object v, string name, out float f)
        {
            f = 0f;
            var t = v.GetType();
            var p = t.GetProperty(name);
            if (p != null) { f = Convert.ToSingle(p.GetValue(v)); return true; }
            var field = t.GetField(name);
            if (field != null) { f = Convert.ToSingle(field.GetValue(v)); return true; }
            return false;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
