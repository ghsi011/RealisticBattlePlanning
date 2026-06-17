using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Fidelity;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    public class CommanderBarksTests
    {
        private const PlannedFormationClass Inf = PlannedFormationClass.Infantry;

        private static StageActivated Activated(DirectiveType type, int stageIndex = 0)
            => new(Inf, stageIndex, new Stage(), new ResolvedDirective(new DirectiveSpec { Type = type }, null, null));

        [Fact]
        public void ActivationIsAttributedToTheNamedCommander()
        {
            var line = CommanderBarks.Line(Activated(DirectiveType.Charge), "Sir Geofroy");
            Assert.NotNull(line);
            Assert.StartsWith("Sir Geofroy:", line);
        }

        [Fact]
        public void AnEmptyNameFallsBackToTheFormationClass()
        {
            var line = CommanderBarks.Line(Activated(DirectiveType.MoveTo), "  ");
            Assert.StartsWith("Infantry:", line);
        }

        [Fact]
        public void SkippedStageIsAnnouncedWithItsReasonNoSilentDeviation()
        {
            var line = CommanderBarks.Line(new StageSkipped(Inf, 1, "the enemy already broke"), "Radoc");
            Assert.NotNull(line);
            Assert.Contains("the enemy already broke", line);
        }

        [Fact]
        public void AbortIsAnnouncedWithItsReason()
        {
            var line = CommanderBarks.Line(new PlanAborted(Inf, "casualties over 50%"), "Radoc");
            Assert.Contains("casualties over 50%", line);
        }

        [Fact]
        public void GreenCommanderHesitationBarks_ButAProficientOneIsSilent()
        {
            Assert.NotNull(CommanderBarks.Line(new ReactionDelayed(Inf, 0, 8f, FidelityTier.Untrained), "Green"));
            Assert.NotNull(CommanderBarks.Line(new ReactionDelayed(Inf, 0, 5f, FidelityTier.Drilled), "Drilled"));
            // A sharp officer just acts — nothing to remark on.
            Assert.Null(CommanderBarks.Line(new ReactionDelayed(Inf, 0, 2f, FidelityTier.Proficient), "Sharp"));
            Assert.Null(CommanderBarks.Line(new ReactionDelayed(Inf, 0, 1f, FidelityTier.Master), "Master"));
        }

        [Fact]
        public void InternalEventsDoNotBark()
        {
            Assert.Null(CommanderBarks.Line(new StageCompleted(Inf, 0, new Stage()), "X"));
            Assert.Null(CommanderBarks.Line(new MoveTargetChanged(Inf, 0, new MapVec(1, 2)), "X"));
            Assert.Null(CommanderBarks.Line(new SteeringTargetChanged(Inf, new MapVec(1, 2)), "X"));
        }

        [Fact]
        public void PhrasingIsDeterministicForAGivenStage()
        {
            var a = CommanderBarks.Line(Activated(DirectiveType.Skirmish, stageIndex: 2), "Geo");
            var b = CommanderBarks.Line(Activated(DirectiveType.Skirmish, stageIndex: 2), "Geo");
            Assert.Equal(a, b); // same stage -> same words, every time (seeded-replay safe)
        }

        [Fact]
        public void FlankArcNamesTheSide()
        {
            var spec = new DirectiveSpec { Type = DirectiveType.FlankArc, Side = FlankSide.Left };
            var ev = new StageActivated(Inf, 0, new Stage(), new ResolvedDirective(spec, null, null));
            Assert.Contains("left", CommanderBarks.Line(ev, "Geo"));
        }

        [Fact]
        public void FireControlReflectsHoldVersusFree()
        {
            var hold = new StageActivated(Inf, 0, new Stage(), new ResolvedDirective(new DirectiveSpec { Type = DirectiveType.FireControl, Fire = FireMode.Hold }, null, null));
            var free = new StageActivated(Inf, 0, new Stage(), new ResolvedDirective(new DirectiveSpec { Type = DirectiveType.FireControl, Fire = FireMode.Free }, null, null));
            Assert.Contains("hold", CommanderBarks.Line(hold, "Geo").ToLowerInvariant());
            Assert.Contains("loose", CommanderBarks.Line(free, "Geo").ToLowerInvariant());
        }
    }
}
