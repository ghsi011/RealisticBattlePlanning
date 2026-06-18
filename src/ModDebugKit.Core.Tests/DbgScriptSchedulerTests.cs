using System.Linq;
using ModDebugKit.Io;
using ModDebugKit.Scripting;
using Xunit;

namespace ModDebugKit.Tests
{
    public class DbgScriptSchedulerTests
    {
        private static DbgScript Script(params DbgScriptStep[] steps)
        {
            var s = new DbgScript { Name = "t" };
            s.Steps.AddRange(steps);
            return s;
        }

        [Fact]
        public void Fires_steps_in_time_order_as_elapsed_advances()
        {
            var sched = new DbgScriptScheduler(Script(
                new DbgScriptStep(5f, "dbg.snapshot late"),
                new DbgScriptStep(0f, "dbg.battle"),
                new DbgScriptStep(2f, "dbg.ready")));

            Assert.Equal(new[] { "dbg.battle" }, sched.Due(0f).Select(s => s.Do));
            Assert.Empty(sched.Due(1f).Select(s => s.Do));
            Assert.Equal(new[] { "dbg.ready" }, sched.Due(2.5f).Select(s => s.Do));
            Assert.False(sched.Done);
            Assert.Equal(new[] { "dbg.snapshot late" }, sched.Due(99f).Select(s => s.Do));
            Assert.True(sched.Done);
        }

        [Fact]
        public void Multiple_due_at_once_fire_together_in_order()
        {
            var sched = new DbgScriptScheduler(Script(
                new DbgScriptStep(1f, "a"),
                new DbgScriptStep(1f, "b"),
                new DbgScriptStep(3f, "c")));
            Assert.Equal(new[] { "a", "b" }, sched.Due(2f).Select(s => s.Do));
            Assert.Equal(new[] { "c" }, sched.Due(5f).Select(s => s.Do));
        }

        [Fact]
        public void Blank_steps_are_dropped()
        {
            var sched = new DbgScriptScheduler(Script(
                new DbgScriptStep(0f, "  "),
                new DbgScriptStep(0f, null),
                new DbgScriptStep(0f, "dbg.ping")));
            Assert.Equal(1, sched.Count);
            Assert.Equal(new[] { "dbg.ping" }, sched.Due(0f).Select(s => s.Do));
        }

        [Fact]
        public void Round_trips_through_json()
        {
            var json = DbgJson.Pretty(Script(new DbgScriptStep(0f, "dbg.battle"), new DbgScriptStep(15f, "dbg.ready")));
            Assert.True(DbgJson.TryDeserialize<DbgScript>(json, out var parsed, out var err), err);
            Assert.Equal(2, parsed.Steps.Count);
            Assert.Equal(15f, parsed.Steps[1].At);
            Assert.Equal("dbg.ready", parsed.Steps[1].Do);
        }
    }
}
