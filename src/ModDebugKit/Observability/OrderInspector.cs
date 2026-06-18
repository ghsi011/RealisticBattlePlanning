using System;
using ModDebugKit.Diagnostics;
using ModDebugKit.Snapshots;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;

namespace ModDebugKit.Observability
{
    /// <summary>
    /// Reads a formation's current movement order into an engine-free
    /// <see cref="OrderDto"/>, with the nav-mesh-face verdict that makes the
    /// silent-ignore move bug visible. Shared by the snapshot and the order
    /// telemetry so they can't disagree.
    /// </summary>
    public static class OrderInspector
    {
        public static OrderDto Describe(Formation formation)
        {
            var order = formation.GetReadonlyMovementOrderReference();
            var dto = new OrderDto { Type = order.OrderEnum.ToString() };

            // Only a Move order carries a positional target whose nav-mesh face matters
            // (the bug class the kit must surface). Other orders have no positional target.
            if (order.OrderEnum == MovementOrder.MovementOrderEnum.Move)
            {
                try
                {
                    var target = order.CreateNewOrderWorldPositionMT(formation, WorldPosition.WorldPositionEnforcedCache.None);
                    dto.MoveTarget = new Vec2Dto(target.AsVec2.x, target.AsVec2.y);
                    dto.TargetIsValid = target.IsValid;
                    dto.TargetHasNavMeshFace = HasNavMeshFace(target);
                }
                catch (Exception e)
                {
                    DbgLog.Error($"OrderInspector: resolving the move target for formation {(int)formation.FormationIndex} failed.", e);
                }
            }

            return dto;
        }

        /// <summary>
        /// True when the target world position resolves to a nav-mesh face the
        /// engine can path to. A move order whose target has no face is the bug
        /// that cost days: the engine silently ignores the order and the
        /// formation never advances.
        /// </summary>
        private static bool? HasNavMeshFace(WorldPosition position)
        {
            try
            {
                return position.GetNavMesh() != UIntPtr.Zero;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
