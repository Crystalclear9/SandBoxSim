using System.Diagnostics;
using System.Threading;
using SandBoxSim.ConsoleApp.Render;
using SandBoxSim.ConsoleApp.Tui;
using SandBoxSim.ConsoleApp.Ui;
using SandBoxSim.Core;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.ConsoleApp;

/// <summary>
/// 交互式 TUI 主循环。
///
/// 三条硬性设计约束（第 81 / 82 条）：
///   1. 渲染 60 FPS 与模拟 tick **完全解耦**：倍速只改"每帧推进多少 tick"，
///      绝不改 Time.deltaTime 或 tick 的语义；
///   2. 单次 Console 写入：整帧先在 <see cref="RenderBuffer"/> 里画好，再一次性 flush，
///      否则终端会闪烁且极慢；
///   3. 输入永不阻塞模拟：用 Console.KeyAvailable 轮询，没有输入就继续推进时间。
/// </summary>
public sealed class GameLoop
{
    private readonly Simulation _sim;
    private readonly Terminal _terminal;
    private readonly RenderBuffer _buffer;
    private readonly Camera _camera;
    private readonly WorldViewRenderer _renderer;
    private readonly AnsiWriter _ansi;

    private int _speedIndex = 1;
    private bool _showHelp;
    private bool _running = true;
    private string _statusMessage = string.Empty;
    private int _statusMessageFrames;

    private double _fps;
    private long _frameCount;
    private readonly Stopwatch _fpsTimer = Stopwatch.StartNew();

    /// <summary>世界生成信息（启动提示用）。</summary>
    public string GenerationSummary { get; private set; } = string.Empty;

    public Simulation Simulation => _sim;

    public GameLoop(Simulation sim, Terminal terminal, MapOverlay overlay)
    {
        _sim = sim;
        _terminal = terminal;
        _buffer = new RenderBuffer(terminal.Width, terminal.Height);
        _camera = new Camera(sim.World.Width, sim.World.Height);
        _renderer = new WorldViewRenderer(_camera)
        {
            Overlay = overlay,
            SelectedX = sim.World.Width / 2,
            SelectedY = sim.World.Height / 2,
        };
        _ansi = new AnsiWriter(terminal.Profile);

        if (sim.GenerationInfo != null)
        {
            GenerationSummary = sim.GenerationInfo.ToString();
        }
    }

    /// <summary>运行交互循环，返回进程退出码。</summary>
    public int Run()
    {
        _terminal.EnterGameMode();
        try
        {
            // 首帧先定好布局与缩放，再进入主循环，避免第一帧出现"跳一下"。
            RecomputeLayout(force: true);

            while (_running)
            {
                HandleInput();
                if (!_running) { break; }

                AdvanceSimulation();
                Render();
                SetStatusFade();
            }
        }
        finally
        {
            _terminal.LeaveGameMode();
        }
        return 0;
    }

    private void RecomputeLayout(bool force)
    {
        bool resized = _terminal.RefreshSize();
        if (resized || force)
        {
            _buffer.Resize(_terminal.Width, _terminal.Height);
        }

        int sidePanel = _terminal.Width >= 100 ? Panels.SidePanelWidth : 0;
        int mapCellsWide = _terminal.Width - sidePanel;
        int mapCellsHigh = _terminal.Height - Panels.TopBarHeight - Panels.BottomBarHeight;

        _camera.SetViewport(mapCellsWide, mapCellsHigh * 2);
        _camera.ApplyFitZoom();
    }

    private void AdvanceSimulation()
    {
        int multiplier = CurrentSpeedMultiplier;
        if (multiplier <= 0) { return; }

        double ticksThisFrame = _sim.Config.Clock.TicksPerSecondAt1x * multiplier / (double)System.Math.Max(1, _fps);
        int ticks = (int)System.Math.Round(ticksThisFrame);

        // 帧率低时限制单帧追赶量，避免"卡一下之后模拟猛冲一段"（第 81 条）。
        int cap = _sim.Config.Clock.MaxCatchUpTicksPerFrame;
        if (ticks > cap) { ticks = cap; }
        if (ticks > 0) { _sim.Tick(ticks); }
    }

