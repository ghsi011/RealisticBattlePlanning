using System.Linq;
using RealisticBattlePlanning.Planning;
using RealisticBattlePlanning.Planning.Editing;
using RealisticBattlePlanning.Planning.Model;
using Xunit;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// The editor's logic layer (I9): the Gauntlet view is a thin shell over
    /// PlanDraft, so authoring correctness is proven here without the game.
    /// </summary>
    public class PlanDraftTests
    {
        [Fact]
        public void NewFormationStartsValidWithAnOpeningStage()
        {
            var draft = new PlanDraft().AddFormation(PlannedFormationClass.Infantry);

            var plan = draft.Build();
            var formation = Assert.Single(plan.Formations);
            Assert.Equal(PlannedFormationClass.Infantry, formation.Formation);
            Assert.Single(formation.Stages);
            Assert.Equal(DirectiveType.Hold, formation.Stages[0].Do.Type);
            Assert.True(draft.Validate().IsValid);
        }

        [Fact]
        public void AddingTheSameFormationTwiceIsANoOp()
        {
            var draft = new PlanDraft()
                .AddFormation(PlannedFormationClass.Infantry)
                .AddFormation(PlannedFormationClass.Infantry);

            Assert.Single(draft.Build().Formations);
        }

        [Fact]
        public void RemoveFormationDropsItsPlan()
        {
            var draft = new PlanDraft()
                .AddFormation(PlannedFormationClass.Infantry)
                .AddFormation(PlannedFormationClass.Ranged)
                .RemoveFormation(PlannedFormationClass.Infantry);

            Assert.Equal(new[] { PlannedFormationClass.Ranged }, draft.Formations);
        }

        [Fact]
        public void EditingCopyOfDoesNotMutateTheOriginalUntilCommitted()
        {
            // The editor edits a deep copy; the live plan must be untouched
            // until the player applies the built result (PlanningModeView).
            var original = new PlanDraft()
                .AddFormation(PlannedFormationClass.Infantry)
                .Build();

            var draft = PlanDraft.EditingCopyOf(original);
            draft.AddFormation(PlannedFormationClass.Ranged);
            draft.AddStage(PlannedFormationClass.Infantry);

            // Original is unchanged: still one formation with its single opening stage.
            var formation = Assert.Single(original.Formations);
            Assert.Single(formation.Stages);

            // The committed copy carries the edits.
            var edited = draft.Build();
            Assert.Equal(2, edited.Formations.Count);
            Assert.Equal(2, edited.Formations.Single(f => f.Formation == PlannedFormationClass.Infantry).Stages.Count);
        }

        [Fact]
        public void EditingCopyOfNullYieldsAnEmptyEditableDraft()
        {
            var draft = PlanDraft.EditingCopyOf(null);

            Assert.Empty(draft.Build().Formations);
            draft.AddFormation(PlannedFormationClass.Cavalry);
            Assert.Single(draft.Build().Formations);
        }

        [Fact]
        public void StagesAddRemoveAndReorder()
        {
            var draft = new PlanDraft();
            draft.AddStage(PlannedFormationClass.Infantry, Named("a"));
            draft.AddStage(PlannedFormationClass.Infantry, Named("b"));
            draft.AddStage(PlannedFormationClass.Infantry, Named("c"));

            draft.MoveStage(PlannedFormationClass.Infantry, 2, 0); // c to front
            Assert.Equal(new[] { "c", "a", "b" }, StageNames(draft));

            draft.RemoveStage(PlannedFormationClass.Infantry, 1); // drop "a"
            Assert.Equal(new[] { "c", "b" }, StageNames(draft));
        }

        [Fact]
        public void OutOfRangeStageOpsAreHarmless()
        {
            var draft = new PlanDraft().AddStage(PlannedFormationClass.Infantry, Named("only"));

            draft.RemoveStage(PlannedFormationClass.Infantry, 9);
            draft.MoveStage(PlannedFormationClass.Infantry, 5, 0);
            draft.MoveStage(PlannedFormationClass.Infantry, 0, 99); // clamps, single stage stays put

            Assert.Equal(new[] { "only" }, StageNames(draft));
        }

        [Fact]
        public void SetTriggerAndDirectiveAuthorAStage()
        {
            var draft = new PlanDraft().AddFormation(PlannedFormationClass.Infantry);
            draft.AddStage(PlannedFormationClass.Infantry);
            draft.SetTrigger(PlannedFormationClass.Infantry, 1, new TriggerSpec { Type = TriggerType.TimerElapsed, Seconds = 30f });
            draft.SetDirective(PlannedFormationClass.Infantry, 1, new DirectiveSpec { Type = DirectiveType.Charge });

            var stage = draft.Build().Formations[0].Stages[1];
            Assert.Equal(TriggerType.TimerElapsed, stage.When[0].Type);
            Assert.Equal(DirectiveType.Charge, stage.Do.Type);
        }

        [Fact]
        public void PlayerSignalsAreCappedAtFourAndDeduped()
        {
            var draft = new PlanDraft()
                .DeclarePlayerSignal("a")
                .DeclarePlayerSignal("a") // dup
                .DeclarePlayerSignal("b")
                .DeclarePlayerSignal("c")
                .DeclarePlayerSignal("d")
                .DeclarePlayerSignal("e"); // over cap

            Assert.Equal(new[] { "a", "b", "c", "d" }, draft.PlayerSignals);
        }

        [Fact]
        public void EmitAndAnchorFeedTheCoordinationGlue()
        {
            var draft = new PlanDraft();
            draft.AddStage(PlannedFormationClass.Infantry, Named("go"));
            draft.EmitSignal(PlannedFormationClass.Infantry, 0, "advancing");
            draft.EmitSignal(PlannedFormationClass.Infantry, 0, "advancing"); // dup ignored
            draft.AddAnchor(new MapAnchor { Id = "rally", Basis = AnchorBasis.OwnStart, Forward = -20f });
            draft.AddAnchor(new MapAnchor { Id = "RALLY" }); // dup id ignored

            var plan = draft.Build();
            Assert.Equal(new[] { "advancing" }, plan.Formations[0].Stages[0].Emit);
            Assert.Single(plan.Anchors);
        }

        [Fact]
        public void PatternInsertBuildsAValidConductedPlan()
        {
            var draft = new PlanDraft().DeclarePlayerSignal("charge");
            foreach (var stage in EditorDefaults.HoldThenChargeOnSignal("charge"))
                draft.AddStage(PlannedFormationClass.Infantry, stage);

            // The auto-added opening stage isn't wanted here; the pattern is the plan.
            draft.RemoveStage(PlannedFormationClass.Infantry, 0);

            var result = draft.Validate();
            Assert.True(result.IsValid, string.Join("; ", result.Errors));
            Assert.Contains("Player signal 'charge'", draft.Summary());
        }

        [Fact]
        public void MultiFormationAuthoringGivesEachAnIndependentStage()
        {
            // A3.6: author one stage for several formations at once, but each
            // gets its OWN copy — editing one afterwards must not touch the
            // others (the "instruct the two HA commanders together" story).
            var draft = new PlanDraft();
            draft.AddStageToEach(
                new[] { PlannedFormationClass.HorseArcher, PlannedFormationClass.LightCavalry },
                () => new Stage { Name = "skirmish", Do = new DirectiveSpec { Type = DirectiveType.Skirmish } });

            draft.SetDirective(PlannedFormationClass.HorseArcher, 0, new DirectiveSpec { Type = DirectiveType.Charge });

            var plan = draft.Build();
            Assert.Equal(DirectiveType.Charge, plan.Formations.First(f => f.Formation == PlannedFormationClass.HorseArcher).Stages[0].Do.Type);
            Assert.Equal(DirectiveType.Skirmish, plan.Formations.First(f => f.Formation == PlannedFormationClass.LightCavalry).Stages[0].Do.Type);
        }

        [Fact]
        public void AbortConditionsAreEditablePerFormation()
        {
            var draft = new PlanDraft().AddFormation(PlannedFormationClass.Infantry);
            draft.SetAbortConditions(PlannedFormationClass.Infantry, casualtiesAbovePercent: 40f, onFormationBroken: false);

            var abort = draft.Build().Formations[0].Abort;
            Assert.Equal(40f, abort.CasualtiesAbovePercent);
            Assert.False(abort.OnFormationBroken);
            Assert.True(abort.OnCommanderIncapacitated); // untouched: only provided values change
        }

        [Fact]
        public void AbortCasualtyThresholdClampsToStayValid()
        {
            // The UI can't author an invalid plan (A3.9): out-of-range percents
            // clamp into the validator's (0, 100] band rather than throw.
            var over = new PlanDraft().AddFormation(PlannedFormationClass.Infantry)
                .SetAbortConditions(PlannedFormationClass.Infantry, casualtiesAbovePercent: 150f);
            var under = new PlanDraft().AddFormation(PlannedFormationClass.Ranged)
                .SetAbortConditions(PlannedFormationClass.Ranged, casualtiesAbovePercent: 0f);

            Assert.Equal(100f, over.Build().Formations[0].Abort.CasualtiesAbovePercent);
            Assert.True(over.Validate().IsValid);
            Assert.True(under.Build().Formations[0].Abort.CasualtiesAbovePercent > 0f);
            Assert.True(under.Validate().IsValid);
        }

        [Fact]
        public void SetAbortOnAnUnplannedFormationIsANoOp()
        {
            var draft = new PlanDraft().SetAbortConditions(PlannedFormationClass.Cavalry, casualtiesAbovePercent: 20f);
            Assert.Empty(draft.Formations);
        }

        [Fact]
        public void WrapsAnExistingPlanForEditing()
        {
            var draft = new PlanDraft(TestPlans.SimpleValid());
            Assert.Contains(PlannedFormationClass.Infantry, draft.Formations);
            Assert.Contains(PlannedFormationClass.Ranged, draft.Formations);

            draft.RemoveFormation(PlannedFormationClass.Ranged);
            Assert.Equal(new[] { PlannedFormationClass.Infantry }, draft.Formations);
        }

        private static Stage Named(string name) => new() { Name = name, Do = new DirectiveSpec { Type = DirectiveType.Hold } };

        private static string[] StageNames(PlanDraft draft)
            => draft.Build().Formations.First().Stages.Select(s => s.Name).ToArray();
    }
}
