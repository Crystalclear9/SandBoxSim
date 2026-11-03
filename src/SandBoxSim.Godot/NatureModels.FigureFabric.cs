using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private bool FittedFigure=>BodyFrame==1&&!ChildHead;
    // Both detail levels use the same rest-space sewn panels. This is a
    // material boundary on the actual fitted cloth, not an independent shell.
    private const string FigureFabricFunctions=@"vec3 figure_colour(vec3 dye,vec2 chart,bool enabled){
if(!enabled)return dye;
vec3 linen=mix(dye,vec3(.82,.78,.67),.65);
if(chart.x>=-1.1)return linen;
float a=(-chart.x-2.0)*6.2831853;
float top=1.175+.012*pow(abs(cos(a)),2.0)-.012*pow(max(0.0,-sin(a)),8.0);
float aa=max(fwidth(chart.y),.0005);
float panel=1.0-smoothstep(top-aa,top+aa,chart.y);
vec3 outer=mix(dye*.95,vec3(.18,.28,.23),.35);
float edge=1.0-smoothstep(.001,.002+aa,abs(chart.y-top));
float princess=1.0-smoothstep(.008,.020+aa*4.0,abs(abs(cos(a))-(.58+(chart.y-1.0)*.40)));
float stitches=princess*(1.0-smoothstep(top-.02,top,chart.y));
outer*=1.0-stitches*.12;
return mix(mix(linen,outer,panel),outer*1.22,edge*.65);
}
";
    private Material FigureGarmentMaterial(string cloth)
    {
        if(!FittedFigure)return ResidentMaterial(cloth);
        string key="fitted-figure:"+cloth;
        if(_residentMaterials.TryGetValue(key,out var existing))return existing;
        var material=(ShaderMaterial)Fabric(cloth);material.SetShaderParameter("fitted_bodice",true);
        material.SetMeta("FittedBodice",true);return _residentMaterials[key]=material;
    }
    private ShaderMaterial? _fittedDistantMaterial;
    private Material FittedDistantMaterial()=>_fittedDistantMaterial??=new ShaderMaterial {Shader=new Shader {
        Code="shader_type spatial;"+FigureFabricFunctions+@"
void fragment(){ALBEDO=figure_colour(COLOR.rgb,UV2,UV.x>.5);ROUGHNESS=.9;SPECULAR=.16;}"}};
}
