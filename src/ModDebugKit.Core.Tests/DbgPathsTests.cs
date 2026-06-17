using System.IO;
using ModDebugKit.Io;
using Xunit;

namespace ModDebugKit.Tests
{
    public class DbgPathsTests
    {
        private static readonly string Root = Path.Combine("X:", "Modules", "ModDebugKit", "Debug");

        [Fact]
        public void Io_and_document_paths_hang_off_the_root()
        {
            var p = new DbgPaths(Root);
            Assert.Equal(Path.Combine(Root, "io"), p.IoDir);
            Assert.Equal(Path.Combine(Root, "io", "in.txt"), p.CommandIn);
            Assert.Equal(Path.Combine(Root, "io", "out.jsonl"), p.CommandOut);
            Assert.Equal(Path.Combine(Root, "battle_state.json"), p.BattleState);
            Assert.Equal(Path.Combine(Root, "telemetry.jsonl"), p.Telemetry);
            Assert.Equal(Path.Combine(Root, "moddebugkit.log"), p.Log);
        }

        [Fact]
        public void Resolve_keeps_an_absolute_path_as_is()
        {
            var p = new DbgPaths(Root);
            var absolute = Path.Combine("C:", "tmp", "state.json");
            Assert.Equal(absolute, p.Resolve(absolute));
        }

        [Fact]
        public void Resolve_makes_a_relative_path_root_relative()
        {
            var p = new DbgPaths(Root);
            Assert.Equal(Path.Combine(Root, "scratch", "state.json"), p.Resolve(Path.Combine("scratch", "state.json")));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Resolve_of_nothing_is_null(string input)
        {
            var p = new DbgPaths(Root);
            Assert.Null(p.Resolve(input));
        }
    }
}
