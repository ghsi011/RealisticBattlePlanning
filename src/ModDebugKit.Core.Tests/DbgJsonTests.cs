using ModDebugKit.Commands;
using ModDebugKit.Io;
using ModDebugKit.Snapshots;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ModDebugKit.Tests
{
    public class DbgJsonTests
    {
        [Fact]
        public void Line_form_has_no_embedded_newline_so_it_is_jsonl_safe()
        {
            var result = new DbgResult { Seq = 1, Ok = true, Command = "dbg.ping", Raw = "dbg.ping", Message = "pong" };
            var line = DbgJson.Line(result);
            Assert.DoesNotContain("\n", line);
            Assert.DoesNotContain("\r", line);
        }

        [Fact]
        public void Result_round_trips_and_omits_null_fields()
        {
            var result = new DbgResult
            {
                Seq = 7,
                TimestampUtc = "2026-06-17T00:00:00.0000000Z",
                Ok = true,
                Command = "dbg.snapshot",
                Raw = "dbg.snapshot",
                Message = "ok",
            };

            var json = DbgJson.Line(result);
            var token = JObject.Parse(json);

            Assert.Equal(7, (int)token["seq"]);
            Assert.True((bool)token["ok"]);
            Assert.Equal("dbg.snapshot", (string)token["cmd"]);
            // Null Error/Data/MissionTime are omitted, not serialized as null.
            Assert.Null(token["error"]);
            Assert.Null(token["data"]);
            Assert.Null(token["missionTime"]);
        }

        [Fact]
        public void Failure_result_carries_the_error()
        {
            var result = new DbgResult { Seq = 2, Ok = false, Command = "dbg.snapshot", Raw = "dbg.snapshot", Message = "no mission", Error = "no mission" };
            var token = JObject.Parse(DbgJson.Line(result));
            Assert.False((bool)token["ok"]);
            Assert.Equal("no mission", (string)token["error"]);
        }

        [Fact]
        public void Battle_state_serializes_expected_shape()
        {
            var dto = new BattleStateDto
            {
                CapturedAtUtc = "2026-06-17T00:00:00.0000000Z",
                SceneName = "battle_terrain_029",
                MissionTime = 12.5f,
                BattleStarted = true,
                PlayerTeamIndex = 0,
            };
            dto.Formations.Add(new FormationStateDto
            {
                Side = "player",
                TeamIndex = 0,
                Number = 1,
                SlotClass = "Infantry",
                RepresentativeClass = "Infantry",
                Count = 100,
                Composition = CompositionClassifier.Classify(90, 10, 0, 0),
                Position = new Vec2Dto(10f, 20f),
                Facing = new Vec2Dto(0f, 1f),
                Order = new OrderDto { Type = "Move", MoveTarget = new Vec2Dto(30f, 40f), TargetIsValid = true, TargetHasNavMeshFace = false },
                CasualtiesPercent = 0f,
                Broken = false,
            });

            var json = DbgJson.Pretty(dto);
            var token = JObject.Parse(json);

            var formation = token["formations"][0];
            Assert.Equal(1, (int)formation["number"]);
            Assert.Equal("Mostly Infantry", (string)formation["composition"]["label"]);
            Assert.Equal("Move", (string)formation["order"]["type"]);
            // The whole point: a Move order whose target has no nav-mesh face is visible in the JSON.
            Assert.False((bool)formation["order"]["targetHasNavMeshFace"]);
            Assert.Equal(30f, (float)formation["order"]["moveTarget"]["x"]);
        }
    }
}
