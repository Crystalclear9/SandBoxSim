using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SandBoxSim.Core.Foundation;

/// <summary>
/// 极简 JSON 值模型。之所以手写而不用 System.Text.Json：
///   1. 本仓库要求"没有 .NET SDK、只有 Roslyn csc 时也能编译"，任何外部包都会破坏这一点；
///   2. 存档与配置格式需要完全可控（字段顺序稳定、数字格式固定、注释可选），
///      这对"同 seed 逐字节可复现"很重要。
///
/// 支持：object / array / string / number / true / false / null。
/// 额外容忍（方便人类编辑配置）：// 行注释、/* 块注释 */、以及"$comment" 之类的键。
/// </summary>
/// <summary>
/// 元素访问：数组用整数下标（越界返回 Null 值，不抛异常），
/// 对象用字符串键。两条索引器分开定义，避免 "json[0]" 在对象上静默返回 null 这类误用。
/// </summary>
public sealed class JsonValue
{
    public enum Kind
    {
        Null = 0,
        Bool = 1,
        Number = 2,
        String = 3,
        Array = 4,
        Object = 5,
    }

    public Kind ValueKind { get; private set; }
    public bool BoolValue { get; private set; }
    public double NumberValue { get; private set; }
    public string StringValue { get; private set; } = string.Empty;
    public List<JsonValue> Items { get; private set; } = new List<JsonValue>();
    public Dictionary<string, JsonValue> Fields { get; private set; } = new Dictionary<string, JsonValue>(System.StringComparer.Ordinal);

    /// <summary>数组按下标取值。非数组或越界时返回 Null 值（调用方用 IsNull 判断）。</summary>
    public JsonValue this[int index]
    {
        get
        {
            if (ValueKind != Kind.Array) { return Null(); }
            if (index < 0 || index >= Items.Count) { return Null(); }
            return Items[index];
        }
    }

    /// <summary>对象按键取值。非对象或键不存在时返回 Null 值。</summary>
    public JsonValue this[string key] => Get(key);

    public static JsonValue Null() => new JsonValue { ValueKind = Kind.Null };
    public static JsonValue From(bool v) => new JsonValue { ValueKind = Kind.Bool, BoolValue = v };
    public static JsonValue From(double v) => new JsonValue { ValueKind = Kind.Number, NumberValue = v };
    public static JsonValue From(int v) => new JsonValue { ValueKind = Kind.Number, NumberValue = v };
    public static JsonValue From(long v) => new JsonValue { ValueKind = Kind.Number, NumberValue = v };
    public static JsonValue From(float v) => new JsonValue { ValueKind = Kind.Number, NumberValue = v };
    public static JsonValue From(string v) => new JsonValue { ValueKind = Kind.String, StringValue = v ?? string.Empty };

    public static JsonValue Array()
    {
        var v = new JsonValue { ValueKind = Kind.Array };
        return v;
    }

    public static JsonValue Object()
    {
        var v = new JsonValue { ValueKind = Kind.Object };
        return v;
    }

    public JsonValue Add(JsonValue item)
    {
        Items.Add(item);
        return this;
    }

    public JsonValue Set(string key, JsonValue value)
    {
        ValueKind = Kind.Object;
        Fields[key] = value;
        return this;
    }

    public bool IsNull => ValueKind == Kind.Null;
    public bool IsObject => ValueKind == Kind.Object;
    public bool IsArray => ValueKind == Kind.Array;
    public bool IsNumber => ValueKind == Kind.Number;

    public bool TryGet(string key, out JsonValue value)
    {
        value = Null();
        if (ValueKind != Kind.Object) { return false; }
        return Fields.TryGetValue(key, out value!);
    }

    public JsonValue Get(string key)
    {
        return TryGet(key, out JsonValue v) ? v : Null();
    }

    public int Count => ValueKind switch
    {
        Kind.Array => Items.Count,
        Kind.Object => Fields.Count,
        _ => 0,
    };

    /// <summary>
    /// 把一个**数组元素**当作整数读。
    ///
    /// 为什么需要这套"无键"取值器：存档用扁平数组存 10 万格的逐格数据
    /// （`terrain`、`moisture`…），读取时是 `array.Items[i]` 而不是 `array.Get("key")`。
    /// 让调用方直接碰 `NumberValue` 会暴露内部表示，而且到处都要写类型判断。
    /// </summary>
    public int AsInt()
        => IsNumber ? (int)System.Math.Round(NumberValue, System.MidpointRounding.AwayFromZero) : 0;

    /// <summary>把一个数组元素当作浮点读。</summary>
    public float AsFloat() => IsNumber ? (float)NumberValue : 0f;

    /// <summary>把一个数组元素当作 double 读。</summary>
    public double AsDouble() => IsNumber ? NumberValue : 0d;

    /// <summary>把一个数组元素当作字符串读。</summary>
    public string AsString() => ValueKind == Kind.String ? StringValue : string.Empty;

