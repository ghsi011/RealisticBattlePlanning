using System;
using System.Collections.Generic;
using ModDebugKit.Diagnostics;
using ModDebugKit.Observability;
using ModDebugKit.Snapshots;
using TaleWorlds.MountAndBlade;

namespace ModDebugKit.Telemetry
{
    /// <summary>
    /// Records movement orders to the flight recorder by polling each
    /// formation's current order on the main thread and logging it when it
    /// changes. Vanilla-first: it observes the effective order state, so it
    /// catches orders from ANY source (vanilla AI, RBP, any mod) without a
    /// Harmony patch — patching <see cref="Formation.SetMovementOrder"/> ran on
    /// the engine's worker threads and crashed type init, so this samples the
    /// result instead. Each logged order carries the nav-mesh-face verdict, so
    /// a move that will be silently ignored shows up in telemetry.jsonl.
    /// </summary>
    public static class OrderTelemetry
    {
        private static readonly Dictionary<Formation, string> LastSignature = new();

        public static void Reset() => LastSignature.Clear();

        /// <summary>Main-thread poll (called from the recorder's mission tick, throttled).</summary>
        public static void Poll(Mission mission)
        {
            if (!TelemetryLog.Enabled || mission == null)
                return;

            foreach (var team in mission.Teams)
            {
                foreach (var formation in team.FormationsIncludingEmpty)
                {
                    if (formation.CountOfUnits <= 0)
                        continue;

                    try
                    {
                        var order = OrderInspector.Describe(formation);
                        var signature = $"{order.Type}|{RoundTarget(order.MoveTarget)}";
                        if (LastSignature.TryGetValue(formation, out var previous) && previous == signature)
                            continue;
                        LastSignature[formation] = signature;

                        TelemetryLog.Write(TelemetryKinds.Order, mission.CurrentTime, order.Type, new
                        {
                            team = team.TeamIndex,
                            formation = (int)formation.FormationIndex + 1,
                            order = order.Type,
                            moveTarget = order.MoveTarget,
                            targetHasNavMeshFace = order.TargetHasNavMeshFace,
                            count = formation.CountOfUnits,
                        });
                    }
                    catch (Exception e)
                    {
                        DbgLog.Error($"OrderTelemetry: polling formation {(int)formation.FormationIndex} failed.", e);
                    }
                }
            }
        }

        private static string RoundTarget(Vec2Dto target) =>
            target == null ? "-" : $"{(int)target.X},{(int)target.Y}";
    }
}
