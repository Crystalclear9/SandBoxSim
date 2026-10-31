using System;
using System.Collections.Generic;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private readonly Dictionary<(int Frame,int Cut),Godot.Collections.Array<Godot.Collections.Array>> _sleevePoseCache=new();
    private void AddSleeveCorrectives(ResidentRig rig)
    {
        if(rig.IsChild)return;
        var source=rig.DetailedBodyMesh;
        string key="sleeve-poses:"+source.GetInstanceId();
        if(!_meshes.TryGetValue(key,out var corrected))
        {
            var result=new ArrayMesh {BlendShapeMode=Mesh.BlendShapeMode.Normalized};
            for(int i=0;i<ResidentRig.SleevePoseCount;i++)result.AddBlendShape("SleevePose"+i);
            if(!_sleevePoseCache.TryGetValue((BodyFrame,_garmentCut),out var poses))
            {
                poses=BakeSleevePoses(source.SurfaceGetArrays(0),rig);
                _sleevePoseCache[(BodyFrame,_garmentCut)]=poses;
            }
            for(int surface=0;surface<source.GetSurfaceCount();surface++)
            {
                var arrays=source.SurfaceGetArrays(surface);
                var shapes=poses;
                if(surface!=0)
                {
                    shapes=new();
                    for(int i=0;i<ResidentRig.SleevePoseCount;i++)
                    {
                        var unchanged=new Godot.Collections.Array();unchanged.Resize((int)Mesh.ArrayType.Max);
                        unchanged[(int)Mesh.ArrayType.Vertex]=arrays[(int)Mesh.ArrayType.Vertex];
                        unchanged[(int)Mesh.ArrayType.Normal]=arrays[(int)Mesh.ArrayType.Normal];
                        shapes.Add(unchanged);
                    }
                }
                arrays[(int)Mesh.ArrayType.Tangent]=default(Variant);
                result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays,shapes);
                result.SurfaceSetMaterial(surface,source.SurfaceGetMaterial(surface));
            }
            corrected=WithScreenLods(result);corrected.SetMeta("BodyFrameFit",true);_meshes[key]=corrected;
        }
        rig.DetailedBodyMesh=corrected;rig.BodySkin.Mesh=corrected;
    }
    private Godot.Collections.Array<Godot.Collections.Array> BakeSleevePoses(Godot.Collections.Array arrays,ResidentRig rig)
    {
        var source=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var ids=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
        var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
        var uv=arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
        var girdleWeights=GirdleWeights(source,ids,uv);
        var adjacent=new HashSet<int>[source.Length];for(int i=0;i<source.Length;i++)adjacent[i]=new();
        for(int i=0;i<ids.Length;i+=3)for(int j=0;j<3;j++)
        {int a=ids[i+j],b=ids[i+(j+1)%3];adjacent[a].Add(b);adjacent[b].Add(a);}
        var patches=ShoulderSurfacePatches(source,ids,adjacent,uv,girdleWeights);
        var bindings=BindShoulderSurface(source,ids,uv);
        var result=new Godot.Collections.Array<Godot.Collections.Array>();
        for(int side=0;side<2;side++)for(int plane=0;plane<ResidentRig.SleevePlaneCount;plane++)for(int height=0;height<ResidentRig.SleeveHeightCount;height++)for(int roll=0;roll<3;roll++)
        {
            float angle=Mathf.DegToRad((height+1)*45);
            float planeAngle=(plane-1)*MathF.PI/4;
            var outward=new Vector3((side==0?-1:1)*MathF.Cos(planeAngle),0,-MathF.Sin(planeAngle));
            var axis=Vector3.Down.Cross(outward).Normalized();var direction=Vector3.Down.Rotated(axis,angle);
            var swing=new Basis(new Quaternion(Vector3.Down,direction));
            float twist=ResidentRig.SleeveRollSample(direction,side,roll);
            var rotation=(new Basis(direction,twist)*swing).GetRotationQuaternion();
            var girdle=rig.ShoulderGirdle(side,direction);var girdleRotation=girdle.Basis.GetRotationQuaternion();
            var shoulder=girdle.Origin;
            var transforms=new Transform3D[ResidentRig.SupportedBodyBones];Array.Fill(transforms,Transform3D.Identity);
            transforms[side+1]=new Transform3D(new Basis(rotation),shoulder)*rig.DetailedBodySkin.GetBindPose(side+1);
            transforms[ResidentRig.GirdleBone(side)]=girdle*rig.DetailedBodySkin.GetBindPose(ResidentRig.GirdleBone(side));
            for(int span=1;span<ResidentRig.SleeveSpans;span++)for(int family=0;family<2;family++)
            {
                float fraction=(float)span/ResidentRig.SleeveSpans;int bone=ResidentRig.SleeveSupportBone(side,span,family==1);
                var centre=shoulder+(family==1?ResidentRig.SleeveCapCorrection(rotation,fraction,girdleRotation):Vector3.Zero);
                transforms[bone]=new Transform3D(new Basis(ResidentRig.SleeveRotation(girdleRotation,rotation,fraction)),centre)*rig.DetailedBodySkin.GetBindPose(bone);
            }
            var posed=new Vector3[source.Length];var blends=new Transform3D[source.Length];var active=new float[source.Length];
            for(int i=0;i<source.Length;i++)
            {
                var basis=new Basis(Vector3.Zero,Vector3.Zero,Vector3.Zero);var origin=Vector3.Zero;
                for(int j=0;j<4;j++)
                {
                    var transform=transforms[bones[i*4+j]];float w=weights[i*4+j];
                    basis.X+=transform.Basis.X*w;basis.Y+=transform.Basis.Y*w;basis.Z+=transform.Basis.Z*w;origin+=transform.Origin*w;
                }
                blends[i]=new(basis,origin);posed[i]=blends[i]*source[i];
                if((source[i].X<0)==(side==0))
                    active[i]=uv[i].X>=0?1-Mathf.SmoothStep(.17f,.23f,uv[i].X):girdleWeights[i];
            }
            var target=(Vector3[])posed.Clone();
            var anatomy=FitPosedShoulder(source,posed,target,ids,uv,active,rig,side,rotation,girdle,bindings);
            var anatomicalTarget=(Vector3[])target.Clone();
            // Fair the entire shoulder/armhole transition, not an isolated
            // sleeve with a pinned ridge at its root. Metric surface patches
            // and area weights avoid dependence on the tiny cap-ring edges.
            // Remove convex cap ridges along the surface normal without
            // filling the axillary hollow or sliding the seam tangentially.
            // This is a garment silhouette edit, separate from body fitting.
            for(int iteration=0;iteration<6;iteration++)
            {
                var next=(Vector3[])target.Clone();var surfaceNormals=ClothSurfaceNormals(target,ids);
                for(int i=0;i<target.Length;i++)if(active[i]>0)
                {
                    var average=Vector3.Zero;foreach(var (index,weight) in patches[i])average+=target[index]*weight;
                    var normal=surfaceNormals[i];float ridge=MathF.Min(0,(average-target[i]).Dot(normal));
                    var offset=target[i]+normal*(ridge*.45f*active[i])-anatomicalTarget[i];
                    if(offset.Length()>.012f)offset=offset.Normalized()*.012f;
                    next[i]=anatomicalTarget[i]+offset;
                }
                target=next;
            }
            var points=(Vector3[])source.Clone();var correctedNormals=(Vector3[])normals.Clone();
            var offsets=new Vector3[source.Length];
            for(int i=0;i<source.Length;i++)if(active[i]>0)offsets[i]=blends[i].AffineInverse()*target[i]-source[i];
            // Even a small edit can tear a 0.3 mm compressed cap edge.
            // Constrain differences in bind space before producing normals,
            // retaining pinned endpoints and the actual final posed surface.
            for(int pass=0;pass<64;pass++)
            {
                for(int a=0;a<source.Length;a++)foreach(int b in adjacent[a])if(b>a)
                {
                    var difference=offsets[a]-offsets[b];float length=difference.Length(),limit=source[a].DistanceTo(source[b])*.5f;
                    if(length<=limit||length<.0000001f)continue;
                    float freeA=active[a]>0?1:0,freeB=active[b]>0?1:0;if(freeA+freeB==0)continue;
                    var correction=difference*((length-limit)/length/(freeA+freeB));
                    offsets[a]-=correction*freeA;offsets[b]+=correction*freeB;
                }
                // Bind-space smoothness alone misses a stretched shared
                // armhole after the different skin transforms are applied.
                // Project that actual posed edge as well, with margin for
                // interpolation between the sampled rotations.
                for(int a=0;a<source.Length;a++)foreach(int b in adjacent[a])if(b>a)
                {
                    float wa=active[a]>0?1:0,wb=active[b]>0?1:0;if(wa+wb==0)continue;
                    var pa=blends[a]*(source[a]+offsets[a]);var pb=blends[b]*(source[b]+offsets[b]);
                    var edge=pa-pb;float length=edge.Length();
                    float limit=source[a].DistanceTo(source[b])*3;
                    if(length<=limit||length<.0000001f)continue;
                    var correction=edge*((length-limit)/length/(wa+wb));
                    if(wa>0)offsets[a]=blends[a].AffineInverse()*(pa-correction)-source[a];
                    if(wb>0)offsets[b]=blends[b].AffineInverse()*(pb+correction)-source[b];
                }
            }
            for(int i=0;i<source.Length;i++){points[i]+=offsets[i];target[i]=blends[i]*points[i];}
            for(int i=0;i<source.Length;i++)if(anatomy.Active[i]>.5f)
            {
                _posedShoulderBefore+=MathF.Abs((posed[i]-anatomy.Goals[i]).Dot(anatomy.Normals[i]));
                _posedShoulderAfter+=MathF.Abs((target[i]-anatomy.Goals[i]).Dot(anatomy.Normals[i]));_posedShoulderSamples++;
                // The visible cap horns lay beyond the former 4 cm search
                // radius. Check their full surface distance, not only the
                // normal component or a mean dominated by nearby vertices.
                float before=posed[i].DistanceTo(anatomy.Goals[i]);
                if(before>.04f)
                {
                    _protrudingCapBefore+=before;_protrudingCapAfter+=target[i].DistanceTo(anatomy.Goals[i]);_protrudingCapSamples++;
                }
            }
            var targetNormals=ClothSurfaceNormals(target,ids);
            for(int i=0;i<source.Length;i++)
            {
                bool own=(source[i].X<0)==(side==0);
                float normalWeight=!own?0:uv[i].X>=0?1-Mathf.SmoothStep(.22f,.263f,uv[i].X):girdleWeights[i];
                if(normalWeight<=0)continue;
                var inverse=blends[i].AffineInverse();
                var corrected=(inverse.Basis*targetNormals[i]).Normalized();
                correctedNormals[i]=normals[i].Slerp(corrected,normalWeight).Normalized();
                if(!points[i].IsFinite()||!correctedNormals[i].IsFinite())throw new InvalidOperationException("Invalid sleeve corrective pose");
            }
            var shape=new Godot.Collections.Array();shape.Resize((int)Mesh.ArrayType.Max);
            shape[(int)Mesh.ArrayType.Vertex]=points;shape[(int)Mesh.ArrayType.Normal]=correctedNormals;result.Add(shape);
        }
        return result;
    }
    private static (int Index,float Weight)[][] ShoulderSurfacePatches(Vector3[] points,int[] triangles,HashSet<int>[] adjacent,Vector2[] uv,float[] girdle)
    {
        var area=new float[points.Length];
        for(int i=0;i<triangles.Length;i+=3)
        {
            int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
            float value=(points[b]-points[a]).Cross(points[c]-points[a]).Length()/6;
            area[a]+=value;area[b]+=value;area[c]+=value;
        }
        const float radius=.04f;
        var result=new (int,float)[points.Length][];
        for(int i=0;i<points.Length;i++)
        {
            if((uv[i].X<0&&girdle[i]<=0)||uv[i].X>=.23f){result[i]=Array.Empty<(int,float)>();continue;}
            var distance=new Dictionary<int,float>{{i,0}};var queue=new PriorityQueue<int,float>();queue.Enqueue(i,0);
            while(queue.TryDequeue(out int a,out float travelled))
            {
                if(travelled>distance[a])continue;
                foreach(int b in adjacent[a])
                {
                    float next=travelled+points[a].DistanceTo(points[b]);
                    if(next>=radius||distance.TryGetValue(b,out float prior)&&prior<=next)continue;
                    distance[b]=next;queue.Enqueue(b,next);
                }
            }
            var patch=new List<(int,float)>();float total=0;
            foreach(var pair in distance)
            {
                float kernel=1-pair.Value/radius,weight=area[pair.Key]*kernel*kernel;
                patch.Add((pair.Key,weight));total+=weight;
            }
            if(total<=0)throw new InvalidOperationException("Shoulder patch has no surface area");
            for(int j=0;j<patch.Count;j++)patch[j]=(patch[j].Item1,patch[j].Item2/total);
            result[i]=patch.ToArray();
        }
        return result;
    }
}
