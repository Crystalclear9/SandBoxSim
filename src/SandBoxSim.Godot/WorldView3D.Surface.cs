using System;
using Godot;

namespace SandBoxSim.Client;

public partial class WorldView3D
{
    private ImageTexture? _waterBed;
    private ShaderMaterial CreateWaterMaterial(Vector3[] vertices,int nx,int ny,int width,int height)
    {
        var bytes=new byte[vertices.Length*4];
        for(int i=0;i<vertices.Length;i++)System.BitConverter.TryWriteBytes(bytes.AsSpan(i*4,4),vertices[i].Y);
        using var bed=Image.CreateFromData(nx+1,ny+1,false,Image.Format.Rf,bytes);
        if(_waterBed==null)_waterBed=ImageTexture.CreateFromImage(bed);else _waterBed.SetImage(bed);
        var shader=new Shader {Code=@"shader_type spatial;render_mode cull_disabled;
uniform sampler2D material_map:filter_nearest,repeat_disable;
uniform sampler2D bed_height:filter_linear,repeat_disable;
uniform vec2 map_size;
varying vec3 p;
float wet(vec2 tile){return texture(material_map,(clamp(tile,vec2(0),map_size-vec2(1))+vec2(.5))/map_size).b;}
float cubic(float a,float b,float c,float d,float t){return .5*(2.0*b+(c-a)*t+(2.0*a-5.0*b+4.0*c-d)*t*t+(-a+3.0*b-3.0*c+d)*t*t*t);}
float row(vec2 base,float y,float u){return cubic(wet(base+vec2(-1,y)),wet(base+vec2(0,y)),wet(base+vec2(1,y)),wet(base+vec2(2,y)),u);}
vec3 ripple(vec2 xz){
    vec2 k1=vec2(.73,.41),k2=vec2(-.29,1.17),k3=vec2(2.7,-1.9);
    float a=dot(xz,k1)+TIME*.7,b=dot(xz,k2)+TIME*.9,c=dot(xz,k3)-TIME*1.3;
    vec2 gradient=cos(a)*.006*k1-sin(b)*.004*k2+cos(c)*.0009*k3;
    return vec3(gradient.x,sin(a)*.006+cos(b)*.004+sin(c)*.0009,gradient.y);
}
void vertex(){
    p=VERTEX;
    float depth=max(0.0,.02-texture(bed_height,(p.xz*2.0+vec2(.5))/(map_size*4.0+vec2(1))).r);
    VERTEX.y+=ripple(p.xz).y*smoothstep(.01,.20,depth);
    NORMAL=vec3(0,1,0);
}
void fragment(){
    vec2 grid=p.xz*.5-vec2(.5),base=floor(grid),f=fract(grid);
    float bank=clamp(cubic(row(base,-1.0,f.x),row(base,0.0,f.x),row(base,1.0,f.x),row(base,2.0,f.x),f.y),0.0,1.0);
    if(bank<.48)discard;
    float depth=max(0.0,.02-texture(bed_height,(p.xz*2.0+vec2(.5))/(map_size*4.0+vec2(1))).r);
    vec3 waves=ripple(p.xz);float shore_fade=smoothstep(.01,.20,depth);
    // Millimetre surface ripples affect light, not the shoreline silhouette.
    // Explicit footprint filtering prevents distant highlights from crawling.
    float footprint=max(length(dFdx(p.xz)),length(dFdy(p.xz)));
    float detail_fade=1.0/(1.0+pow(footprint*60.0,2.0));
    vec2 k4=vec2(43.0,27.0),k5=vec2(-19.0,61.0);
    vec2 small=(cos(dot(p.xz,k4)-TIME*2.3)*.0007*k4+cos(dot(p.xz,k5)+TIME*1.7)*.00045*k5)*detail_fade;
    vec3 n=normalize(vec3(-(waves.x+small.x)*shore_fade,1,-(waves.z+small.y)*shore_fade));
    NORMAL=normalize(mat3(VIEW_MATRIX)*n);
    float fresnel=.02+.98*pow(1.0-clamp(dot(NORMAL,normalize(VIEW)),0.0,1.0),5.0);
    float bed_detail=sin(p.x*17.0+sin(p.z*7.0))*sin(p.z*21.0+p.x*3.0)*.012;
    vec3 shallow=vec3(.135,.145,.105)+vec3(bed_detail);
    vec3 absorbed=mix(vec3(.018,.048,.051),shallow,exp(-depth*3.4));
    ALBEDO=mix(absorbed,vec3(.24,.30,.32),fresnel*.72);
    float contact=(1.0-smoothstep(.015,.075,depth))*smoothstep(.001,.010,depth);
    ALBEDO+=vec3(.025,.027,.024)*contact;
    ROUGHNESS=.24+contact*.12;SPECULAR=.46;METALLIC=0.0;
}"};
        var material=new ShaderMaterial {Shader=shader};material.SetShaderParameter("material_map",_groundMapTexture!);
        material.SetShaderParameter("bed_height",_waterBed!);material.SetShaderParameter("map_size",new Vector2(width,height));
        return material;
    }
    private ShaderMaterial CreateGroundMaterial()
    {
        var shader=new Shader {Code=@"shader_type spatial;
render_mode cull_disabled;
uniform sampler2D material_map:filter_nearest,repeat_disable;
uniform sampler2D hydrology_map:filter_linear,repeat_disable;
uniform sampler2D tint_map:filter_linear,repeat_disable;
uniform sampler2D grass_diffuse:source_color,filter_linear_mipmap_anisotropic,repeat_enable;
uniform sampler2D grass_nr:filter_linear_mipmap_anisotropic,repeat_enable;
uniform sampler2D earth_diffuse:source_color,filter_linear_mipmap_anisotropic,repeat_enable;
uniform sampler2D earth_nr:filter_linear_mipmap_anisotropic,repeat_enable;
uniform sampler2D rock_diffuse:source_color,filter_linear_mipmap_anisotropic,repeat_enable;
uniform sampler2D rock_nr:filter_linear_mipmap_anisotropic,repeat_enable;
uniform sampler2D forest_diffuse:source_color,filter_linear_mipmap_anisotropic,repeat_enable;
uniform sampler2D forest_nr:filter_linear_mipmap_anisotropic,repeat_enable;
uniform sampler2D sand_diffuse:source_color,filter_linear_mipmap_anisotropic,repeat_enable;
uniform vec2 map_size;
varying vec3 world_position;
varying vec3 terrain_normal;
void vertex(){
    VERTEX.y-=texture(material_map,clamp(VERTEX.xz*.5/map_size,vec2(0),vec2(1))).g*.024;
    world_position=(MODEL_MATRIX*vec4(VERTEX,1)).xyz;
    terrain_normal=normalize(mat3(MODEL_MATRIX)*NORMAL);
}
float noise2(vec2 p){
    vec2 i=floor(p),f=fract(p);f=f*f*(3.0-2.0*f);
    vec2 q=vec2(127.1,311.7);
    return mix(mix(fract(sin(dot(i,q))*43758.5453),fract(sin(dot(i+vec2(1,0),q))*43758.5453),f.x),
    mix(fract(sin(dot(i+vec2(0,1),q))*43758.5453),fract(sin(dot(i+vec2(1),q))*43758.5453),f.x),f.y);
}
vec4 tile_data(vec2 tile){return texture(material_map,(clamp(tile,vec2(0),map_size-vec2(1))+vec2(.5))/map_size);}
float kind(vec2 tile){return floor(tile_data(tile).r*15.0+.5);}
float is_kind(float value,float target){return 1.0-step(.5,abs(value-target));}
vec4 layers(float id){
    vec3 types=vec3(is_kind(id,0.0),is_kind(id,1.0),is_kind(id,3.0));
    return vec4(types,1.0-types.x-types.y-types.z);
}
vec4 special_layers(float id){return vec4(is_kind(id,4.0)+is_kind(id,9.0),is_kind(id,7.0),is_kind(id,10.0),is_kind(id,14.0));}
float trail(vec2 grid){
    vec2 tile=floor(grid+vec2(.5)),offset=grid-tile;
    float center=tile_data(tile).g;if(center<=.015)return 0.0;
    float path=(1.0-smoothstep(.10,.35,length(offset)))*smoothstep(.015,.45,center);
    for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++){
        if(x==0&&y==0)continue;
        vec2 direction=vec2(float(x),float(y));
        float joint=smoothstep(.015,.45,min(center,tile_data(tile+direction).g));
        float t=clamp(dot(offset,direction)/dot(direction,direction),0.0,.5);
        path=max(path,(1.0-smoothstep(.10,.29,length(offset-direction*t)))*joint);
    }
    return path;
}
vec3 top_normal(vec3 sample_normal,vec3 base){
    vec3 axis=abs(base.x)>.9?vec3(0,0,1):vec3(1,0,0);
    vec3 tangent=normalize(axis-base*dot(base,axis));
    vec3 bitangent=normalize(cross(tangent,base));
    return normalize(tangent*sample_normal.x+bitangent*sample_normal.y+base*max(.2,sample_normal.z));
}
vec3 rock_sample(sampler2D map,vec3 p,vec3 weights,vec3 base){
    vec3 side=sign(base+vec3(.000001));
    return texture(map,vec2(p.z,p.y*side.x)).rgb*weights.x+texture(map,p.xz).rgb*weights.y+texture(map,vec2(p.x,-p.y*side.z)).rgb*weights.z;
}
vec3 decode_normal(vec3 packed){vec2 xy=packed.rg*2.0-1.0;return normalize(vec3(xy,sqrt(max(.01,1.0-dot(xy,xy)))));}
vec2 tile_hash(vec2 p){return fract(sin(vec2(dot(p,vec2(127.1,311.7)),dot(p,vec2(269.5,183.3))))*43758.5453);}
vec3 meadow_cell(sampler2D map,vec2 uv,vec2 cell,vec2 dx,vec2 dy,bool normal_data){
    vec2 random=tile_hash(cell);float angle=floor(random.x*4.0)*1.570796327;
    mat2 rotate=mat2(vec2(cos(angle),sin(angle)),vec2(-sin(angle),cos(angle)));
    vec3 value=textureGrad(map,rotate*uv+random*7.0,rotate*dx,rotate*dy).rgb;
    if(normal_data)value.rg=transpose(rotate)*(value.rg*2.0-1.0)*.5+.5;
    return value;
}
vec3 meadow_sample(sampler2D map,vec2 uv,bool normal_data){
    // Three offset samples on a triangular lattice remove the scanned lawn's
    // repeated rows. Explicit gradients retain mip levels across cell boundaries.
    vec2 skew=vec2(uv.x+uv.y*.57735027,uv.y*1.15470054)*.43;
    vec2 base=floor(skew),f=fract(skew),a,b,c;vec3 w;
    if(f.x+f.y<1.0){a=base;b=base+vec2(1,0);c=base+vec2(0,1);w=vec3(1.0-f.x-f.y,f.x,f.y);}
    else{a=base+vec2(1);b=base+vec2(0,1);c=base+vec2(1,0);w=vec3(f.x+f.y-1.0,1.0-f.x,1.0-f.y);}
    w=w*w;w/=dot(w,vec3(1));
    vec2 dx=dFdx(uv),dy=dFdy(uv);
    return meadow_cell(map,uv,a,dx,dy,normal_data)*w.x
        +meadow_cell(map,uv,b,dx,dy,normal_data)*w.y
        +meadow_cell(map,uv,c,dx,dy,normal_data)*w.z;
}
void fragment(){
    vec2 map_uv=clamp(world_position.xz*.5/map_size,vec2(0),vec2(1));
    vec2 grid=world_position.xz*.5-vec2(.5);
    // Small colour-boundary variation leaves the physical collision and grid untouched.
    vec2 paint_grid=grid+(vec2(noise2(world_position.xz*.47),noise2(world_position.zx*.51+17.0))-.5)*.32;
    vec2 origin=floor(paint_grid),blend=smoothstep(vec2(0),vec2(1),fract(paint_grid));
    vec4 ids=vec4(kind(origin),kind(origin+vec2(1,0)),kind(origin+vec2(0,1)),kind(origin+vec2(1)));
    vec4 weights=mix(mix(layers(ids.x),layers(ids.y),blend.x),mix(layers(ids.z),layers(ids.w),blend.x),blend.y);
    vec4 special=mix(mix(special_layers(ids.x),special_layers(ids.y),blend.x),mix(special_layers(ids.z),special_layers(ids.w),blend.x),blend.y);
    vec3 base=normalize(terrain_normal);
    float slope=1.0-clamp(base.y,0.0,1.0);
    float damp=texture(hydrology_map,map_uv).b,moisture=texture(hydrology_map,map_uv).a;
    float shore=smoothstep(.12,.42,damp)*(1.0-smoothstep(.60,.86,damp));
    float exposed=smoothstep(.08,.32,slope)*.9;
    weights=mix(weights,vec4(0,0,1,0),exposed);
    float sediment=shore*(.55+noise2(world_position.xz*.83)*.45);
    weights=mix(weights,vec4(0,0,0,1),sediment*.88);
    float wear=clamp(trail(grid)*.9+texture(material_map,map_uv).g*.12,0.0,.95);
    weights=mix(weights,vec4(0,0,0,1),wear);
    vec2 grass_uv=world_position.xz/2.0;
    vec2 earth_uv=world_position.xz/1.3;
    vec2 forest_uv=world_position.xz*.22;
    vec3 rock_uv=world_position/2.7;
    vec3 projection=pow(abs(base),vec3(4));projection/=max(.001,projection.x+projection.y+projection.z);
    grass_uv=mat2(vec2(.819,.574),vec2(-.574,.819))*grass_uv;
    vec3 grass=meadow_sample(grass_diffuse,grass_uv,false);
    float grass_luma=dot(grass,vec3(.2126,.7152,.0722));
    // Compress the scan's broad contrast while retaining close leaf
    // detail. Moisture and low-frequency patches carry the meadow colour.
    grass=mix(grass,vec3(grass_luma),.32)*.74;
    grass*=clamp(.30/max(.08,grass_luma),.76,1.22);
    float meadow=noise2(world_position.xz*.17)+noise2(world_position.xz*.43)*.28;
    grass*=mix(vec3(.72,.90,.71),vec3(.97,.96,.82),smoothstep(.25,.95,meadow));
    float dry=smoothstep(.28,.78,1.0-moisture)*smoothstep(.38,.85,noise2(world_position.xz*.29+vec2(23,7)));
    grass=mix(grass,grass*vec3(1.13,1.035,.82),dry*.52);
    vec3 forest=texture(forest_diffuse,forest_uv).rgb;
    vec3 earth=texture(earth_diffuse,earth_uv).rgb;
    vec3 rock=rock_sample(rock_diffuse,rock_uv,projection,base);
    // Dry mineral faces retain broad strata, without glittering pale flecks.
    float mineral=dot(rock,vec3(.2126,.7152,.0722));
    rock=mix(rock,vec3(mineral)*vec3(1.02,1.01,.96),.35);
    float strata=noise2(vec2(world_position.x*.14+world_position.z*.11,world_position.y*3.2));
    rock*=.88+strata*.18;
    vec3 colour=grass*weights.x+forest*weights.y+rock*weights.z+earth*weights.w;
    float sand=special.x;
    colour=mix(colour,texture(sand_diffuse,world_position.xz*.12).rgb,sand*(1.0-exposed)*(1.0-wear));
    float snow=special.y*(1.0-smoothstep(.10,.33,slope));
    colour=mix(colour,vec3(.75,.79,.80)*(0.95+noise2(world_position.xz*2.0)*.05),snow);
    colour*=mix(1.0,.68,shore*.72+moisture*.18);
    float silt=shore*(1.0-smoothstep(.02,.18,world_position.y))*(.7+noise2(world_position.xz*2.4)*.3);
    colour=mix(colour,earth*vec3(.68,.73,.66),silt*.38);
    colour*=.94+noise2(world_position.xz*.043)*.10;
    colour=mix(colour,colour*vec3(.15,.13,.12),special.w);
    float lava=special.z;colour=mix(colour,vec3(.065,.045,.035),lava);
    EMISSION=vec3(1.0,.10,.005)*pow(max(0.0,sin(world_position.x*5.0+world_position.z*3.0+noise2(world_position.xz*2.0)*4.0)),24.0)*lava*.7;
    ALBEDO=colour*clamp(texture(tint_map,map_uv).rgb/vec3(.9,.94,.85),vec3(0),vec3(2));
    vec3 grass_data=meadow_sample(grass_nr,grass_uv,true),forest_data=texture(forest_nr,forest_uv).rgb;
    vec3 side=sign(base+vec3(.000001));
    vec3 rdx=texture(rock_nr,vec2(rock_uv.z,rock_uv.y*side.x)).rgb;
    vec3 rdy=texture(rock_nr,rock_uv.xz).rgb;
    vec3 rdz=texture(rock_nr,vec2(rock_uv.x,-rock_uv.y*side.z)).rgb;
    vec3 earth_data=texture(earth_nr,earth_uv).rgb,rock_data=rdx*projection.x+rdy*projection.y+rdz*projection.z;
    float rough=grass_data.b*weights.x+forest_data.b*weights.y+rock_data.b*weights.z+earth_data.b*weights.w;
    ROUGHNESS=clamp(rough-shore*.10,.64,.98);SPECULAR=.16;
    vec3 gn=decode_normal(grass_data);gn.xy=mat2(vec2(.819,-.574),vec2(.574,.819))*gn.xy;
    vec3 grain=gn*weights.x+decode_normal(forest_data)*weights.y+decode_normal(earth_data)*weights.w;
    // Decode each projection in its own tangent frame; packed RGB cannot
    // be blended first and interpreted as a single upward-facing normal.
    vec3 rx=decode_normal(rdx),ry=decode_normal(rdy),rz=decode_normal(rdz);
    vec3 rock_detail=vec3(0,rx.y*side.x,rx.x)*projection.x+vec3(ry.x,0,ry.y)*projection.y+vec3(rz.x,-rz.y*side.z,0)*projection.z;
    grain.z+=weights.z;grain.xy*=.26;
    vec3 n=normalize(top_normal(grain,base)+rock_detail*weights.z*.32);
    NORMAL=normalize(mat3(VIEW_MATRIX)*n);
}"};
        var material=new ShaderMaterial {Shader=shader};
        foreach(var layer in new[]{("grass","leafy_grass"),("earth","brown_mud_leaves_01"),("rock","rock_face_03")})
        {
            material.SetShaderParameter(layer.Item1+"_diffuse",ScannedSurfaces.Map(layer.Item2,"diffuse"));
            material.SetShaderParameter(layer.Item1+"_nr",ScannedSurfaces.NormalRoughness(ScannedSurfaces.Map(layer.Item2,"normal"),ScannedSurfaces.Map(layer.Item2,"roughness")));
        }
        material.SetShaderParameter("sand_diffuse",ScannedSurfaces.Map("coast_sand_01","diffuse"));
        foreach(var layer in new[]{("forest","forest_ground_04")})
        {
            string path="res://assets/models/environment/"+layer.Item2+"/textures/"+layer.Item2;
            material.SetShaderParameter(layer.Item1+"_diffuse",GD.Load<Texture2D>(path+"_diff_1k.jpg"));
            material.SetShaderParameter(layer.Item1+"_nr",ScannedSurfaces.NormalRoughness(GD.Load<Texture2D>(path+"_nor_gl_1k.jpg"),GD.Load<Texture2D>(path+"_rough_1k.jpg")));
        }
        return material;
    }
}
