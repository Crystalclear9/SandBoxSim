using System;
using Godot;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    public void ValidateCraftedModels()
    {
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
        void Validate(Node3D model,bool architecture)
        {
            int vertices=0,instances=0; Inspect(model,ref vertices,ref instances);
            if(vertices<100 || vertices>150000) throw new InvalidOperationException("Crafted model outside geometry budget: "+vertices);
            if(architecture && instances!=1) throw new InvalidOperationException("Static architecture was not consolidated");
            model.Free();
        }
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
            if(animal.Legs[0].Rotation.LengthSquared()<.0001f) throw new InvalidOperationException("Animal gait does not articulate legs");
            var pose=animal.Legs[0].Transform; animal.Pose(1,true,true);
            if(animal.Legs[0].Transform!=pose) throw new InvalidOperationException("Paused animal moved");
            Validate(animal,false);
        }
        Validate(Tree(false),false); Validate(Tree(true),false);
        GD.Print("CRAFTED_MODELS_PASS: 80 architecture LOD variants/states, deer/wolf gait and pause, vegetation, finite geometry/normals/UV and consolidation budgets");
    }
}
