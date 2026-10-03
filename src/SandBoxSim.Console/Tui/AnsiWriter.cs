using System.Text;

namespace SandBoxSim.ConsoleApp.Tui;

/// <summary>
///  ANSI 转义序列生成器。
///
/// 设计要点：**按"从上一个颜色到当前颜色"的差异输出**。
/// 一帧 80×40 个格子如果每格都发一段颜色序列，单帧会产生几十 KB 的转义文本，
/// 终端会明显卡顿；差异输出能把大部分相邻同色格子合并掉。
/// </summary>
public sealed class AnsiWriter
{
    private readonly StringBuilder _sb = new StringBuilder(64 * 1024);
    private readonly ColorProfile _profile;

    private Rgb _currentFore;
    private Rgb _currentBack;
    private bool _foreValid;
    private bool _backValid;
    private bool _bold;
    private bool _inverse;

    public AnsiWriter(ColorProfile profile)
    {
        _profile = profile;
    }

    public ColorProfile Profile => _profile;
    public int Length => _sb.Length;

    public void Clear()
    {
        _sb.Clear();
        _foreValid = false;
        _backValid = false;
        _bold = false;
        _inverse = false;
    }

    /// <summary>清屏并归位。</summary>
    public void BeginFrame()
    {
        _sb.Append("\x1b[0m\x1b[H");
        _foreValid = false;
        _backValid = false;
        _bold = false;
        _inverse = false;
    }

    /// <summary>重置所有样式（行尾调用，避免颜色渗到 UI 框线）。</summary>
    public void Reset()
    {
        if (_bold || _inverse || _foreValid || _backValid)
        {
            _sb.Append("\x1b[0m");
            _bold = false;
            _inverse = false;
            _foreValid = false;
            _backValid = false;
        }
    }

    private void EnsureFore(Rgb color)
    {
        if (_profile == ColorProfile.None) { return; }
        if (_foreValid && color.Equals(_currentFore)) { return; }

        // 只重置前景（39），保留背景 —— 全量重置会迫使下一格重发背景色，
        // 在 80×40 的帧上会多出上千字节的转义序列。
        if (_foreValid) { _sb.Append("\x1b[39m"); }

        switch (_profile)
        {
            case ColorProfile.TrueColor:
                _sb.Append("\x1b[38;2;").Append(color.R).Append(';').Append(color.G).Append(';').Append(color.B).Append('m');
                break;
            case ColorProfile.Ansi256:
                _sb.Append("\x1b[38;5;").Append(color.ToAnsi256()).Append('m');
                break;
            case ColorProfile.Ansi16:
                _sb.Append("\x1b[9").Append(color.ToAnsi16() % 8).Append('m');
                break;
            default:
                return;
        }

        _currentFore = color;
        _foreValid = true;
    }

    private void EnsureBack(Rgb color)
    {
        if (_profile == ColorProfile.None) { return; }
        if (_backValid && color.Equals(_currentBack)) { return; }

        if (_backValid) { _sb.Append("\x1b[49m"); }

        switch (_profile)
        {
            case ColorProfile.TrueColor:
                _sb.Append("\x1b[48;2;").Append(color.R).Append(';').Append(color.G).Append(';').Append(color.B).Append('m');
                break;
            case ColorProfile.Ansi256:
                _sb.Append("\x1b[48;5;").Append(color.ToAnsi256()).Append('m');
                break;
            case ColorProfile.Ansi16:
                _sb.Append("\x1b[4").Append(color.ToAnsi16() % 8).Append('m');
                break;
            default:
                return;
        }

        _currentBack = color;
        _backValid = true;
    }

    private void EnsureBold(bool bold)
    {
        if (_profile == ColorProfile.None || _bold == bold) { return; }
        _sb.Append(bold ? "\x1b[1m" : "\x1b[22m");
        _bold = bold;
    }

    private void EnsureInverse(bool inverse)
    {
        if (_profile == ColorProfile.None || _inverse == inverse) { return; }
        _sb.Append(inverse ? "\x1b[7m" : "\x1b[27m");
        _inverse = inverse;
    }

    /// <summary>写一个单元格。</summary>
    public void WriteCell(in Cell cell)
    {
        EnsureBold(cell.Bold);
        EnsureInverse(cell.Inverse);

        if (_profile == ColorProfile.None)
        {
            _sb.Append(cell.UseFore ? cell.Glyph : ' ');
            return;
        }

        // 半方块技巧：需要前景+背景同时着色的格子（地形像素）
        // 与只需前景的文本格子（UI）走不同路径。
        if (cell.UseFore)
        {
            EnsureFore(cell.Fore);
            EnsureBack(cell.Back);
            _sb.Append(cell.Glyph);
        }
        else
        {
            EnsureBack(cell.Back);
            EnsureFore(cell.Back);   // 让前景与背景一致，避免方块之间出现缝
            _sb.Append(' ');
        }
    }

    /// <summary>写纯文本（样式沿用当前状态）。</summary>
    public void WriteText(string text)
    {
        _sb.Append(text);
    }

    /// <summary>换行（保持列对齐：终端会自动回到下一行行首）。</summary>
    public void NewLine()
    {
        _sb.Append("\r\n");
    }

    /// <summary>移动光标到指定位置（行列从 1 开始）。</summary>
    public void MoveTo(int row, int col)
    {
        _sb.Append("\x1b[").Append(row).Append(';').Append(col).Append('H');
    }

    public void HideCursor() => _sb.Append("\x1b[?25l");
    public void ShowCursor() => _sb.Append("\x1b[?25h");

    /// <summary>取出本帧文本。</summary>
    public string TakeText() => _sb.ToString();
}
