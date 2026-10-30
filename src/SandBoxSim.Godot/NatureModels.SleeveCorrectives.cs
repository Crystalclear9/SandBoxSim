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
    private static Godot.Collections.Array<Godot.Collections.Array> BakeSleevePoses(Godot.Collections.Array arrays,ResidentRig rig)
    {
        var source=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var ids=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
        var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
        var uv=arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
        var adjacent=new HashSet<int>[source.Length];for(int i=0;i<source.Length;i++)adjacent[i]=new();
        for(int i=0;i<ids.Length;i+=3)for(int j=0;j<3;j++)
        {int a=ids[i+j],b=ids[i+(j+1)%3];adjacent[a].Add(b);adjacent[b].Add(a);}
        var result=new Godot.Collections.Array<Godot.Collections.Array>();
        for(int side=0;side<2;side++)for(int plane=0;plane<3;plane++)for(int height=0;height<2;height++)
        {
            float angle=Mathf.DegToRad(height==0?90:135);
            float planeAngle=plane*MathF.PI/4;
            var outward=new Vector3((side==0?-1:1)*MathF.Cos(planeAngle),0,-MathF.Sin(planeAngle));
            var axis=Vector3.Down.Cross(outward).Normalized();var direction=Vector3.Down.Rotated(axis,angle);
            var swing=new Basis(new Quaternion(Vector3.Down,direction));
            float twist=Math.Clamp(swing.X.SignedAngleTo(axis,direction),-1.65f,1.65f);
            var rotation=(new Basis(direction,twist)*swing).GetRotationQuaternion();
            var rest=rig.ShoulderRest(side);float lift=Mathf.SmoothStep(Mathf.DegToRad(30),Mathf.DegToRad(105),angle);
            var shoulder=rest+new Vector3((side==0?-1:1)*.004f*lift,.020f*lift,-.014f*Mathf.SmoothStep(.10f,.40f,-direction.Z*.55f));
            var transforms=new Transform3D[ResidentRig.SupportedBodyBones];Array.Fill(transforms,Transform3D.Identity);
            transforms[side+1]=new Transform3D(new Basis(rotation),shoulder)*rig.DetailedBodySkin.GetBindPose(side+1);
            for(int span=1;span<ResidentRig.SleeveSpans;span++)for(int family=0;family<2;family++)
            {
                float fraction=(float)span/ResidentRig.SleeveSpans;int bone=ResidentRig.SleeveSupportBone(side,span,family==1);
                var centre=rest.Lerp(shoulder,fraction)+(family==1?ResidentRig.SleeveCapCorrection(rotation,fraction):Vector3.Zero);
                transforms[bone]=new Transform3D(new Basis(Quaternion.Identity.Slerp(rotation,fraction)),centre)*rig.DetailedBodySkin.GetBindPose(bone);
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
                if((source[i].X<0)==(side==0)&&uv[i].X>=0)
                    active[i]=Mathf.SmoothStep(0,.025f,uv[i].X)*(1-Mathf.SmoothStep(.17f,.23f,uv[i].X));
            }
            var target=(Vector3[])posed.Clone();
            // Tangential cap fairing in posed space removes compressed little
            // ledges without inflating the sleeve or shrinking the whole arm.
            // Both seam and cuff are pinned. Bound the final edit to 4 mm.
            for(int iteration=0;iteration<6;iteration++)
            {
                var next=(Vector3[])target.Clone();float amount=iteration%2==0?.45f:-.47f;
                for(int i=0;i<target.Length;i++)if(active[i]>0)
                {
                    var average=Vector3.Zero;foreach(int j in adjacent[i])average+=target[j];average/=adjacent[i].Count;
                    var offset=(target[i]+(average-target[i])*amount*active[i])-posed[i];
                    if(offset.Length()>.004f)offset=offset.Normalized()*.004f;
                    next[i]=posed[i]+offset;
                }
                target=next;
            }
            var points=(Vector3[])source.Clone();var correctedNormals=(Vector3[])normals.Clone();
            var offsets=new Vector3[source.Length];
            for(int i=0;i<source.Length;i++)if(active[i]>0)offsets[i]=blends[i].AffineInverse()*target[i]-source[i];
            // A 4 mm edit can still tear a 0.3 mm compressed cap edge.
            // Constrain differences in bind space before producing normals,
            // retaining pinned endpoints and the actual final posed surface.
            for(int pass=0;pass<24;pass++)
                for(int a=0;a<source.Length;a++)foreach(int b in adjacent[a])if(b>a)
                {
                    var difference=offsets[a]-offsets[b];float length=difference.Length(),limit=source[a].DistanceTo(source[b])*.20f;
                    if(length<=limit||length<.0000001f)continue;
                    float freeA=active[a]>0?1:0,freeB=active[b]>0?1:0;if(freeA+freeB==0)continue;
                    var correction=difference*((length-limit)/length/(freeA+freeB));
                    offsets[a]-=correction*freeA;offsets[b]+=correction*freeB;
                }
            for(int i=0;i<source.Length;i++){points[i]+=offsets[i];target[i]=blends[i]*points[i];}
            var targetNormals=ClothSurfaceNormals(target,ids);
            for(int i=0;i<source.Length;i++)
            {
                bool own=(source[i].X<0)==(side==0);
                float normalWeight=own&&uv[i].X>=0?1-Mathf.SmoothStep(.22f,.263f,uv[i].X):0;
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
}
