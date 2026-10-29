using System;
using System.Text.Json;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private static string AuthoredRoot(bool wolf)=>"res://assets/models/animals/"+(wolf?"0ad-wolf/":"cdmir-deer/");
    private readonly (ArrayMesh Mesh,Vector3[] Joints,Vector3[] Soles)?[] _authoredAnimals=new (ArrayMesh,Vector3[],Vector3[])?[2];
    private Material AuthoredAnimalMaterial(bool wolf,string name)
    {
        string folder=AuthoredRoot(wolf);string key="authored-animal:"+wolf+":"+name;if(_residentMaterials.TryGetValue(key,out var cached))return cached;
        if(name=="Eye")return _residentMaterials[key]=new ShaderMaterial {Shader=new Shader {Code=@"shader_type spatial;
void fragment(){
vec2 p=UV2;
float radius=length(p),angle=atan(p.y,p.x);
float fiber=sin(angle*67.0+radius*49.0)/(1.0+pow(fwidth(angle)*67.0,2.0));
float iris=1.0-smoothstep(.64,.77,radius);
float pupil=1.0-smoothstep(.90,1.05,length(p/vec2(.49,.14)));
ALBEDO=mix(vec3(.012,.009,.006),vec3(.12,.065,.022)*(1.0+fiber*.12),iris);
ALBEDO=mix(ALBEDO,vec3(.002,.0025,.002),pupil);ROUGHNESS=.16;SPECULAR=.45;CLEARCOAT=.45;CLEARCOAT_ROUGHNESS=.16;
}"}};
        string texture=wolf?"wolf-grey":name=="Body"?"deer-body":name=="Antlers"?"deer-antlers":"deer-head";
        var material=new ShaderMaterial {Shader=new Shader {Code=@"shader_type spatial;
uniform sampler2D albedo_map:source_color,filter_linear_mipmap_anisotropic;uniform bool predator=false;
uniform sampler2D normal_map:hint_normal,filter_linear_mipmap_anisotropic;uniform bool has_normal=true;uniform float gain=1.0;uniform bool antler=false;
"+CoatGradingShader+@"
void fragment(){
vec3 colour=texture(albedo_map,UV).rgb;
vec3 graded=coat_grade(colour,predator);
if(antler){float luma=dot(colour,vec3(.2126,.7152,.0722));graded=mix(colour,vec3(luma)*vec3(1.10,1.04,.88),.65);}
ALBEDO=mix(graded,colour,COLOR.r)*gain;
if(predator)ALBEDO=wolf_eye_backing(albedo_map,ALBEDO,UV,gain);
vec3 detail=has_normal?texture(normal_map,UV).rgb:vec3(.5,.5,1);
float coordinate=UV.x*2100.0+sin(UV.y*97.0)*.7;
float fibre=sin(coordinate)*sin(UV.y*310.0+UV.x*43.0);
fibre/=1.0+pow(max(length(dFdx(UV)),length(dFdy(UV)))*2100.0,2.0);
detail.xy+=(antler?vec2(0.0):vec2(fibre*.024,fibre*.010));
NORMAL_MAP=detail;NORMAL_MAP_DEPTH=.32;
ROUGHNESS=antler?.92:mix(clamp(.83+fibre*.035,.75,.90),.28,COLOR.r);
ROUGHNESS=mix(ROUGHNESS,.59,COLOR.g);SPECULAR=antler?.12:mix(.21,.38,COLOR.r);
CLEARCOAT=COLOR.r*.45;CLEARCOAT_ROUGHNESS=.22;
// Nose grain is metric and anchored before skinning. Its micrometre
// relief fades with pixel footprint and never displaces the silhouette.
vec2 coat_point=UV2;
float pore=sin(coat_point.x*2100.0+sin(coat_point.y*1100.0))*sin(coat_point.y*1900.0-coat_point.x*900.0);
pore/=1.0+pow(max(length(dFdx(coat_point)),length(dFdy(coat_point)))*2100.0,2.0);
// Alpha is an opaque, bind-space foot mask, not transparency. These metric
// coordinates are written only on the four existing lower-leg skin regions.
float foot=1.0-COLOR.a;
float toe_crease=0.0;
if(foot>.001){
    float fore=1.0-smoothstep(.025,.075,coat_point.y);
    float middle=exp(-pow(coat_point.x/(predator?.0038:.0065),2.0));
    if(predator){
        // Three shallow fur divisions match the four lobes in the geometry.
        float sides=exp(-pow((abs(coat_point.x)-.0224)/.0038,2.0));
        toe_crease=(middle+sides)*fore*foot;
        ALBEDO*=1.0-toe_crease*.22;
    }else{
        toe_crease=middle*fore*foot;
        // Keep the source pattern but give the keratin a readable dark brown
        // floor. Black source texels previously erased the cloven silhouette.
        ALBEDO=mix(ALBEDO,max(ALBEDO,vec3(.10,.082,.060)),foot*.90);
        ALBEDO*=1.0-toe_crease*.42;
        ROUGHNESS=mix(ROUGHNESS,.64,foot);SPECULAR=mix(SPECULAR,.26,foot);
    }
}
vec3 dx=dFdx(VERTEX),dy=dFdy(VERTEX),tx=cross(dy,NORMAL),ty=cross(NORMAL,dx);float det=dot(dx,tx);
float height=pore*.000035*COLOR.r-toe_crease*.00035;
if(abs(det)>.0000000001)NORMAL=normalize(abs(det)*NORMAL-sign(det)*(dFdx(height)*tx+dFdy(height)*ty));
BACKLIGHT=ALBEDO*vec3(.085,.045,.026)*COLOR.b;
}"}};
        var albedo=GD.Load<Texture2D>(folder+texture+".png");ScannedSurfaces.RequireMipmaps(albedo);material.SetShaderParameter("albedo_map",albedo);
        if(!wolf){var normal=GD.Load<Texture2D>(folder+texture+"-normal.png");ScannedSurfaces.RequireMipmaps(normal);material.SetShaderParameter("normal_map",normal);}
        material.SetShaderParameter("has_normal",!wolf);
        material.SetShaderParameter("gain",wolf?.86f:.90f);material.SetShaderParameter("predator",wolf);
        ConfigureWolfEyeRepair(material);
        material.SetShaderParameter("antler",name=="Antlers");
        return _residentMaterials[key]=material;
    }
    private (ArrayMesh Mesh,Vector3[] Joints,Vector3[] Soles) AuthoredAnimalData(bool wolf)
    {
        int species=wolf?1:0;if(_authoredAnimals[species].HasValue)return _authoredAnimals[species]!.Value;
        string folder=AuthoredRoot(wolf);
        string raw=FileAccess.GetFileAsString(folder+(wolf?"wolf-authored.mesh.json":"deer-authored.mesh.json"));
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        if(hash!=(wolf?"a1e1ce89988afefa3fd8af54ad46665f83a2f29cefe69e628678e0c11656327f":"59c8a6ae08b32f185ab3670d64ebb05660cc369301f6911cb2c3a2f323b9e280"))throw new InvalidOperationException("Authored animal hash mismatch");
        using var document=JsonDocument.Parse(raw);var root=document.RootElement;
        if(root.GetProperty("source").GetProperty("license").GetString()!=(wolf?"CC-BY-SA-3.0":"CC0-1.0")||!FileAccess.FileExists(folder+"LICENSE.txt"))throw new InvalidOperationException("Missing deer licence");
        float[] Floats(JsonElement e){var a=new float[e.GetArrayLength()];int i=0;foreach(var v in e.EnumerateArray())a[i++]=v.GetSingle();return a;}
        int[] Ints(JsonElement e){var a=new int[e.GetArrayLength()];int i=0;foreach(var v in e.EnumerateArray())a[i++]=v.GetInt32();return a;}
        Vector3[] Vectors(JsonElement e){var a=new Vector3[e.GetArrayLength()];int i=0;foreach(var v in e.EnumerateArray())a[i++]=new(v[0].GetSingle(),v[1].GetSingle(),v[2].GetSingle());return a;}
        var mesh=new ArrayMesh();
        foreach(var source in root.GetProperty("surfaces").EnumerateArray())
        {
            var p=Floats(source.GetProperty("positions"));var n=Floats(source.GetProperty("normals"));var t=Floats(source.GetProperty("uv"));
            var vertices=new Vector3[p.Length/3];var normals=new Vector3[vertices.Length];var uv=new Vector2[vertices.Length];
            var detail=new Color[vertices.Length];var eyeUv=new Vector2[vertices.Length];
            if(n.Length!=p.Length||t.Length!=vertices.Length*2)throw new InvalidOperationException("Invalid deer attributes");
            for(int i=0;i<vertices.Length;i++){vertices[i]=new(p[i*3],p[i*3+1],p[i*3+2]);normals[i]=new(n[i*3],n[i*3+1],n[i*3+2]);uv[i]=new(t[i*2],t[i*2+1]);}
            string materialName=source.GetProperty("material").GetString()!;
            var sourceBones=source.GetProperty("bones");var sourceWeights=source.GetProperty("weights");
            for(int i=0;i<vertices.Length;i++)
            {
                var center=wolf?new Vector3(0,.826f,-.908f):new Vector3(0,1.370f,-1.106f);
                var radius=wolf?new Vector3(.036f,.027f,.028f):new Vector3(.026f,.027f,.025f);
                float nose=materialName is "Wolf" or "Head"?1-Mathf.SmoothStep(0,1,(((vertices[i]-center)/radius).Length()-.55f)/.60f):0;
                float hoof=!wolf&&materialName=="Body"?1-Mathf.SmoothStep(0,1,(vertices[i].Y-.035f)/.075f):0;
                float ear=0;
                for(int b=0;b<4;b++)if(sourceBones[i*4+b].GetInt32() is 12 or 13)ear+=sourceWeights[i*4+b].GetSingle();
                detail[i]=new(nose,hoof,Math.Clamp(ear,0,1),1);
                eyeUv[i]=materialName=="Eye"?new((vertices[i].Z+.85454f)/.018f,(vertices[i].Y-1.42125f)/.018f):new(vertices[i].X,vertices[i].Y+vertices[i].Z*.65f);
            }
            var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex]=vertices;arrays[(int)Mesh.ArrayType.Normal]=normals;arrays[(int)Mesh.ArrayType.TexUV]=uv;
            arrays[(int)Mesh.ArrayType.Color]=detail;arrays[(int)Mesh.ArrayType.TexUV2]=eyeUv;
            arrays[(int)Mesh.ArrayType.Bones]=Ints(source.GetProperty("bones"));arrays[(int)Mesh.ArrayType.Weights]=Floats(source.GetProperty("weights"));arrays[(int)Mesh.ArrayType.Index]=Ints(source.GetProperty("indices"));
            RefineAuthoredFeet(arrays,wolf,materialName);
            RefineAnimalHead(arrays,wolf,materialName);
            BindAuthoredPaws(arrays,wolf,materialName);
            if(wolf)BindWolfTail(arrays);
            var part=new ArrayMesh();part.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);
            using var tool=new SurfaceTool();tool.CreateFrom(part,0);tool.GenerateTangents();tool.SetMaterial(AuthoredAnimalMaterial(wolf,source.GetProperty("material").GetString()!));tool.Commit(mesh);
        }
        return (_authoredAnimals[species]=((ArrayMesh)WithScreenLods(mesh),Vectors(root.GetProperty("joints")),Vectors(root.GetProperty("soles")))).Value;
    }
    private AnimalRig AuthoredAnimal(bool wolf)
    {
        var data=AuthoredAnimalData(wolf);var rig=new AnimalRig {Predator=wolf,RestSoles=new Vector3[4]};
        rig.Body=new Node3D {Name="Body"};rig.AddChild(rig.Body);
        rig.Neck=new Node3D {Name="Neck",Position=data.Joints[11]};rig.Body.AddChild(rig.Neck);
        rig.Head=new Node3D {Name="Head",Position=data.Joints[1]-data.Joints[11]};rig.Neck.AddChild(rig.Head);
        rig.Tail=new Node3D {Name="Tail",Position=data.Joints[2]};rig.Body.AddChild(rig.Tail);
        for(int i=0;i<4;i++)
        {
            rig.Legs[i]=new Node3D {Name="Leg"+i,Position=data.Joints[3+i*2]};rig.Body.AddChild(rig.Legs[i]);
            rig.Knees[i]=new Node3D {Name="Knee",Position=data.Joints[4+i*2]-data.Joints[3+i*2]};rig.Legs[i].AddChild(rig.Knees[i]);
            rig.RestSoles[i]=data.Soles[i]-data.Joints[4+i*2];
            rig.Paws[i]=new Node3D {Name="Paw",Position=AuthoredPawAnchor(data.Soles[i],wolf)-data.Joints[4+i*2]};rig.Knees[i].AddChild(rig.Paws[i]!);
        }
        for(int i=0;i<2;i++)
        {
            rig.Ears[i]=new Node3D {Name=i==0?"LeftEar":"RightEar",Position=data.Joints[12+i]-data.Joints[1]};rig.Head.AddChild(rig.Ears[i]);
            var lining=Sculpt(rig.Ears[i],AuthoredEarLining(wolf,i),Vector3.Zero,ResidentMaterial(wolf?"#736f65":"#93816e"));
            lining.Name="InnerEarFibres";lining.VisibilityRangeEnd=18;lining.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
        }
        if(wolf)InstallWolfTail(rig);
        var nodes=new Node3D[data.Joints.Length+(wolf?6:4)];AnimalBones(rig).CopyTo(nodes,0);nodes[12]=rig.Ears[0];nodes[13]=rig.Ears[1];
        if(wolf){rig.Jaw=new Node3D {Name="Jaw",Position=data.Joints[14]-data.Joints[1]};rig.Head.AddChild(rig.Jaw);nodes[14]=rig.Jaw;}
        for(int i=0;i<4;i++)nodes[data.Joints.Length+i]=rig.Paws[i]!;
        if(wolf){nodes[19]=rig.TailMiddle!;nodes[20]=rig.TailTip!;}
        var skeleton=rig.FurSkeleton=new Skeleton3D {Name="FurSkeleton"};rig.Body.AddChild(skeleton);var skin=new Skin();
        for(int i=0;i<nodes.Length;i++)skeleton.AddBone("AnimalBone"+i);
        for(int i=0;i<nodes.Length;i++)
        {
            int parent=i==0?-1:wolf&&i==19?2:wolf&&i==20?19:i>=data.Joints.Length?4+(i-data.Joints.Length)*2:i>=12?1:i==1?11:i>=4&&i<=10&&i%2==0?i-1:0;
            if(parent>=0)skeleton.SetBoneParent(i,parent);
            skeleton.SetBoneRest(i,i==0?Transform3D.Identity:nodes[i].Transform);skin.AddBind(i,InTorso(nodes[i],rig.Body).AffineInverse());
        }
        skeleton.ResetBonePoses();rig.FurSkin=new MeshInstance3D {Name=wolf?"AuthoredWolf":"AuthoredDeer",Mesh=data.Mesh,Skin=skin,Skeleton=new NodePath("../FurSkeleton")};rig.Body.AddChild(rig.FurSkin);
        var guard=new MeshInstance3D {Name="AuthoredGuardCoat",Mesh=AuthoredGuardCoat(wolf),Skin=skin,Skeleton=new NodePath("../FurSkeleton"),VisibilityRangeEnd=18,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};rig.Body.AddChild(guard);
        if(wolf)for(int side=-1;side<=1;side+=2)
        {
            var eye=Sculpt(rig.Head,AuthoredWolfCornea(side),Vector3.Zero,AuthoredWolfEyeMaterial());eye.Name="Cornea"+side;
            eye.VisibilityRangeEnd=20;eye.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
        }
        var whiskers=Sculpt(rig.Head,AuthoredWhiskers(wolf),Vector3.Zero,ResidentMaterial(wolf?"#77756c":"#776b5b"));
        whiskers.Name="MuzzleWhiskers";whiskers.VisibilityRangeEnd=14;whiskers.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
        InstallAuthoredEyelids(rig,wolf);InstallPawKeratin(rig,wolf);return rig;
    }
    private Mesh AuthoredWhiskers(bool wolf)
    {
        string key="authored-whiskers:"+wolf;if(_meshes.TryGetValue(key,out var cached))return cached;
        var data=AuthoredAnimalData(wolf);var candidates=new System.Collections.Generic.List<(Vector3 P,Vector3 N)>();
        for(int s=0;s<data.Mesh.GetSurfaceCount();s++)
        {
            var arrays=data.Mesh.SurfaceGetArrays(s);var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var n=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            for(int i=0;i<p.Length;i++)
            {
                float head=0;for(int b=0;b<4;b++)if(bones[i*4+b]==1)head+=weights[i*4+b];
                if(head>.9999f&&MathF.Abs(n[i].X)>.3f)candidates.Add((p[i],n[i]));
            }
        }
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        foreach(int side in new[]{-1,1})
        for(int hair=0;hair<(wolf?5:3);hair++)
        {
            var target=wolf?new Vector3(side*.045f,.821f+(hair%3)*.009f,-.869f+(hair/3)*.014f):new Vector3(side*.032f,1.362f+(hair%3)*.008f,-1.074f);
            var source=candidates[0];float closest=float.PositiveInfinity;
            foreach(var candidate in candidates)
            {
                if(candidate.P.X*side<=0||candidate.N.X*side<=.3f)continue;
                float distance=candidate.P.DistanceSquaredTo(target);if(distance<closest){source=candidate;closest=distance;}
            }
            if(closest>.012f)throw new InvalidOperationException("No source-bound muzzle for tactile hair");
            var origin=source.P-data.Joints[1]+source.N*.00010f;
            var direction=(source.N*.65f+new Vector3(side*.3f,.08f+(hair%3)*.05f,-.28f)).Normalized();
            var across=direction.Cross(Vector3.Up).Normalized();var up=direction.Cross(across).Normalized();float length=(wolf?.017f:.009f)*(1-hair*.06f);
            void Vertex(int ring,int sector)
            {
                float t=ring/4f,a=sector*Mathf.Tau/4;var normal=across*Mathf.Cos(a)+up*Mathf.Sin(a);
                tool.SetNormal(normal);tool.SetUV(new(sector/4f,t));tool.AddVertex(origin+direction*length*t+Vector3.Down*(length*.15f*t*t)+normal*(.00012f*(1-t)+.00001f));
            }
            for(int ring=0;ring<4;ring++)for(int sector=0;sector<4;sector++)
            {Vertex(ring,sector);Vertex(ring+1,sector);Vertex(ring,sector+1);Vertex(ring,sector+1);Vertex(ring+1,sector);Vertex(ring+1,sector+1);}
        }
        tool.Index();return _meshes[key]=tool.Commit();
    }
    private Material AuthoredWolfEyeMaterial()
    {
        const string key="authored-wolf-cornea";if(_residentMaterials.TryGetValue(key,out var cached))return cached;
        var material=new ShaderMaterial {Shader=new Shader {Code=@"shader_type spatial;
uniform sampler2D source_albedo:source_color,filter_linear_mipmap_anisotropic;
"+CoatGradingShader+@"
void fragment(){
vec2 p=(UV-vec2(.5))*vec2(2.0,1.0);float radius=length(p),angle=atan(p.y,p.x);
float phase_a=angle*71.0+radius*97.0,phase_b=angle*43.0-radius*31.0;
float fibre=sin(phase_a)*sin(phase_b)/(1.0+pow(max(fwidth(phase_a),fwidth(phase_b)),2.0));
float iris=1.0-smoothstep(.49,.56,radius),pupil=1.0-smoothstep(.13,.17,radius);
vec3 pigment=mix(vec3(.035,.025,.017),vec3(.36,.215,.060)*(1.0+fibre*.14),iris);
pigment=mix(pigment,vec3(.002,.0025,.002),pupil);
vec2 aperture=(UV-vec2(.5))*2.0;
// The optical dome is broader than the visible opening. A continuous pair
// of curved fur margins crops it to an almond-shaped canine eye, instead of
// drawing the whole elliptical dome as a round glass bead.
float lid_height=.78*pow(max(0.0,1.0-aperture.x*aperture.x),.65);
float lid_edge=smoothstep(lid_height-.07,lid_height+.10,abs(aperture.y+aperture.x*.08));
float edge=max(lid_edge,smoothstep(.80,1.0,length(aperture)));
ALBEDO=mix(pigment,wolf_eye_backing(source_albedo,coat_grade(texture(source_albedo,UV2).rgb,true)*.86,UV2,.86),edge);ROUGHNESS=mix(.24,.83,edge);SPECULAR=mix(.32,.18,edge);
CLEARCOAT=(1.0-edge)*.10;CLEARCOAT_ROUGHNESS=.24;
}"}};
        material.SetShaderParameter("source_albedo",GD.Load<Texture2D>(AuthoredRoot(true)+"wolf-grey.png"));
        ConfigureWolfEyeRepair(material);return _residentMaterials[key]=material;
    }
    private Mesh AuthoredWolfCornea(int side)
    {
        string key="authored-wolf-cornea:"+side;if(_meshes.TryGetValue(key,out var cached))return cached;
        var data=AuthoredAnimalData(true);var arrays=data.Mesh.SurfaceGetArrays(0);
        var p=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var n=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var uv=arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        var centerUv=new Vector2(23.5f/256,189.5f/256);var faces=new System.Collections.Generic.List<int>();
        for(int i=0;i<indices.Length;i+=3)
        {
            int a=indices[i],b=indices[i+1],c=indices[i+2];
            if(p[a].X*side<=0||p[a].Y<.85f||p[a].Z>-.60f)continue;
            if(MathF.Min(uv[a].X,MathF.Min(uv[b].X,uv[c].X))>centerUv.X+.040f||MathF.Max(uv[a].X,MathF.Max(uv[b].X,uv[c].X))<centerUv.X-.040f||
                MathF.Min(uv[a].Y,MathF.Min(uv[b].Y,uv[c].Y))>centerUv.Y+.040f||MathF.Max(uv[a].Y,MathF.Max(uv[b].Y,uv[c].Y))<centerUv.Y-.040f)continue;
            faces.Add(i);
        }
        (Vector3 P,Vector3 N) Sample(Vector2 at)
        {
            foreach(int face in faces)
            {
                int a=indices[face],b=indices[face+1],c=indices[face+2];var ab=uv[b]-uv[a];var ac=uv[c]-uv[a];float det=ab.Cross(ac);
                if(MathF.Abs(det)<1e-12f)continue;var delta=at-uv[a];float v=delta.Cross(ac)/det,w=ab.Cross(delta)/det;
                if(v<-.00001f||w<-.00001f||v+w>1.00001f)continue;
                return(p[a]*(1-v-w)+p[b]*v+p[c]*w,(n[a]*(1-v-w)+n[b]*v+n[c]*w).Normalized());
            }
            throw new InvalidOperationException("Wolf cornea leaves the authored eye surface");
        }
        var center=Sample(centerUv);var across=Vector3.Up.Cross(center.N).Normalized();var up=center.N.Cross(across).Normalized();
        var du=(Sample(centerUv+Vector2.Right*.0001f).P-center.P)/.0001f;var dv=(Sample(centerUv+Vector2.Down*.0001f).P-center.P)/.0001f;
        float a11=du.Dot(across),a12=dv.Dot(across),a21=du.Dot(up),a22=dv.Dot(up),determinant=a11*a22-a12*a21;
        if(MathF.Abs(determinant)<.00001f)throw new InvalidOperationException("Invalid authored eye projection");
        const int rings=8,sectors=40;
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        (Vector3 P,Vector3 N,Vector2 Uv,Vector2 Source) Point(int ring,int sector)
        {
            float r=ring/(float)rings,angle=sector*Mathf.Tau/sectors;var q=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*r;
            float x=q.X*.013f,y=q.Y*.007f;var at=centerUv+new Vector2((a22*x-a12*y)/determinant,(-a21*x+a11*y)/determinant);
            var hit=Sample(at);return(hit.P-data.Joints[1]+hit.N*(.00012f+.0008f*(1-r*r)),(hit.N+across*q.X*.13f+up*q.Y*.23f).Normalized(),q*.5f+Vector2.One*.5f,at);
        }
        void Triangle((int R,int S) a,(int R,int S) b,(int R,int S) c)
        {
            var va=Point(a.R,a.S);var vb=Point(b.R,b.S);var vc=Point(c.R,c.S);
            if((vb.P-va.P).Cross(vc.P-va.P).Dot(center.N)>0)(vb,vc)=(vc,vb);
            foreach(var v in new[]{va,vb,vc}){tool.SetNormal(v.N);tool.SetUV(v.Uv);tool.SetUV2(v.Source);tool.AddVertex(v.P);}
        }
        for(int s=0;s<sectors;s++)Triangle((0,s),(1,s),(1,s+1));
        for(int r=1;r<rings;r++)for(int s=0;s<sectors;s++){Triangle((r,s),(r+1,s),(r,s+1));Triangle((r,s+1),(r+1,s),(r+1,s+1));}
        tool.Index();return _meshes[key]=tool.Commit();
    }
    private Mesh AuthoredEarLining(bool wolf,int ear)
    {
        string key=$"authored-ear-lining:{wolf}:{ear}";if(_meshes.TryGetValue(key,out var cached))return cached;
        var data=AuthoredAnimalData(wolf);var roots=new System.Collections.Generic.List<(Vector3 P,Vector3 N)>();
        var seen=new System.Collections.Generic.HashSet<(int,int,int)>();
        var facing=new Vector3(ear==0?-.35f:.35f,.1f,-1).Normalized();
        for(int s=0;s<data.Mesh.GetSurfaceCount();s++)
        {
            var arrays=data.Mesh.SurfaceGetArrays(s);var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            for(int i=0;i<points.Length;i++)
            {
                float weight=0;for(int b=0;b<4;b++)if(bones[i*4+b]==12+ear)weight+=weights[i*4+b];
                var point=points[i];
                if(weight<.75f||point.Y<data.Joints[12+ear].Y+.005f||normals[i].Dot(facing)<.2f)continue;
                if(seen.Add(((int)(point.X*10000),(int)(point.Y*10000),(int)(point.Z*10000))))roots.Add((point,normals[i]));
            }
        }
        if(roots.Count==0)throw new InvalidOperationException("Authored ear has no bound inner surface");
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        int count=Math.Min(48,roots.Count);
        for(int hair=0;hair<count;hair++)
        {
            var source=roots[hair*roots.Count/count];var root=source.P-data.Joints[12+ear]+source.N*.0002f;
            var direction=(source.N*.65f+Vector3.Up*.6f).Normalized();var across=direction.Cross(Vector3.Right).Normalized();
            float length=(wolf?.004f:.005f)*(1+(hair%5)*.12f);
            foreach(var side in new[]{across,direction.Cross(across).Normalized()})
            {
                var a=root-side*.00035f;var b=root+side*.00035f;var tip=root+direction*length;
                var n=(b-a).Cross(tip-a).Normalized();tool.SetNormal(n);
                tool.SetUV(new(0,0));tool.AddVertex(a);tool.SetUV(new(.5f,1));tool.AddVertex(tip);tool.SetUV(new(1,0));tool.AddVertex(b);
                tool.SetNormal(-n);tool.SetUV(new(0,0));tool.AddVertex(a);tool.SetUV(new(1,0));tool.AddVertex(b);tool.SetUV(new(.5f,1));tool.AddVertex(tip);
            }
        }
        tool.Index();return _meshes[key]=tool.Commit();
    }
    private void ValidateAuthoredAnimals()
    {
      foreach(bool wolf in new[]{false,true})
      {
        var rig=AuthoredAnimal(wolf);var peer=AuthoredAnimal(wolf);var mesh=rig.FurSkin!.Mesh;
        try
        {
        ValidateAuthoredFeet(rig,wolf);
        if(wolf)ValidateWolfEyeAtlas(mesh);
        int boneCount=wolf?21:18;
        var guard=rig.Body.GetNode<MeshInstance3D>("AuthoredGuardCoat");var guardArrays=guard.Mesh.SurfaceGetArrays(0);
        if(guard.Mesh!=peer.Body.GetNode<MeshInstance3D>("AuthoredGuardCoat").Mesh||guard.VisibilityRangeEnd!=18||guard.CastShadow!=GeometryInstance3D.ShadowCastingSetting.Off||guard.Skin!=rig.FurSkin.Skin)
            throw new InvalidOperationException("Source-bound guard coat loses its shared mesh, range, skeleton or shadow policy");
        var guardP=guardArrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var guardB=guardArrays[(int)Mesh.ArrayType.Bones].AsInt32Array();var guardW=guardArrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
        if(guardArrays[(int)Mesh.ArrayType.Index].AsInt32Array().Length/3>4000||guardP.Length*4!=guardB.Length||guardB.Length!=guardW.Length)throw new InvalidOperationException("Guard coat exceeds its geometry or attribute budget");
        for(int i=0;i<guardP.Length;i++)
        {
            if(!guardP[i].IsFinite())throw new InvalidOperationException("Non-finite guard hair");float sum=0;
            for(int j=0;j<4;j++){int k=i*4+j;if(guardB[k]<0||guardB[k]>=boneCount||!float.IsFinite(guardW[k])||guardW[k]<0)throw new InvalidOperationException("Guard hair loses source bone weights");sum+=guardW[k];}
            if(MathF.Abs(sum-1)>.001f)throw new InvalidOperationException("Guard hair weights are not normalized");
        }
        var whiskers=rig.Head.GetNode<MeshInstance3D>("MuzzleWhiskers");
        if(whiskers.Mesh!=peer.Head.GetNode<MeshInstance3D>("MuzzleWhiskers").Mesh||whiskers.VisibilityRangeEnd!=14||whiskers.CastShadow!=GeometryInstance3D.ShadowCastingSetting.Off)
            throw new InvalidOperationException("Tactile hair loses its shared mesh or close-range budget");
        var hairArrays=whiskers.Mesh.SurfaceGetArrays(0);var hairPoints=hairArrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var hairUv=hairArrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        var hairNormals=hairArrays[(int)Mesh.ArrayType.Normal].AsVector3Array();var hairIndex=hairArrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        var sourcePoints=new System.Collections.Generic.List<Vector3>();var authored=AuthoredAnimalData(wolf);
        for(int s=0;s<mesh.GetSurfaceCount();s++)sourcePoints.AddRange(mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array());
        if(hairIndex.Length/3>400)throw new InvalidOperationException("Tactile hair exceeds its geometry budget");
        var local=InTorso(whiskers,rig.Body);
        for(int i=0;i<hairPoints.Length;i++)
        {
            if(!hairPoints[i].IsFinite()||!hairNormals[i].IsFinite()||MathF.Abs(hairNormals[i].Length()-1)>.01f)throw new InvalidOperationException("Invalid tactile hair geometry");
            if(hairUv[i].Y!=0)continue;
            float gap=float.PositiveInfinity;foreach(var p in sourcePoints)gap=MathF.Min(gap,p.DistanceTo(local*hairPoints[i]));
            if(gap>.00035f)throw new InvalidOperationException("Tactile hair root floats off the source muzzle: "+gap);
        }
        for(int i=0;i<hairIndex.Length;i+=3)
        {int a=hairIndex[i],b=hairIndex[i+1],c=hairIndex[i+2];if((hairPoints[c]-hairPoints[a]).Cross(hairPoints[b]-hairPoints[a]).Dot(hairNormals[a]+hairNormals[b]+hairNormals[c])<=0)throw new InvalidOperationException("Reversed tactile hair surface");}
        if(wolf)
        {
            var source=AuthoredAnimalData(true).Mesh.SurfaceGetArrays(0);var p=source[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var uv=source[(int)Mesh.ArrayType.TexUV].AsVector2Array();var indices=source[(int)Mesh.ArrayType.Index].AsInt32Array();
            foreach(int side in new[]{-1,1})
            {
                var eye=rig.Head.GetNode<MeshInstance3D>("Cornea"+side);var other=peer.Head.GetNode<MeshInstance3D>("Cornea"+side);
                if(eye.Mesh!=other.Mesh||eye.VisibilityRangeEnd!=20||eye.Mesh.GetAabb().Size.Length()>.04f)throw new InvalidOperationException("Authored cornea loses cache, range or scale");
                var a=eye.Mesh.SurfaceGetArrays(0);var points=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var tex=a[(int)Mesh.ArrayType.TexUV2].AsVector2Array();var placement=InTorso(eye,rig.Body);
                for(int v=0;v<points.Length;v+=Math.Max(1,points.Length/16))
                {
                    float gap=float.PositiveInfinity;
                    for(int i=0;i<indices.Length;i+=3)
                    {
                        int aa=indices[i],b=indices[i+1],c=indices[i+2];if(p[aa].X*side<=0||p[aa].Y<.85f)continue;
                        var ab=uv[b]-uv[aa];var ac=uv[c]-uv[aa];float det=ab.Cross(ac);if(MathF.Abs(det)<1e-12f)continue;
                        var d=tex[v]-uv[aa];float s=d.Cross(ac)/det,t=ab.Cross(d)/det;if(s<-.00001f||t<-.00001f||s+t>1.00001f)continue;
                        var surface=p[aa]*(1-s-t)+p[b]*s+p[c]*t;gap=MathF.Min(gap,(placement*points[v]).DistanceTo(surface));
                    }
                    if(gap<.00007f||gap>.0011f)throw new InvalidOperationException("Wolf cornea floats or intersects its authored head: "+gap);
                }
            }
        }
        for(int ear=0;ear<2;ear++)
        {
            var lining=AuthoredEarLining(wolf,ear);if(lining!=AuthoredEarLining(wolf,ear)||lining.GetAabb().Size.Length()>.40f)throw new InvalidOperationException("Ear lining cache or dimensions changed");
            var a=lining.SurfaceGetArrays(0);var points=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=a[(int)Mesh.ArrayType.Normal].AsVector3Array();
            if(points.Length>576)throw new InvalidOperationException("Inner ear detail exceeds budget");
            for(int i=0;i<points.Length;i++)if(!points[i].IsFinite()||!normals[i].IsFinite()||normals[i].LengthSquared()<.9f)throw new InvalidOperationException("Invalid inner ear detail");
        }
        if(mesh!=peer.FurSkin!.Mesh||rig.FurSkeleton!.GetBoneCount()!=boneCount)throw new InvalidOperationException("Animal cache or skeleton mismatch");
        int triangles=0;
        float maxNose=0,maxHoof=0,maxEar=0;
        for(int s=0;s<mesh.GetSurfaceCount();s++)
        {
            var a=mesh.SurfaceGetArrays(s);var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var n=a[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var bs=a[(int)Mesh.ArrayType.Bones].AsInt32Array();var ws=a[(int)Mesh.ArrayType.Weights].AsFloat32Array();triangles+=a[(int)Mesh.ArrayType.Index].AsInt32Array().Length/3;
            var masks=a[(int)Mesh.ArrayType.Color].AsColorArray();var anchors=a[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
            if(masks.Length!=p.Length||anchors.Length!=p.Length)throw new InvalidOperationException("Animal loses bind-space surface attributes");
            for(int i=0;i<p.Length;i++)
            {
                if(!anchors[i].IsFinite()||!float.IsFinite(masks[i].R)||!float.IsFinite(masks[i].G)||!float.IsFinite(masks[i].B)||masks[i].R<0||masks[i].R>1||masks[i].G<0||masks[i].G>1||masks[i].B<0||masks[i].B>1)throw new InvalidOperationException("Invalid animal surface mask");
                maxNose=MathF.Max(maxNose,masks[i].R);maxHoof=MathF.Max(maxHoof,masks[i].G);maxEar=MathF.Max(maxEar,masks[i].B);
            }
            for(int i=0;i<p.Length;i++){float sum=0;for(int k=0;k<4;k++){if(bs[i*4+k]<0||bs[i*4+k]>=boneCount||ws[i*4+k]<0)throw new InvalidOperationException("Invalid animal binding");sum+=ws[i*4+k];}if(!p[i].IsFinite()||!n[i].IsFinite()||MathF.Abs(sum-1)>4f/65535f)throw new InvalidOperationException("Invalid authored animal surface");}
        }
        if(maxEar<.9f)throw new InvalidOperationException("Animal ear transmission loses source-weight coverage");
        if(triangles>24000||triangles<10000)throw new InvalidOperationException("Deer surface budget changed");
        int PresentationTriangles(Node node)
        {
            int count=0;if(node is MeshInstance3D instance)for(int surface=0;surface<instance.Mesh.GetSurfaceCount();surface++)
            {var a=instance.Mesh.SurfaceGetArrays(surface);int indices=a[(int)Mesh.ArrayType.Index].AsInt32Array().Length;count+=(indices>0?indices:a[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length)/3;}
            foreach(Node child in node.GetChildren())count+=PresentationTriangles(child);return count;
        }
        int presented=PresentationTriangles(rig);if(presented>24000)throw new InvalidOperationException("Complete authored animal exceeds 24,000 triangles including close-range details");
        GD.Print($"AUTHORED_PRESENTATION_TRIANGLES: {(wolf?"wolf":"deer")} {presented}, including guard coat, eyes, ear fibres and whiskers");
        if(maxNose<.5f||!wolf&&maxHoof<.9f)throw new InvalidOperationException("Animal nose or hoof loses its independent surface response");
        // The source-contact checks above require the bind/rest pose. Eye
        // animation validation deliberately poses this instance afterwards.
        ValidateAuthoredEyelids(rig,peer,wolf);
        foreach(float slope in new[]{0f,.16f,-.16f})for(int frame=0;frame<24;frame++)
        {
            float Ground(Vector3 p)=>p.Z*slope;
            rig.Pose(.04f,true,false,false,Ground);
            for(int leg=0;leg<4;leg++){var foot=rig.ContactSole(leg);if(!foot.IsFinite()||foot.Y-Ground(foot)<-.035f||foot.Y-Ground(foot)>.14f)throw new InvalidOperationException($"Authored animal terrain contact: wolf={wolf}, slope={slope}, frame={frame}, leg={leg}, gap={foot.Y-Ground(foot)}");}
        }
        if(wolf){var moving=rig.Jaw!.Rotation.X;for(int i=0;i<90;i++)rig.Pose(1f/60,false,false);if(rig.Jaw.Rotation.X<=moving||!rig.Jaw.Transform.IsFinite())throw new InvalidOperationException("Wolf jaw does not settle from moving to resting pose");}
        ValidatePawArticulation(rig,wolf);
        if(wolf)ValidateWolfTail(rig);
        var pose=rig.Head.Transform;var jaw=rig.Jaw?.Transform;rig.Pose(1,true,true);if(pose!=rig.Head.Transform||jaw!=rig.Jaw?.Transform)throw new InvalidOperationException("Paused authored animal moved");
        }
        finally {rig.Free();peer.Free();}
      }
      GD.Print("AUTHORED_ANIMALS_PASS: attributed surfaces, source-bound tactile hair, finite outward geometry, cache/range, 18/21 joints, weights, budgets, slopes, articulated jaw and pause");
    }
}
