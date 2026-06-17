using ModDebugKit.Battles;
using ModDebugKit.Io;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ModDebugKit.Tests
{
    public class BattlePresetTests
    {
        [Fact]
        public void Default_preset_is_empire_vs_aserai_and_validates()
        {
            var p = BattlePreset.CreateDefault();
            Assert.Equal("empire", p.Player.Culture);
            Assert.Equal("commander_1", p.Player.Commander);
            Assert.Equal(new[] { 150, 49, 0, 0 }, p.Player.Counts);
            Assert.Equal("aserai", p.Enemy.Culture);
            Assert.Equal(new[] { 120, 40, 40, 0 }, p.Enemy.Counts);
            Assert.True(BattlePresetValidator.IsValid(p));
        }

        [Fact]
        public void Preset_round_trips_through_json()
        {
            var json = DbgJson.Pretty(BattlePreset.CreateDefault());
            Assert.True(DbgJson.TryDeserialize<BattlePreset>(json, out var p, out var error), error);
            Assert.Equal("empire", p.Player.Culture);
            Assert.Equal(120, p.Enemy.Counts[0]);
        }

        [Fact]
        public void Empty_preset_is_valid_all_fields_default()
        {
            // A bare {} preset is legal — every field falls back to a default at build time.
            Assert.True(DbgJson.TryDeserialize<BattlePreset>("{}", out var p, out _));
            Assert.Empty(BattlePresetValidator.Validate(p));
        }

        [Fact]
        public void Partial_preset_keeps_unset_fields_null()
        {
            Assert.True(DbgJson.TryDeserialize<BattlePreset>(
                "{ \"player\": { \"culture\": \"battania\" } }", out var p, out _));
            Assert.Equal("battania", p.Player.Culture);
            Assert.Null(p.Player.Counts);
            Assert.Null(p.Enemy);
            Assert.Empty(BattlePresetValidator.Validate(p));
        }

        [Fact]
        public void Wrong_count_length_is_rejected()
        {
            var p = new BattlePreset { Player = new SidePreset { Counts = new[] { 10, 10, 10 } } };
            var errors = BattlePresetValidator.Validate(p);
            Assert.Contains(errors, e => e.Contains("player.counts must have 4"));
        }

        [Fact]
        public void Negative_count_is_rejected()
        {
            var p = new BattlePreset { Enemy = new SidePreset { Counts = new[] { 10, -5, 0, 0 } } };
            var errors = BattlePresetValidator.Validate(p);
            Assert.Contains(errors, e => e.Contains("enemy.counts[1] is negative"));
        }

        [Theory]
        [InlineData("Defender", true)]
        [InlineData("attacker", true)]
        [InlineData("sideways", false)]
        public void Player_side_parsing(string text, bool ok)
        {
            Assert.Equal(ok, BattlePresetValidator.TryParseSide(text, out _));
        }

        [Theory]
        [InlineData("Commander", PlayerRoleKind.Commander)]
        [InlineData("sergeant", PlayerRoleKind.Sergeant)]
        [InlineData("soldier", PlayerRoleKind.Sergeant)]
        public void Player_role_parsing(string text, PlayerRoleKind expected)
        {
            Assert.True(BattlePresetValidator.TryParseRole(text, out var role));
            Assert.Equal(expected, role);
        }

        [Fact]
        public void Bad_side_role_and_time_are_all_reported()
        {
            var p = new BattlePreset { PlayerSide = "north", PlayerType = "wizard", TimeOfDay = 30f };
            var errors = BattlePresetValidator.Validate(p);
            Assert.Contains(errors, e => e.Contains("playerSide"));
            Assert.Contains(errors, e => e.Contains("playerType"));
            Assert.Contains(errors, e => e.Contains("timeOfDay"));
        }
    }
}
