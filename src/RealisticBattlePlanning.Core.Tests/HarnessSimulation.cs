using System.Collections.Generic;
using System.Linq;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Harness;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// Kinematic stand-in for a harness battle: drives PlanMonitor and
    /// RunRecorder over a scripted clock, moving each formation toward the
    /// move target its plan events last issued — the same contract the engine
    /// executor follows. Lets the shipped scenario pack be validated end to
    /// end without the game.
    /// </summary>
    internal static class HarnessSimulation
    {
        /// <summary>Roughly walking speed in m/s.</summary>
        public const float DefaultSpeed = 2f;

        public static BattleRecord Run(
            BattlePlan plan,
            string scenarioName,
            float durationSeconds = 240f,
            float dt = 0.5f,
            float speed = DefaultSpeed,
            string result = "PlayerVictory")
        {
            var monitor = new PlanMonitor(plan);
            var recorder = new RunRecorder(scenarioName, plan);
            var positions = plan.Formations.ToDictionary(f => f.Formation, f => StartPosition(f.Formation));
            var targets = new Dictionary<PlannedFormationClass, MapVec?>();

            for (var time = 0f; time <= durationSeconds; time += dt)
            {
                var snapshot = new FakeBattlefield(time, started: true);
                foreach (var (cls, position) in positions)
                    snapshot.WithOwn(cls, position.X, position.Y);

                var events = monitor.Tick(snapshot);
                recorder.Tick(snapshot, events);

                foreach (var planEvent in events)
                {
                    switch (planEvent)
                    {
                        case StageActivated activated:
                            targets[activated.Formation] = activated.Directive.Spec.Type == DirectiveType.MoveTo
                                ? activated.Directive.FirstMoveTarget
                                : null;
                            break;
                        case MoveTargetChanged moved:
                            targets[moved.Formation] = moved.Target;
                            break;
                    }
                }

                foreach (var cls in positions.Keys.ToList())
                {
                    if (!targets.TryGetValue(cls, out var maybeTarget) || maybeTarget is not { } goal)
                        continue;

                    var position = positions[cls];
                    var distance = position.DistanceTo(goal);
                    var step = speed * dt;
                    positions[cls] = distance <= step
                        ? goal
                        : position + (goal - position) * (step / distance);
                }
            }

            return recorder.Complete(result);
        }

        /// <summary>Deployment line along X, facing north (FakeBattlefield's default attack direction).</summary>
        private static MapVec StartPosition(PlannedFormationClass cls) => cls switch
        {
            PlannedFormationClass.Infantry => new MapVec(0f, 0f),
            PlannedFormationClass.Ranged => new MapVec(15f, 0f),
            PlannedFormationClass.Cavalry => new MapVec(-15f, 0f),
            _ => new MapVec(30f, 0f),
        };
    }
}
