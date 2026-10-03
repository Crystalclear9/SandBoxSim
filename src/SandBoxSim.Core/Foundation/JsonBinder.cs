using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace SandBoxSim.Core.Foundation;

/// <summary>
/// 用反射把 <see cref="JsonValue"/> 映射到强类型配置对象（第 96.3 / 96.6 条：
/// 优先数据驱动，系统参数放配置文件）。
///
/// 设计取舍：比手写每个字段的解析省几百行样板，代价是启动时一次反射（可忽略）。
/// 未知键会被记录到 <see cref="ConfigLoadResult.Warnings"/>，避免"配置写了但没生效"
/// 这种在模拟游戏里极难排查的问题。
/// </summary>
public static class JsonBinder
{
    private static readonly Dictionary<System.Type, FieldInfo[]> FieldCache = new Dictionary<System.Type, FieldInfo[]>();

    private static FieldInfo[] GetFields(System.Type type)
    {
        if (FieldCache.TryGetValue(type, out FieldInfo[]? cached) && cached != null) { return cached; }

        var list = new List<FieldInfo>();
        FieldInfo[] all = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].IsInitOnly) { continue; }
            list.Add(all[i]);
        }
        FieldInfo[] result = list.ToArray();
        FieldCache[type] = result;
        return result;
    }

    /// <summary>把 JSON 对象映射到已有实例上；反序列化失败的字段保留原值。</summary>
    public static void Bind(JsonValue source, object target, List<string> warnings, string path)
    {
        if (target == null || !source.IsObject) { return; }
        System.Type type = target.GetType();

        foreach (KeyValuePair<string, JsonValue> kv in source.Fields)
        {
            string key = kv.Key;

            // 约定：以 '$' 开头的键是给读者看的注释，直接跳过。
            if (key.Length > 0 && key[0] == '$') { continue; }

            FieldInfo? field = FindField(type, key);
            if (field == null)
            {
                warnings.Add("未知配置项被忽略：" + Join(path, key));
                continue;
            }

            string childPath = Join(path, key);
            JsonValue value = kv.Value;
            System.Type ft = field.FieldType;

            if (ft == typeof(float))
            {
                if (value.IsNumber) { field.SetValue(target, (float)value.NumberValue); }
                else { warnings.Add("类型不匹配（期望 float）：" + childPath); }
            }
            else if (ft == typeof(double))
            {
                if (value.IsNumber) { field.SetValue(target, value.NumberValue); }
                else { warnings.Add("类型不匹配（期望 double）：" + childPath); }
            }
            else if (ft == typeof(int))
            {
                if (value.IsNumber) { field.SetValue(target, (int)System.Math.Round(value.NumberValue, System.MidpointRounding.AwayFromZero)); }
                else { warnings.Add("类型不匹配（期望 int）：" + childPath); }
            }
            else if (ft == typeof(long))
            {
                if (value.IsNumber) { field.SetValue(target, (long)System.Math.Round(value.NumberValue, System.MidpointRounding.AwayFromZero)); }
                else { warnings.Add("类型不匹配（期望 long）：" + childPath); }
            }
            else if (ft == typeof(ulong))
            {
                if (value.IsNumber) { field.SetValue(target, (ulong)System.Math.Max(0, System.Math.Round(value.NumberValue))); }
                else { warnings.Add("类型不匹配（期望 ulong）：" + childPath); }
            }
            else if (ft == typeof(bool))
            {
                if (value.ValueKind == JsonValue.Kind.Bool) { field.SetValue(target, value.BoolValue); }
                else { warnings.Add("类型不匹配（期望 bool）：" + childPath); }
            }
            else if (ft == typeof(string))
            {
                if (value.ValueKind == JsonValue.Kind.String) { field.SetValue(target, value.StringValue); }
                else { warnings.Add("类型不匹配（期望 string）：" + childPath); }
            }
            else if (ft.IsArray)
            {
                System.Type element = ft.GetElementType()!;
                if (!value.IsArray)
                {
                    warnings.Add("类型不匹配（期望数组）：" + childPath);
                    continue;
                }
                System.Array arr = System.Array.CreateInstance(element, value.Items.Count);
                for (int i = 0; i < value.Items.Count; i++)
                {
                    if (element == typeof(int) && value.Items[i].IsNumber)
                    {
                        arr.SetValue((int)System.Math.Round(value.Items[i].NumberValue, System.MidpointRounding.AwayFromZero), i);
                    }
                    else if (element == typeof(float) && value.Items[i].IsNumber)
                    {
                        arr.SetValue((float)value.Items[i].NumberValue, i);
                    }
                }
                field.SetValue(target, arr);
            }
            else if (ft.IsClass)
            {
                object? nested = field.GetValue(target);
                if (nested == null)
                {
                    try { nested = System.Activator.CreateInstance(ft); }
                    catch { nested = null; }
                    if (nested != null) { field.SetValue(target, nested); }
                }
                if (nested == null)
                {
                    warnings.Add("无法实例化嵌套配置：" + childPath);
                    continue;
                }
                Bind(value, nested, warnings, childPath);
            }
            else
            {
                warnings.Add("不支持的配置类型：" + childPath + " (" + ft.Name + ")");
            }
        }
    }

    private static FieldInfo? FindField(System.Type type, string key)
    {
        FieldInfo[] fields = GetFields(type);
        for (int i = 0; i < fields.Length; i++)
        {
            if (string.Equals(fields[i].Name, key, System.StringComparison.OrdinalIgnoreCase))
            {
                return fields[i];
            }
        }
        return null;
    }

    /// <summary>
    /// 把配置对象序列化成 JSON（存档与"当前参数快照"用）。
    /// 只写出与默认值不同的字段会更好读，但为了存档可完整还原，这里全量写出。
    /// </summary>
    public static JsonValue ToJson(object source)
    {
        if (source == null) { return JsonValue.Null(); }
        System.Type type = source.GetType();

        if (type == typeof(float)) { return JsonValue.From((float)source); }
        if (type == typeof(double)) { return JsonValue.From((double)source); }
        if (type == typeof(int)) { return JsonValue.From((int)source); }
        if (type == typeof(long)) { return JsonValue.From((long)source); }
        if (type == typeof(ulong)) { return JsonValue.From((double)(ulong)source); }
        if (type == typeof(bool)) { return JsonValue.From((bool)source); }
        if (type == typeof(string)) { return JsonValue.From((string)source); }

        if (type.IsArray)
        {
            var arr = JsonValue.Array();
            System.Array a = (System.Array)source;
            for (int i = 0; i < a.Length; i++)
            {
                object? item = a.GetValue(i);
                arr.Add(item == null ? JsonValue.Null() : ToJson(item));
            }
            return arr;
        }

        var obj = JsonValue.Object();
        FieldInfo[] fields = GetFields(type);
        for (int i = 0; i < fields.Length; i++)
        {
            object? value = fields[i].GetValue(source);
            obj.Set(fields[i].Name, value == null ? JsonValue.Null() : ToJson(value));
        }
        return obj;
    }

    private static string Join(string path, string key)
        => string.IsNullOrEmpty(path) ? key : path + "." + key;
}

/// <summary>加载结果：值 + 警告 + 错误（错误为空时一定是可用配置）。</summary>
public sealed class ConfigLoadResult<T> where T : class, new()
{
    public T Value { get; set; } = new T();
    public List<string> Warnings { get; } = new List<string>();
    public string? Error { get; set; }
    public string Source { get; set; } = string.Empty;

    public bool Ok => Error == null;
}
