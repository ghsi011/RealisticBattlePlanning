using System;
using RealisticBattlePlanning.Diagnostics;
using RealisticBattlePlanning.Planning.Model;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace RealisticBattlePlanning.Execution
{
    /// <summary>
    /// Turns resolved directives from the Plan Monitor into engine orders.
    /// The only place plan execution touches Formation. I3 vocabulary:
    /// Hold / MoveTo / Charge; the rest log once and leave the formation on
    /// its previous order (full vocabulary lands in I6).
    /// </summary>
    internal sealed class FormationOrderExecutor
    {
        private readonly Mission _mission;

        public FormationOrderExecutor(Mission mission)
        {
            _mission = mission;
        }

        /// <summary>
        /// Plan governance (spec B1): formations under the plan must not be
        /// touched by the team general AI. With the player as general that AI
        /// only steers formations flagged AI-controlled, so clearing the flag
        /// is the suppression.
        /// </summary>
        public void Adopt(Formation formation)
        {
            formation.SetControlledByAI(false);
        }

        public void Apply(Formation formation, ResolvedDirective directive)
        {
            var spec = directive.Spec;
            switch (spec.Type)
            {
                case DirectiveType.Hold:
                {
                    ApplyShape(formation, spec);
                    var holdAt = directive.Target ?? new MapVec(formation.CurrentPosition.x, formation.CurrentPosition.y);
                    Move(formation, holdAt);
                    break;
                }

                case DirectiveType.MoveTo:
                {
                    ApplyShape(formation, spec);
                    if (directive.FirstMoveTarget is { } target)
                        Move(formation, target);
                    else
                        RbpLog.Warn($"[FAULT] MoveTo for {formation.FormationIndex} has no resolvable destination; holding.");
                    break;
                }

                case DirectiveType.Charge:
                    formation.SetMovementOrder(MovementOrder.MovementOrderCharge);
                    break;

                default:
                    RbpLog.Warn($"Directive {spec.Type} is not implemented yet (planned for I6); {formation.FormationIndex} keeps its previous order.");
                    break;
            }

            if (spec.Speed != null)
                RbpLog.Info($"Directive speed '{spec.Speed}' noted but not applied yet (I6).");
        }

        public void Move(Formation formation, MapVec target)
        {
            var position = new WorldPosition(_mission.Scene, UIntPtr.Zero, new Vec3(target.X, target.Y), hasValidZ: false);
            formation.SetMovementOrder(MovementOrder.MovementOrderMove(position));
        }

        private void ApplyShape(Formation formation, DirectiveSpec spec)
        {
            if (spec.Arrangement is { } arrangement)
                formation.SetArrangementOrder(MapArrangement(arrangement));

            if (spec.WidthMeters is { } width)
                formation.SetFormOrder(FormOrder.FormOrderCustom(width));

            formation.SetFacingOrder(FacingOrder.FacingOrderLookAtEnemy);
        }

        private static ArrangementOrder MapArrangement(Arrangement arrangement) => arrangement switch
        {
            Arrangement.Line => ArrangementOrder.ArrangementOrderLine,
            Arrangement.ShieldWall => ArrangementOrder.ArrangementOrderShieldWall,
            Arrangement.Loose => ArrangementOrder.ArrangementOrderLoose,
            Arrangement.Square => ArrangementOrder.ArrangementOrderSquare,
            Arrangement.Circle => ArrangementOrder.ArrangementOrderCircle,
            _ => ArrangementOrder.ArrangementOrderLine,
        };
    }
}
