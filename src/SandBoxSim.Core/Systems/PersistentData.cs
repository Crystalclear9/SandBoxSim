using System.Collections.Generic;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Systems;

internal static class PersistentData
{
    public static JsonValue Encode<T>(IEnumerable<T> items)
    {
        var array = JsonValue.Array();
        foreach (T item in items) { array.Add(JsonBinder.ToJson(item!)); }
        return array;
    }

    public static void Restore<T>(JsonValue array, List<T> target) where T : class, new()
    {
        target.Clear();
        if (!array.IsArray) { return; }
        foreach (JsonValue value in array.Items)
        {
            var item = new T();
            JsonBinder.Bind(value, item, new List<string>(), string.Empty);
            target.Add(item);
        }
    }
}
