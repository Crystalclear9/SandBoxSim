namespace SandBoxSim.ConsoleApp.Cli;

/// <summary>
/// 极简命令行解析器。
///
/// 不用 System.CommandLine 的理由：本仓库要求在"只有 Roslyn csc"的环境下也能构建，
/// 任何 NuGet 依赖都会破坏这条降级通道（见 docs/12-Milestones.md）。
/// 参数集很小，手写解析反而更好控制 —— 包括"未知参数必须报错而不是静默忽略"这条。
/// </summary>
public sealed class Args
{
    private readonly System.Collections.Generic.Dictionary<string, string> _values =
        new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

    private readonly System.Collections.Generic.HashSet<string> _flags =
        new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

    private readonly System.Collections.Generic.List<string> _positional = new System.Collections.Generic.List<string>();
    private readonly System.Collections.Generic.List<string> _errors = new System.Collections.Generic.List<string>();

    /// <summary>已知的开关参数（不接受值）。</summary>
    private static readonly string[] KnownFlags =
    {
        "--headless", "--digest", "--batch", "--snapshot", "--no-color", "--help", "--verbose",
        "--no-alternate-screen", "--invariants", "--no-invariants", "--repeat", "--profile",
        // M4：读档启动（存档路径由 --load-file 给出）
        "--load",
    };

    /// <summary>已知的取值参数。</summary>
    private static readonly string[] KnownValues =
    {
        "--seed", "--days", "--ticks", "--width", "--height", "--config", "--out",
        "--seeds", "--snapshot-days", "--width-px", "--cell-scale", "--overlay", "--speed",
        "--report", "--quiet-after",
        // M1：初始放置的居民数量与散布半径（"玩家创造条件"的最小入口）
        "--agents", "--agent-radius",
        // M4：存档 / 读档 / 自动存档
        "--save-file", "--load-file", "--auto-save-days",
    };

