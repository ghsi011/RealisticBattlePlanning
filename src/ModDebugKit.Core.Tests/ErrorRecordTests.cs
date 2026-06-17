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
    }
}
