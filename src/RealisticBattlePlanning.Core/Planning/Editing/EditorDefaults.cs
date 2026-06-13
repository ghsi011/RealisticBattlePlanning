using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Planning.Editing
{
    /// <summary>
    /// Sensible defaults for authoring (spec A3.9): a new formation is never
    /// invalid, and common stage patterns are one-click inserts. Engine-free
    /// so the editor logic is unit-tested; the Gauntlet view just calls these.
    /// </summary>
    public static class EditorDefaults
    {
        /// <summary>
        /// The opening stage every new formation starts with: hold position in
        /// a line at current facing (A3.9 — an empty plan is never invalid).
        /// </summary>
        public static Stage OpeningStage() => new()
        {
            Name = "hold position",
            Do = new DirectiveSpec { Type = DirectiveType.Hold, Arrangement = Arrangement.Line },
        };

        /// <summary>One-click pattern: skirmish, then withdraw on contact (A3.9).</summary>
        public static Stage[] SkirmishThenWithdraw(string withdrawAnchor) => new[]
        {
            new Stage
            {
                Name = "skirmish",
                Do = new DirectiveSpec { Type = DirectiveType.Skirmish, Target = "Nearest", StandoffMeters = 60f },
            },
            new Stage
            {
                Name = "withdraw on contact",
                When = { new TriggerSpec { Type = TriggerType.EnemyCommits } },
                Do = new DirectiveSpec { Type = DirectiveType.FeignRetreat, Anchor = withdrawAnchor, FireWhileWithdrawing = true },
            },
        };

        /// <summary>One-click pattern: hold, then charge on a player signal (A3.9).</summary>
        public static Stage[] HoldThenChargeOnSignal(string signal) => new[]
        {
            new Stage
            {
                Name = "hold",
                Do = new DirectiveSpec { Type = DirectiveType.Hold, Arrangement = Arrangement.ShieldWall },
            },
            new Stage
            {
                Name = "charge on signal",
                When = { new TriggerSpec { Type = TriggerType.PlayerSignal, Signal = signal } },
                Do = new DirectiveSpec { Type = DirectiveType.Charge },
            },
        };
    }
}
