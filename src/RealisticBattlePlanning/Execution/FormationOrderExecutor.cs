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
    /// The only place plan execution touches Formation. The full A5
    /// vocabulary is expressed through the vanilla order system (movement /
    /// arrangement / facing / firing orders): steering directives (Skirmish,
    /// FlankArc, Screen, Follow) get their movement through the monitor's
    /// SteeringTargetChanged stream, so this class stays a stateless relay
    /// and every behavior stays scripted-timeline-testable in Core.
    /// </summary>
    internal sealed class FormationOrderExecutor
    {
        public FormationOrderExecutor()
        {
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
                    ApplyShape(formation, spec, fallbackArrangement: null);
                    FaceEnemy(formation);
                    var holdAt = directive.Target ?? new MapVec(formation.CurrentPosition.x, formation.CurrentPosition.y);
                    Move(formation, holdAt);
                    break;
                }

                case DirectiveType.MoveTo:
                {
                    ApplyShape(formation, spec, fallbackArrangement: null);
                    FaceEnemy(formation);
                    if (directive.FirstMoveTarget is { } target)
                        Move(formation, target);
                    else
                        RbpLog.Warn($"[FAULT] MoveTo for {formation.FormationIndex} has no resolvable destination; holding.");
                    break;
                }

                case DirectiveType.Charge:
                    // Target selector noted in the plan; vanilla charge picks
                    // its own melee targets sensibly (A5 "or nearest").
                    formation.SetMovementOrder(MovementOrder.MovementOrderCharge);
                    break;

                case DirectiveType.Skirmish:
                case DirectiveType.FlankArc:
                    // Movement arrives via the steering stream; here only the
                    // posture: spread out, face the enemy, shoot at will.
                    ApplyShape(formation, spec, fallbackArrangement: Arrangement.Loose);
                    FaceEnemy(formation);
                    break;

                case DirectiveType.Screen:
                    ApplyShape(formation, spec, fallbackArrangement: null);
                    FaceEnemy(formation);
                    break;

                case DirectiveType.Follow:
                    ApplyShape(formation, spec, fallbackArrangement: null);
                    break;

                case DirectiveType.FeignRetreat:
                {
                    // Must read as flight (A6): movement-direction facing,
                    // loose, shooting on the way out when the flag is set.
                    ApplyShape(formation, spec, fallbackArrangement: Arrangement.Loose);
                    if (directive.Target is { } target)
                    {
                        FaceMovementDirection(formation, target);
                        Move(formation, target);
                    }
                    else
                    {
                        RbpLog.Warn($"[FAULT] FeignRetreat for {formation.FormationIndex} has no resolvable anchor; holding.");
                    }
                    break;
                }

                case DirectiveType.PullBack:
                {
                    ApplyShape(formation, spec, fallbackArrangement: null);
                    if (spec.MaintainFacing != false)
                        FaceEnemy(formation);
                    if (directive.Target is { } target)
                        Move(formation, target);
                    else
                        RbpLog.Warn($"[FAULT] PullBack for {formation.FormationIndex} has no resolvable anchor; holding.");
                    break;
                }

                case DirectiveType.FireControl:
                    // Fire mode only; movement and shape stay as they are.
                    break;

                default:
                    RbpLog.Warn($"Directive {spec.Type} is not implemented; {formation.FormationIndex} keeps its previous order.");
                    break;
            }

            if (DesiredFireMode(spec) is { } fire)
                formation.SetFiringOrder(fire == FireMode.Hold
                    ? FiringOrder.FiringOrderHoldYourFire
                    : FiringOrder.FiringOrderFireAtWill);
        }

        public void Move(Formation formation, MapVec target)
        {
            // Issue the move exactly the way the vanilla OrderController does: take a
            // world position the formation can already resolve a nav-mesh face from,
            // then slide it to the target. A raw WorldPosition built with no nav-mesh
            // face (UIntPtr.Zero) can't be pathed to, so the formation silently
            // ignores the order and never moves (the playtest's stuck cavalry / HA).
            var position = formation.CreateNewOrderWorldPosition(WorldPosition.WorldPositionEnforcedCache.None);
            position.SetVec2(new Vec2(target.X, target.Y));
            formation.SetMovementOrder(MovementOrder.MovementOrderMove(position));
        }

        /// <summary>Commit a formation to a vanilla charge (e.g. a FlankArc pressing home).</summary>
        public void Charge(Formation formation)
        {
            formation.SetMovementOrder(MovementOrder.MovementOrderCharge);
        }

        /// <summary>
        /// Explicit fire mode wins; otherwise missile-posture directives
        /// default to free fire, feign retreat to its flag (A5).
        /// </summary>
        private static FireMode? DesiredFireMode(DirectiveSpec spec) => spec.Fire ?? spec.Type switch
        {
            DirectiveType.Skirmish or DirectiveType.FlankArc or DirectiveType.Screen => FireMode.Free,
            DirectiveType.FeignRetreat => spec.FireWhileWithdrawing == true ? FireMode.Free : FireMode.Hold,
            _ => null,
        };

        private void ApplyShape(Formation formation, DirectiveSpec spec, Arrangement? fallbackArrangement)
        {
            if ((spec.Arrangement ?? fallbackArrangement) is { } arrangement)
                formation.SetArrangementOrder(MapArrangement(arrangement));

            if (spec.WidthMeters is { } width)
                formation.SetFormOrder(FormOrder.FormOrderCustom(width));
        }

        private static void FaceEnemy(Formation formation)
        {
            formation.SetFacingOrder(FacingOrder.FacingOrderLookAtEnemy);
        }

        private static void FaceMovementDirection(Formation formation, MapVec target)
        {
            var direction = new Vec2(target.X - formation.CurrentPosition.x, target.Y - formation.CurrentPosition.y);
            if (direction.LengthSquared > 1e-6f)
                formation.SetFacingOrder(FacingOrder.FacingOrderLookAtDirection(direction.Normalized()));
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
