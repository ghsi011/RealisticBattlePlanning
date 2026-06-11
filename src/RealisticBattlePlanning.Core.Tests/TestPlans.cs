using System.Collections.Generic;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>Shared plan builders for tests.</summary>
    public static class TestPlans
    {
        /// <summary>A small, valid plan (the shipped sample's shape).</summary>
        public static BattlePlan SimpleValid()
        {
            return new BattlePlan
            {
                PlayerSignals = { "hammer" },
                Anchors =
                {
                    new MapAnchor { Id = "advance-50", Basis = AnchorBasis.OwnStart, Forward = 50f },
                },
                Formations =
                {
                    new FormationPlan
                    {
                        Formation = PlannedFormationClass.Infantry,
                        Abort = new AbortConditions { CasualtiesAbovePercent = 50f },
                        Stages =
                        {
                            new Stage
                            {
                                Name = "hold the line",
                                Do = new DirectiveSpec { Type = DirectiveType.Hold, Arrangement = Arrangement.ShieldWall },
                            },
                            new Stage
                            {
                                Name = "advance",
                                When = { new TriggerSpec { Type = TriggerType.TimerElapsed, Seconds = 30f } },
                                Do = new DirectiveSpec { Type = DirectiveType.MoveTo, Anchor = "advance-50", Speed = MoveSpeed.Walk },
                                Emit = { "advancing" },
                            },
                        },
                    },
                    new FormationPlan
                    {
                        Formation = PlannedFormationClass.Ranged,
                        Stages =
                        {
                            new Stage
                            {
                                Do = new DirectiveSpec { Type = DirectiveType.Hold, Arrangement = Arrangement.Loose },
                            },
                            new Stage
                            {
                                Name = "follow the advance",
                                When = { new TriggerSpec { Type = TriggerType.SignalReceived, Signal = "advancing" } },
                                Do = new DirectiveSpec { Type = DirectiveType.MoveTo, Anchor = "advance-50" },
                            },
                        },
                    },
                },
            };
        }

        /// <summary>
        /// A plan touching every trigger type, every directive type, every
        /// arrangement, both speeds, both sides, both fire modes, and every
        /// anchor basis — the round-trip fixture.
        /// </summary>
        public static BattlePlan EveryEnumValue()
        {
            return new BattlePlan
            {
                PlayerSignals = { "go-1", "go-2" },
                Anchors =
                {
                    new MapAnchor { Id = "own", Basis = AnchorBasis.OwnStart, Forward = 10f, Right = -5f },
                    new MapAnchor { Id = "team", Basis = AnchorBasis.TeamCenter, Forward = -20f, Right = 15f },
                    new MapAnchor { Id = "scene", Basis = AnchorBasis.Scene, X = 100f, Y = 250f },
                },
                Formations =
                {
                    new FormationPlan
                    {
                        Formation = PlannedFormationClass.Infantry,
                        Abort = new AbortConditions { CasualtiesAbovePercent = 35f, OnCommanderIncapacitated = false, OnFormationBroken = true },
                        Stages =
                        {
                            new Stage
                            {
                                When = { new TriggerSpec { Type = TriggerType.BattleStart } },
                                Do = new DirectiveSpec { Type = DirectiveType.Hold, Arrangement = Arrangement.ShieldWall, WidthMeters = 40f },
                            },
                            new Stage
                            {
                                When =
                                {
                                    new TriggerSpec { Type = TriggerType.EnemyCommits, Formation = "Player", SustainSeconds = 4f, SpeedThreshold = 2.5f },
                                    new TriggerSpec { Type = TriggerType.TimerElapsed, Seconds = 10f },
                                    new TriggerSpec { Type = TriggerType.CasualtiesAbove, Percent = 5f },
                                },
                                Do = new DirectiveSpec { Type = DirectiveType.MoveTo, Path = new List<string> { "own", "team" }, Speed = MoveSpeed.Run, Arrangement = Arrangement.Line },
                                Emit = { "moving-out" },
                            },
                            new Stage
                            {
                                When = { new TriggerSpec { Type = TriggerType.PositionReached, Anchor = "team", ToleranceMeters = 8f } },
                                Do = new DirectiveSpec { Type = DirectiveType.Charge, Target = "Nearest" },
                            },
                        },
                    },
                    new FormationPlan
                    {
                        Formation = PlannedFormationClass.HorseArcher,
                        Stages =
                        {
                            new Stage
                            {
                                Do = new DirectiveSpec { Type = DirectiveType.Skirmish, Target = "Nearest", StandoffMeters = 60f },
                            },
                            new Stage
                            {
                                When = { new TriggerSpec { Type = TriggerType.EnemyWithinDistance, Formation = "Nearest", Meters = 80f } },
                                Do = new DirectiveSpec { Type = DirectiveType.FeignRetreat, Anchor = "own", FireWhileWithdrawing = true, Speed = MoveSpeed.Run },
                            },
                            new Stage
                            {
                                When = { new TriggerSpec { Type = TriggerType.SignalReceived, Signal = "moving-out" } },
                                Do = new DirectiveSpec { Type = DirectiveType.FlankArc, Side = FlankSide.Left, Target = "Nearest", StandoffMeters = 50f, MissileOnly = true },
                            },
                            new Stage
                            {
                                When = { new TriggerSpec { Type = TriggerType.EnemyBroken, Formation = "Nearest" } },
                                Do = new DirectiveSpec { Type = DirectiveType.FireControl, Fire = FireMode.Hold },
                            },
                        },
                    },
                    new FormationPlan
                    {
                        Formation = PlannedFormationClass.Cavalry,
                        Stages =
                        {
                            new Stage
                            {
                                Do = new DirectiveSpec { Type = DirectiveType.Screen, Target = "Infantry", GapMeters = 30f },
                            },
                            new Stage
                            {
                                When = { new TriggerSpec { Type = TriggerType.PlayerSignal, Signal = "go-1" } },
                                Do = new DirectiveSpec { Type = DirectiveType.PullBack, Anchor = "scene", MaintainFacing = true },
                            },
                        },
                    },
                    new FormationPlan
                    {
                        Formation = PlannedFormationClass.Ranged,
                        Stages =
                        {
                            new Stage
                            {
                                Do = new DirectiveSpec { Type = DirectiveType.Follow, Target = "Infantry", OffsetForwardMeters = -15f, OffsetRightMeters = 5f },
                            },
                            new Stage
                            {
                                When = { new TriggerSpec { Type = TriggerType.FriendlyWithinDistance, Formation = "Player", Meters = 25f } },
                                Do = new DirectiveSpec { Type = DirectiveType.Hold, Arrangement = Arrangement.Square },
                            },
                            new Stage
                            {
                                When = { new TriggerSpec { Type = TriggerType.PlayerSignal, Signal = "go-2" } },
                                Do = new DirectiveSpec { Type = DirectiveType.Hold, Arrangement = Arrangement.Circle, Fire = FireMode.Free },
                            },
                            new Stage
                            {
                                When = { new TriggerSpec { Type = TriggerType.TimerElapsed, Seconds = 90f } },
                                Do = new DirectiveSpec { Type = DirectiveType.Hold, Arrangement = Arrangement.Loose },
                            },
                        },
                    },
                },
            };
        }
    }
}
