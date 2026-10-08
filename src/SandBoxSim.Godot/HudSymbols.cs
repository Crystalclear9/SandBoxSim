using System.Collections.Generic;
using Godot;

namespace SandBoxSim.Client;

internal static class HudSymbols
{
    private static readonly Dictionary<string, Texture2D> Cache = new();
    public static Texture2D For(string name,int pixels=96)
    {
        string key=name+":"+pixels;
        if (Cache.TryGetValue(key, out var texture)) return texture;
        string path = name switch
        {
            "居民" => "<circle cx='24' cy='12' r='5'/><path d='M13 38v-7c0-12 22-12 22 0v7M18 27v11m12-11v11'/>",
            "聚落" => "<path d='M8 39V22l9-7 9 7v17m-5-20V12l9-6 9 6v27M4 39h40M13 30h8m7-11h6'/>",
            "建筑" or "营造" => "<path d='M7 23 24 8l17 15M12 20v20h24V20M21 40V28h7v12M24 4v4'/>",
            "食物储备" => "<path d='M24 43V9m0 10-8-6-2-7c8 0 10 5 10 13m0 10-8-6-2-7c8 0 10 5 10 13m0-5 8-6 2-7c-8 0-10 5-10 13m0 10 8-6 2-7c-8 0-10 5-10 13'/>",
            "木材储备" => "<path d='m11 34 22-22 7 7-22 22ZM14 31l7 7m-2-12 7 7m-2-12 7 7'/>",
            "石料储备" => "<path d='m7 34 6-18 17-6 12 22-12 9-16-2ZM13 16l8 13 9-19m-9 19-7 10m7-10 21 3'/>",
            "曲线" => "<path d='M8 8v32h32M12 32l9-12 8 7 11-16'/>",
            "观察" => "<circle cx='24' cy='24' r='17'/><path d='m30 17-4 12-9 3 4-12ZM24 3v4m0 34v4M3 24h4m34 0h4'/>",
            "创造" => "<path d='m12 36 20-20 5 5-20 20ZM30 7v-4m11 15h4M15 12l-4-4M36 10l5-5M8 24H4'/>",
            "生态" => "<path d='M10 38C5 17 20 6 40 7c1 20-10 34-30 31Zm0 0L32 16m-11 11V15m0 12h12'/>",
            "手记" => "<path d='M24 12C17 7 10 8 5 10v27c7-2 13-1 19 4 6-5 12-6 19-4V10c-5-2-12-3-19 2Zm0 0v29M10 17l8 1m-8 6 8 1m12-7 8-1m-8 8 8-1'/>",
            _ => "<path d='M12 5h24v25L24 43 12 30ZM24 13v17m-7-9h14'/>",
        };
        var image = new Image();
        image.LoadSvgFromString("<svg xmlns='http://www.w3.org/2000/svg' width='"+pixels+"' height='"+pixels+"' viewBox='0 0 48 48'><g fill='none' stroke='#e9e3d6' stroke-width='1.6' stroke-linecap='round' stroke-linejoin='round'>" + path + "</g></svg>");
        texture = ImageTexture.CreateFromImage(image); Cache[key] = texture; return texture;
    }
}
