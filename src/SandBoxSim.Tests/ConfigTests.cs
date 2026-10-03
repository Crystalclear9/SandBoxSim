using System.Collections.Generic;
using SandBoxSim.Core.Foundation;
using SandBoxSim.Tests.Framework;

namespace SandBoxSim.Tests;

/// <summary>
/// 配置与 JSON 测试。
///
/// 最关键的一条是 <c>DefaultsMatchJson</c>：它在"内置默认值"与 config/sim.default.json 之间
/// 建立强制一致性。否则会出现"玩家改配置文件没效果"或"配置文件与代码默认值悄悄分叉"
/// 这类极难排查的问题 —— 这在数据驱动的模拟游戏里是最常见的坑之一。
/// </summary>
public sealed class ConfigTests
{
    /// <summary>从测试可执行文件向上寻找仓库根（含 config/sim.default.json 的那一级）。</summary>
    public static string FindRepoRoot()
    {
        string start = System.AppContext.BaseDirectory;
        var directory = new System.IO.DirectoryInfo(start);

        for (int depth = 0; depth < 12 && directory != null; depth++)
        {
            string candidate = System.IO.Path.Combine(directory.FullName, "config", "sim.default.json");
            if (System.IO.File.Exists(candidate)) { return directory.FullName; }

            if (directory.Parent == null) { break; }
            directory = directory.Parent;
        }

        // 兜底：当前工作目录
        string cwdCandidate = System.IO.Path.Combine(System.Environment.CurrentDirectory, "config", "sim.default.json");
        if (System.IO.File.Exists(cwdCandidate)) { return System.Environment.CurrentDirectory; }

        return start;
    }

    [Fact("JSON 解析器必须支持对象/数组/字符串/数字/布尔/null")]
    public void JsonParsesBasicValues()
    {
        JsonValue root = JsonParser.Parse("{\"a\": 1, \"b\": [1,2,3], \"c\": \"x\", \"d\": true, \"e\": null, \"f\": 1.5e3}");

        Assert.True(root.IsObject, "根节点应是对象");
        Assert.Equal("a,b,c,d,e,f", string.Join(",", root.Fields.Keys), "键集合不符");

        JsonValue a = root.Get("a");
        Assert.True(a.IsNumber, "a 应是数字");
        Assert.Equal(1, (int)a.NumberValue, "a 的值应是 1");

        JsonValue b = root.Get("b");
        Assert.True(b.IsArray, "b 应是数组");
        Assert.Equal(3, b.Items.Count, "b 应有 3 个元素");
        Assert.Equal(1, (int)b.Items[0].NumberValue, "b[0]");
        Assert.Equal(3, (int)b.Items[2].NumberValue, "b[2]");

        Assert.Equal("x", root.Get("c").StringValue, "c 应是字符串");
        Assert.True(root.Get("d").BoolValue, "d 应是 true");
        Assert.True(root.Get("e").IsNull, "e 应是 null");
        Assert.Near(1500.0, root.Get("f").NumberValue, 1e-9, "f 应是 1500");
    }

    [Fact("JSON 解析器必须容忍注释（配置文件要给人读）")]
    public void JsonToleratesComments()
    {
        string text = "{\n  // 行注释\n  \"a\": 1, /* 块注释 */\n  \"b\": 2\n}";
        JsonValue root = JsonParser.Parse(text);
        Assert.Equal(1, root.GetInt("a"));
        Assert.Equal(2, root.GetInt("b"));
    }

    [Fact("JSON 非法输入必须报出可定位的行列号")]
    public void JsonReportsLineAndColumn()
    {
        JsonParseException ex = Assert.Throws<JsonParseException>(() => JsonParser.Parse("{\n  \"a\": ,\n}"));
        Assert.Greater(ex.Line, 1, "错误行号应指向第 2 行");
        Assert.True(ex.Message.Contains("line"), "错误信息里应包含行列提示：" + ex.Message);
    }

    [Fact("JSON 未知转义必须报错而不是静默吞掉")]
    public void JsonRejectsUnknownEscape()
    {
        Assert.Throws<JsonParseException>(() => JsonParser.Parse("{\"a\": \"\\q\"}"));
    }

