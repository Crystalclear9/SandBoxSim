using System;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace SandBoxSim.Client;

/// <summary>Packaged fonts keep typography independent of host font discovery.</summary>
internal static class InterfaceFonts
{
    private const string Root="res://assets/fonts/";
    private static FontFile Load(string name)
    {
        var font=GD.Load<FontFile>(Root+name)??throw new InvalidOperationException("Missing interface font: "+name);
        font.AllowSystemFallback=false;
        return font;
    }
    private static readonly FontFile Symbols=Load("NotoSansSymbols2.ttf");
    private static readonly FontFile Cjk=Load("NotoSansSC.ttf");
    private static FontVariation Weight(int weight,bool tabular=false)
    {
        var server=TextServerManager.GetPrimaryInterface();
        var font=new FontVariation {BaseFont=Cjk,VariationOpentype=new Godot.Collections.Dictionary {{server.NameToTag("wght"),weight}},Fallbacks=new Godot.Collections.Array<Font> {Symbols}};
        if(tabular)font.OpentypeFeatures=new Godot.Collections.Dictionary {{server.NameToTag("tnum"),1}};
        return font;
    }
    internal static readonly FontVariation Regular=Weight(400);
    internal static readonly FontVariation Heading=Weight(600);
    internal static readonly FontVariation Numeric=Weight(500,true);
    internal static void Validate()
    {
        using var manifest=JsonDocument.Parse(FileAccess.GetFileAsString(Root+"manifest.json"));
        if(manifest.RootElement.GetProperty("license").GetString()!="OFL-1.1")throw new InvalidOperationException("Missing interface font license");
        foreach(var record in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            string name=record.GetProperty("file").GetString()!;
            byte[] data=name=="NotoSansSC.ttf"?Cjk.Data:name=="NotoSansSymbols2.ttf"?Symbols.Data:FileAccess.GetFileAsBytes(Root+name);
            if(Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()!=record.GetProperty("sha256").GetString())throw new InvalidOperationException("Interface font/license hash mismatch: "+name);
        }
        if(Cjk.AllowSystemFallback||Symbols.AllowSystemFallback||Regular.Fallbacks.Count!=1||Regular.BaseFont!=Cjk||Heading.BaseFont!=Cjk||Numeric.BaseFont!=Cjk)
            throw new InvalidOperationException("Interface fonts depend on host font discovery");
        foreach(char c in "SandBoxSim0123456789中文观察居民生命饥饿干渴精力珅曦麒麟概览生活决策⌘↗→‹›−×✓·%")
            if(!Regular.HasChar(c)&&!Symbols.HasChar(c))throw new InvalidOperationException("Missing packaged interface glyph: "+c);
        foreach(var font in new Font[]{Regular,Heading,Numeric})
        {
            var size=font.GetStringSize("中文 SandBoxSim 0123456789",fontSize:16);
            if(!size.IsFinite()||size.X<100||size.Y<=0)throw new InvalidOperationException("Packaged interface font cannot shape text");
        }
        var server=TextServerManager.GetPrimaryInterface();long axis=server.NameToTag("wght");
        foreach(var pair in new[]{(Font:Regular,Weight:400),(Font:Heading,Weight:600),(Font:Numeric,Weight:500)})
        {
            var coordinates=server.FontGetVariationCoordinates(pair.Font.GetRids()[0]);
            if(!coordinates.ContainsKey(axis)||Math.Abs(coordinates[axis].AsDouble()-pair.Weight)>.1)throw new InvalidOperationException("Requested UI font weight was not applied to its actual font RID");
        }
        if(MathF.Abs(Numeric.GetStringSize("111111",fontSize:22).X-Numeric.GetStringSize("888888",fontSize:22).X)>.1f)
            throw new InvalidOperationException("Numeric columns lost tabular spacing");
        GD.Print("INTERFACE_FONTS_PASS: packaged CJK/symbol glyphs, real weights, tabular digits, source/license hashes and no system fallback");
    }
}
