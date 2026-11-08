using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private bool FittedFigure=>BodyFrame==1&&!ChildHead;
    // Both detail levels use the same rest-space sewn panels. This is a
    // material boundary on the actual fitted cloth, not an independent shell.
    private const string FigureFabricFunctions=@"float figure_hem_finish(float hem_metric,float angle){
float metres=hem_metric*.024;
float aa=max(.0002,fwidth(metres));
float facing=1.0-smoothstep(.007-aa,.010+aa,metres);
float fold_edge=1.0-smoothstep(.0007,.0015+aa,abs(metres-.0035));
float thread=1.0-smoothstep(.00055,.0012+aa,abs(metres-.0085));
float stitches=mix(.68,.68+.32*cos(angle*96.0),1.0-smoothstep(.01,.04,fwidth(angle)));
return 1.0-.13*facing-.05*fold_edge+.14*thread*stitches;
}
vec3 figure_colour(vec3 dye,vec2 chart,bool enabled,float torso_weight,int cut,float neck_metric){
if(!enabled)return dye;
vec3 linen=mix(dye,vec3(.72,.69,.61),.12);

float a=(-chart.x-2.0)*6.2831853;
float aa=max(fwidth(chart.y),.0005);
vec3 outer=mix(dye,vec3(.29,.35,.32),.06);
float princess=1.0-smoothstep(.003,.007+aa*2.0,abs(abs(cos(a))-(.58+(chart.y-1.0)*.40)));
float stitches=princess*smoothstep(.34,.78,neck_metric);
stitches*=1.0-.85*smoothstep(1.04,1.09,chart.y)*(1.0-smoothstep(1.17,1.22,chart.y));
outer*=1.0-stitches*.07;
vec3 surface=outer;
// Placket lies on the skinned fabric chart, with a tapered stitched end.
float end_height=cut==2?1.041:(cut==0?1.193:1.174);
float lower_width=cut==2?.010:(cut==0?.0048:.006);
float upper_width=cut==2?.013:(cut==0?.008:.010);
float half_width=mix(lower_width,upper_width,smoothstep(end_height,1.27,chart.y));
float side=abs(cos(a))*.112;
float seam_aa=max(.0005,fwidth(side));
float length_mask=smoothstep(end_height-aa,end_height+.005+aa,chart.y)*smoothstep(.78,.95,-sin(a));
float band=(1.0-smoothstep(half_width-seam_aa,half_width+seam_aa,side))*length_mask;
float stitch=(1.0-smoothstep(.0005,.0012+seam_aa,abs(side-half_width+.0018)))*length_mask;
surface=mix(surface,outer*(cut==2?.82:.90),band*(cut==2?.72:.55));
surface=mix(surface,linen*1.05,stitch*.28*(1.0-smoothstep(.001,.005,seam_aa)));
// The edge distance is measured along the sewn mesh, including its return.
// Both the facing and the line of stitches follow the animated opening.
float neck=neck_metric*.028;
float neck_aa=max(.0003,fwidth(neck));
float facing_width=cut==2?.010:(cut==1?.007:.006);
float facing=1.0-smoothstep(facing_width-neck_aa,facing_width+neck_aa,neck);
float under_stitch=1.0-smoothstep(.00035,.0011+neck_aa,abs(neck-facing_width+.0014));
float thread=1.0-smoothstep(.01,.05,fwidth(a));
float dash=mix(.68,.68+.32*cos(a*115.0),thread);
vec3 binding=cut==2?outer*.78:mix(outer*.93,linen,.42);
surface=mix(surface,binding,facing);
surface=mix(surface,linen*1.13,under_stitch*dash*.36*(1.0-smoothstep(.001,.006,neck_aa)));
return mix(linen,surface,clamp(torso_weight,0.0,1.0));
}
";
    private Material FigureGarmentMaterial(string cloth)
    {
        if(!FittedFigure)return ResidentMaterial(cloth);
        string key="fitted-figure:"+cloth+":"+_garmentCut;
        if(_residentMaterials.TryGetValue(key,out var existing))return existing;
        var material=(ShaderMaterial)Fabric(cloth);material.SetShaderParameter("fitted_bodice",true);
        material.SetShaderParameter("garment_cut",_garmentCut);
        material.SetMeta("FittedBodice",true);return _residentMaterials[key]=material;
    }
    private Shader? _fittedDistantShader;
    private readonly ShaderMaterial?[] _fittedDistantMaterials=new ShaderMaterial?[3];
    private Material FittedDistantMaterial()
    {
        if(_fittedDistantMaterials[_garmentCut] is {} cached)return cached;
        _fittedDistantShader??=new Shader {
        Code="shader_type spatial;"+FigureFabricFunctions+@"
uniform int garment_cut=0;
varying vec2 torso_chart;varying float torso_weight;
void vertex(){torso_chart=vec2(-2.0-atan(VERTEX.z,VERTEX.x)/6.2831853,VERTEX.y);torso_weight=UV2.x < -1.1 ? 1.0 : 0.0;}
void fragment(){ALBEDO=figure_colour(COLOR.rgb,torso_chart,UV.x>.5,torso_weight,garment_cut,UV.x-1.0);
if(UV.x>.5)ALBEDO*=figure_hem_finish(UV.y,(-torso_chart.x-2.0)*6.2831853);
ROUGHNESS=.9;SPECULAR=.16;}"};
        var material=new ShaderMaterial {Shader=_fittedDistantShader};
        material.SetShaderParameter("garment_cut",_garmentCut);
        return _fittedDistantMaterials[_garmentCut]=material;
    }
}
