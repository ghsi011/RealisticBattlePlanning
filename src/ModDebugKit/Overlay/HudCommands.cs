using System;
using ModDebugKit.Commands;
using ModDebugKit.Diagnostics;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ModDebugKit.Overlay
{
    /// <summary>
    /// The on-screen debug text channel (the readily-buildable, verifiable part
    /// of the M2.4 overlay): <c>dbg.hud &lt;msg&gt;</c> pushes a line to the
    /// game's information channel so a human watching sees it. The richer
    /// persistent Gauntlet overlay (formation numbers/orders/gizmos) is deferred
    /// — its data already lives in battle_state.json / telemetry.jsonl, which is
    /// what the agent actually reads.
    /// </summary>
    public static class HudCommands
    {
        public static void RegisterAll(CommandDispatcher dispatcher)
        {
            dispatcher.Register("dbg.hud", "dbg.hud <message> - show a message on the in-game debug channel", Hud);
        }

        private static DbgOutcome Hud(DbgCommand command)
        {
            var message = command.Args.Count > 0 ? string.Join(" ", command.Args) : null;
            if (string.IsNullOrWhiteSpace(message))
                return DbgOutcome.Failure("usage: dbg.hud <message>");

            try
            {
                InformationManager.DisplayMessage(new InformationMessage($"[MDK] {message}", Color.FromUint(0xFF00FFFF)));
            }
            catch (Exception e)
            {
                DbgLog.Error("dbg.hud failed.", e);
                return DbgOutcome.Failure($"hud failed: {e.Message}");
            }

            return DbgOutcome.Success($"hud: {message}");
        }
    }
}
