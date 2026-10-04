namespace SandBoxSim.Core.Environment;

/// <summary>
/// 天气类型（第 42 节）。天气是"跨系统传播"的关键上游：
/// Weather → Moisture → FarmProduction → FoodSupply → (M8) FoodPrice → Hunger → Population → Migration。
/// 因此天气必须写进 Tile 的湿度/温度，而不是只在 UI 上显示。
/// </summary>
public enum WeatherKind : byte
{
    Clear = 0,
    Cloudy = 1,
    Rain = 2,
    Storm = 3,
    Drought = 4,
    Snow = 5,
}

/// <summary>天气静态信息：对湿度/温度/作物/行动的影响系数。</summary>
public static class WeatherInfo
{
    /// <summary>
    /// 每小时对全图湿度的基础增量（正=变湿，负=变干）。
    ///
    /// # 这组数被 M5 改过一次，原因值得记下来
    ///
    /// 原值：Clear −0.006、Cloudy −0.001、Rain +0.030、Storm +0.045、Drought −0.035。
    /// 问题在于**变湿比变干强一个数量级，而且"多云"几乎不干**（−0.001）：
    /// 只要降雨占到两成时间，净收支就是正的，于是全图湿度**长期钉在 1.0**。
    ///
    /// 实测（100×100、200 天）：平均湿度最小 0.73、最大 1.0。
    /// 这不是"世界比较湿"，而是一个漂移到了上限的死状态，它同时让三件事失效：
    ///   * **火灾不可能发生** —— 干燥度≈0，M5 联调时"200 天 0 起火"的根因；
    ///   * **农田产量的湿度因子被钉死** `(0.25 + 0.75×1.0) = 1.0`，等于没有这一项；
    ///   * **干旱只在 `CropFactor` 上体现**，永远无法真正把地表弄干。
    ///
    /// 改法是把"蒸发的强度"提到与降雨同一量级 —— 物理上也对：
    /// 只要没在下雨，地表就在蒸发，而晴天蒸发得最快。
    /// 现在晴朗/多云/干旱分别是 −0.020 / −0.008 / −0.050。
    ///
    /// 配合 `Simulation` 里的**渐近**推进（变湿按 `(1−m)`、变干按 `m`），
    /// 湿度会在 0.4~0.6 附近自然波动，不再有上限死状态。
    /// </summary>
    public static float MoistureDeltaPerHour(WeatherKind kind)
    {
        switch (kind)
        {
            case WeatherKind.Clear: return -0.020f;
            case WeatherKind.Cloudy: return -0.008f;
            case WeatherKind.Rain: return 0.030f;
            case WeatherKind.Storm: return 0.045f;
            case WeatherKind.Drought: return -0.050f;
            case WeatherKind.Snow: return 0.010f;
            default: return 0f;
        }
    }

    /// <summary>每小时对全图温度的基础增量。</summary>
    public static float TemperatureDeltaPerHour(WeatherKind kind)
    {
        switch (kind)
        {
            case WeatherKind.Clear: return 0.004f;
            case WeatherKind.Cloudy: return -0.002f;
            case WeatherKind.Rain: return -0.008f;
            case WeatherKind.Storm: return -0.012f;
            case WeatherKind.Drought: return 0.010f;
            case WeatherKind.Snow: return -0.020f;
            default: return 0f;
        }
    }

    /// <summary>作物产出系数（第 23 节的 WeatherFactor，范围 [0,1.5]）。</summary>
    public static float CropFactor(WeatherKind kind)
    {
        switch (kind)
        {
            case WeatherKind.Clear: return 1.0f;
            case WeatherKind.Cloudy: return 0.9f;
            case WeatherKind.Rain: return 1.2f;
            case WeatherKind.Storm: return 0.7f;
            case WeatherKind.Drought: return 0.4f;
            case WeatherKind.Snow: return 0.15f;
            default: return 1.0f;
        }
    }

    /// <summary>火灾风险系数（第 43 节：干旱显著提高风险）。</summary>
    public static float FireRiskFactor(WeatherKind kind)
    {
        switch (kind)
        {
            case WeatherKind.Clear: return 1.0f;
            case WeatherKind.Cloudy: return 0.7f;
            case WeatherKind.Rain: return 0.15f;
            case WeatherKind.Storm: return 0.5f;   // 雷击起火，但雨本身压火
            case WeatherKind.Drought: return 2.5f;
            case WeatherKind.Snow: return 0.05f;
            default: return 1.0f;
        }
    }

    /// <summary>移动代价乘数（暴雨/暴雪让行进变慢）。</summary>
    public static float MovementFactor(WeatherKind kind)
    {
        switch (kind)
        {
            case WeatherKind.Storm: return 1.4f;
            case WeatherKind.Snow: return 1.3f;
            case WeatherKind.Rain: return 1.1f;
            default: return 1.0f;
        }
    }

