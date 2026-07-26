using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FramesToMMSpriteResources
{
    public struct IntVector2(int x, int y)
    {
        public int X { get; set; } = x;
        public int Y { get; set; } = y;

        public readonly bool Equals(IntVector2 other)
            => X == other.X && Y == other.Y;

        public override readonly bool Equals(object? obj)
            => obj is IntVector2 other && Equals(other);

        public override readonly int GetHashCode()
            => HashCode.Combine(X, Y);

        public static bool operator ==(IntVector2 left, IntVector2 right)
            => left.Equals(right);

        public static bool operator !=(IntVector2 left, IntVector2 right)
            => !left.Equals(right);

        public override readonly string ToString()
            => $"({X}, {Y})";
    }
}
