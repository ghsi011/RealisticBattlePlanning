using System;
using System.Collections.Generic;
using System.Linq;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Harness
{
    /// <summary>
    /// Fires a scenario's scripted player inputs against the Plan Monitor at
    /// their battle-relative times, so player-conducted scenarios (signals,
    /// override/resume) run unattended. Pure Core logic driven by the same
    /// clock the recorder uses — the engine harness ticks it, and Layer-1
    /// tests drive it directly. Each action fires exactly once.
    /// </summary>
    public sealed class ScenarioActionScheduler
    {
        private readonly List<ScenarioAction> _pending;
        private int _next;

        public ScenarioActionScheduler(IEnumerable<ScenarioAction> actions)
        {
            _pending = (actions ?? Enumerable.Empty<ScenarioAction>())
                .Where(a => a != null)
                .OrderBy(a => a.AtSeconds)
                .ToList();
        }

        public bool Done => _next >= _pending.Count;

        /// <summary>
        /// Fires every action whose time has passed since the last tick.
        /// Returns human-readable descriptions of what fired (for the log),
        /// or an empty list. Never throws.
        /// </summary>
        public IReadOnlyList<string> Tick(float battleTimeSeconds, PlanMonitor monitor)
        {
            if (monitor == null)
                return Array.Empty<string>();

            List<string> fired = null;
            while (_next < _pending.Count && _pending[_next].AtSeconds <= battleTimeSeconds)
            {
                var action = _pending[_next++];
                (fired ??= new List<string>()).Add(Fire(action, monitor));
            }

            return fired ?? (IReadOnlyList<string>)Array.Empty<string>();
        }

        private static string Fire(ScenarioAction action, PlanMonitor monitor)
        {
            switch (action.Type)
            {
                case ScenarioActionType.Signal:
                    monitor.RaiseExternalSignal(action.Signal);
                    return $"fired signal '{action.Signal}'";

                case ScenarioActionType.Override:
                    foreach (var cls in Resolve(action.Formation, monitor))
                        monitor.NotifyPlayerOverride(cls);
                    return $"overrode {action.Formation}";

                case ScenarioActionType.Resume:
                    foreach (var cls in Resolve(action.Formation, monitor))
                        monitor.RequestResume(cls);
                    return $"resumed {action.Formation}";

                default:
                    return $"unknown action {action.Type}";
            }
        }

        private static IEnumerable<PlannedFormationClass> Resolve(string selector, PlanMonitor monitor)
        {
            if (string.Equals(selector, "all", StringComparison.OrdinalIgnoreCase))
            {
                foreach (PlannedFormationClass cls in Enum.GetValues(typeof(PlannedFormationClass)))
                    if (monitor.Governs(cls))
                        yield return cls;
                yield break;
            }

            if (FormationSelector.ParseClass(selector) is { } parsed && monitor.Governs(parsed))
                yield return parsed;
        }
    }
}