    [Fact("JSON 往返必须保持值不变")]
    public void JsonRoundTrip()
    {
        JsonValue original = JsonValue.Object();
        original.Set("i", JsonValue.From(42));
        original.Set("d", JsonValue.From(3.25));
        original.Set("s", JsonValue.From("带中文的字符串"));
        original.Set("b", JsonValue.From(true));
        var array = JsonValue.Array();
        array.Add(JsonValue.From(1));
        array.Add(JsonValue.From(2));
        original.Set("arr", array);

        string text = original.ToJson();
        JsonValue parsed = JsonParser.Parse(text);

        Assert.Equal(42, parsed.GetInt("i"));
        Assert.Near(3.25, parsed.GetDouble("d"), 1e-9);
        Assert.Equal("带中文的字符串", parsed.GetString("s"));
        Assert.True(parsed.GetBool("b"));
        Assert.Equal(2, parsed.Get("arr").Count);
    }

    [Fact("整数在序列化时不应出现小数点（避免配置与存档变得难读）")]
    public void IntegerFormattingIsClean()
    {
        Assert.Equal("42", JsonValue.FormatNumber(42.0));
        Assert.Equal("-7", JsonValue.FormatNumber(-7.0));
        Assert.Equal("0", JsonValue.FormatNumber(0.0));
        Assert.True(JsonValue.FormatNumber(0.5).Contains("0.5"));
    }

    [Fact("配置文件必须存在且能被解析（这是启动的硬前提）")]
    public void DefaultConfigFileLoads()
    {
        string root = FindRepoRoot();
        string path = System.IO.Path.Combine(root, "config", "sim.default.json");

        Assert.True(System.IO.File.Exists(path), "找不到 " + path);

        ConfigLoadResult<SimConfig> result = ConfigLoader.Load<SimConfig>(path);
        Assert.True(result.Ok, "配置加载失败：" + result.Error);
        Assert.Equal(0, result.Warnings.Count, "配置里存在未知项：" + string.Join(" / ", result.Warnings));
    }

    [Fact("内置默认值必须与 config/sim.default.json 完全一致")]
    public void DefaultsMatchJson()
    {
        string root = FindRepoRoot();
        string path = System.IO.Path.Combine(root, "config", "sim.default.json");
        if (!System.IO.File.Exists(path))
        {
            // 无配置文件时（例如把测试二进制单独拷走）跳过这条例行检查。
            return;
        }

        var builtIn = new SimConfig();
        ConfigLoadResult<SimConfig> loaded = ConfigLoader.Load<SimConfig>(path);
        Assert.True(loaded.Ok, loaded.Error);
        SimConfig fromJson = loaded.Value;

        // 逐个比较"关键参数"，任何一处不一致都说明代码与配置文件分叉了。
        Assert.Equal(builtIn.Clock.TicksPerHour, fromJson.Clock.TicksPerHour, "clock.ticksPerHour 不一致");
        Assert.Equal(builtIn.Clock.HoursPerDay, fromJson.Clock.HoursPerDay, "clock.hoursPerDay 不一致");
        Assert.Equal(builtIn.Clock.TicksPerSecondAt1x, fromJson.Clock.TicksPerSecondAt1x, "clock.ticksPerSecondAt1x 不一致");
        Assert.Equal(builtIn.Clock.SpeedMultipliers.Length, fromJson.Clock.SpeedMultipliers.Length, "速度档数量不一致");

        Assert.Equal(builtIn.World.Width, fromJson.World.Width, "world.width 不一致");
        Assert.Equal(builtIn.World.Height, fromJson.World.Height, "world.height 不一致");
        Assert.Equal(builtIn.World.ChunkSize, fromJson.World.ChunkSize, "world.chunkSize 不一致");
        Assert.Near(builtIn.World.AmbientTemperature, fromJson.World.AmbientTemperature, 1e-6, "world.ambientTemperature 不一致");
        Assert.Near(builtIn.World.AmbientMoisture, fromJson.World.AmbientMoisture, 1e-6, "world.ambientMoisture 不一致");

        Assert.Equal(builtIn.WorldGen.Seed, fromJson.WorldGen.Seed, "worldgen.seed 不一致");
        Assert.Near(builtIn.WorldGen.ContinentFrequency, fromJson.WorldGen.ContinentFrequency, 1e-6, "continentFrequency 不一致");
        Assert.Equal(builtIn.WorldGen.ContinentOctaves, fromJson.WorldGen.ContinentOctaves, "continentOctaves 不一致");
        Assert.Near(builtIn.WorldGen.WaterLevel, fromJson.WorldGen.WaterLevel, 1e-6, "waterLevel 不一致");
        Assert.Near(builtIn.WorldGen.MountainLevel, fromJson.WorldGen.MountainLevel, 1e-6, "mountainLevel 不一致");
        Assert.Near(builtIn.WorldGen.ForestMoistureThreshold, fromJson.WorldGen.ForestMoistureThreshold, 1e-6, "forestMoistureThreshold 不一致");

        Assert.Near(builtIn.Resources.WoodCapacityPerForestTile, fromJson.Resources.WoodCapacityPerForestTile, 1e-4, "woodCapacity 不一致");
        Assert.Near(builtIn.Resources.WoodGrowthRate, fromJson.Resources.WoodGrowthRate, 1e-6, "woodGrowthRate 不一致");
        Assert.Near(builtIn.Resources.FoodGrowthRate, fromJson.Resources.FoodGrowthRate, 1e-6, "foodGrowthRate 不一致");
        Assert.Near(builtIn.Resources.DepletionWarnFraction, fromJson.Resources.DepletionWarnFraction, 1e-6, "depletionWarnFraction 不一致");

        Assert.Equal(builtIn.Debug.StateHashEveryTicks, fromJson.Debug.StateHashEveryTicks, "debug.stateHashEveryTicks 不一致");
    }

