using SandBoxSim.Core.Foundation;

namespace SandBoxSim.ConsoleApp.Tui;

/// <summary>
/// 终端单元格缓冲：先在本地的 Cell 数组里画完整的帧，再一次性转成 ANSI 文本输出。
///
/// 为什么必须缓冲：直接往终端写会逐格产生光标移动与颜色切换，
/// 80×40 的帧在 Windows 终端下会明显闪烁。缓冲 + 单次写入是 TUI 能做到 60 FPS 的前提。
/// </summary>
public sealed class RenderBuffer
{
    private Cell[] _cells;
    private int _width;
    private int _height;

    public int Width => _width;
    public int Height => _height;

    public RenderBuffer(int width, int height)
    {
        _width = width > 0 ? width : 1;
        _height = height > 0 ? height : 1;
        _cells = new Cell[_width * _height];
        Clear();
    }

    /// <summary>调整尺寸。返回 true 表示尺寸确实变化了（调用方需要重算布局）。</summary>
    public bool Resize(int width, int height)
    {
        if (width < 1) { width = 1; }
        if (height < 1) { height = 1; }
        if (width == _width && height == _height) { return false; }

        _width = width;
        _height = height;
        _cells = new Cell[_width * _height];
        Clear();
        return true;
    }

    public void Clear()
    {
        for (int i = 0; i < _cells.Length; i++) { _cells[i] = Cell.Empty; }
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < _width && y < _height;

    public void Set(int x, int y, in Cell cell)
    {
        if (!InBounds(x, y)) { return; }
        _cells[(y * _width) + x] = cell;
    }

    public Cell Get(int x, int y)
    {
        if (!InBounds(x, y)) { return Cell.Empty; }
        return _cells[(y * _width) + x];
    }

    /// <summary>画一个纯背景色的像素块（地形渲染的主力）。</summary>
    public void SetBackground(int x, int y, Rgb back)
    {
        if (!InBounds(x, y)) { return; }
        _cells[(y * _width) + x] = new Cell
        {
            Glyph = ' ',
            Fore = back,
            Back = back,
            UseFore = false,
        };
    }

    /// <summary>用前景色 + 字形画（文字、图标、半方块）。</summary>
    public void SetGlyph(int x, int y, char glyph, Rgb fore, Rgb back, bool bold = false)
    {
        if (!InBounds(x, y)) { return; }
        _cells[(y * _width) + x] = new Cell
        {
            Glyph = glyph,
            Fore = fore,
            Back = back,
            UseFore = true,
            Bold = bold,
        };
    }

    /// <summary>
    /// 写一行文本。返回写入的列数。
    /// 宽字符（中文）按 2 列计算：终端里 CJK 字符占两格，若按 1 列算会导致整行错位。
    /// </summary>
    public int WriteText(int x, int y, string text, Rgb fore, Rgb back, bool bold = false)
    {
        if (text == null || y < 0 || y >= _height) { return 0; }
        int col = x;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r' || c == '\n') { continue; }

            int advance = IsWide(c) ? 2 : 1;
            if (col >= _width) { break; }

            if (col >= 0)
            {
                SetGlyph(col, y, c, fore, back, bold);
                // 宽字符占用的第二格用空格填充，避免上一帧的残留字形露出来。
                if (advance == 2 && col + 1 < _width)
                {
                    SetGlyph(col + 1, y, ' ', fore, back);
                }
            }
            col += advance;
        }
        return col - x;
    }

    /// <summary>用背景色填充一个矩形（面板底）。</summary>
    public void FillRect(int x, int y, int width, int height, Rgb back)
    {
        for (int ry = y; ry < y + height; ry++)
        {
            for (int rx = x; rx < x + width; rx++)
            {
                SetBackground(rx, ry, back);
            }
        }
    }

    /// <summary>画水平分隔线。</summary>
    public void DrawHorizontalLine(int x, int y, int width, char glyph, Rgb fore, Rgb back)
    {
        for (int i = 0; i < width; i++)
        {
            SetGlyph(x + i, y, glyph, fore, back);
        }
    }

    /// <summary>把整个缓冲输出到 ANSI 文本。</summary>
    public void Blit(AnsiWriter writer)
    {
        writer.BeginFrame();

        for (int y = 0; y < _height; y++)
        {
            if (y > 0) { writer.NewLine(); }
            int rowBase = y * _width;
            for (int x = 0; x < _width; x++)
            {
                writer.WriteCell(in _cells[rowBase + x]);
            }
        }

        writer.Reset();
    }

    /// <summary>
    /// CJK 与全角字符判定（用于列宽计算）。
    /// 只覆盖常见区间，不追求覆盖全部 Unicode：这个项目不会用冷僻字符画界面。
    /// </summary>
    public static bool IsWide(char c)
    {
        return (c >= 0x1100 && c <= 0x115F)      // 韩文字母
            || (c >= 0x2E80 && c <= 0xA4CF)      // CJK 部首 ~ 彝文
            || (c >= 0xAC00 && c <= 0xD7A3)      // 韩文音节
            || (c >= 0xF900 && c <= 0xFAFF)      // CJK 兼容表意
            || (c >= 0xFE30 && c <= 0xFE6F)      // CJK 兼容形式
            || (c >= 0xFF00 && c <= 0xFF60)      // 全角形式
            || (c >= 0xFFE0 && c <= 0xFFE6);     // 全角符号
    }

    public static int DisplayWidth(string text)
    {
        if (text == null) { return 0; }
        int width = 0;
        for (int i = 0; i < text.Length; i++)
        {
            width += IsWide(text[i]) ? 2 : 1;
        }
        return width;
    }

    /// <summary>按显示宽度截断字符串（用于状态栏等固定宽度区域）。</summary>
    public static string Truncate(string text, int maxColumns)
    {
        if (string.IsNullOrEmpty(text) || maxColumns <= 0) { return string.Empty; }
        int width = 0;
        for (int i = 0; i < text.Length; i++)
        {
            int advance = IsWide(text[i]) ? 2 : 1;
            if (width + advance > maxColumns) { return text.Substring(0, i); }
            width += advance;
        }
        return text;
    }
}
