using System;
using System.Text.Json;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private float[]? _frameMale,_frameFeminine,_frameRatios;
    private static float[] FrameRadii(string file,string expected)
    {
        string raw=FileAccess.GetFileAsString("res://assets/models/human/"+file).Replace("\r\n","\n",StringComparison.Ordinal);
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        if(hash!=expected)throw new InvalidOperationException("Changed body frame support: "+file);
        using var doc=JsonDocument.Parse(raw);var root=doc.RootElement;
        if(root.GetProperty("schemaVersion").GetInt32()!=1||root.GetProperty("rows").GetInt32()!=65||root.GetProperty("columns").GetInt32()!=128)
            throw new InvalidOperationException("Invalid body frame grid");
        var radii=new float[65*128];int i=0;
        foreach(var value in root.GetProperty("radii").EnumerateArray())
        {
            float r=value.GetSingle();
            if(i>=radii.Length||!float.IsFinite(r)||r<.025f||r>.34f)throw new InvalidOperationException("Invalid body frame radius");
            radii[i++]=r;
        }
        if(i!=radii.Length)throw new InvalidOperationException("Incomplete body frame support");
        return radii;
    }
    private Vector3 FramePoint(Vector3 point)
    {
        float chest=Mathf.SmoothStep(1.02f,1.12f,point.Y)*(1-Mathf.SmoothStep(1.255f,1.30f,point.Y));
        float inboard=1-Mathf.SmoothStep(.13f,.195f,MathF.Abs(point.X));
        float ratio=1;
        if(chest*inboard>0&&point.X*point.X+point.Z*point.Z>.0001f)
        {
            _frameMale??=FrameRadii("torso-cage.json","ecd8b6f42e68931caae38d48a76758f5b071b8c556ccaed0d5b58c92aa67d1db");
            _frameFeminine??=FrameRadii("torso-feminine-cage.json","043ce8aff6766d2691d3c58c2c2baa2d110fb041cf91d41f4001986dc3617181");
            if(_frameRatios==null)
            {
                // Cloth bridges local source hollows. Filter the ratio field
                // before fitting, rather than embossing small body features.
                var raw=new float[65*128];for(int i=0;i<raw.Length;i++)raw[i]=Math.Clamp(_frameFeminine[i]/_frameMale[i],.86f,1.16f);
                _frameRatios=new float[raw.Length];
                for(int rowIndex=0;rowIndex<65;rowIndex++)for(int column=0;column<128;column++)
                {
                    float sum=0;int count=0;
                    for(int dy=-2;dy<=2;dy++)for(int dx=-3;dx<=3;dx++)
                    {sum+=raw[Math.Clamp(rowIndex+dy,0,64)*128+(column+dx+128)%128];count++;}
                    _frameRatios[rowIndex*128+column]=sum/count;
                }
            }
            float row=Math.Clamp((point.Y-1.02f)/.005f,0,64),col=Mathf.PosMod(MathF.Atan2(point.Z,point.X),MathF.Tau)/MathF.Tau*128;
            int r=Math.Min(63,(int)row),c=(int)col%128;float u=col-MathF.Floor(col),v=row-r;
            float Radius(float[] data)=>Mathf.Lerp(Mathf.Lerp(data[r*128+c],data[r*128+(c+1)%128],u),Mathf.Lerp(data[(r+1)*128+c],data[(r+1)*128+(c+1)%128],u),v);
            ratio=Mathf.Lerp(1,Radius(_frameRatios),chest*inboard);
        }
        // Restrained clothed waist/hip silhouette; arm roots and leg lengths
        // stay fixed. This is a presentation fit, not a demographic classifier.
        float hip=MathF.Exp(-MathF.Pow((point.Y-.83f)/.085f,2));
        float waist=MathF.Exp(-MathF.Pow((point.Y-1.02f)/.07f,2));
        float lateral=1+.065f*hip-.045f*waist;
        return new(point.X*Mathf.Lerp(1,ratio,.35f)*lateral,point.Y,point.Z*ratio);
    }
    private Mesh FrameMesh(Mesh source,Transform3D space)
    {
        string key="body-frame:"+source.GetInstanceId()+":"+space;
        if(_meshes.TryGetValue(key,out var cached))return cached;
        var result=new ArrayMesh();var inverse=space.AffineInverse();
        for(int s=0;s<source.GetSurfaceCount();s++)
        {
            var arrays=source.SurfaceGetArrays(s);var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            for(int i=0;i<points.Length;i++)
            {
                var p=space*points[i];const float h=.0001f;
                var dx=(FramePoint(p+Vector3.Right*h)-FramePoint(p-Vector3.Right*h))/(2*h);
                var dy=(FramePoint(p+Vector3.Up*h)-FramePoint(p-Vector3.Up*h))/(2*h);
                var dz=(FramePoint(p+Vector3.Back*h)-FramePoint(p-Vector3.Back*h))/(2*h);
                var normal=space.Basis.Inverse().Transposed()*normals[i];
                normals[i]=(space.Basis.Transposed()*(new Basis(dx,dy,dz).Inverse().Transposed()*normal)).Normalized();
                points[i]=inverse*FramePoint(p);
                if(!points[i].IsFinite()||!normals[i].IsFinite())throw new InvalidOperationException("Invalid frame geometry");
            }
            arrays[(int)Mesh.ArrayType.Vertex]=points;arrays[(int)Mesh.ArrayType.Normal]=normals;arrays[(int)Mesh.ArrayType.Tangent]=default(Variant);
            result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);result.SurfaceSetMaterial(s,source.SurfaceGetMaterial(s));
        }
        var fitted=WithScreenLods(result);fitted.SetMeta("BodyFrameFit",true);
        return _meshes[key]=fitted;
    }
    private void ApplyBodyFrame(ResidentRig rig)
    {
        if(BodyFrame==0)return;
        void Walk(Node3D parent,Transform3D space)
        {
            foreach(Node child in parent.GetChildren())
            {
                if(child is not Node3D node||node==rig.Head||node==rig.Arms[0]||node==rig.Arms[1])continue;
                if(node==rig.Holster||node==rig.PackMount)
                {
                    var origin=space*node.Position;var fitted=FramePoint(origin);
                    if(node==rig.PackMount)fitted.Z+=FramePoint(new Vector3(0,1.15f,origin.Z)).Z-origin.Z;
                    node.Position=space.AffineInverse()*fitted;
                    continue; // Mounted tools and packs retain their rigid shape.
                }
                var transform=space*node.Transform;
                if(node is MeshInstance3D mesh&&mesh.Mesh!=null)
                {
                    mesh.Mesh=FrameMesh(mesh.Mesh,transform);
                    if(mesh!=rig.BodySkin&&mesh.HasMeta("DistantMesh"))
                        mesh.SetMeta("DistantMesh",FrameMesh((Mesh)mesh.GetMeta("DistantMesh").AsGodotObject(),transform));
                }
                else Walk(node,transform);
            }
        }
        Walk(rig,Transform3D.Identity);
        rig.DetailedBodyMesh=rig.BodySkin.Mesh;
        rig.DistantBodyMesh=FrameMesh(rig.DistantBodyMesh,Transform3D.Identity);
    }
    private void ValidateBodyFrames()
    {
        int previous=_faceVariant;
        var male=Resident(17,false,SandBoxSim.Core.Agents.JobType.Gatherer);
        var female=Resident(42,false,SandBoxSim.Core.Agents.JobType.Gatherer);
        try
        {
            if(male.DetailedBodyMesh==female.DetailedBodyMesh||male.DistantBodyMesh==female.DistantBodyMesh)
                throw new InvalidOperationException("Adult frames share their fitted body cache");
            foreach(var rig in new[]{male,female})
                if(rig.Scale.X*rig.Head.Scale.X<.985f||rig.Scale.X*rig.Head.Scale.X>1.035f)
                    throw new InvalidOperationException("Body width distorts the adult head proportions");
            female.ValidateDistanceMeshes();
            void CheckFrameCaches(Node3D parent)
            {
                foreach(Node child in parent.GetChildren())
                {
                    if(child is not Node3D node||node==female.Head||node==female.Arms[0]||node==female.Arms[1]||node==female.Holster||node==female.PackMount)continue;
                    if(node is MeshInstance3D mesh&&mesh!=female.BodySkin&&mesh.HasMeta("DistantMesh"))
                    {
                        var far=(Mesh)mesh.GetMeta("DistantMesh").AsGodotObject();
                        if(!mesh.Mesh.HasMeta("BodyFrameFit")||!far.HasMeta("BodyFrameFit"))
                            throw new InvalidOperationException("Adult accessory reverts to an unfitted distance mesh");
                    }
                    CheckFrameCaches(node);
                }
            }
            CheckFrameCaches(female);
            var p=new Vector3(.08f,1.16f,-.13f);
            if(FramePoint(p).DistanceTo(p)<.002f)throw new InvalidOperationException("Source female frame has no chest contour difference");
            foreach(float y in new[]{.035f,.075f,.10f,.145f,.20f,.255f,.265f,.275f})
            foreach(int frame in new[]{0,1})
            {
                var point=new Vector3(.032f,y,.035f);var normal=new Vector3(.3f,.4f,.8660254f).Normalized();
                var fitted=RefineForearmProfile(point,normal,frame);const float h=.00001f;
                var dx=(RefineForearmProfile(point+Vector3.Right*h,normal,frame).Point-RefineForearmProfile(point-Vector3.Right*h,normal,frame).Point)/(2*h);
                var dy=(RefineForearmProfile(point+Vector3.Up*h,normal,frame).Point-RefineForearmProfile(point-Vector3.Up*h,normal,frame).Point)/(2*h);
                var dz=(RefineForearmProfile(point+Vector3.Back*h,normal,frame).Point-RefineForearmProfile(point-Vector3.Back*h,normal,frame).Point)/(2*h);
                var reference=(new Basis(dx,dy,dz).Inverse().Transposed()*normal).Normalized();
                if(reference.DistanceTo(fitted.Normal)>.001f)throw new InvalidOperationException("Forearm volume normals miss their composite deformation");
                if(y<=.075f&&fitted.Point!=RefineWristContour(point,normal).Point)
                    throw new InvalidOperationException("Muscle fit changes the grip or wrist attachment");
            }
            foreach(var rig in new[]{male,female})
            foreach(var action in new[]{SandBoxSim.Core.Agents.ActionKind.Eat,SandBoxSim.Core.Agents.ActionKind.BuildHouse})
            for(int f=0;f<150;f++)
            {
                rig.PoseAction(1f/60,action,SandBoxSim.Core.Agents.ActionPhase.Executing,false,0);
                for(int side=0;side<2;side++)
                {
                    var elbow=InTorso(rig.Elbows[side],rig.Torso).Origin;
                    var wrist=InTorso(rig.Hands[side],rig.Torso).Origin;
                    if(MathF.Abs(elbow.DistanceTo(rig.Arms[side].Position)-.28f)>.0001f||MathF.Abs(elbow.DistanceTo(wrist)-rig.Hands[side].Position.Length())>.0001f)
                        throw new InvalidOperationException("Adult frame detaches an animated arm segment");
                }
            }
        }
        finally{male.Free();female.Free();_faceVariant=previous;_facePoints=null;_faceIndices=null;}
        GD.Print("BODY_FRAMES_PASS: source chest/waist/hip presentation, separate near/far caches, bounded forearm fit/Jacobian, exact wrist and animated limb lengths");
    }
}
