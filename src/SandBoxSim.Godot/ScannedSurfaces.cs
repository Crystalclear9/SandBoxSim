using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace SandBoxSim.Client;

internal static class ScannedSurfaces
{
    private const string Root="res://assets/materials/scanned/";
    private static readonly Dictionary<string,Texture2D> Textures=new();
    public static Texture2D Map(string asset,string kind)
    {
        string key=asset+":"+kind;if(Textures.TryGetValue(key,out var texture))return texture;
        using var doc=JsonDocument.Parse(FileAccess.GetFileAsString(Root+"manifest.json"));
        if(doc.RootElement.GetProperty("license").GetString()!="CC0-1.0"||!FileAccess.FileExists(Root+"LICENSE.txt"))
            throw new InvalidOperationException("Missing scanned surface attribution");
        var record=doc.RootElement.GetProperty("assets").GetProperty(asset);
        var expected=record.TryGetProperty("pixelSize",out var size)?new Vector2I(size[0].GetInt32(),size[1].GetInt32()):new Vector2I(1024,1024);
        string path=Root+record.GetProperty("maps").GetProperty(kind).GetProperty("path").GetString();
        if(!ResourceLoader.Exists(path))throw new InvalidOperationException("Missing scanned surface: "+path);
        texture=GD.Load<Texture2D>(path);
        if(texture.GetWidth()!=expected.X||texture.GetHeight()!=expected.Y)throw new InvalidOperationException("Unexpected surface resolution");
        RequireMipmaps(texture);
        Textures[key]=texture;return texture;
    }
    internal static void RequireMipmaps(Texture2D texture)
    {
        using var image=texture.GetImage();
        if(image==null||!image.HasMipmaps())throw new InvalidOperationException("Imported mip chain missing: "+texture.ResourcePath+". Run tools/godot.ps1 -Mode test -ReimportTextures.");
    }
    public static Texture2D NormalRoughness(Texture2D normalMap,Texture2D roughMap)
    {
        string key="packed:"+normalMap.GetInstanceId()+":"+roughMap.GetInstanceId();
        if(Textures.TryGetValue(key,out var cached))return cached;
        using var normal=normalMap.GetImage();using var rough=roughMap.GetImage();
        if(normal.IsCompressed())normal.Decompress();if(rough.IsCompressed())rough.Decompress();
        normal.ClearMipmaps();rough.ClearMipmaps();
        normal.Convert(Image.Format.Rgba8);rough.Convert(Image.Format.Rgba8);
        if(normal.GetSize()!=rough.GetSize())throw new InvalidOperationException("Surface map dimensions disagree");
        var n=normal.GetData();var r=rough.GetData();var packed=new byte[n.Length];
        for(int i=0;i<packed.Length;i+=4){packed[i]=n[i];packed[i+1]=n[i+1];packed[i+2]=r[i];packed[i+3]=255;}
        using var image=Image.CreateFromData(normal.GetWidth(),normal.GetHeight(),false,Image.Format.Rgba8,packed);
        image.GenerateMipmaps();var texture=ImageTexture.CreateFromImage(image);Textures[key]=texture;return texture;
    }
}