    public Args(string[] raw)
    {
        string[] args = raw ?? System.Array.Empty<string>();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (string.IsNullOrEmpty(arg)) { continue; }

            if (arg[0] == '-')
            {
                // 支持 "--key=value"
                int eq = arg.IndexOf('=');
                string key = eq >= 0 ? arg.Substring(0, eq) : arg;
                string inlineValue = eq >= 0 ? arg.Substring(eq + 1) : string.Empty;

                if (IsKnownFlag(key))
                {
                    if (eq >= 0)
                    {
                        _errors.Add("开关参数不接受值：" + key);
                    }
                    _flags.Add(key);
                    continue;
                }

                if (IsKnownValue(key))
                {
                    string value;
                    if (eq >= 0)
                    {
                        value = inlineValue;
                    }
                    else if (i + 1 < args.Length)
                    {
                        value = args[++i];
                    }
                    else
                    {
                        _errors.Add("参数缺少值：" + key);
                        continue;
                    }
                    _values[key] = value;
                    continue;
                }

                _errors.Add("未知参数：" + key);
                continue;
            }

            _positional.Add(arg);
        }
    }

    public System.Collections.Generic.IReadOnlyList<string> Errors => _errors;
    public bool HasErrors => _errors.Count > 0;
    public System.Collections.Generic.IReadOnlyList<string> Positional => _positional;

    private static bool IsKnownFlag(string key)
    {
        for (int i = 0; i < KnownFlags.Length; i++)
        {
            if (string.Equals(KnownFlags[i], key, System.StringComparison.OrdinalIgnoreCase)) { return true; }
        }
        return false;
    }

    private static bool IsKnownValue(string key)
    {
        for (int i = 0; i < KnownValues.Length; i++)
        {
            if (string.Equals(KnownValues[i], key, System.StringComparison.OrdinalIgnoreCase)) { return true; }
        }
        return false;
    }

    public bool Flag(string name) => _flags.Contains(name);

    public string String(string name, string fallback = "")
        => _values.TryGetValue(name, out string? value) ? value : fallback;

    public int Int(string name, int fallback)
    {
        if (!_values.TryGetValue(name, out string? value)) { return fallback; }
        if (int.TryParse(value, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int parsed))
        {
            return parsed;
        }
        _errors.Add("参数 " + name + " 需要整数，得到：" + value);
        return fallback;
    }

    public float Float(string name, float fallback)
    {
        if (!_values.TryGetValue(name, out string? value)) { return fallback; }
        if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float parsed))
        {
            return parsed;
        }
        _errors.Add("参数 " + name + " 需要数字，得到：" + value);
        return fallback;
    }

    /// <summary>逗号分隔的整数列表，支持 "1,2,3" 与 "1..20" 两种写法。</summary>
    public int[] IntList(string name)
    {
        if (!_values.TryGetValue(name, out string? value) || string.IsNullOrWhiteSpace(value))
        {
            return System.Array.Empty<int>();
        }

        var result = new System.Collections.Generic.List<int>();
        string[] parts = value.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            int rangeIndex = part.IndexOf("..", System.StringComparison.Ordinal);
            if (rangeIndex > 0)
            {
                string fromText = part.Substring(0, rangeIndex);
                string toText = part.Substring(rangeIndex + 2);
                if (int.TryParse(fromText, out int from) && int.TryParse(toText, out int to))
                {
                    int step = from <= to ? 1 : -1;
                    for (int v = from; step > 0 ? v <= to : v >= to; v += step)
                    {
                        result.Add(v);
                        if (result.Count > 1000) { break; }
                    }
                    continue;
                }
                _errors.Add("参数 " + name + " 的区间写法非法：" + part);
                continue;
            }

            if (int.TryParse(part, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int single))
            {
                result.Add(single);
            }
            else
            {
                _errors.Add("参数 " + name + " 含有非整数项：" + part);
            }
        }
        return result.ToArray();
    }

    public static string HelpText()
    {
        return string.Join("\n", new[]
        {
            "SandBoxSim —— 高涌现性沙盒模拟游戏",
            "",
            "用法：",
            "  SandBoxSim.Console [选项]",
            "",
            "运行模式：",
            "  （默认）              交互 TUI：观察世界、平移缩放、切换叠加层",
            "  --headless            无界面长跑，产出报告与统计（人造观测证据）",
            "  --digest              跑两遍并比对状态摘要，用于验证确定性（CI 用）",
            "  --batch               多 seed 批量长跑，检查鲁棒性与涌现性",
            "  --snapshot            长跑并在若干天数导出世界 PNG 快照",
            "",
            "世界参数：",
            "  --seed <int>          世界种子（默认 839102，决定一切）",
            "  --width <int>         世界宽（默认取配置 100）",
            "  --height <int>        世界高（默认取配置 100）",
            "  --config <path>       配置文件路径（默认 config/sim.default.json）",
            "  --overlay <name>      起始叠加层：none/fertility/moisture/temperature/wood/food/vegetation/firerisk/walkable/population/aistate/buildings",
            "  --agents <int>        初始放置的居民数量（默认 0；玩家创造的是条件，不是结果）",
            "  --agent-radius <int>  居民初始散布半径（格，默认 8）",
            "",
            "存档与读档：",
            "  --save-file <path>    跑完之后把世界存到指定文件（默认写到产出目录）",
            "  --load-file <path>    从存档启动；世界状态完全来自存档，自动存档",
            "  --load                等价于 --load-file 的语义提示（配合 --load-file 使用）",
            "  --auto-save-days <n>  每 n 天自动存档一次（0 = 关闭；仅 TUI 与长跑模式）",
            "",
            "时间与产出：",
            "  --days <int>          headless/batch 跑多少游戏天（默认 100）",
            "  --ticks <int>         直接指定 tick 数（优先于 --days）",
            "  --snapshot-days <int> 快照间隔天数",
            "  --width-px <int>      快照图像宽度（默认 800）",
            "  --cell-scale <int>    快照每格放大倍数（默认按宽度自动）",
            "  --out <dir>           产出目录（默认 runs/）",
            "  --seeds <list>        批量 seed，支持 1,2,3 或 1..20",
            "",
            "其它：",
            "  --no-color            关闭颜色（重定向输出时自动关闭）",
            "  --no-invariants       关闭每 64 tick 的不变量检查（性能对比用）",
            "  --profile             输出各模拟阶段的实际 CPU 时间",
            "  --help                显示本帮助",
        });
    }
}
