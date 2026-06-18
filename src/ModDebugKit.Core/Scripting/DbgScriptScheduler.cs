using System.Collections.Generic;
using System.Linq;

namespace ModDebugKit.Scripting
{
    /// <summary>
    /// Fires a script's steps in time order as elapsed time advances. Pure and
    /// unit-tested: the engine feeds it the elapsed seconds each tick and runs
    /// whatever it yields. Steps are ordered by <see cref="DbgScriptStep.At"/>;
    /// blank/empty steps are dropped.
    /// </summary>
    public sealed class DbgScriptScheduler
    {
        private readonly List<DbgScriptStep> _ordered;
        private int _cursor;

        public DbgScriptScheduler(DbgScript script)
        {
            _ordered = (script?.Steps ?? new List<DbgScriptStep>())
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Do))
                .OrderBy(s => s.At)
                .ToList();
        }

        public int Count => _ordered.Count;

        public bool Done => _cursor >= _ordered.Count;

        /// <summary>Yields the steps now due at <paramref name="elapsedSeconds"/>, advancing the cursor.</summary>
        public IEnumerable<DbgScriptStep> Due(float elapsedSeconds)
        {
            while (_cursor < _ordered.Count && _ordered[_cursor].At <= elapsedSeconds)
            {
                yield return _ordered[_cursor];
                _cursor++;
            }
        }
    }
}
