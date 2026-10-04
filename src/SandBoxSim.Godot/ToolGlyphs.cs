using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Systems;

namespace SandBoxSim.Client;

/// <summary>Original vector HUD symbols: a consistent stroke and optical size at every display scale.</summary>
internal static class ToolGlyphs
{
    private static readonly Dictionary<PlayerTool, Texture2D> Cache = new();
    public static Texture2D For(PlayerTool tool)
    {
        if (Cache.TryGetValue(tool, out var texture)) { return texture; }
        string paths = tool switch
        {
            PlayerTool.Inspect => "<circle cx='21' cy='21' r='11'/><path d='m29 29 9 9M16 21h10m-5-5v10'/>",
            PlayerTool.Human => "<circle cx='24' cy='11' r='5'/><path d='M14 26c0-10 20-10 20 0M24 19v12m0 0-7 11m7-11 7 11'/>",
            PlayerTool.Animal => "<path d='M10 28h21l6-10 4 2-5 11M13 28v13m15-13v13M15 28 9 21M35 18l-1-8m0 3-5-4m5 6 5-5'/>",
            PlayerTool.Wolf => "<path d='m8 14 9 3 7-6 7 6 9-3-4 19-12 9-12-9ZM17 25h2m10 0h2m-11 7 4 3 4-3'/>",
            PlayerTool.Forest or PlayerTool.Wood => "<path d='m24 5-12 14h7L9 30h12v12h6V30h12L29 19h7Z'/>",
            PlayerTool.Food => "<path d='M10 27c0-9 11-12 14-7 3-5 14-2 14 7 0 13-11 17-14 12-3 5-14 1-14-12ZM24 20V9m0 5c0-8 10-8 12-8-1 8-7 10-12 8'/>",
            PlayerTool.Stone => "<path d='m7 32 7-17 17-5 11 19-10 12-18-1ZM14 15l7 12 10-17M21 27l-7 13m7-13 21 2'/>",
            PlayerTool.Iron => "<path d='m9 18 11-9 16 5 4 18-12 10-17-6ZM20 9l2 19 14-14m-14 14 6 14m-6-14-11 8'/>",
            PlayerTool.River or PlayerTool.Flood => "<path d='M29 5c-18 12 9 17-6 26-6 4-8 8-5 12M38 5C21 18 47 21 33 33c-5 4-7 6-5 10M5 20h7m-7 8h6'/>",
            PlayerTool.Mountain => "<path d='m5 39 14-27 9 17 6-10 10 20ZM14 22l5 3 5-3'/>",
            PlayerTool.Grass or PlayerTool.Fertility => "<path d='M9 41h30M24 41V22C9 24 7 15 8 9c11 0 16 5 16 13 0-11 7-17 16-17 1 12-5 20-16 19M14 16l10 8m9-11-9 11'/>",
            PlayerTool.Sand or PlayerTool.Drought => "<circle cx='32' cy='13' r='6'/><path d='M32 3V1m10 12h3M7 30c7-9 15-9 23 0s10 6 13 4M5 40c11-9 21-7 36 0'/>",
            PlayerTool.Raise => "<path d='M24 33V7m-9 9 9-9 9 9M6 39l10-5 8 4 8-4 10 5'/>",
            PlayerTool.Lower => "<path d='M24 7v26m-9-9 9 9 9-9M6 39l10-5 8 4 8-4 10 5'/>",
            PlayerTool.RemoveWater => "<path d='M25 6C17 16 11 23 11 30a13 13 0 0 0 26 0c0-5-3-10-6-14M7 41 41 7'/>",
            PlayerTool.Fire => "<path d='M24 4c6 11-4 14 3 21l6-10c15 16 7 29-9 29C7 44 3 30 14 20c-1 9 4 10 7 5 4-6-1-12 3-21Z'/>",
            PlayerTool.Lightning => "<path d='M27 4 11 27h13l-4 17 18-25H25Z'/>",
            PlayerTool.Plague => "<circle cx='24' cy='24' r='11'/><path d='M24 7v6m0 22v6M7 24h6m22 0h6M12 12l4 4m16 16 4 4M12 36l4-4m16-16 4-4'/><circle cx='20' cy='22' r='1'/><circle cx='28' cy='27' r='1'/>",
            PlayerTool.Meteor => "<circle cx='15' cy='33' r='9'/><path d='m22 27 17-17M16 20 29 7M28 33l13-13'/>",
            PlayerTool.Heal => "<path d='M19 7h10v12h12v10H29v12H19V29H7V19h12Z'/>",
            PlayerTool.BirthBlessing => "<path d='M24 40C5 28 5 17 12 12c5-4 10-1 12 4 2-5 7-8 12-4 7 5 7 16-12 28ZM24 23v10m-5-5h10'/>",
            PlayerTool.Production => "<path d='M24 5v7m0 24v7M5 24h7m24 0h7M10 10l5 5m18 18 5 5M10 38l5-5m18-18 5-5m-14-1 3 9 9 3-9 3-3 9-3-9-9-3 9-3Z'/>",
            _ => "<path d='M9 37h30M12 30l6-13 6 9 6-15 6 19'/>",
        };
        var image = new Image();
        image.LoadSvgFromString("<svg xmlns='http://www.w3.org/2000/svg' width='96' height='96' viewBox='0 0 48 48'><g fill='none' stroke='#edf1e8' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'>" + paths + "</g></svg>");
        texture = ImageTexture.CreateFromImage(image); Cache[tool] = texture; return texture;
    }
}
