using System.Collections.Generic;
using System.Linq;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Harness;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// A scripted enemy blob for the simulation: chases the nearest own
    /// formation at a constant speed (the aggressive-AI stand-in the A6
    /// scenario needs), or stands still at speed 0.
    /// </summary>
    internal sealed class SimEnemy
    {
        public SimEnemy(int id, float x, float y, float speed = 0f, PlannedFormationClass? cls = null)
        {
            Id = id;
            Position = new MapVec(x, y);
            Speed = speed;
            Class = cls;
        }

        public int Id { get; }
        public MapVec Position { get; set; }
        public float Speed { get; }
        public PlannedFormationClass? Class { get; }
    }

    /// <summary>
    /// Kinematic stand-in for a harness battle: drives PlanMonitor and
    /// RunRecorder over a scripted clock, moving each formation toward the
    /// move target its plan events last issued — the same contract the engine
    /// executor follows (steering events update the goal; Charge chases the
    /// nearest enemy; FireControl leaves movement untouched). Lets the
    /// shipped scenario pack be validated end to end without the game.
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
            string result = "PlayerVictory",
            IReadOnlyList<SimEnemy> enemies = null)
        {
            var monitor = new PlanMonitor(plan);
            var recorder = new RunRecorder(scenarioName, plan);
            var positions = plan.Formations.ToDictionary(f => f.Formation, f => StartPosition(f.Formation));
            var targets = new Dictionary<PlannedFormationClass, MapVec?>();
            var charging = new HashSet<PlannedFormationClass>();
            enemies ??= new List<SimEnemy>();

            for (var time = 0f; time <= durationSeconds; time += dt)
            {
                var snapshot = new FakeBattlefield(time, started: true);
                foreach (var (cls, position) in positions)
                    snapshot.WithOwn(cls, position.X, position.Y);
                foreach (var enemy in enemies)
                    snapshot.WithEnemy(enemy.Id, enemy.Position.X, enemy.Position.Y, cls: enemy.Class);

                var events = monitor.Tick(snapshot);
                recorder.Tick(snapshot, events);

                foreach (var planEvent in events)
                {
                    switch (planEvent)
                    {
                        case StageActivated activated:
                            ApplyActivation(activated, targets, charging);
                            break;
                        case MoveTargetChanged moved:
                            targets[moved.Formation] = moved.Target;
                            break;
                        case SteeringTargetChanged steering:
                            targets[steering.Formation] = steering.Target;
                            break;
                    }
                }

                foreach (var cls in charging)
                {
                    if (NearestEnemyTo(positions[cls], enemies) is { } prey)
                        targets[cls] = prey.Position;
                }

                foreach (var cls in positions.Keys.ToList())
                {
                    if (targets.TryGetValue(cls, out var maybeTarget) && maybeTarget is { } goal)
                        positions[cls] = StepToward(positions[cls], goal, speed * dt);
                }

                foreach (var enemy in enemies)
                {
                    if (enemy.Speed <= 0f || positions.Count == 0)
                        continue;
                    var prey = positions.Values.OrderBy(p => p.DistanceTo(enemy.Position)).First();
                    enemy.Position = StepToward(enemy.Position, prey, enemy.Speed * dt);
                }
            }

            return recorder.Complete(result);
        }

        /// <summary>Mirrors FormationOrderExecutor's movement semantics per directive type.</summary>
        private static void ApplyActivation(
            StageActivated activated,
            Dictionary<PlannedFormationClass, MapVec?> targets,
            HashSet<PlannedFormationClass> charging)
        {
            charging.Remove(activated.Formation);
            switch (activated.Directive.Spec.Type)
            {
                case DirectiveType.MoveTo:
                case DirectiveType.FeignRetreat:
                case DirectiveType.PullBack:
                    targets[activated.Formation] = activated.Directive.FirstMoveTarget;
                    break;

                case DirectiveType.Charge:
                    charging.Add(activated.Formation);
                    break;

                case DirectiveType.FireControl:
                    break; // Movement untouched, like the engine.

                case DirectiveType.Skirmish:
                case DirectiveType.FlankArc:
                case DirectiveType.Screen:
                case DirectiveType.Follow:
                    break; // Movement arrives via the steering stream.

                default:
                    targets[activated.Formation] = null;
                    break;
            }
        }

        private static SimEnemy NearestEnemyTo(MapVec from, IReadOnlyList<SimEnemy> enemies)
            => enemies.OrderBy(e => from.DistanceTo(e.Position)).FirstOrDefault();

        private static MapVec StepToward(MapVec from, MapVec goal, float step)
        {
            var distance = from.DistanceTo(goal);
            return distance <= step ? goal : from + (goal - from) * (step / distance);
        }

        /// <summary>Deployment lines along X, facing north (FakeBattlefield's default attack direction).</summary>
        private static MapVec StartPosition(PlannedFormationClass cls) => cls switch
        {
            PlannedFormationClass.Infantry => new MapVec(-20f, 0f),
            PlannedFormationClass.HeavyInfantry => new MapVec(20f, 0f),
            PlannedFormationClass.Ranged => new MapVec(0f, -10f),
            PlannedFormationClass.Skirmisher => new MapVec(0f, 10f),
            PlannedFormationClass.Cavalry => new MapVec(-40f, 0f),
            PlannedFormationClass.HeavyCavalry => new MapVec(40f, 0f),
            PlannedFormationClass.HorseArcher => new MapVec(-20f, 20f),
            _ => new MapVec(20f, 20f), // LightCavalry
        };
    }
}
