using System.Collections.Generic;
using RealisticBattlePlanning.Execution;
using RealisticBattlePlanning.Planning.Model;

namespace RealisticBattlePlanning.Tests
{
    /// <summary>
    /// Scriptable battlefield snapshot for timeline tests. Attack direction
    /// defaults to (0,1) (north): "forward N" anchors resolve to +N on Y.
    /// </summary>
    internal sealed class FakeBattlefield : IBattlefieldSnapshot
    {
        private readonly Dictionary<PlannedFormationClass, IFormationSnapshot> _own = new();
        private readonly List<IEnemyFormationSnapshot> _enemies = new();

        public FakeBattlefield(float time, bool started = true)
        {
            TimeSeconds = time;
            BattleStarted = started;
        }

        public float TimeSeconds { get; }
        public bool BattleStarted { get; }
        public MapVec AttackDirection { get; set; } = new(0f, 1f);
        public MapVec TeamCenter { get; set; } = new(0f, 0f);
        public MapVec? PlayerPosition { get; set; }
        public IReadOnlyList<IEnemyFormationSnapshot> Enemies => _enemies;

        public IFormationSnapshot GetOwn(PlannedFormationClass formationClass)
            => _own.TryGetValue(formationClass, out var snapshot) ? snapshot : null;

        public FakeBattlefield WithOwn(
            PlannedFormationClass cls, float x, float y, float casualtiesPercent = 0f,
            bool commanderDown = false, bool broken = false)
        {
            _own[cls] = new FakeFormation(cls, new MapVec(x, y), casualtiesPercent, commanderDown, broken);
            return this;
        }

        public FakeBattlefield WithEnemy(int id, float x, float y, bool broken = false, PlannedFormationClass? cls = null)
        {
            _enemies.Add(new FakeEnemy(id, cls, new MapVec(x, y), broken));
            return this;
        }

        public FakeBattlefield WithPlayer(float x, float y)
        {
            PlayerPosition = new MapVec(x, y);
            return this;
        }

        private sealed class FakeFormation : IFormationSnapshot
        {
            public FakeFormation(PlannedFormationClass cls, MapVec position, float casualtiesPercent, bool commanderDown, bool isBroken)
            {
                Class = cls;
                Position = position;
                CasualtiesPercent = casualtiesPercent;
                CommanderDown = commanderDown;
                IsBroken = isBroken;
            }

            public PlannedFormationClass Class { get; }
            public bool Exists => true;
            public MapVec Position { get; }
            public float CasualtiesPercent { get; }
            public bool CommanderDown { get; }
            public bool IsBroken { get; }
        }

        private sealed class FakeEnemy : IEnemyFormationSnapshot
        {
            public FakeEnemy(int id, PlannedFormationClass? cls, MapVec position, bool isBroken)
            {
                Id = id;
                Class = cls;
                Position = position;
                IsBroken = isBroken;
            }

            public int Id { get; }
            public PlannedFormationClass? Class { get; }
            public MapVec Position { get; }
            public bool IsBroken { get; }
        }
    }
}