    [Fact("未知配置项必须产生警告而不是静默忽略")]
    public void UnknownKeysProduceWarnings()
    {
        ConfigLoadResult<SimConfig> result = ConfigLoader.LoadFromJson<SimConfig>(
            "{\"world\": {\"width\": 50, \"nonexistentKey\": 3}}");

        Assert.True(result.Ok, result.Error);
        Assert.Greater(result.Warnings.Count, 0, "未知键必须产生警告");
        Assert.Equal(50, result.Value.World.Width);
    }

    [Fact("缺失的字段必须保留内置默认值")]
    public void MissingFieldsKeepDefaults()
    {
        ConfigLoadResult<SimConfig> result = ConfigLoader.LoadFromJson<SimConfig>("{\"world\": {\"width\": 42}}");
        Assert.True(result.Ok, result.Error);
        Assert.Equal(42, result.Value.World.Width);
        Assert.Equal(new SimConfig().World.Height, result.Value.World.Height);
    }

    [Fact("JSON 解析失败时必须返回错误而不是抛异常")]
    public void BrokenJsonReturnsError()
    {
        ConfigLoadResult<SimConfig> result = ConfigLoader.LoadFromJson<SimConfig>("{ this is not json }");
        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Fact("配置克隆必须是深拷贝（世界重置时参数不能被串改）")]
    public void CloneIsDeepCopy()
    {
        var original = new SimConfig();
        original.World.Width = 77;
        original.Resources.WoodGrowthRate = 0.5f;

        SimConfig clone = original.Clone();
        clone.World.Width = 12;
        clone.Resources.WoodGrowthRate = 0.9f;

        Assert.Equal(77, original.World.Width, "克隆体修改影响了原对象");
        Assert.Near(0.5, original.Resources.WoodGrowthRate, 1e-6, "克隆体的浮点字段与原对象共享了内存");
    }

    [Fact("ClockConfig 的派生值必须自洽")]
    public void ClockDerivedValues()
    {
        var clock = new ClockConfig();
        Assert.Equal(60, clock.TicksPerHour);
        Assert.Equal(24, clock.HoursPerDay);
        Assert.Equal(1440, clock.TicksPerDay);

        // 速度档必须包含暂停与 1×
        var speeds = new List<int>(clock.SpeedMultipliers);
        Assert.True(speeds.Contains(0), "速度档里必须有暂停（0）");
        Assert.True(speeds.Contains(1), "速度档里必须有 1×");
    }

    [Fact("配置序列化必须能还原（用于写出'生效参数快照'）")]
    public void ConfigSerializationRoundTrip()
    {
        var config = new SimConfig();
        config.World.Width = 64;
        config.Resources.StoneHarvestPerAction = 7.5f;

        string json = ConfigLoader.ToJson(config);
        ConfigLoadResult<SimConfig> reloaded = ConfigLoader.LoadFromJson<SimConfig>(json);

        Assert.True(reloaded.Ok, reloaded.Error);
        Assert.Equal(64, reloaded.Value.World.Width);
        Assert.Near(7.5, reloaded.Value.Resources.StoneHarvestPerAction, 1e-6);
    }
}
// MARKER_639266516141853148