    private int CurrentSpeedMultiplier
    {
        get
        {
            int[] speeds = _sim.Config.Clock.SpeedMultipliers;
            if (speeds.Length == 0) { return 1; }
            if (_speedIndex < 0) { _speedIndex = 0; }
            if (_speedIndex >= speeds.Length) { _speedIndex = speeds.Length - 1; }
            return speeds[_speedIndex];
        }
    }

    private void HandleInput()
    {
        // 每帧把输入队列排空（一次 ReadKey 只取一个事件，长按会积压）。
        System.ConsoleKeyInfo? key = _terminal.PollKey();
        while (key.HasValue)
        {
            ProcessKey(key.Value);
            key = _terminal.PollKey();
        }
    }

    private void ProcessKey(System.ConsoleKeyInfo key)
    {
        bool shift = (key.Modifiers & System.ConsoleModifiers.Shift) != 0;
        int panStep = shift ? 8 : 3;

        switch (key.Key)
        {
            case System.ConsoleKey.LeftArrow:
            case System.ConsoleKey.A:
                _camera.Pan(-panStep, 0);
                break;

            case System.ConsoleKey.RightArrow:
            case System.ConsoleKey.D:
                _camera.Pan(panStep, 0);
                break;

            case System.ConsoleKey.UpArrow:
            case System.ConsoleKey.W:
                _camera.Pan(0, -panStep * 2);
                break;

            case System.ConsoleKey.DownArrow:
            case System.ConsoleKey.S:
                _camera.Pan(0, panStep * 2);
                break;

            case System.ConsoleKey.OemPlus:
            case System.ConsoleKey.Add:
                _camera.ZoomIn();
                break;

            case System.ConsoleKey.OemMinus:
            case System.ConsoleKey.Subtract:
                _camera.ZoomOut();
                break;

            case System.ConsoleKey.Spacebar:
                _speedIndex = CurrentSpeedMultiplier == 0 ? 1 : 0;
                Notify(CurrentSpeedMultiplier == 0 ? "已暂停" : "继续");
                break;

            case System.ConsoleKey.D0:
            case System.ConsoleKey.NumPad0:
                SetSpeedMultiplier(0);
                break;

            case System.ConsoleKey.D1:
            case System.ConsoleKey.NumPad1:
                SetSpeedMultiplier(1);
                break;

            case System.ConsoleKey.D2:
            case System.ConsoleKey.NumPad2:
                SetSpeedMultiplier(2);
                break;

            case System.ConsoleKey.D4:
            case System.ConsoleKey.NumPad4:
                SetSpeedMultiplier(4);
                break;

            case System.ConsoleKey.D8:
            case System.ConsoleKey.NumPad8:
                SetSpeedMultiplier(8);
                break;

            case System.ConsoleKey.O:
                CycleOverlay();
                break;

            case System.ConsoleKey.H:
            case System.ConsoleKey.F1:
            case System.ConsoleKey.Oem2:   // '/'（很多终端里等价于帮助）
                _showHelp = !_showHelp;
                break;

            case System.ConsoleKey.N:
                RegenerateWithNewSeed();
                break;

            case System.ConsoleKey.R:
                _sim.RegenerateWorld(_sim.World.Seed);
                RecomputeLayout(force: true);
                Notify("已用同一种子重生成世界（" + _sim.World.Seed + "）");
                break;

            case System.ConsoleKey.Home:
                _camera.CenterOn(_sim.World.Width / 2, _sim.World.Height / 2);
                Notify("相机已复位");
                break;

            case System.ConsoleKey.Enter:
            {
                _camera.GetCenter(out int cx, out int cy);
                cx = SimMath.Clamp(cx, 0, _sim.World.Width - 1);
                cy = SimMath.Clamp(cy, 0, _sim.World.Height - 1);
                _renderer.SelectedX = cx;
                _renderer.SelectedY = cy;
                Notify("已选中 " + cx + "," + cy);
                break;
            }

            case System.ConsoleKey.Q:
            case System.ConsoleKey.Escape:
                _running = false;
                break;

            default:
                break;
        }
    }

