namespace SandBoxSim.ConsoleApp.Tui;

/// <summary>
/// 终端颜色能力等级。运行环境差异极大（Windows Terminal / 老 conhost / 重定向到文件），
/// 因此渲染层必须能逐级降级，而不是"假设 TrueColor 一定可用"。
/// </summary>
public enum ColorProfile
{
    /// <summary>无颜色（输出被重定向、或用户显式 --no-color）。用字符区分地形。</summary>
    None = 0,

    /// <summary>16 色（老 conhost 或 TERM=xterm）。</summary>
    Ansi16 = 1,

    /// <summary>256 色。</summary>
    Ansi256 = 2,

    /// <summary>24 位真彩（Windows Terminal、现代终端）。地形渐变最好看。</summary>
    TrueColor = 3,
}

/// <summary>一个终端单元格。宽字符（CJK）需要两格，这里用 IsWide 标记占用。</summary>
public struct Cell
{
    public char Glyph;

    /// <summary>前景色。</summary>
    public Rgb Fore;

    /// <summary>背景色。</summary>
    public Rgb Back;

    /// <summary>前景是否参与渲染（false 表示只画背景块）。</summary>
    public bool UseFore;

    /// <summary>是否反显（用于选中高亮）。</summary>
    public bool Inverse;

    /// <summary>加粗（用于强调文本）。</summary>
    public bool Bold;

    public static Cell Empty => new Cell
    {
        Glyph = ' ',
        Fore = Rgb.White,
        Back = Rgb.Black,
        UseFore = false,
        Inverse = false,
        Bold = false,
    };

    public static Cell Text(char glyph, Rgb fore)
    {
        return new Cell
        {
            Glyph = glyph,
            Fore = fore,
            Back = Rgb.Black,
            UseFore = true,
            Inverse = false,
            Bold = false,
        };
    }

    public static Cell Text(string text, int index, Rgb fore, Rgb back)
    {
        return new Cell
        {
            Glyph = index < text.Length ? text[index] : ' ',
            Fore = fore,
            Back = back,
            UseFore = true,
            Inverse = false,
            Bold = false,
        };
    }
}

/// <summary>24 位 RGB。渲染层只认这个，颜色映射策略在 Palette 里。</summary>
public readonly struct Rgb : System.IEquatable<Rgb>
{
    public readonly byte R;
    public readonly byte G;
    public readonly byte B;

    public Rgb(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
    }

    public static readonly Rgb Black = new Rgb(0, 0, 0);
    public static readonly Rgb White = new Rgb(255, 255, 255);
    public static readonly Rgb Gray = new Rgb(128, 128, 128);
    public static readonly Rgb DarkGray = new Rgb(60, 60, 60);
    public static readonly Rgb Yellow = new Rgb(255, 214, 64);
    public static readonly Rgb Cyan = new Rgb(96, 220, 235);
    public static readonly Rgb Red = new Rgb(235, 80, 80);
    public static readonly Rgb Green = new Rgb(110, 210, 110);
    public static readonly Rgb Orange = new Rgb(255, 150, 50);
    public static readonly Rgb Blue = new Rgb(90, 140, 240);
    public static readonly Rgb Magenta = new Rgb(220, 120, 220);

    /// <summary>线性插值（渲染用，不影响模拟）。</summary>
    public static Rgb Lerp(Rgb a, Rgb b, float t)
    {
        if (t <= 0f) { return a; }
        if (t >= 1f) { return b; }
        return new Rgb(
            (byte)(a.R + ((b.R - a.R) * t)),
            (byte)(a.G + ((b.G - a.G) * t)),
            (byte)(a.B + ((b.B - a.B) * t)));
    }

    /// <summary>整体明暗调整（用于昼夜光照与选中高亮）。</summary>
    public Rgb Scale(float factor)
    {
        if (factor < 0f) { factor = 0f; }
        if (factor > 4f) { factor = 4f; }
        return new Rgb(
            ClampByte(R * factor),
            ClampByte(G * factor),
            ClampByte(B * factor));
    }

    private static byte ClampByte(float v)
    {
        if (v <= 0f) { return 0; }
        if (v >= 255f) { return 255; }
        return (byte)v;
    }

    public bool Equals(Rgb other) => R == other.R && G == other.G && B == other.B;
    public override bool Equals(object? obj) => obj is Rgb other && Equals(other);
    public override int GetHashCode() => (R << 16) | (G << 8) | B;

    /// <summary>量化到 16 色（降级用）。</summary>
    public int ToAnsi16()
    {
        bool bright = (R + G + B) > 480;
        int r = R > 110 ? 1 : 0;
        int g = G > 110 ? 1 : 0;
        int b = B > 110 ? 1 : 0;
        int baseIndex;
        if (r == 0 && g == 0 && b == 0) { baseIndex = 0; }
        else if (r == 1 && g == 0 && b == 0) { baseIndex = 1; }
        else if (r == 0 && g == 1 && b == 0) { baseIndex = 2; }
        else if (r == 1 && g == 1 && b == 0) { baseIndex = 3; }
        else if (r == 0 && g == 0 && b == 1) { baseIndex = 4; }
        else if (r == 1 && g == 0 && b == 1) { baseIndex = 5; }
        else if (r == 0 && g == 1 && b == 1) { baseIndex = 6; }
        else { baseIndex = 7; }

        if (bright && baseIndex > 0) { baseIndex += 8; }
        return baseIndex;
    }

    /// <summary>量化到 256 色立方体（降级用）。</summary>
    public int ToAnsi256()
    {
        // 6×6×6 色彩立方（16 + 36r + 6g + b），加上灰阶近似。
        int r = (R * 5 + 127) / 255;
        int g = (G * 5 + 127) / 255;
        int b = (B * 5 + 127) / 255;

        int cubeR = (int)((r / 5f) * 255f);
        int cubeG = (int)((g / 5f) * 255f);
        int cubeB = (int)((b / 5f) * 255f);
        int cubeError = Sq(R - cubeR) + Sq(G - cubeG) + Sq(B - cubeB);

        // 灰阶：232..255 共 24 级，间隔 10
        int grayIndex = ((R + G + B) / 3 - 8) / 10;
        if (grayIndex < 0) { grayIndex = 0; }
        if (grayIndex > 23) { grayIndex = 23; }
        int grayValue = 8 + (grayIndex * 10);
        int grayError = Sq(R - grayValue) + Sq(G - grayValue) + Sq(B - grayValue);

        if (grayError < cubeError) { return 232 + grayIndex; }
        return 16 + (36 * r) + (6 * g) + b;
    }

    private static int Sq(int v) => v * v;

    public override string ToString()
        => "#" + R.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)
              + G.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)
              + B.ToString("x2", System.Globalization.CultureInfo.InvariantCulture);
}
