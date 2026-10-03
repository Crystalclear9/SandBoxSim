using System.Text;

namespace SandBoxSim.ConsoleApp.Tui;

/// <summary>
/// 终端能力与生命周期管理。
///
/// 职责：
///   * 探测颜色能力（TrueColor / 256 / 16 / 无）并逐级降级；
///   * 进入/退出"备用屏幕 + 隐藏光标"的游戏显示模式，异常退出也要恢复；
///   * 提供非阻塞按键读取（模拟循环绝不能被输入阻塞）。
///
/// 明确不做的事：不做鼠标、不做窗口缩放事件（M5 才做鼠标拾取；
/// 尺寸每帧轮询即可，Windows 下没有可靠的 resize 事件）。
/// </summary>
public sealed class Terminal : System.IDisposable
{
    private readonly bool _useAlternateScreen;
    private bool _disposed;

    public ColorProfile Profile { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>是否成功进入了交替屏幕（退出时要对称恢复）。</summary>
    public bool InAlternateScreen { get; private set; }

    public Terminal(bool noColor, bool forceNoAlternateScreen = false)
    {
        Profile = DetectColorProfile(noColor);
        _useAlternateScreen = !forceNoAlternateScreen;
        Width = SafeGet(() => System.Console.WindowWidth, 100);
        Height = SafeGet(() => System.Console.WindowHeight, 30);
    }

    /// <summary>
    /// 颜色能力探测。
    /// 判据（按可靠性排序）：显式 --no-color > 输出被重定向 > 环境变量提示 > 保守 16 色。
    /// 注意：TERM 在 Windows 上通常不存在，因此 WT_SESSION/COLORTERM 才是主判据。
    /// </summary>
    public static ColorProfile DetectColorProfile(bool noColor)
    {
        if (noColor || !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("NO_COLOR")))
        {
            return ColorProfile.None;
        }

        try
        {
            if (System.Console.IsOutputRedirected) { return ColorProfile.None; }
        }
        catch
        {
            return ColorProfile.None;
        }

        string? colorTerm = System.Environment.GetEnvironmentVariable("COLORTERM");
        string? wtSession = System.Environment.GetEnvironmentVariable("WT_SESSION");
        string? term = System.Environment.GetEnvironmentVariable("TERM");

        if (!string.IsNullOrEmpty(wtSession)) { return ColorProfile.TrueColor; }
        if (!string.IsNullOrEmpty(colorTerm))
        {
            if (colorTerm.Contains("truecolor") || colorTerm.Contains("24bit")) { return ColorProfile.TrueColor; }
            return ColorProfile.Ansi256;
        }
        if (!string.IsNullOrEmpty(term))
        {
            if (term.Contains("256color")) { return ColorProfile.Ansi256; }
            if (term.Contains("color")) { return ColorProfile.Ansi16; }
            if (term == "dumb") { return ColorProfile.None; }
        }

        // 现代 Windows Terminal 之外，conhost 也基本支持 16 色。
        return ColorProfile.Ansi16;
    }

    public void EnterGameMode()
    {
        try
        {
            System.Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch
        {
            // 某些宿主不允许改编码；继续跑（字形可能降级为 '?'）。
        }

        try
        {
            System.Console.InputEncoding = Encoding.UTF8;
        }
        catch
        {
            // 同上。
        }

        if (_useAlternateScreen && !System.Console.IsOutputRedirected)
        {
            WriteRaw("\x1b[?1049h");   // 备用屏幕：退出后恢复原终端内容
            InAlternateScreen = true;
        }

        WriteRaw("\x1b[?25l");         // 隐藏光标
        WriteRaw("\x1b[2J");           // 清屏
        WriteRaw("\x1b[0m");           // 重置样式
    }

    public void LeaveGameMode()
    {
        if (_disposed) { return; }

        WriteRaw("\x1b[0m");
        WriteRaw("\x1b[?25h");         // 恢复光标

        if (InAlternateScreen)
        {
            WriteRaw("\x1b[?1049l");
            InAlternateScreen = false;
        }
        System.Console.Out.Flush();
    }

    /// <summary>重新读取窗口尺寸（每帧调用；尺寸变化时渲染层重建缓冲）。</summary>
    public bool RefreshSize()
    {
        int w = SafeGet(() => System.Console.WindowWidth, Width);
        int h = SafeGet(() => System.Console.WindowHeight, Height);

        // 防止极端尺寸把渲染缓冲搞崩（重定向到文件时窗口尺寸可能是 0）。
        if (w < 20) { w = 20; }
        if (h < 8) { h = 8; }

        if (w == Width && h == Height) { return false; }
        Width = w;
        Height = h;
        return true;
    }

    public void WriteRaw(string text)
    {
        System.Console.Out.Write(text);
    }

    public void Flush() => System.Console.Out.Flush();

    /// <summary>
    /// 非阻塞读一个按键。没有输入时返回 null。
    /// Console.KeyAvailable 在重定向输入下会返回 false，不会抛异常，可以安全调用。
    /// </summary>
    public System.ConsoleKeyInfo? PollKey()
    {
        try
        {
            if (!System.Console.KeyAvailable) { return null; }
            return System.Console.ReadKey(intercept: true);
        }
        catch (System.InvalidOperationException)
        {
            return null;   // 无控制台输入（重定向）
        }
    }

    private static int SafeGet(System.Func<int> getter, int fallback)
    {
        try
        {
            int value = getter();
            return value > 0 ? value : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        LeaveGameMode();
    }
}
