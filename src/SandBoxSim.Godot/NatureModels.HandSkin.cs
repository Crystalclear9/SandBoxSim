using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    public Node3D HandStudy(int side,bool distant)
    {
        var rig=Resident(17,false,SandBoxSim.Core.Agents.JobType.Gatherer);
        rig.SetHandDetail(!distant);rig.PoseHandReference(0);
        var hand=rig.Hands[side];hand.GetParent().RemoveChild(hand);hand.Position=new(0,.14f,0);hand.Rotation=Vector3.Zero;
        rig.Free();return hand;
    }
    private void AddHandSkin(ResidentRig rig,string skin)
    {
        for(int side=0;side<2;side++)
        {
            var skeleton=rig.HandSkeletons[side]=new Skeleton3D {Name="HandSkeleton"};rig.Hands[side].AddChild(skeleton);
            var nodes=new Node3D[16];nodes[0]=rig.Hands[side];
            for(int d=0;d<4;d++){nodes[1+d*3]=rig.Fingers[side,d];nodes[2+d*3]=rig.FingerMiddles[side,d];nodes[3+d*3]=rig.Fingertips[side,d];}
            nodes[13]=rig.Thumbs[side];nodes[14]=rig.ThumbTips[side];
            var binds=new Skin();var rest=new Transform3D[16];
            for(int bone=0;bone<16;bone++)
            {
                skeleton.AddBone("HandBone"+bone);
                int parent=bone==0?-1:bone==15?0:bone==14?13:bone==13||((bone-1)%3==0)?0:bone-1;
                if(parent>=0)skeleton.SetBoneParent(bone,parent);
                var local=bone is 0 or 15?Transform3D.Identity:nodes[bone].Transform;
                skeleton.SetBoneRest(bone,local);rest[bone]=parent<0?local:rest[parent]*local;
                binds.AddBind(bone,rest[bone].AffineInverse());
            }
            skeleton.ResetBonePoses();
            var mesh=AnatomicalHandMesh(side,skin);
            rig.HandSkins[side].MaterialOverride=null;rig.HandSkins[side].Mesh=mesh;rig.HandSkins[side].Skin=binds;rig.HandSkins[side].Skeleton=new NodePath("../HandSkeleton");
        }
    }
    private Mesh AnatomicalHandMesh(int side,string skin,bool lowDetail=false)
    {
        string key="anatomical-hand:"+side+skin+lowDetail;if(_meshes.TryGetValue(key,out var cached))return cached;
        string raw=FileAccess.GetFileAsString("res://assets/models/human/"+(lowDetail?"human-hand-lod.mesh.json":"human-hand.mesh.json"));
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        if(hash!=(lowDetail?"fd12b6c389014590c0a58bf0b089b36ca690d6e3bbd437f0f8f34368d45dffa4":"03fb4ec965287396050ecf2f2009b765aa034f7e16fa2819d0bd8aaa22e979ba"))throw new InvalidOperationException("Anatomical hand source mismatch");
        using var document=System.Text.Json.JsonDocument.Parse(raw);var root=document.RootElement;
        float[] Floats(string name){var e=root.GetProperty(name);var f=new float[e.GetArrayLength()];int i=0;foreach(var item in e.EnumerateArray())f[i++]=item.GetSingle();return f;}
        int[] Integers(string name){var e=root.GetProperty(name);var f=new int[e.GetArrayLength()];int i=0;foreach(var item in e.EnumerateArray())f[i++]=item.GetInt32();return f;}
        var p=Floats("positions");var n=Floats("normals");var tex=Floats("uv");var points=new Vector3[p.Length/3];var normals=new Vector3[points.Length];var uv=new Vector2[points.Length];
        float mirror=ResidentRig.HandMirror(side);
        for(int i=0;i<points.Length;i++){points[i]=new(p[i*3]*mirror,p[i*3+1],p[i*3+2]);normals[i]=new(n[i*3]*mirror,n[i*3+1],n[i*3+2]);uv[i]=new(tex[i*2],tex[i*2+1]);}
        var bones=Integers("bones");var weights=Floats("weights");var mesh=new ArrayMesh();
        void Surface(string indicesName,Material material,bool nail)
        {
            var indices=Integers(indicesName);
            if(indices.Length==0)return;
            if(!nail)
            {
                // A material partition of the same surface, never an overlapping decal.
                var nailTriangles=Integers("nailIndices");var excluded=new System.Collections.Generic.HashSet<(int,int,int)>();
                for(int i=0;i<nailTriangles.Length;i+=3)excluded.Add((nailTriangles[i],nailTriangles[i+1],nailTriangles[i+2]));
                var skinTriangles=new System.Collections.Generic.List<int>();
                for(int i=0;i<indices.Length;i+=3)if(!excluded.Contains((indices[i],indices[i+1],indices[i+2])))
                {skinTriangles.Add(indices[i]);skinTriangles.Add(indices[i+1]);skinTriangles.Add(indices[i+2]);}
                indices=skinTriangles.ToArray();
            }
            if(mirror<0)for(int i=0;i<indices.Length;i+=3)(indices[i+1],indices[i+2])=(indices[i+2],indices[i+1]);
            int[] sourceMap;
            if(nail)
            {
                var lookup=new System.Collections.Generic.Dictionary<int,int>();var used=new System.Collections.Generic.List<int>();
                for(int i=0;i<indices.Length;i++){int old=indices[i];if(!lookup.TryGetValue(old,out int mapped)){mapped=used.Count;used.Add(old);lookup[old]=mapped;}indices[i]=mapped;}
                sourceMap=used.ToArray();
            }
            else{sourceMap=new int[points.Length];for(int i=0;i<sourceMap.Length;i++)sourceMap[i]=i;}
            var vertices=new Vector3[sourceMap.Length];var surfaceNormals=new Vector3[sourceMap.Length];var surfaceUv=new Vector2[sourceMap.Length];
            var surfaceBones=new int[sourceMap.Length*4];var surfaceWeights=new float[sourceMap.Length*4];
            for(int i=0;i<sourceMap.Length;i++)
            {
                int source=sourceMap[i];vertices[i]=points[source];surfaceNormals[i]=normals[source];surfaceUv[i]=uv[source];
                for(int b=0;b<4;b++){surfaceBones[i*4+b]=bones[source*4+b];surfaceWeights[i*4+b]=weights[source*4+b];}
            }
            var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex]=vertices;arrays[(int)Mesh.ArrayType.Normal]=surfaceNormals;arrays[(int)Mesh.ArrayType.TexUV]=surfaceUv;
            arrays[(int)Mesh.ArrayType.Bones]=surfaceBones;arrays[(int)Mesh.ArrayType.Weights]=surfaceWeights;arrays[(int)Mesh.ArrayType.Index]=indices;
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);mesh.SurfaceSetMaterial(mesh.GetSurfaceCount()-1,material);
        }
        Surface("indices",FaceSurfaceMaterial(skin),false);
        Surface("nailIndices",new StandardMaterial3D {AlbedoColor=new Color(skin).Lerp(new Color("#c5afa0"),.26f),Roughness=.54f,MetallicSpecular=.16f},true);
        _meshes[key]=mesh;return mesh;
    }
}