    private void SetSpeedMultiplier(int multiplier)
    {
        int[] speeds = _sim.Config.Clock.SpeedMultipliers;
        for (int i = 0; i < speeds.Length; i++)
        {
            if (speeds[i] == multiplier)
            {
                _speedIndex = i;
                Notify(multiplier == 0 ? "已暂停" : "速度 ×" + multiplier);
                return;
            }
        }
        Notify("配置里没有 ×" + multiplier + " 这个速度档");
    }

    private void CycleOverlay()
    {
        int count = System.Enum.GetValues(typeof(MapOverlay)).Length;
        int next = ((int)_renderer.Overlay + 1) % count;
        _renderer.Overlay = (MapOverlay)next;
        Notify("叠加层：" + (_renderer.Overlay == MapOverlay.None ? "地形" : _renderer.Overlay.ToString()));
    }

    private void RegenerateWithNewSeed()
    {
        // 新种子来自玩家交互（非模拟内部），因此用真实随机是允许的：
        // 它只影响"选哪个世界"，一旦选定，模拟内部仍然完全确定性。
        int newSeed = (int)((uint)System.Environment.TickCount ^ ((uint)_frameCount * 2654435761u % 100000u));
        if (newSeed < 0) { newSeed = -newSeed; }
        _sim.RegenerateWorld(newSeed);
        _renderer.SelectedX = _sim.World.Width / 2;
        _renderer.SelectedY = _sim.World.Height / 2;
        _camera.CenterOn(_sim.World.Width / 2, _sim.World.Height / 2);
        Notify("新世界 seed=" + newSeed);
    }

    private void Notify(string message)
    {
        _statusMessage = message;
        _statusMessageFrames = 90;   // 约 1.5 秒
    }

    private void SetStatusFade()
    {
        if (_statusMessageFrames > 0)
        {
            _statusMessageFrames--;
            if (_statusMessageFrames == 0) { _statusMessage = string.Empty; }
        }
    }

    private void Render()
    {
        _frameCount++;

        // FPS 用 0.5 秒窗口平滑：单帧波动不该跳数字。
        if (_fpsTimer.Elapsed.TotalSeconds >= 0.5)
        {
            _fps = _frameCount / _fpsTimer.Elapsed.TotalSeconds;
            _frameCount = 0;
            _fpsTimer.Restart();
        }

        if (_fps <= 1.0) { _fps = 60.0; }

        RecomputeLayout(force: false);

        _buffer.Clear();
        _renderer.LightLevel = _sim.World.Calendar.LightLevel;
        _renderer.HighlightPhase = (float)((_sim.World.Tick % 60) / 60.0);
        _renderer.ShowSelection = !_showHelp;

        Panels.DrawTopBar(_buffer, _sim, CurrentSpeedMultiplier, _sim.World.Seed.ToString(
            System.Globalization.CultureInfo.InvariantCulture), _fps);

        int sidePanel = _terminal.Width >= 100 ? Panels.SidePanelWidth : 0;
        int mapCellsWide = _terminal.Width - sidePanel;
        int mapCellsHigh = _terminal.Height - Panels.TopBarHeight - Panels.BottomBarHeight;

        _renderer.Render(_buffer, _sim.World, 0, Panels.TopBarHeight, mapCellsWide, mapCellsHigh);

        if (sidePanel > 0)
        {
            Panels.DrawSidePanel(_buffer, _sim, mapCellsWide, sidePanel, Panels.TopBarHeight,
                mapCellsHigh, _renderer.SelectedX, _renderer.SelectedY);
        }

        Panels.DrawBottomBar(_buffer, _sim, _renderer.Overlay, _statusMessage);

        if (_showHelp)
        {
            Panels.DrawHelpOverlay(_buffer, _renderer.Overlay);
        }

        _ansi.Clear();
        _buffer.Blit(_ansi);
        _terminal.WriteRaw(_ansi.TakeText());
        _terminal.Flush();

        // 把 CPU 让出去：全速空转会占满一个核心，而 TUI 只需要 60 次/秒的刷新。
        Thread.Sleep(_speedIndex == 0 ? 30 : 8);
    }
}
