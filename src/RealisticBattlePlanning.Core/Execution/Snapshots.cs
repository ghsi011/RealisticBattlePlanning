using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Execution
{
    /// <summary>
    /// One formation's state as seen at a single monitor tick. The only way
    /// engine state reaches Core (testing architecture, Layer 1): the engine
    /// captures these eagerly each tick; tests script them as timelines.
    /// Grows fields (casualties, median speed, broken) in later iterations.
    /// </summary>
    public interface IFormationSnapshot
    {
        PlannedFormationClass Class { get; }

        /// <summary>False once the formation has no units.</summary>
        bool Exists { get; }

        MapVec Position { get; }
    }

    /// <summary>One tick's view of the battlefield.</summary>
    public interface IBattlefieldSnapshot
    {
        /// <summary>Mission time in seconds.</summary>
        float TimeSeconds { get; }

        /// <summary>True once deployment is over and the battle is running.</summary>
        bool BattleStarted { get; }

        /// <summary>
        /// Normalized direction from our deployment toward the enemy, captured
        /// at battle start. The "forward" axis for relative anchors.
        /// </summary>
        MapVec AttackDirection { get; }

        /// <summary>Player team's average position (TeamCenter anchor basis).</summary>
        MapVec TeamCenter { get; }

        /// <summary>Own (player-team) formation by class; null when absent/empty.</summary>
        IFormationSnapshot GetOwn(PlannedFormationClass formationClass);
    }
}
