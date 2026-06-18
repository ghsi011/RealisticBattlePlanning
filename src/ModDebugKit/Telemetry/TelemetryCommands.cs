using ModDebugKit.Commands;

namespace ModDebugKit.Telemetry
{
    /// <summary>Control surface for the flight recorder: toggle, clear, and report status.</summary>
    public static class TelemetryCommands
    {
        public static void RegisterAll(CommandDispatcher dispatcher)
        {
            dispatcher.Register("dbg.telemetry",
                "dbg.telemetry [on|off|clear|status] - control the flight recorder (telemetry.jsonl)",
                Telemetry);
        }

        private static DbgOutcome Telemetry(DbgCommand command)
        {
            switch (command.Arg(0)?.Trim().ToLowerInvariant())
            {
                case "on":
                    TelemetryLog.Enabled = true;
                    return DbgOutcome.Success("telemetry on");
                case "off":
                    TelemetryLog.Enabled = false;
                    return DbgOutcome.Success("telemetry off");
                case "clear":
                    TelemetryLog.Clear();
                    return DbgOutcome.Success("telemetry cleared");
                case null:
                case "status":
                    return DbgOutcome.Success(
                        $"telemetry {(TelemetryLog.Enabled ? "on" : "off")} -> {ModDebugKitRuntime.Paths.Telemetry}",
                        new { enabled = TelemetryLog.Enabled, path = ModDebugKitRuntime.Paths.Telemetry });
                default:
                    return DbgOutcome.Failure("usage: dbg.telemetry [on|off|clear|status]");
            }
        }
    }
}
