using System.Collections.Generic;

namespace ModDebugKit.Battles
{
    /// <summary>Player side, engine-free (the factory maps it to the engine enum).</summary>
    public enum PlayerSideKind { Defender, Attacker }

    /// <summary>Player role, engine-free. The engine custom battle has only these two (no spectator).</summary>
    public enum PlayerRoleKind { Commander, Sergeant }

    /// <summary>
    /// Validates a <see cref="BattlePreset"/> before the engine acts on it, and
    /// parses its string-typed side/role into engine-free enums. Pure and
    /// unit-tested so a bad preset is rejected with a clear message rather than
    /// throwing deep inside the custom-battle builder. A null field means
    /// "use the default" and is valid; only a present-but-wrong value is an
    /// error.
    /// </summary>
    public static class BattlePresetValidator
    {
        public static IReadOnlyList<string> Validate(BattlePreset preset)
        {
            var errors = new List<string>();
            if (preset == null)
            {
                errors.Add("preset is null");
                return errors;
            }

            if (preset.PlayerSide != null && TryParseSide(preset.PlayerSide, out _) == false)
                errors.Add($"playerSide '{preset.PlayerSide}' is not Defender or Attacker");
            if (preset.PlayerType != null && TryParseRole(preset.PlayerType, out _) == false)
                errors.Add($"playerType '{preset.PlayerType}' is not Commander or Sergeant");
            if (preset.TimeOfDay is { } tod && (tod < 0f || tod > 24f))
                errors.Add($"timeOfDay {tod} is outside 0-24");

            ValidateSide(preset.Player, "player", errors);
            ValidateSide(preset.Enemy, "enemy", errors);
            return errors;
        }

        public static bool IsValid(BattlePreset preset) => Validate(preset).Count == 0;

        public static bool TryParseSide(string text, out PlayerSideKind side)
        {
            switch (text?.Trim().ToLowerInvariant())
            {
                case "defender": side = PlayerSideKind.Defender; return true;
                case "attacker": side = PlayerSideKind.Attacker; return true;
                default: side = PlayerSideKind.Defender; return false;
            }
        }

        public static bool TryParseRole(string text, out PlayerRoleKind role)
        {
            switch (text?.Trim().ToLowerInvariant())
            {
                case "commander": role = PlayerRoleKind.Commander; return true;
                case "sergeant":
                case "soldier": role = PlayerRoleKind.Sergeant; return true;
                default: role = PlayerRoleKind.Commander; return false;
            }
        }

        private static void ValidateSide(SidePreset side, string which, List<string> errors)
        {
            if (side?.Counts == null)
                return; // null counts -> default roster, which is valid
            if (side.Counts.Length != 4)
            {
                errors.Add($"{which}.counts must have 4 entries [inf, rng, cav, ha], got {side.Counts.Length}");
                return;
            }
            for (var i = 0; i < 4; i++)
            {
                if (side.Counts[i] < 0)
                    errors.Add($"{which}.counts[{i}] is negative ({side.Counts[i]})");
            }
        }
    }
}