    public static string NameOf(WeatherKind kind)
    {
        switch (kind)
        {
            case WeatherKind.Clear: return "clear";
            case WeatherKind.Cloudy: return "cloudy";
            case WeatherKind.Rain: return "rain";
            case WeatherKind.Storm: return "storm";
            case WeatherKind.Drought: return "drought";
            case WeatherKind.Snow: return "snow";
            default: return "unknown";
        }
    }

    /// <summary>中文短名（只用于 TUI 显示）。</summary>
    public static string DisplayNameOf(WeatherKind kind)
    {
        switch (kind)
        {
            case WeatherKind.Clear: return "晴";
            case WeatherKind.Cloudy: return "多云";
            case WeatherKind.Rain: return "雨";
            case WeatherKind.Storm: return "暴风雨";
            case WeatherKind.Drought: return "干旱";
            case WeatherKind.Snow: return "雪";
            default: return "?";
        }
    }
}

/// <summary>
/// 世界天气状态。每个游戏小时推进一次（HourTick），并直接写入 Tile 的湿度/温度，
/// 这样"天气影响农业"不是一句设定，而是通过 Tile 数据真实传播的因果链。
/// </summary>
public sealed class Weather
{
    public WeatherKind Kind { get; private set; } = WeatherKind.Clear;

    /// <summary>已持续小时数（用于天气惯性：天气不会每小时乱跳）。</summary>
    public int DurationHours { get; private set; }

    /// <summary>下一次天气切换的剩余小时数。</summary>
    private int _hoursUntilChange;

    /// <summary>本天气累计降雨量（统计/报告用）。</summary>
    public float AccumulatedRain { get; private set; }

    /// <summary>干旱累计小时（决定火灾与饥荒事件是否触发，第 44 节）。</summary>
    public int DroughtHours { get; private set; }

    public Weather() => _hoursUntilChange = 8;

    public void Reset()
    {
        Kind = WeatherKind.Clear;
        DurationHours = 0;
        _hoursUntilChange = 8;
        AccumulatedRain = 0f;
        DroughtHours = 0;
    }

    /// <summary>
    /// 每小时推进。用"换天气"而不是"每小时重抽"：
    /// 每小时的独立重抽会让天气变成白噪声，无法形成"连续干旱导致饥荒"的长链条。
    /// </summary>
    public void AdvanceHour(Foundation.DeterministicRandom rng, float averageMoisture, float averageTemperature)
    {
        DurationHours++;

        if (Kind == WeatherKind.Drought) { DroughtHours++; }
        else if (DroughtHours > 0) { DroughtHours--; }

        float rain = WeatherInfo.MoistureDeltaPerHour(Kind);
        if (rain > 0f) { AccumulatedRain += rain; }

        _hoursUntilChange--;
        if (_hoursUntilChange > 0) { return; }

        WeatherKind next = ChooseNext(rng, averageMoisture, averageTemperature);
        if (next != Kind)
        {
            Kind = next;
            DurationHours = 0;
        }
        _hoursUntilChange = 4 + rng.NextInt(13); // 4~16 小时
    }

    /// <summary>
    /// 天气转移。规则是"状态驱动 + 少量随机"，不是纯随机（第 44 节）：
    /// 已经很湿 → 更容易下雨；已经很干 → 更容易进入干旱；
    /// 温度高 + 湿度低 → 火灾风险自然升高。
    /// </summary>
    private static WeatherKind ChooseNext(Foundation.DeterministicRandom rng, float moisture, float temperature)
    {
        double wetBias = Foundation.SimMath.Clamp01(moisture);
        double dryBias = 1.0 - wetBias;
        double coldBias = 1.0 - Foundation.SimMath.Clamp01(temperature);

        // 七个候选状态与它们的权重（顺序固定 => 确定性）
        double[] weights = new double[7];
        weights[0] = 0.35 * dryBias;                 // Clear
        weights[1] = 0.25;                           // Cloudy
        weights[2] = 0.30 * wetBias;                 // Rain
        weights[3] = 0.10 * wetBias;                 // Storm
        weights[4] = 0.15 * dryBias * dryBias;       // Drought
        weights[5] = 0.40 * coldBias;                // Snow
        weights[6] = 0.0;                            // 占位，保持数组长度与枚举无关

        int picked = rng.SampleWeightedIndex(weights, 6);
        if (picked < 0) { return WeatherKind.Clear; }
        return (WeatherKind)picked;
    }

    /// <summary>强制设置天气（玩家灾害工具用，见第 45 节）。</summary>
    public void ForceKind(WeatherKind kind, int durationHours)
    {
        Kind = kind;
        DurationHours = 0;
        _hoursUntilChange = durationHours > 0 ? durationHours : 6;
    }

    /// <summary>导出状态（存档）。</summary>
    public int HoursUntilChange => _hoursUntilChange;

    public void RestoreFromSave(WeatherKind kind, int durationHours, int hoursUntilChange, float accumulatedRain, int droughtHours)
    {
        Kind = kind;
        DurationHours = durationHours;
        _hoursUntilChange = hoursUntilChange;
        AccumulatedRain = accumulatedRain;
        DroughtHours = droughtHours;
    }
}
