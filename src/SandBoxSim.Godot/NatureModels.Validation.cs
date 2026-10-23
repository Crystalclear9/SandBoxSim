using System;
using Godot;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    private void ValidateAnimalRoots(AnimalRig animal,bool wolf)
    {
        var data=_meshes[wolf?"wolf-body":"deer-body"].SurfaceGetArrays(0);
        var points=data[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var indices=data[(int)Mesh.ArrayType.Index].AsInt32Array();
        for(int frame=0;frame<16;frame++)
        {
            animal.Pose(.10f,true,false);
            foreach(var leg in animal.Legs)
            {
                var root=leg.Transform*new Vector3(0,.12f,0);
                float bottom=float.PositiveInfinity,top=float.NegativeInfinity;
                // Intersect a vertical line with the actual closed torso, rather
                // than its bounding box, to detect exposed thigh end caps.
                for(int i=0;i<indices.Length;i+=3)
                {
                    var a=points[indices[i]];var b=points[indices[i+1]];var c=points[indices[i+2]];
                    if(root.X<MathF.Min(a.X,MathF.Min(b.X,c.X))-.00001f||root.X>MathF.Max(a.X,MathF.Max(b.X,c.X))+.00001f||
                        root.Z<MathF.Min(a.Z,MathF.Min(b.Z,c.Z))-.00001f||root.Z>MathF.Max(a.Z,MathF.Max(b.Z,c.Z))+.00001f)continue;
                    float determinant=(b.Z-c.Z)*(a.X-c.X)+(c.X-b.X)*(a.Z-c.Z);
                    if(MathF.Abs(determinant)<.0000001f)continue;
                    float u=((b.Z-c.Z)*(root.X-c.X)+(c.X-b.X)*(root.Z-c.Z))/determinant;
                    float v=((c.Z-a.Z)*(root.X-c.X)+(a.X-c.X)*(root.Z-c.Z))/determinant;
                    if(u<-.00001f||v<-.00001f||u+v>1.00001f)continue;
                    float y=a.Y*u+b.Y*v+c.Y*(1-u-v);bottom=MathF.Min(bottom,y);top=MathF.Max(top,y);
                }
                if(root.Y<bottom+.010f||root.Y>top-.010f||!float.IsFinite(bottom)||!float.IsFinite(top))
                    throw new InvalidOperationException("Animal leg root escapes the moving torso surface");
            }
        }
    }
    public void ValidateCraftedModels()
    {
        ValidateCeramics();
        ValidateCropModels();
        ValidateOrganicDetails();
        ValidateBotanicalModels();
        ValidateEnvironmentSources();
        ValidateGarmentCuts();
        ValidateLeatherGoods();
        ValidateHumanAsset();
        ValidateWindowRecesses();
        var batch=new Node3D();
        var placements=new System.Collections.Generic.List<Transform3D>();
        foreach(float x in new[]{-30f,0,23.9f,24.1f,60})placements.Add(new Transform3D(Basis.Identity,new(x,0,x)));
        var chunks=PartitionInstances(placements);var remaining=new System.Collections.Generic.List<Transform3D>(placements);
        // The headless dummy renderer cannot read MultiMesh transforms back. Validate the
        // CPU data and conservative bounds consumed by Batch, then its instance counts.
        foreach(var group in chunks.Values)
        {
            var bounds=InstanceBounds(group,Shape("cylinder").GetAabb());
            foreach(var transform in group)
                if(!remaining.Remove(transform)||!bounds.Encloses(transform*Shape("cylinder").GetAabb()))
                    throw new InvalidOperationException("Spatial batch loses an instance or its bounds");
        }
        Batch(batch,"cylinder",0,placements);int instanceCount=0;
        foreach(Node child in batch.GetChildren())instanceCount+=((MultiMeshInstance3D)child).Multimesh.InstanceCount;
        if(remaining.Count!=0||instanceCount!=placements.Count||batch.GetChildCount()!=4)throw new InvalidOperationException("Spatial batches do not separate distant objects");batch.Free();
        var faceVariants=new System.Collections.Generic.HashSet<Mesh>();int currentFace=_faceVariant;
        for(int variant=0;variant<8;variant++)
        {
            _faceVariant=variant;_facePoints=null;_faceIndices=null;faceVariants.Add(Shape("face"));
            float noseProjection=FaceDepth(.026f,HumanNoseY)-FaceDepth(0,HumanNoseY);
            if(noseProjection<.009f||noseProjection>.036f)throw new InvalidOperationException("Nasal surface lost its bounded anatomical projection");
            foreach(var feature in new[]{new Vector2(HumanEyeCenter(0).X,HumanEyeCenter(0).Y),new Vector2(HumanEyeCenter(1).X,HumanEyeCenter(1).Y),new Vector2(0,HumanMouthCenter.Y)})
                if(FaceDepth(feature.X,feature.Y)>=-.03f)throw new InvalidOperationException("Facial feature does not attach to the front surface: "+variant+" "+feature+" depth="+FaceDepth(feature.X,feature.Y));
            foreach(var mesh in new[]{Shape("face"),Shape("hair"),HumanMesh("eye",0),EarSurface(-1),EarSurface(1)})
            {
                var arrays=mesh.SurfaceGetArrays(0);
                foreach(var point in arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                    if(!point.IsFinite())throw new InvalidOperationException("Facial rim or brow has non-finite geometry");
                foreach(var normal in arrays[(int)Mesh.ArrayType.Normal].AsVector3Array())
                    if(!normal.IsFinite())throw new InvalidOperationException("Facial rim or brow has non-finite normals");
            }
            var brows=Brows();var browData=brows.SurfaceGetArrays(0);
            if(brows!=Brows()||browData[(int)Mesh.ArrayType.Index].AsInt32Array().Length>3600)
                throw new InvalidOperationException("Brow detail exceeds its cached geometry budget");
            foreach(var point in browData[(int)Mesh.ArrayType.Vertex].AsVector3Array())
            {
                float gap=FaceDepth(point.X,point.Y)-point.Z;
                if(gap<.00030f||gap>.00065f)throw new InvalidOperationException("Brow hair detached from the actual face");
            }
        }
        if(faceVariants.Count!=8)throw new InvalidOperationException("Resident face structures share the same mesh");
        GD.Print("FACE_CONTOURS_PASS: eight face/hair structures, attached eyes/lips, bounded nasal projection, finite ear/eyelid geometry and normals");
        _faceVariant=currentFace;_facePoints=null;_faceIndices=null;
        var prototype=Building(BuildingKind.House,true,1);var repeated=Building(BuildingKind.House,true,141);
        if(prototype==repeated||prototype.GetChild<MeshInstance3D>(0).Mesh!=repeated.GetChild<MeshInstance3D>(0).Mesh||prototype.Scale!=repeated.Scale)
            throw new InvalidOperationException("Architecture cache changes normalized variant or shares a scene node");
        prototype.Free();repeated.Free();
        void Inspect(Node3D node,ref int vertices,ref int instances)
        {
            if(!node.Transform.IsFinite()) throw new InvalidOperationException("Model has invalid transform");
            if(node is MeshInstance3D part)
            {
                instances++;
                for(int surface=0;surface<part.Mesh.GetSurfaceCount();surface++)
                {
                    var arrays=part.Mesh.SurfaceGetArrays(surface);
                    var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                    var uv=arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                    vertices+=points.Length;
                    if(points.Length!=normals.Length || points.Length!=uv.Length) throw new InvalidOperationException("Crafted mesh lost normals or UV");
                    foreach(var p in points) if(!p.IsFinite()) throw new InvalidOperationException("Invalid crafted vertex");
                    foreach(var n in normals) if(!n.IsFinite()) throw new InvalidOperationException("Invalid crafted normal");
                }
            }
            foreach(Node child in node.GetChildren()) if(child is Node3D model) Inspect(model,ref vertices,ref instances);
        }
        void Validate(Node3D model,bool architecture,int vertexBudget=150000)
        {
            int vertices=0,instances=0; Inspect(model,ref vertices,ref instances);
            if(vertices<100 || vertices>vertexBudget) throw new InvalidOperationException("Crafted model outside geometry budget: "+vertices);
            if(architecture && instances!=1) throw new InvalidOperationException("Static architecture was not consolidated");
            model.Free();
        }
        var meadow=Shape("grass");var meadowData=meadow.SurfaceGetArrays(0);
        var meadowPoints=meadowData[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var meadowNormals=meadowData[(int)Mesh.ArrayType.Normal].AsVector3Array();
        if(meadowPoints.Length!=meadowNormals.Length)throw new InvalidOperationException("Meadow mesh lost normals");
        foreach(var p in meadowPoints)if(!p.IsFinite()||p.Y<0)throw new InvalidOperationException("Meadow blade has invalid root or vertex");
        foreach(var n in meadowNormals)if(!n.IsFinite())throw new InvalidOperationException("Meadow blade has invalid normal");
        foreach(var mesh in new[]{Shape("face"),Shape("hair"),Shape("rock"),Garment()})
        {
            var arrays=mesh.SurfaceGetArrays(0); var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array(); var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            float facing=0;
            for(int i=0;i<points.Length;i++) facing+=normals[i].X*points[i].X+normals[i].Z*points[i].Z;
            if(facing<=0) throw new InvalidOperationException("Sculpted model normals face inward");
        }
        foreach(var kind in new[]{BuildingKind.House,BuildingKind.Storage,BuildingKind.Farm,BuildingKind.Mine})
            for(uint identity=0;identity<5;identity++)
            {
                foreach(bool complete in new[]{false,true})
                {
                    var near=Building(kind,complete,identity);var nearBounds=near.GetChild<MeshInstance3D>(0).Mesh.GetAabb();
                    var far=DistantBuilding(kind,complete,identity);var farBounds=far.GetAabb();
                    if(nearBounds.Position.DistanceTo(farBounds.Position)>.12f||nearBounds.Size.DistanceTo(farBounds.Size)>.18f)throw new InvalidOperationException("Building LOD changes silhouette");
                    var proxy=new Node3D();proxy.AddChild(new MeshInstance3D {Mesh=far});Validate(proxy,true);Validate(near,true);
                }
            }
        foreach(bool wolf in new[]{false,true})
        {
            var animal=(AnimalRig)Animal(wolf); animal.Pose(.2f,true,false);
            if(animal.FurSkeleton==null||animal.FurSkin==null||animal.FurSkeleton.GetBoneCount()!=12)throw new InvalidOperationException("Animal lacks a continuous skeletal surface");
            var grooming=animal.Body.GetNode<MeshInstance3D>("GuardHairCoat");
            if(grooming.Skin!=animal.FurSkin.Skin||grooming.Skeleton!=animal.FurSkin.Skeleton||grooming.VisibilityRangeEnd!=18)
                throw new InvalidOperationException("Animal grooming loses shared articulation or distance limit");
            var groom=grooming.Mesh.SurfaceGetArrays(0);var groomPoints=groom[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var groomBones=groom[(int)Mesh.ArrayType.Bones].AsInt32Array();var groomWeights=groom[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            if(groom[(int)Mesh.ArrayType.Index].AsInt32Array().Length!=(wolf?2200:1200)*18)
                throw new InvalidOperationException("Animal grooming exceeds fixed strand budget");
            var strandColors=groom[(int)Mesh.ArrayType.Color].AsColorArray();float darkest=1,lightest=0;
            if(strandColors.Length!=groomPoints.Length)throw new InvalidOperationException("Animal coat lost strand colours");
            foreach(var tint in strandColors){darkest=MathF.Min(darkest,tint.R);lightest=MathF.Max(lightest,tint.R);}
            if(lightest-darkest<.15f)throw new InvalidOperationException("Animal strands lost coat variation");
            if(GroomAnimalCoat(animal.FurSkin.Mesh,wolf)!=grooming.Mesh)
                throw new InvalidOperationException("Animal grooming cache not reused");
            for(int v=0;v<groomPoints.Length;v++)
            {
                float sum=0;
                for(int j=0;j<4;j++)
                {
                    int k=v*4+j;
                    if(groomBones[k]<0||groomBones[k]>=12||groomWeights[k]<0||!float.IsFinite(groomWeights[k]))
                        throw new InvalidOperationException("Invalid animal grooming binding");
                    sum+=groomWeights[k];
                }
                if(!groomPoints[v].IsFinite()||MathF.Abs(sum-1)>.0001f)
                    throw new InvalidOperationException("Animal grooming has invalid geometry/weights");
            }
            var coat=animal.FurSkin.Mesh.SurfaceGetArrays(0);var coatPoints=coat[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var coatIndices=coat[(int)Mesh.ArrayType.Index].AsInt32Array();var coatBones=coat[(int)Mesh.ArrayType.Bones].AsInt32Array();var coatWeights=coat[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            var coatEdges=new System.Collections.Generic.Dictionary<long,int>();var parent=new int[coatPoints.Length];for(int i=0;i<parent.Length;i++)parent[i]=i;
            int Root(int vertex){while(parent[vertex]!=vertex){parent[vertex]=parent[parent[vertex]];vertex=parent[vertex];}return vertex;}
            for(int i=0;i<coatIndices.Length;i+=3)for(int j=0;j<3;j++)
            {
                int a=coatIndices[i+j],b=coatIndices[i+(j+1)%3];parent[Root(a)]=Root(b);if(a>b)(a,b)=(b,a);long edge=((long)a<<32)|(uint)b;
                coatEdges[edge]=coatEdges.TryGetValue(edge,out int count)?count+1:1;
            }
            foreach(int count in coatEdges.Values)if(count!=2)throw new InvalidOperationException("Animal coat is not a closed manifold");
            for(int i=0;i<coatPoints.Length;i++)
            {
                if(Root(i)!=Root(0))throw new InvalidOperationException("Animal coat contains a detached component");
                float sum=0;for(int b=0;b<4;b++){int bone=coatBones[i*4+b];if(bone<0||bone>=12)throw new InvalidOperationException("Animal bone outside skin palette");sum+=coatWeights[i*4+b];}
                if(MathF.Abs(sum-1)>4f/65535f)throw new InvalidOperationException("Animal skin weights lose normalization");
            }
            for(int i=0;i<4;i++)if(MathF.Abs(animal.FurSkeleton.GetBonePoseRotation(3+i*2).Dot(animal.Legs[i].Basis.GetRotationQuaternion()))<.999f)throw new InvalidOperationException("Animal skin does not follow limb articulation");
            if(animal.Legs[0].Rotation.LengthSquared()<.0001f) throw new InvalidOperationException("Animal gait does not articulate legs");
            var pose=animal.Legs[0].Transform;var earPose=animal.Ears[0].Transform; animal.Pose(1,true,true);
            if(animal.Legs[0].Transform!=pose||animal.Ears[0].Transform!=earPose) throw new InvalidOperationException("Paused animal moved");
            animal.Pose(2,false,false,true);
            if(!wolf&&animal.Neck.Rotation.X>-.8f)throw new InvalidOperationException("Grazing does not bend the continuous neck");
            var neckPose=animal.Neck.Transform;animal.Pose(1,false,true,true);if(animal.Neck.Transform!=neckPose)throw new InvalidOperationException("Paused grazing moved the neck");
            ValidateAnimalRoots(animal,wolf);
            Validate(animal,false);
        }
        // The textured broadleaf includes duplicated UV/normal seam vertices.
        Validate(Tree(false),false,220000); Validate(Tree(true),false,190000);
        GD.Print("CRAFTED_MODELS_PASS: 80 architecture LOD variants/states, deer/wolf groomed coat binding/cache/budget, gait, pause and embedded thigh roots, vegetation, finite geometry/normals/UV and consolidation budgets");
    }
}
