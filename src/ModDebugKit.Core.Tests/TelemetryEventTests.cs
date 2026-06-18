using ModDebugKit.Io;
using ModDebugKit.Telemetry;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ModDebugKit.Tests
{
    public class TelemetryEventTests
    {
        [Fact]
        public void Line_form_is_single_line_jsonl()
        {
            var ev = new TelemetryEvent { Seq = 1, TimestampUtc = "2026-06-18T00:00:00.0000000Z", Kind = TelemetryKinds.MissionStart, Message = "battle_terrain_029" };
            var line = DbgJson.Line(ev);
            Assert.DoesNotContain("\n", line);
            Assert.DoesNotContain("\r", line);
        }

        [Fact]
        public void Serializes_expected_shape_and_omits_nulls()
        {
            var ev = new TelemetryEvent
            {
                Seq = 5,
                TimestampUtc = "2026-06-18T00:00:00.0000000Z",
                MissionTime = 12.5f,
                Kind = TelemetryKinds.AgentRemoved,
                Data = new { agent = "Recruit", state = "Killed" },
            };
            var token = JObject.Parse(DbgJson.Line(ev));
            Assert.Equal(5, (int)token["seq"]);
            Assert.Equal("agent_removed", (string)token["kind"]);
            Assert.Equal(12.5f, (float)token["t"]);
            Assert.Equal("Recruit", (string)token["data"]["agent"]);
            // No message set -> omitted.
            Assert.Null(token["msg"]);
        }

        [Fact]
        public void Mission_time_omitted_when_null()
        {
            var ev = new TelemetryEvent { Seq = 2, TimestampUtc = "x", Kind = TelemetryKinds.MissionEnd };
            var token = JObject.Parse(DbgJson.Line(ev));
            Assert.Null(token["t"]);
        }
    }
}