    /// <summary>
    /// 把一个数组元素当作 <c>ulong</c> 读。
    ///
    /// 优先按字符串解析：随机流状态是 64 位，而 JSON 的数字是 double（53 位有效），
    /// 直接写数字会丢精度 —— 那会表现为"偶尔分叉"，是最难查的一类问题。
    /// 仍然接受数字形式，是为了容忍手工编辑过的存档。
    /// </summary>
    public ulong AsULong()
    {
        if (IsNumber) { return (ulong)NumberValue; }
        if (ValueKind != Kind.String) { return 0UL; }

        return ulong.TryParse(StringValue, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out ulong parsed) ? parsed : 0UL;
    }

    // ---- 便捷取值（带默认值，避免调用方到处写类型判断） ----

    public int GetInt(string key, int fallback = 0)
    {
        JsonValue v = Get(key);
        if (!v.IsNumber) { return fallback; }
        return (int)System.Math.Round(v.NumberValue, System.MidpointRounding.AwayFromZero);
    }

    public long GetLong(string key, long fallback = 0)
    {
        JsonValue v = Get(key);
        if (!v.IsNumber) { return fallback; }
        return (long)System.Math.Round(v.NumberValue, System.MidpointRounding.AwayFromZero);
    }

    public float GetFloat(string key, float fallback = 0f)
    {
        JsonValue v = Get(key);
        return v.IsNumber ? (float)v.NumberValue : fallback;
    }

    public double GetDouble(string key, double fallback = 0d)
    {
        JsonValue v = Get(key);
        return v.IsNumber ? v.NumberValue : fallback;
    }

    public bool GetBool(string key, bool fallback = false)
    {
        JsonValue v = Get(key);
        return v.ValueKind == Kind.Bool ? v.BoolValue : fallback;
    }

    public string GetString(string key, string fallback = "")
    {
        JsonValue v = Get(key);
        return v.ValueKind == Kind.String ? v.StringValue : fallback;
    }

    public ulong GetULong(string key, ulong fallback = 0)
    {
        JsonValue v = Get(key);
        if (!v.IsNumber) { return fallback; }
        return (ulong)v.NumberValue;
    }

    /// <summary>读取 int 数组（例如 speedMultipliers）。</summary>
    public int[] GetIntArray(string key)
    {
        JsonValue v = Get(key);
        if (!v.IsArray) { return System.Array.Empty<int>(); }
        var result = new int[v.Items.Count];
        for (int i = 0; i < v.Items.Count; i++)
        {
            result[i] = v.Items[i].IsNumber
                ? (int)System.Math.Round(v.Items[i].NumberValue, System.MidpointRounding.AwayFromZero)
                : 0;
        }
        return result;
    }

    /// <summary>
    /// 确定性序列化。字段顺序 = 插入顺序（C# 的 Dictionary 在无删除时保持插入顺序，
    /// 且这里只用于写出，重建顺序也稳定），数字格式固定为不变文化的小数表示。
    /// </summary>
    public string ToJson(bool indented = true)
    {
        var sb = new StringBuilder(4096);
        Write(sb, this, indented, 0);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, JsonValue v, bool indented, int depth)
    {
        switch (v.ValueKind)
        {
            case Kind.Null:
                sb.Append("null");
                break;

            case Kind.Bool:
                sb.Append(v.BoolValue ? "true" : "false");
                break;

            case Kind.Number:
                sb.Append(FormatNumber(v.NumberValue));
                break;

            case Kind.String:
                WriteString(sb, v.StringValue);
                break;

            case Kind.Array:
            {
                if (v.Items.Count == 0) { sb.Append("[]"); break; }
                sb.Append('[');
                for (int i = 0; i < v.Items.Count; i++)
                {
                    if (i > 0) { sb.Append(','); }
                    if (indented) { NewLine(sb, depth + 1); }
                    Write(sb, v.Items[i], indented, depth + 1);
                }
                if (indented) { NewLine(sb, depth); }
                sb.Append(']');
                break;
            }

            case Kind.Object:
            {
                if (v.Fields.Count == 0) { sb.Append("{}"); break; }
                sb.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, JsonValue> kv in v.Fields)
                {
                    if (!first) { sb.Append(','); }
                    first = false;
                    if (indented) { NewLine(sb, depth + 1); }
                    WriteString(sb, kv.Key);
                    sb.Append(indented ? ": " : ":");
                    Write(sb, kv.Value, indented, depth + 1);
                }
                if (indented) { NewLine(sb, depth); }
                sb.Append('}');
                break;
            }
        }
    }

    private static void NewLine(StringBuilder sb, int depth)
    {
        sb.Append('\n');
        sb.Append(' ', depth * 2);
    }

    /// <summary>整数值不带小数点，小数保留必要精度（R 格式最紧凑且可回转）。</summary>
    public static string FormatNumber(double value)
    {
        if (double.IsNaN(value)) { return "0"; }
        if (double.IsPositiveInfinity(value)) { return "1e308"; }
        if (double.IsNegativeInfinity(value)) { return "-1e308"; }

        double rounded = System.Math.Round(value);
        if (System.Math.Abs(value - rounded) < 1e-9 && System.Math.Abs(value) < 1e15)
        {
            return ((long)rounded).ToString(CultureInfo.InvariantCulture);
        }
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u");
                        sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('"');
    }

    public override string ToString() => ToJson(indented: false);
}
