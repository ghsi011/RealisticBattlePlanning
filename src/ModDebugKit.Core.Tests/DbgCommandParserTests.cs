using ModDebugKit.Commands;
using Xunit;

namespace ModDebugKit.Tests
{
    public class DbgCommandParserTests
    {
        [Fact]
        public void Parses_namespace_name_and_args()
        {
            Assert.True(DbgCommandParser.TryParse("dbg.snapshot foo 12", out var cmd, out _));
            Assert.Equal("dbg", cmd.Namespace);
            Assert.Equal("snapshot", cmd.Name);
            Assert.Equal("dbg.snapshot", cmd.Full);
            Assert.Equal(new[] { "foo", "12" }, cmd.Args);
            Assert.Equal("dbg.snapshot foo 12", cmd.Raw);
        }

        [Fact]
        public void No_dot_means_empty_namespace()
        {
            Assert.True(DbgCommandParser.TryParse("ping", out var cmd, out _));
            Assert.Equal(string.Empty, cmd.Namespace);
            Assert.Equal("ping", cmd.Name);
            Assert.Equal("ping", cmd.Full);
        }

        [Fact]
        public void Only_the_first_dot_splits_namespace_from_name()
        {
            Assert.True(DbgCommandParser.TryParse("dbg.camp.goto town", out var cmd, out _));
            Assert.Equal("dbg", cmd.Namespace);
            Assert.Equal("camp.goto", cmd.Name);
            Assert.Equal("dbg.camp.goto", cmd.Full);
        }

        [Fact]
        public void Double_quotes_group_an_arg_with_spaces()
        {
            Assert.True(DbgCommandParser.TryParse("dbg.shot \"my best shot\"", out var cmd, out _));
            Assert.Equal(new[] { "my best shot" }, cmd.Args);
        }

        [Fact]
        public void Backslash_escapes_the_next_character()
        {
            Assert.True(DbgCommandParser.TryParse("dbg.echo a\\ b \"say \\\"hi\\\"\"", out var cmd, out _));
            Assert.Equal(new[] { "a b", "say \"hi\"" }, cmd.Args);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("# a comment")]
        [InlineData("// also a comment")]
        public void Blank_and_comment_lines_are_skipped(string line)
        {
            Assert.False(DbgCommandParser.TryParse(line, out var cmd, out var reason));
            Assert.Null(cmd);
            Assert.False(string.IsNullOrEmpty(reason));
        }

        [Fact]
        public void Surrounding_whitespace_is_trimmed()
        {
            Assert.True(DbgCommandParser.TryParse("   dbg.ping   ", out var cmd, out _));
            Assert.Equal("dbg.ping", cmd.Full);
            Assert.Empty(cmd.Args);
        }

        [Fact]
        public void Leading_utf8_bom_is_stripped()
        {
            // A UTF-8 BOM (U+FEFF) prepended by a writing tool lands on the first line.
            var withBom = (char)0xFEFF + "dbg.ping";
            Assert.True(DbgCommandParser.TryParse(withBom, out var cmd, out _));
            Assert.Equal("dbg.ping", cmd.Full);
            Assert.Equal("dbg", cmd.Namespace);
            Assert.Equal("ping", cmd.Name);
        }

        [Fact]
        public void Arg_index_out_of_range_returns_null()
        {
            Assert.True(DbgCommandParser.TryParse("dbg.ping", out var cmd, out _));
            Assert.Null(cmd.Arg(0));
        }
    }
}
