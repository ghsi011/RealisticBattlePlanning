using System.Collections.Generic;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Planning.Editing
{
    /// <summary>
    /// Multi-formation plays (the 80% of Phase-3 templates at 20% of the cost):
    /// a play assigns roles across the selected formations and stamps the
    /// spec's canonical structures onto the draft. Engine-free and unit-tested;
    /// the editor picker calls these with the live composition (mounted-ness
    /// comes from what a formation actually HOLDS, not its slot name — the
    /// slot-vs-composition gotcha).
    /// </summary>
    public static class PlayBook
    {
        public sealed class PlayResult
        {
            public bool Ok;
            /// <summary>Why the play could not be built (role requirements unmet).</summary>
            public string Problem;
            public List<PlannedFormationClass> Bait = new();
            public List<PlannedFormationClass> Anvil = new();
        }

        public const string SpringTrapSignal = "spring-trap";
        public const string TrapAnchorId = "trap";

        /// <summary>
        /// A6, the spec's worked example: mounted formations bait — skirmish,
        /// then feign retreat to a trap anchor when the enemy commits, then
        /// wheel out (alternating flanks) when the trap springs; foot
        /// formations anvil — shieldwall, then charge when the pursuers reach
        /// the trap (the first anvil emits the spring-trap signal that releases
        /// the bait). The signal is also declared as a player signal, so the
        /// player can spring the bait early by hand. Stages are APPENDED to
        /// each formation's existing plan, like the single-formation patterns.
        /// </summary>
        public static PlayResult FeignedRetreat(PlanDraft draft, IReadOnlyList<(PlannedFormationClass Cls, bool Mounted)> selection)
        {
            var result = new PlayResult();
            if (draft == null || selection == null)
            {
                result.Problem = "nothing selected";
                return result;
            }

            foreach (var (cls, mounted) in selection)
                (mounted ? result.Bait : result.Anvil).Add(cls);

            if (result.Bait.Count == 0 || result.Anvil.Count == 0)
            {
                result.Problem = "needs at least one mounted formation (the bait) and one foot formation (the anvil)";
                return result;
            }

            draft.AddAnchor(new MapAnchor { Id = TrapAnchorId, Basis = AnchorBasis.TeamCenter, Forward = -30f });
            draft.DeclarePlayerSignal(SpringTrapSignal);

            for (var i = 0; i < result.Bait.Count; i++)
            {
                var cls = result.Bait[i];
                draft.AddStage(cls, new Stage
                {
                    Name = "bait: skirmish",
                    Do = new DirectiveSpec { Type = DirectiveType.Skirmish, StandoffMeters = 60f },
                });
                draft.AddStage(cls, new Stage
                {
                    Name = "bait: feign retreat",
                    When = { new TriggerSpec { Type = TriggerType.EnemyCommits } },
                    Do = new DirectiveSpec { Type = DirectiveType.FeignRetreat, Anchor = TrapAnchorId, FireWhileWithdrawing = true },
                });
                draft.AddStage(cls, new Stage
                {
                    Name = i % 2 == 0 ? "bait: wheel out left" : "bait: wheel out right",
                    When = { new TriggerSpec { Type = TriggerType.SignalReceived, Signal = SpringTrapSignal } },
                    Do = new DirectiveSpec
                    {
                        Type = DirectiveType.FlankArc,
                        Side = i % 2 == 0 ? FlankSide.Left : FlankSide.Right,
                        StandoffMeters = 50f,
                        MissileOnly = true,
                    },
                });
            }

            for (var i = 0; i < result.Anvil.Count; i++)
            {
                var cls = result.Anvil[i];
                draft.AddStage(cls, new Stage
                {
                    Name = "anvil: shieldwall",
                    Do = new DirectiveSpec { Type = DirectiveType.Hold, Arrangement = Arrangement.ShieldWall },
                });
                var spring = new Stage
                {
                    Name = "anvil: spring the trap",
                    When = { new TriggerSpec { Type = TriggerType.EnemyWithinDistance, Meters = 40f, Anchor = TrapAnchorId } },
                    Do = new DirectiveSpec { Type = DirectiveType.Charge },
                };
                if (i == 0)
                    spring.Emit.Add(SpringTrapSignal); // one broadcaster releases every bait
                draft.AddStage(cls, spring);
            }

            result.Ok = true;
            return result;
        }
    }
}
