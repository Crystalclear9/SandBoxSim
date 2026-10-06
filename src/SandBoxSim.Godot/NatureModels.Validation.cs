using System;
using Godot;
using SandBoxSim.Core.Environment;

namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    public void ValidateCraftedModels()
    {
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
            for(uint identity=0;identity<5;identity++) { Validate(Building(kind,true,identity),true); Validate(Building(kind,false,identity),true); }
        foreach(bool wolf in new[]{false,true})
        {
            var animal=(AnimalRig)Animal(wolf); animal.Pose(.2f,true,false);
            if(animal.Legs[0].Rotation.LengthSquared()<.0001f) throw new InvalidOperationException("Animal gait does not articulate legs");
            var pose=animal.Legs[0].Transform; animal.Pose(1,true,true);
            if(animal.Legs[0].Transform!=pose) throw new InvalidOperationException("Paused animal moved");
            Validate(animal,false);
        }
        Validate(Tree(false),false); Validate(Tree(true),false);
        GD.Print("CRAFTED_MODELS_PASS: 40 architecture variants/states, deer/wolf gait and pause, vegetation, finite geometry/normals/UV and consolidation budgets");
    }
}
