using System;
using System.IO;
using System.Linq;
using RealisticBattlePlanning.Harness;
using RealisticBattlePlanning.Planning;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// Keeps the shipped scenario pack honest: every scenario file must
    /// parse, its plan must validate, and its assertions must pass against a
    /// simulated run — so an in-game pack failure points at the engine
    /// adapters, not at the scenarios themselves (front-loaded-risk rule).
    /// </summary>
    public class HarnessPackTests
    {
        private static string PackDir => Path.Combine(AppContext.BaseDirectory, "HarnessPack");

        public static TheoryData<string> ScenarioNames()
        {
            var data = new TheoryData<string>();
            foreach (var file in Directory.GetFiles(PackDir, "*.scenario.json"))
                data.Add(Path.GetFileName(file).Replace(".scenario.json", ""));
            return data;
        }

        [Fact]
        public void ThePackContainsTheTwoSmokeScenarios()
        {
            var names = Directory.GetFiles(PackDir, "*.scenario.json")
                .Select(f => Path.GetFileName(f).Replace(".scenario.json", ""))
                .ToList();
            Assert.Contains("walk_waypoints", names);
            Assert.Contains("signal_coordination", names);
        }

        [Theory]
        [MemberData(nameof(ScenarioNames))]
        public void ScenarioParsesPlanValidatesAndAssertionsPassOnASimulatedRun(string name)
        {
            var spec = LoadSpec(name);
            var plan = LoadPlan(spec);

            var record = HarnessSimulation.Run(plan, name, enemies: SimEnemiesFor(name));
            var result = ScenarioEvaluator.Evaluate(spec, record);

            Assert.True(result.Pass, result.Summary());
        }

        /// <summary>
        /// Scripted stand-ins for the enemy army each scenario expects
        /// in-game: an aggressive chaser for A6, a static threat for the
        /// screen, none where the plan is self-contained.
        /// </summary>
        private static System.Collections.Generic.IReadOnlyList<SimEnemy> SimEnemiesFor(string name) => name switch
        {
            "a6_feigned_retreat" => new[] { new SimEnemy(1, 0, 200, speed: 4f, cls: Planning.Model.PlannedFormationClass.Infantry) },
            "screen_guard" => new[] { new SimEnemy(1, -20, 120) },
            _ => null,
        };

        [Fact]
        public void ACrippledRunFailsTheWalkScenarioReadably()
        {
            var spec = LoadSpec("walk_waypoints");
            var plan = LoadPlan(spec);

            // Troops at a tenth of walking speed: the path is never finished.
            var record = HarnessSimulation.Run(plan, "walk_waypoints", speed: 0.2f);
            var result = ScenarioEvaluator.Evaluate(spec, record);

            Assert.False(result.Pass);
            var failed = result.Assertions.Where(a => !a.Pass).ToList();
            Assert.NotEmpty(failed);
            Assert.All(failed, a => Assert.False(string.IsNullOrWhiteSpace(a.Message)));

            // And the diff against a passing baseline reports it readably.
            var baseline = new PackResult { Scenarios = { ScenarioEvaluator.Evaluate(spec, HarnessSimulation.Run(plan, "walk_waypoints")) } };
            var current = new PackResult { Scenarios = { result } };
            var diff = ResultsDiff.Diff(baseline, current);

            Assert.False(diff.Clean);
            Assert.Contains(diff.Lines, l => l.Contains("PASS->FAIL walk_waypoints"));
        }

        private static ScenarioSpec LoadSpec(string name)
        {
            var json = File.ReadAllText(Path.Combine(PackDir, $"{name}.scenario.json"));
            Assert.True(HarnessSerializer.TryDeserializeScenario(json, out var spec, out var error), error);
            spec.Name = name;

            var errors = ScenarioValidator.Validate(spec);
            Assert.True(errors.Count == 0, string.Join("; ", errors));
            return spec;
        }

        private static Planning.Model.BattlePlan LoadPlan(ScenarioSpec spec)
        {
            var json = File.ReadAllText(Path.Combine(PackDir, spec.PlanFile));
            Assert.True(PlanSerializer.TryDeserialize(json, out var plan, out var error), error);

            var validation = PlanValidator.Validate(plan);
            Assert.True(validation.IsValid, string.Join("; ", validation.Errors));
            return plan;
        }
    }
}
