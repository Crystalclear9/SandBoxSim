using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private bool FittedFigure=>BodyFrame==1&&!ChildHead;
    // Both detail levels use the same rest-space sewn panels. This is a
    // material boundary on the actual fitted cloth, not an independent shell.
    private const string FigureFabricFunctions=@"vec3 figure_colour(vec3 dye,vec2 chart,bool enabled,float torso_weight){
if(!enabled)return dye;
vec3 linen=mix(dye,vec3(.82,.78,.67),.65);

float a=(-chart.x-2.0)*6.2831853;
// A continuous fitted body with a shallow curved linen insert at the neck.
// The old straight colour break across the upper chest read as a box edge.
float top=1.270-.026*pow(max(0.0,-sin(a)),6.0)-.010*pow(max(0.0,sin(a)),4.0);
float aa=max(fwidth(chart.y),.0005);
float panel=1.0-smoothstep(top-aa,top+aa,chart.y);
vec3 outer=mix(dye,vec3(.24,.31,.27),.28);
float edge=1.0-smoothstep(.001,.002+aa,abs(chart.y-top));
float princess=1.0-smoothstep(.003,.007+aa*2.0,abs(abs(cos(a))-(.58+(chart.y-1.0)*.40)));
float stitches=princess*(1.0-smoothstep(top-.02,top,chart.y));
stitches*=1.0-.85*smoothstep(1.04,1.09,chart.y)*(1.0-smoothstep(1.17,1.22,chart.y));
outer*=1.0-stitches*.12;
return mix(linen,mix(mix(linen,outer,panel),outer*1.22,edge*.30),clamp(torso_weight,0.0,1.0));
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
varying vec2 torso_chart;varying float torso_weight;
void vertex(){torso_chart=vec2(-2.0-atan(VERTEX.z,VERTEX.x)/6.2831853,VERTEX.y);torso_weight=UV2.x < -1.1 ? 1.0 : 0.0;}
void fragment(){ALBEDO=figure_colour(COLOR.rgb,torso_chart,UV.x>.5,torso_weight);ROUGHNESS=.9;SPECULAR=.16;}"}};
}
