namespace ModDebugKit.Battles
{
    /// <summary>Which units a formation-assignment command targets, by live class.</summary>
    public enum AgentSelector { All, Infantry, Ranged, Cavalry, HorseArcher }

    /// <summary>
    /// Parses the selector token used by <c>dbg.assign</c>/<c>dbg.layout</c>.
    /// Engine-free and unit-tested; the engine matches a live agent against the
    /// resulting <see cref="AgentSelector"/> (mounted × shoots).
    /// </summary>
    public static class AgentSelectors
    {
        public static bool TryParse(string text, out AgentSelector selector)
        {
            switch (text?.Trim().ToLowerInvariant())
            {
                case "all": selector = AgentSelector.All; return true;
                case "inf":
                case "infantry": selector = AgentSelector.Infantry; return true;
                case "rng":
                case "ranged":
                case "archer":
                case "archers": selector = AgentSelector.Ranged; return true;
                case "cav":
                case "cavalry": selector = AgentSelector.Cavalry; return true;
                case "ha":
                case "horsearcher":
                case "horse_archer": selector = AgentSelector.HorseArcher; return true;
                default: selector = AgentSelector.All; return false;
            }
        }
    }
}
