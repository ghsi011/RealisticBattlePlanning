using System;

namespace RealisticBattlePlanning.Execution
{
    /// <summary>
    /// 2D battlefield position/direction in meters. Deliberately our own tiny
    /// struct — Core must not depend on TaleWorlds.Library.Vec2, and avoiding
    /// System.Numerics sidesteps facade-binding differences between net472
    /// in-game and net8 under test. Convention matches the engine: X east,
    /// Y north; "right of direction d" = (d.Y, -d.X).
    /// </summary>
    public readonly struct MapVec : IEquatable<MapVec>
    {
        public float X { get; }
        public float Y { get; }

        public MapVec(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static MapVec operator +(MapVec a, MapVec b) => new(a.X + b.X, a.Y + b.Y);
        public static MapVec operator -(MapVec a, MapVec b) => new(a.X - b.X, a.Y - b.Y);
        public static MapVec operator *(MapVec v, float s) => new(v.X * s, v.Y * s);

        public float Length => (float)Math.Sqrt(X * X + Y * Y);

        public MapVec Normalized()
        {
            var length = Length;
            return length > 1e-6f ? new MapVec(X / length, Y / length) : new MapVec(0f, 1f);
        }

        /// <summary>Perpendicular pointing to the right when facing along this direction.</summary>
        public MapVec Right() => new(Y, -X);

        public float Dot(MapVec other) => X * other.X + Y * other.Y;

        public float DistanceTo(MapVec other) => (other - this).Length;

        public bool Equals(MapVec other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is MapVec other && Equals(other);
        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Y.GetHashCode();
        public override string ToString() => $"({X:0.#}, {Y:0.#})";
    }
}
