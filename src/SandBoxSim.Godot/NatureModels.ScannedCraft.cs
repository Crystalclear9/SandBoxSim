using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void InstallScannedCraftMaterials()
    {
        var shader=new Shader {Code=@"shader_type spatial;
uniform sampler2D diffuse_map:source_color,filter_linear_mipmap_anisotropic,repeat_enable;
uniform sampler2D packed_map:filter_linear_mipmap_anisotropic,repeat_enable;
uniform vec4 pigment:source_color;
uniform float metres=1.5;
uniform float tint_strength=.3;
uniform float gain=1.0;
uniform float mineral_desaturation=0.0;
varying vec3 p;
varying vec3 face_normal;
void vertex(){p=VERTEX;face_normal=NORMAL;}
void fragment(){
    vec3 weights=pow(abs(normalize(face_normal)),vec3(6));weights/=max(.001,weights.x+weights.y+weights.z);
    vec3 point=p/metres;
    vec3 cx=texture(diffuse_map,point.zy).rgb,cy=texture(diffuse_map,point.xz).rgb,cz=texture(diffuse_map,point.xy).rgb;
    vec3 nx=texture(packed_map,point.zy).rgb,ny=texture(packed_map,point.xz).rgb,nz=texture(packed_map,point.xy).rgb;
    vec3 colour=cx*weights.x+cy*weights.y+cz*weights.z;
    colour=mix(colour,vec3(dot(colour,vec3(.2126,.7152,.0722))),mineral_desaturation);
    float variation=sin(dot(floor(p*vec3(2.5,8.0,2.5)),vec3(1.73,3.17,2.41)))*.035;
    ALBEDO=colour*mix(vec3(1),pigment.rgb,tint_strength)*(1.0+variation)*gain;
    ROUGHNESS=clamp(nx.b*weights.x+ny.b*weights.y+nz.b*weights.z,.58,.98);SPECULAR=.21;
    vec3 grain=vec3(0,nx.g*2.0-1.0,nx.r*2.0-1.0)*weights.x
        +vec3(ny.r*2.0-1.0,0,ny.g*2.0-1.0)*weights.y+vec3(nz.r*2.0-1.0,nz.g*2.0-1.0,0)*weights.z;
    grain-=normalize(face_normal)*dot(grain,normalize(face_normal));
    NORMAL=normalize(NORMAL+mat3(VIEW_MATRIX*MODEL_MATRIX)*grain*.26);
}"};
        void Assign(int index,string asset,string tint,float metres)
        {
            var material=new ShaderMaterial {Shader=shader};
            material.SetShaderParameter("diffuse_map",ScannedSurfaces.Map(asset,"diffuse"));
            material.SetShaderParameter("packed_map",ScannedSurfaces.NormalRoughness(ScannedSurfaces.Map(asset,"normal"),ScannedSurfaces.Map(asset,"roughness")));
            material.SetShaderParameter("pigment",new Color(tint));material.SetShaderParameter("metres",metres);_materials[index]=material;
            material.SetShaderParameter("gain",index==1?1.5f:index==0?1.15f:1f);
            material.SetShaderParameter("mineral_desaturation",index is 6 or 7?.65f:0f);
        }
        Assign(0,"wood_planks_grey","#8f8170",1.5f);
        Assign(1,"wood_planks_grey","#b5a18a",1.5f);
        Assign(3,"plastered_wall_03","#d6cdb9",4f);
        Assign(6,"rock_face_03","#b8b7ae",2.7f);
        Assign(7,"rock_face_03","#96988f",2.7f);
    }
}
