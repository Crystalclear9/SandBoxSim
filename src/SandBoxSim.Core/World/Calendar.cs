namespace SandBoxSim.Core.Environment;

/// <summary>
/// 游戏日历与昼夜循环。
///
/// 约定（第 8 节）：1 Tick = 1 游戏分钟，全局只有一个时钟 —— Simulation.Clock。
/// 任何系统都不允许自己计时或读真实时间。
/// </summary>
public sealed class Calendar
{
    /// <summary>天亮（日出）的小时。</summary>
    public const int DawnHour = 6;

    /// <summary>天黑（日落）的小时。</summary>
    public const int DuskHour = 19;

    /// <summary>累计 tick 数（模拟的唯一时间基准）。</summary>
    public long Tick { get; internal set; }

    public int TicksPerHour { get; }
    public int HoursPerDay { get; }

    public Calendar(int ticksPerHour, int hoursPerDay)
    {
        TicksPerHour = ticksPerHour > 0 ? ticksPerHour : 60;
        HoursPerDay = hoursPerDay > 0 ? hoursPerDay : 24;
        Tick = 0;
    }

    public int TicksPerDay => TicksPerHour * HoursPerDay;

    /// <summary>第几天（从 1 开始，便于玩家理解"第 3 天"）。</summary>
    public int Day => (int)(Tick / TicksPerDay) + 1;

    /// <summary>
    /// "刚结束的那一天（1 起）"。在日边界（tick 是 TicksPerDay 的整数倍）上，
    /// Day 已经翻到新的一天，而统计样本描述的是**上一天**的收支，
    /// 因此日统计必须用这个属性而不是 Day，否则报告里的日期会整体虚报一天。
    /// </summary>
    public int CompletedDay
    {
        get
        {
            long completed = (Tick / TicksPerDay);
            return (int)(completed <= 0 ? 1 : completed);
        }
    }

    /// <summary>当天已过小时数 [0, HoursPerDay)。</summary>
    public int Hour => (int)((Tick % TicksPerDay) / TicksPerHour);

    /// <summary>当天已过分钟数 [0, 60)。</summary>
    public int Minute => (int)(Tick % TicksPerHour);

    /// <summary>是否白天。</summary>
    public bool IsDay => Hour >= DawnHour && Hour < DuskHour;

    public bool IsNight => !IsDay;

    /// <summary>
    /// 光照强度 [0,1]：正午为 1，午夜约 0.05，日出日落平滑过渡。
    /// 供渲染明暗、夜间行为（睡眠效用）与未来的夜行生物使用。
    /// </summary>
    public float LightLevel
    {
        get
        {
            float hour = Hour + (Minute / (float)TicksPerHour);

            if (hour >= DawnHour && hour <= DuskHour)
            {
                // 白天：以正午为峰值的余弦
                float t = (hour - DawnHour) / (float)(DuskHour - DawnHour);
                return 0.35f + (0.65f * Foundation.SimMath.Clamp01((float)System.Math.Sin(System.Math.PI * t)));
            }

            // 夜晚：从黄昏到黎明抬升回日出值
            float nightHours = (HoursPerDay - DuskHour) + DawnHour;
            float intoNight = hour >= DuskHour ? (hour - DuskHour) : (hour + (HoursPerDay - DuskHour));
            float nt = nightHours > 0f ? intoNight / nightHours : 0f;
            return 0.35f - (0.30f * Foundation.SimMath.Clamp01((float)System.Math.Sin(System.Math.PI * nt)));
        }
    }

    /// <summary>推进一个 tick。</summary>
    public void Advance(int ticks = 1)
    {
        Tick += ticks;
        if (Tick < 0) { Tick = 0; }
    }

    /// <summary>本 tick 是否恰好跨过一个小时候边界。</summary>
    public bool IsHourBoundary => Tick % TicksPerHour == 0;

    /// <summary>本 tick 是否恰好跨过一天边界。</summary>
    public bool IsDayBoundary => Tick % TicksPerDay == 0;

    /// <summary>形如 "Day 12 07:30" 的显示字符串（仅 UI 使用）。</summary>
    public string DisplayStamp()
        => "Day " + Day + " " + Hour.ToString("00", System.Globalization.CultureInfo.InvariantCulture)
         + ":" + Minute.ToString("00", System.Globalization.CultureInfo.InvariantCulture);

    public void RestoreFromSave(long tick) => Tick = tick;
}
