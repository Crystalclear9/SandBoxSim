namespace SandBoxSim.Core.Foundation;

/// <summary>
/// 整数二维坐标。模拟内核里所有"格子坐标"都用它，禁止用两个裸 int 参数传递，
/// 避免参数顺序写反这类在模拟代码里极难发现的错误。
/// </summary>
public readonly struct Int2 : System.IEquatable<Int2>
{
    public readonly int X;
    public readonly int Y;

    public Int2(int x, int y)
    {
        X = x;
        Y = y;
    }

    public static readonly Int2 Zero = new Int2(0, 0);
    public static readonly Int2 One = new Int2(1, 1);
    public static readonly Int2 Up = new Int2(0, -1);
    public static readonly Int2 Down = new Int2(0, 1);
    public static readonly Int2 Left = new Int2(-1, 0);
    public static readonly Int2 Right = new Int2(1, 0);

    public static Int2 operator +(Int2 a, Int2 b) => new Int2(a.X + b.X, a.Y + b.Y);
    public static Int2 operator -(Int2 a, Int2 b) => new Int2(a.X - b.X, a.Y - b.Y);
    public static Int2 operator *(Int2 a, int k) => new Int2(a.X * k, a.Y * k);
    public static bool operator ==(Int2 a, Int2 b) => a.X == b.X && a.Y == b.Y;
    public static bool operator !=(Int2 a, Int2 b) => !(a == b);

    /// <summary>切比雪夫距离（八方向移动下的"格子距离"）。</summary>
    public static int ChebyshevDistance(Int2 a, Int2 b)
    {
        int dx = System.Math.Abs(a.X - b.X);
        int dy = System.Math.Abs(a.Y - b.Y);
        return dx > dy ? dx : dy;
    }

    /// <summary>曼哈顿距离。</summary>
    public static int ManhattanDistance(Int2 a, Int2 b)
        => System.Math.Abs(a.X - b.X) + System.Math.Abs(a.Y - b.Y);

    /// <summary>欧氏距离（用于 Utility 的衰减）。</summary>
    public static double Distance(Int2 a, Int2 b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return System.Math.Sqrt((dx * dx) + (dy * dy));
    }

    public static int SquaredDistance(Int2 a, Int2 b)
    {
        int dx = a.X - b.X;
        int dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy);
    }

    public bool Equals(Int2 other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is Int2 other && Equals(other);

    // 确定性：哈希只依赖坐标本身，不依赖运行时随机化种子（FNV-1a 风格）。
    public override int GetHashCode()
    {
        unchecked
        {
            return (X * 397) ^ Y;
        }
    }

    public override string ToString() => "(" + X + "," + Y + ")";
}
