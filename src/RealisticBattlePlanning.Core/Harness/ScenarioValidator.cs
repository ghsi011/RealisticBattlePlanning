using System.Collections.Generic;

namespace RealisticBattlePlanning.Harness
{
    /// <summary>
    /// Structural validation of a scenario, mirroring PlanValidator's role
    /// for plans: every assertion parameter is nullable for JSON's sake, but
    /// each assertion type has required ones — a missing bound must fail in
    /// the console at arm time, not as a misleading assertion failure after
    /// a full battle (lifted float? comparisons against null are just false).
    /// </summary>
    public static class ScenarioValidator
    {
        public static List<string> Validate(ScenarioSpec spec)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(spec.PlanFile))
                errors.Add("planFile is missing.");

            if (spec.TimeLimitSeconds is <= 0f)
                errors.Add("timeLimitSeconds must be positive.");

            if (spec.Assertions.Count == 0)
                errors.Add("Scenario has no assertions; a run could never fail.");

            for (var i = 0; i < spec.Assertions.Count; i++)
            {
                var assertion = spec.Assertions[i];
                var who = $"Assertion {i + 1} ({assertion.Type})";

                void Require(bool present, string parameter)
                {
                    if (!present)
                        errors.Add($"{who}: '{parameter}' is required.");
                }

                void RequireBand()
                {
                    Require(assertion.MinSeconds != null, "minSeconds");
                    Require(assertion.MaxSeconds != null, "maxSeconds");
                    if (assertion.MinSeconds > assertion.MaxSeconds)
                        errors.Add($"{who}: minSeconds exceeds maxSeconds.");
                }

                switch (assertion.Type)
                {
                    case AssertionType.StageActivatedBetween:
                        Require(assertion.Formation != null, "formation");
                        Require(assertion.Stage is >= 1, "stage (1-based)");
                        RequireBand();
                        break;

                    case AssertionType.StageActivatedAfterPrevious:
                        Require(assertion.Formation != null, "formation");
                        Require(assertion.Stage is >= 2, "stage (needs a previous stage, so >= 2)");
                        RequireBand();
                        break;

                    case AssertionType.SignalEmittedBetween:
                        Require(!string.IsNullOrWhiteSpace(assertion.Signal), "signal");
                        RequireBand();
                        break;

                    case AssertionType.StageAfterSignal:
                        Require(assertion.Formation != null, "formation");
                        Require(assertion.Stage is >= 1, "stage (1-based)");
                        Require(!string.IsNullOrWhiteSpace(assertion.Signal), "signal");
                        Require(assertion.MaxDelaySeconds is >= 0f, "maxDelaySeconds");
                        break;

                    case AssertionType.ReachesAnchor:
                        Require(assertion.Formation != null, "formation");
                        Require(!string.IsNullOrWhiteSpace(assertion.Anchor), "anchor");
                        Require(assertion.WithinMeters is > 0f, "withinMeters");
                        if (assertion.BySeconds is <= 0f)
                            errors.Add($"{who}: bySeconds must be positive.");
                        break;

                    case AssertionType.PlanEventBetween:
                        Require(assertion.Formation != null, "formation");
                        Require(assertion.Event != null, "event");
                        // Only the plan-control events carry a formation+time the
                        // way this assertion reads them; the others (StageActivated,
                        // SignalEmitted, WaypointReached) have their own assertions
                        // and would match ambiguously here.
                        if (assertion.Event is { } ev && !IsPlanControlEvent(ev))
                            errors.Add($"{who}: event must be one of PlanSuspended/PlanResumed/PlanAborted/StageSkipped/PlanHolding; got {ev}.");
                        RequireBand();
                        break;

                    default:
                        errors.Add($"{who}: unknown assertion type.");
                        break;
                }
            }

            for (var i = 0; i < spec.Actions.Count; i++)
            {
                var action = spec.Actions[i];
                var who = $"Action {i + 1} ({action.Type})";

                if (action.AtSeconds < 0f)
                    errors.Add($"{who}: atSeconds must be >= 0.");

                switch (action.Type)
                {
                    case ScenarioActionType.Signal:
                        if (string.IsNullOrWhiteSpace(action.Signal))
                            errors.Add($"{who}: 'signal' is required.");
                        break;

                    case ScenarioActionType.Override:
                    case ScenarioActionType.Resume:
                        if (string.IsNullOrWhiteSpace(action.Formation))
                            errors.Add($"{who}: 'formation' is required (a class name or 'all').");
                        break;

                    default:
                        errors.Add($"{who}: unknown action type.");
                        break;
                }
            }

            return errors;
        }

        private static bool IsPlanControlEvent(RecordedEventKind kind)
            => kind == RecordedEventKind.PlanSuspended
               || kind == RecordedEventKind.PlanResumed
               || kind == RecordedEventKind.PlanAborted
               || kind == RecordedEventKind.StageSkipped
               || kind == RecordedEventKind.PlanHolding;
    }
}
