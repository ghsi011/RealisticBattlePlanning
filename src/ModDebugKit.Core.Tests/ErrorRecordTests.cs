using ModDebugKit.Diagnostics;
using ModDebugKit.Io;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ModDebugKit.Tests
{
    public class ErrorRecordTests
    {
        [Fact]
        public void Line_form_is_single_line()
        {
            var rec = new ErrorRecord { Seq = 1, TimestampUtc = "x", Source = "modkit", Message = "boom", Stack = "line1\nline2" };
            var line = DbgJson.Line(rec);
            // The record is one JSONL line; the multi-line stack is JSON-escaped, not a real newline.
            Assert.DoesNotContain("\n", line.Replace("\\n", ""));
        }

        [Fact]
        public void Serializes_expected_shape_and_omits_nulls()
        {
            var rec = new ErrorRecord
            {
                Seq = 3,
                TimestampUtc = "2026-06-18T00:00:00.0000000Z",
                Source = "appdomain",
                Message = "NRE",
                ExceptionType = "System.NullReferenceException",
                Terminating = true,
            };
            var token = JObject.Parse(DbgJson.Line(rec));
            Assert.Equal(3, (int)token["seq"]);
            Assert.Equal("appdomain", (string)token["source"]);
            Assert.Equal("System.NullReferenceException", (string)token["exceptionType"]);
            Assert.True((bool)token["terminating"]);
            Assert.Null(token["stack"]);    // not set -> omitted
            Assert.Null(token["snapshot"]); // not set -> omitted
        }

        [Fact]
        public void Snapshot_path_is_emitted_when_set()
        {
            var rec = new ErrorRecord { Seq = 1, TimestampUtc = "x", Source = "modkit", Message = "boom", Snapshot = "error_snapshot.json" };
            var token = JObject.Parse(DbgJson.Line(rec));
            Assert.Equal("error_snapshot.json", (string)token["snapshot"]);
        }

        [Fact]
        public void False_terminating_is_present_not_omitted()
        {
            // Only null is omitted; a false nullable bool must still serialize as false.
            var rec = new ErrorRecord { Seq = 1, TimestampUtc = "x", Source = "appdomain", Message = "m", Terminating = false };
            var token = JObject.Parse(DbgJson.Line(rec));
            Assert.NotNull(token["terminating"]);
            Assert.False((bool)token["terminating"]);
        }
    }
}
