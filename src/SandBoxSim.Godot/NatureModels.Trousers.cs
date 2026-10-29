using System;
using System.Text.Json;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private Mesh ContinuousTrousers()
    {
        string frame=BodyFrame==1?"female":"male",key="continuous-trousers:"+frame;
        if(_meshes.TryGetValue(key,out var cached))return cached;
        string raw=FileAccess.GetFileAsString("res://assets/models/human/trousers-"+frame+".mesh.json").Replace("\r\n","\n",StringComparison.Ordinal);
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        if(hash!=(BodyFrame==1?"3c2f47a1bb71af00b70cf11295de9182ac43288a30e9d6699ee2783eec1d3790":"cd79838302cb28a482239cf84aa5c4a5c44b69b3b4d23becaf8aef8b257c80eb"))
            throw new InvalidOperationException("Changed source trousers");
        using var doc=JsonDocument.Parse(raw);var data=doc.RootElement;
        float[] Floats(string field){var a=new float[data.GetProperty(field).GetArrayLength()];int i=0;foreach(var v in data.GetProperty(field).EnumerateArray())a[i++]=v.GetSingle();return a;}
        int[] Ints(string field){var a=new int[data.GetProperty(field).GetArrayLength()];int i=0;foreach(var v in data.GetProperty(field).EnumerateArray())a[i++]=v.GetInt32();return a;}
        var p=Floats("positions");var n=Floats("normals");var uv=Floats("uv");var b=Ints("bones");var w=Floats("weights");var ids=Ints("indices");int count=p.Length/3;
        if(data.GetProperty("schemaVersion").GetInt32()!=2||data.GetProperty("jointCount").GetInt32()!=5||count==0||n.Length!=p.Length||uv.Length!=count*2||b.Length!=count*4||w.Length!=b.Length||ids.Length==0||ids.Length>30000)
            throw new InvalidOperationException("Invalid continuous trousers");
        var points=new Vector3[count];var normals=new Vector3[count];var coords=new Vector2[count];
        for(int i=0;i<count;i++)
        {
            points[i]=new(p[i*3],p[i*3+1],p[i*3+2]);normals[i]=new(n[i*3],n[i*3+1],n[i*3+2]);coords[i]=new(uv[i*2],uv[i*2+1]);
            float total=0;for(int j=0;j<4;j++){int k=i*4+j;if(b[k]<0||b[k]>4||!float.IsFinite(w[k])||w[k]<0)throw new InvalidOperationException("Invalid trouser weight");total+=w[k];}
            if(!points[i].IsFinite()||!normals[i].IsFinite()||MathF.Abs(normals[i].Length()-1)>.01f||MathF.Abs(total-1)>.0001f)
                throw new InvalidOperationException("Invalid trouser geometry");
        }
        foreach(int id in ids)if(id<0||id>=count)throw new InvalidOperationException("Invalid trouser index");
        var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex]=points;arrays[(int)Mesh.ArrayType.Normal]=normals;arrays[(int)Mesh.ArrayType.TexUV]=coords;
        arrays[(int)Mesh.ArrayType.Bones]=b;arrays[(int)Mesh.ArrayType.Weights]=w;arrays[(int)Mesh.ArrayType.Index]=ids;
        var mesh=new ArrayMesh();mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);mesh.SurfaceSetMaterial(0,Fabric("#645e53"));
        return _meshes[key]=WithScreenLods(mesh);
    }
    private void AddContinuousTrousers(ResidentRig rig)
    {
        if(rig.IsChild)return;
        var skeleton=rig.TrouserSkeleton=new Skeleton3D {Name="TrouserSkeleton"};rig.AddChild(skeleton);
        skeleton.AddBone("Waist");skeleton.SetBoneRest(0,Transform3D.Identity);
        var skin=new Skin();skin.AddBind(0,Transform3D.Identity);
        for(int side=0;side<2;side++)
        {
            int bone=side+1;skeleton.AddBone(side==0?"LeftThigh":"RightThigh");skeleton.SetBoneParent(bone,0);
            var rest=rig.Legs[side].Position;skeleton.SetBoneRest(bone,new(Basis.Identity,rest));skin.AddBind(bone,new(Basis.Identity,-rest));
        }
        for(int side=0;side<2;side++)
        {
            int bone=side+3;skeleton.AddBone(side==0?"LeftKnee":"RightKnee");skeleton.SetBoneParent(bone,side+1);
            skeleton.SetBoneRest(bone,rig.Knees[side].Transform);skin.AddBind(bone,(rig.Legs[side].Transform*rig.Knees[side].Transform).AffineInverse());
        }
        skeleton.ResetBonePoses();
        rig.Trousers=new MeshInstance3D {Name="ContinuousTrousers",Mesh=ContinuousTrousers(),Skin=skin,Skeleton=new NodePath("../TrouserSkeleton")};rig.AddChild(rig.Trousers);
        rig.SyncTrousers();
    }
    private void ValidateContinuousTrousers()
    {
        float maximum=0;int samples=0;
        foreach(int identity in new[]{17,55})
        {
            var rig=Resident(identity,false,SandBoxSim.Core.Agents.JobType.None);
            ((SceneTree)Engine.GetMainLoop()).Root.AddChild(rig);
            try
            {
                var mesh=rig.Trousers!.Mesh;var skin=rig.Trousers.Skin;var skeleton=rig.TrouserSkeleton!;
                if(ContinuousTrousers()!=mesh||mesh.GetSurfaceCount()!=1)throw new InvalidOperationException("Trouser cache or draw surface changed");
                var a=mesh.SurfaceGetArrays(0);var p=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var b=a[(int)Mesh.ArrayType.Bones].AsInt32Array();var w=a[(int)Mesh.ArrayType.Weights].AsFloat32Array();var ids=a[(int)Mesh.ArrayType.Index].AsInt32Array();
                var edges=new System.Collections.Generic.HashSet<(int A,int B)>();
                for(int i=0;i<ids.Length;i+=3)for(int j=0;j<3;j++){int x=ids[i+j],y=ids[i+(j+1)%3];edges.Add(x<y?(x,y):(y,x));}
                var posed=new Vector3[p.Length];
                foreach(var action in new[]{SandBoxSim.Core.Agents.ActionKind.None,SandBoxSim.Core.Agents.ActionKind.BuildHouse,SandBoxSim.Core.Agents.ActionKind.Take})
                for(int f=0;f<120;f++)
                {
                    rig.PoseAction(1f/60,action,action==SandBoxSim.Core.Agents.ActionKind.None?SandBoxSim.Core.Agents.ActionPhase.Moving:SandBoxSim.Core.Agents.ActionPhase.Executing,false,0);
                    if(f%10!=0)continue;
                    skeleton.ForceUpdateAllBoneTransforms();
                    var transforms=new Transform3D[5];
                    for(int bone=0;bone<5;bone++)transforms[bone]=skeleton.GetBoneGlobalPose(bone)*skin.GetBindPose(bone);
                    for(int side=0;side<2;side++)if(!transforms[side+1].IsEqualApprox(rig.Legs[side].Transform*skin.GetBindPose(side+1)))
                        throw new InvalidOperationException($"Trouser skin mismatch: {transforms[side+1]} expected {rig.Legs[side].Transform*skin.GetBindPose(side+1)}");
                    for(int side=0;side<2;side++)if(!transforms[side+3].IsEqualApprox(rig.Legs[side].Transform*rig.Knees[side].Transform*skin.GetBindPose(side+3)))
                        throw new InvalidOperationException("Trouser knee skin does not follow the actual boot rim");
                    for(int i=0;i<p.Length;i++)
                    {
                        var v=Vector3.Zero;for(int j=0;j<4;j++)v+=(transforms[b[i*4+j]]*p[i])*w[i*4+j];
                        if(!v.IsFinite())throw new InvalidOperationException("Invalid animated trousers");
                        if(p[i].Y>.88f&&v.DistanceTo(transforms[0]*p[i])>.00001f)throw new InvalidOperationException("Leg movement drags the trouser waist");
                        if(p[i].Y<.40f&&v.DistanceTo(transforms[p[i].X<0?3:4]*p[i])>.00001f)throw new InvalidOperationException("Trouser cuff detaches from the moving knee");
                        posed[i]=v;
                    }
                    foreach(var edge in edges)
                    {
                        float length=p[edge.A].DistanceTo(p[edge.B]);if(length<.00001f)continue;
                        float ratio=posed[edge.A].DistanceTo(posed[edge.B])/length;maximum=MathF.Max(maximum,ratio);
                        if(ratio>3)throw new InvalidOperationException($"Trouser crotch tears during {action}: stretch={ratio}");
                    }
                    samples++;
                }
                var frozen=skeleton.GetBonePose(1);rig.PoseAction(1,SandBoxSim.Core.Agents.ActionKind.Take,SandBoxSim.Core.Agents.ActionPhase.Executing,true,0);
                if(skeleton.GetBonePose(1)!=frozen)throw new InvalidOperationException("Paused trousers continue moving");
                rig.ValidateDistanceMeshes();if(rig.Trousers.Mesh!=mesh)throw new InvalidOperationException("Distance change replaces the bound lower garment");
            }
            finally{rig.Free();}
        }
        GD.Print($"CONTINUOUS_TROUSERS_PASS: two source frames, {samples} actual gait/work/pickup samples, thigh/knee binds, attached cuffs, stable waist, pause, distance and edge stretch={maximum:F3}");
    }
}

internal partial class ResidentRig
{
    internal Skeleton3D? TrouserSkeleton;
    internal MeshInstance3D? Trousers;
    internal void SyncTrousers()
    {
        if(TrouserSkeleton==null)return;
        // The waistband follows the same pelvic frame as the lower shirt;
        // thigh poses remain independent in rig space through the inverse.
        var waist=LowerGarmentFrame;
        TrouserSkeleton.SetBonePose(0,waist);
        var inverse=waist.AffineInverse();
        for(int side=0;side<2;side++)
        {
            var local=inverse*Legs[side].Transform;
            TrouserSkeleton.SetBonePose(side+1,local);
            TrouserSkeleton.SetBonePose(side+3,Knees[side].Transform);
        }
    }
}
