using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private int _garmentCut;
    private float GarmentBottom=>_garmentCut==1?.63f:_garmentCut==2?.535f:.60f;
    private void SelectGarmentCut(int cut)
    {
        if(_garmentCut==cut)return;
        _garmentCut=cut;_garmentPoints=null;_garmentIndices=null;
    }
    private readonly Dictionary<string, Material> _residentMaterials = new();
    private Material ResidentMaterial(string color, float roughness = .88f)
    {
        if (!_residentMaterials.TryGetValue(color, out var material))
        {
            bool fabric = color is "#637471" or "#8c8978" or "#806f65" or "#6a7680" or "#717961" or "#9a9080" or "#75654f" or "#645e53" or "#a59c86" or "#a8937d";
            material = color is "#aa8b78" or "#8e705e" or "#ba9d89" or "#806653" ? SkinSurface(color) : color is "#42352c" or "#695344" ? HairSurface(color) : fabric ? Fabric(color) : new StandardMaterial3D { AlbedoColor = new Color(color), Roughness = roughness, MetallicSpecular = .22f };
            _residentMaterials[color] = material;
        }
        return material;
    }
    private MeshInstance3D Detail(Node3D parent, string shape, Vector3 position, Vector3 scale, string color, Vector3 rotation = default)
    {
        var detail = new MeshInstance3D { Mesh = Shape(shape), Position = position, Scale = scale,
            Rotation = rotation, MaterialOverride = ResidentMaterial(color) };
        parent.AddChild(detail); return detail;
    }
    private Mesh Garment()
    {
        string key="tunic:"+_garmentCut;
        if (_meshes.TryGetValue(key, out var existing)) { return existing; }
        const int sides = 64, rings=40;
        float[] height = { GarmentBottom, GarmentBottom+.035f, .73f, .82f, .97f, 1.10f, 1.19f, 1.24f, 1.28f };
        float[] width = { _garmentCut==1?.185f:.20f, _garmentCut==1?.190f:.208f, .174f, .172f, .180f, .196f, .204f, .166f, .060f };
        float[] depth = { _garmentCut==2?.165f:.145f, _garmentCut==2?.165f:.15f, .125f, .125f, .140f, .145f, .125f, .10f, .07f };
        var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles); surface.SetSmoothGroup(0);
        Vector3 Point(int ring, int side)
        {
            float y=Mathf.Lerp(height[0],height[^1],ring/(float)rings);
            float Profile(float[] values)
            {
                int i=0;while(i<height.Length-2 && y>height[i+1])i++;
                float t=(y-height[i])/(height[i+1]-height[i]);int a=Math.Max(0,i-1),b=Math.Min(height.Length-1,i+2);
                float m0=(values[i+1]-values[a])/(height[i+1]-height[a])*(height[i+1]-height[i]);
                float m1=(values[b]-values[i])/(height[b]-height[i])*(height[i+1]-height[i]);
                float value=(2*t*t*t-3*t*t+1)*values[i]+(t*t*t-2*t*t+t)*m0+(-2*t*t*t+3*t*t)*values[i+1]+(t*t*t-t*t)*m1;
                return value;
            }
            float angle = side * MathF.Tau / sides;
            float belt=1-MathF.Exp(-MathF.Pow((y-.765f)*22,2));
            float hem=Math.Clamp((.80f-y)/(.80f-GarmentBottom),0,1);
            float front=MathF.Pow(MathF.Max(0,-MathF.Sin(angle)),4);
            float tension=MathF.Exp(-MathF.Pow((y-.82f)/.08f,2));
            float fold=1+(.012f+.035f*hem)*MathF.Cos(angle*11+(y-.6f)*7)*belt;
            // Cloth gathers below the belt; diagonal compression folds radiate from it.
            fold+=front*tension*.018f*MathF.Sin((y-.76f)*92+MathF.Abs(MathF.Cos(angle))*11);
            fold+=MathF.Exp(-MathF.Pow((y-1.10f)/.12f,2))*.012f*MathF.Sin(angle*7+y*29);
            float vent=_garmentCut==2?hem*(.075f*MathF.Pow(MathF.Max(0,-MathF.Sin(angle)),16)+.040f*MathF.Pow(MathF.Max(0,MathF.Sin(angle)),16)):0;
            return new Vector3(MathF.Cos(angle)*Profile(width)*fold,y+vent,MathF.Sin(angle)*Profile(depth)*fold);
        }
        for (int ring = 0; ring < rings; ring++) for (int side = 0; side < sides; side++)
        {
            Vector3 a = Point(ring, side), b = Point(ring, side + 1), c = Point(ring + 1, side), d = Point(ring + 1, side + 1);
            foreach (Vector3 vertex in new[] { a, b, c, b, d, c })
            {
                surface.SetUV(new(MathF.Atan2(vertex.Z,vertex.X)/MathF.Tau+.5f,(vertex.Y-GarmentBottom)/(1.28f-GarmentBottom)));
                surface.AddVertex(vertex);
            }
        }
        // Turn the fabric under at the hem. This is an open garment with a sewn
        // inner facing, not a solid cap spanning through the wearer's legs.
        surface.SetSmoothGroup(1);
        for(int side=0;side<sides;side++)
        {
            var a=Point(0,side);var b=Point(0,side+1);
            var c=new Vector3(a.X*.984f,a.Y+.003f,a.Z*.984f);var d=new Vector3(b.X*.984f,b.Y+.003f,b.Z*.984f);
            EquipmentTriangle(surface,a,b,c,Vector3.Down);EquipmentTriangle(surface,b,d,c,Vector3.Down);
            var e=c+Vector3.Up*.010f;var f=d+Vector3.Up*.010f;
            var inward=-new Vector3(c.X,0,c.Z).Normalized();
            EquipmentTriangle(surface,c,d,e,inward);EquipmentTriangle(surface,d,f,e,inward);
        }
        surface.GenerateNormals(); surface.Index(); var mesh = surface.Commit(); _meshes[key] = mesh; return mesh;
    }
    private void MergeResidentParts(Node3D parent, string key)
    {
        var parts = new List<MeshInstance3D>();
        foreach (Node child in parent.GetChildren())
        {
            if (child is MeshInstance3D part) { if(part.MaterialOverride != null) parts.Add(part); }
            else if (child is Node3D joint) { MergeResidentParts(joint, key + "/" + joint.Name + (joint.Name=="Head" ? ":"+_faceVariant : "")); }
        }
        if (parts.Count < 2) { return; }
        if (!_meshes.TryGetValue(key, out Mesh? combined))
        {
            var groups = new Dictionary<Material, List<MeshInstance3D>>();
            foreach (var part in parts)
            {
                if (!groups.TryGetValue(part.MaterialOverride, out var group)) { group = new(); groups[part.MaterialOverride] = group; }
                group.Add(part);
            }
            var mesh = new ArrayMesh();
            foreach (var group in groups)
            {
                AppendMergedSurface(mesh,group.Key,group.Value);
            }
            combined = key.StartsWith("resident:") || key.StartsWith("crafted-wolf") || key.StartsWith("crafted-deer") ? WithScreenLods(mesh) : mesh; _meshes[key] = combined;
        }
        foreach (var part in parts) { part.Free(); }
        var instance=new MeshInstance3D {Mesh=combined};parent.AddChild(instance);
        if(key.StartsWith("resident:"))instance.SetMeta("DistantMesh",DistantColors(combined));
    }
    public ResidentRig Resident(int identity, bool child, JobType job)
    {
        uint variation = unchecked((uint)identity * 2654435761u);
        SelectGarmentCut(child?0:job==JobType.Trader?2:job is JobType.Gatherer or JobType.Hunter or JobType.Soldier?1:0);
        _faceVariant=(int)((variation>>8)%8);_facePoints=null;_faceIndices=null;
        string[] clothColors = { "#637471", "#8c8978", "#806f65", "#6a7680", "#717961", "#9a9080" };
        string[] skinColors = { "#aa8b78", "#8e705e", "#ba9d89", "#806653" };
        string cloth = clothColors[variation % clothColors.Length], skin = skinColors[(variation >> 4) % skinColors.Length];
        string hair = (variation & 1) == 0 ? "#42352c" : "#695344";
        var rig = new ResidentRig { Name = "Resident", IsChild = child };
        rig.Torso = new Node3D { Name = "Torso" }; rig.AddChild(rig.Torso);
        rig.Torso.AddChild(new MeshInstance3D { Mesh = Garment(), MaterialOverride = ResidentMaterial(cloth) });
        // Collar, placket and the hem distinguish tailored cloth from the body's silhouette.
        Sculpt(rig.Torso,TurnedNeckline(),Vector3.Zero,CollarMaterial(cloth));
        if(_garmentCut==2)foreach(float side in new[]{-1f,1f})
            Sculpt(rig.Torso,CollarLeaf(side),Vector3.Zero,CollarMaterial(cloth));
        Sculpt(rig.Torso,FittedRibbon("placket",1.03f,1.205f),Vector3.Zero,CollarMaterial(cloth));
        for (int button = 0; button < 3; button++)
        { Detail(rig.Torso, "sphere", TorsoSurface(.006f,1.17f-button*.055f,false,.006f), Vector3.One * .011f, "#514237"); }
        Sculpt(rig.Torso, FittedRibbon("belt", .748f, .775f), Vector3.Zero, ResidentMaterial("#514237"));
        var buckle=TorsoSurface(.015f,.7615f,false,.009f);
        var buckleCorners=new[]{buckle+new Vector3(-.0205f,-.0155f,0),buckle+new Vector3(.0205f,-.0155f,0),buckle+new Vector3(.0205f,.0155f,0),buckle+new Vector3(-.0205f,.0155f,0)};
        for(int edge=0;edge<4;edge++)FineBeam(rig.Torso,buckleCorners[edge],buckleCorners[(edge+1)%4],.004f,"#b8a17b");
        FineBeam(rig.Torso,buckle+new Vector3(-.006f,0,-.002f),buckle+new Vector3(.0205f,0,-.002f),.003f,"#b8a17b");
        Detail(rig.Torso, "capsule", new Vector3(.23f, .7f, .075f), new Vector3(.075f, .065f, .055f), "#725b40");
        Sculpt(rig.Torso,Loft("resident-neck",new[]{new Vector4(0,0,.039f,.039f),new(.035f,-.006f,.038f,.037f),new(.080f,-.012f,.039f,.038f)}),new(0,1.265f,-.004f),ResidentMaterial(skin),new(-MathF.PI/2,0,0));
        rig.Head = new Node3D { Name = "Head", Position = new Vector3(0, 1.382f, 0) }; rig.Torso.AddChild(rig.Head);
        Detail(rig.Head, "face", Vector3.Zero, new Vector3(.177f, .220f, .195f), skin).MaterialOverride=FaceSurfaceMaterial(skin);
        Detail(rig.Head, "hair", new Vector3(0, .008f, 0), new Vector3(.181f, .224f, .200f), hair);
        rig.Head.AddChild(new MeshInstance3D {Name="Brows",Mesh=Brows(),MaterialOverride=ResidentMaterial(hair)});

        // The short cut exposes the trouser waist. A continuous pelvis connects
        // the two articulated legs instead of leaving a gap beneath the tunic.
        Sculpt(rig,Loft("trouser-pelvis",new[]{new Vector4(.565f,0,.020f,.020f),new(.61f,0,.132f,.072f),
            new(.66f,0,.159f,.082f),new(.70f,0,.166f,.088f),new(.73f,0,.150f,.090f)}),Vector3.Zero,
            ResidentMaterial("#645e53"),new(-MathF.PI/2,0,0));
        FineBeam(rig,new(0,.648f,-.082f),new(0,.699f,-.090f),.0025f,"#645e53");

        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1 : 1;
            var arm = rig.Arms[i] = new Node3D { Name = i == 0 ? "LeftArm" : "RightArm", Position = new Vector3(side * .202f, 1.18f, 0) }; rig.Torso.AddChild(arm);
            // Bury the sleeve end inside the deforming shoulder instead of placing
            // a flat cap at the shoulder pivot, where it creates a visible ledge.
            Sculpt(arm,Loft("tailored-sleeve",new[]{new Vector4(.040f,0,.045f,.040f),new(.085f,0,.056f,.051f),new(.17f,0,.053f,.049f),new(.24f,0,.047f,.044f),new(.263f,0,.043f,.042f)}),Vector3.Zero,ResidentMaterial(cloth),new(MathF.PI/2,0,0));
            var elbow = rig.Elbows[i] = new Node3D { Name = "Elbow", Position = new Vector3(0, -.28f, 0) }; arm.AddChild(elbow);
            Detail(elbow,"sphere",Vector3.Zero,new(.070f,.067f,.067f),skin);
            Sculpt(elbow,Loft("resident-forearm",new[]{new Vector4(-.015f,0,.036f,.034f),new(.045f,-.003f,.039f,.035f),new(.10f,-.004f,.036f,.031f),new(.17f,-.003f,.029f,.026f),new(.24f,0,.028f,.026f)}),Vector3.Zero,ResidentMaterial(skin),new(MathF.PI/2,0,0));
            var hand = rig.Hands[i] = new Node3D { Name = "Hand", Position = new(0,-.255f,-.01f) }; elbow.AddChild(hand);
            rig.HandSkins[i]=Sculpt(hand,HandPalm((int)side),Vector3.Zero,ResidentMaterial(skin));
            for(int digit=0;digit<4;digit++)
            {
                var finger=rig.Fingers[i,digit]=new Node3D {Name="Finger"+digit,Position=new(-.022f+digit*.014f,-.04f,-.005f)}; hand.AddChild(finger);
                float lengthScale=digit==0?.92f:digit==1?1:digit==2?.96f:.86f;
                float proximal=.026f*lengthScale,middle=.018f*lengthScale,distal=.014f*lengthScale;
                var mid=rig.FingerMiddles[i,digit]=new Node3D {Name="Middle",Position=new(0,-proximal,0)};finger.AddChild(mid);
                var tip=rig.Fingertips[i,digit]=new Node3D {Name="Tip",Position=new(0,-middle,0)};mid.AddChild(tip);
            }
            var thumb=rig.Thumbs[i]=new Node3D {Name="Thumb",Position=new(side*.034f,-.021f,.001f)};hand.AddChild(thumb);
            var thumbTip=rig.ThumbTips[i]=new Node3D {Name="ThumbTip",Position=new(0,-.018f,0)};thumb.AddChild(thumbTip);
            rig.HandOpen[i]=FingerProxy(skin,false);rig.HandClosed[i]=FingerProxy(skin,true);
            rig.HandProxies[i]=new MeshInstance3D {Name="DistantFingers",Mesh=rig.HandOpen[i],Visible=false};hand.AddChild(rig.HandProxies[i]);
            Sculpt(arm,RolledCuff(),Vector3.Zero,CollarMaterial(cloth));
            var leg = rig.Legs[i] = new Node3D { Name = i == 0 ? "LeftLeg" : "RightLeg", Position = new Vector3(side * .10f, .67f, 0) }; rig.AddChild(leg);
            Sculpt(leg,Loft("resident-trouser",new[]{new Vector4(-.01f,0,.065f,.058f),new(.08f,0,.077f,.073f),new(.20f,0,.070f,.060f),new(.33f,0,.051f,.05f)}),Vector3.Zero,ResidentMaterial("#645e53"),new(MathF.PI/2,0,0));
            var knee = rig.Knees[i] = new Node3D { Name = "Knee", Position = new Vector3(0, -.31f, 0) }; leg.AddChild(knee);
            Sculpt(knee,Loft("resident-boot",new[]{new Vector4(0,0,.055f,.052f),new(.07f,0,.060f,.058f),new(.19f,0,.05f,.048f),new(.28f,-.012f,.052f,.051f)}),Vector3.Zero,LeatherSurface("#514237"),new(MathF.PI/2,0,0));
            Sculpt(knee,TurnedBootRim(),Vector3.Zero,LeatherSurface("#514237"));
            var foot=rig.Feet[i]=new Node3D {Name="Foot",Position=new(0,-.295f,-.04f)};knee.AddChild(foot);
            Sculpt(foot,BootLast(false),Vector3.Zero,LeatherSurface("#423a32"));
            Sculpt(foot,BootLast(true),Vector3.Zero,LeatherSurface("#302e28"));
            for(int lace=0;lace<4;lace++)
            {
                float z=.006f-lace*.021f,y=.048f-lace*.005f;
                FineBeam(foot,new(-.025f,y,z),new(.025f,y-.004f,z-.018f),.003f,"#a48d6d");
                FineBeam(foot,new(.025f,y,z),new(-.025f,y-.004f,z-.018f),.003f,"#a48d6d");
            }
        }
        AddResidentEquipment(rig, job);
        AddResidentTailoring(rig, cloth, skin, job);
        MergeResidentParts(rig.Head,"resident:head:"+skin+hair+job+":"+_faceVariant);
        MergeResidentParts(rig, "resident:" + cloth + skin + hair + job+":"+_garmentCut);
        rig.BodySkin=rig.Torso.GetChild<MeshInstance3D>(rig.Torso.GetChildCount()-1);
        AddShoulderBridges(rig,cloth,skin);
        rig.HeadSkin=rig.Head.GetChild<MeshInstance3D>(rig.Head.GetChildCount()-1);
        AddResidentFace(rig,skin);
        rig.HeadProxy=new MeshInstance3D {Name="DistantHead",Mesh=DistantHead(skin,hair),Visible=false};rig.Head.AddChild(rig.HeadProxy);
        AddHandSkin(rig,skin);
        rig.Head.Scale=new Vector3(.96f+((variation>>15)%4)*.025f,.98f+((variation>>18)%3)*.025f,1);
        if (child) { rig.Scale = new Vector3(.72f, .65f, .72f); rig.Head.Scale = Vector3.One * 1.15f; }
        else { rig.Scale = new Vector3(1 + ((variation >> 8) % 5 - 2f) * .025f, 1.055f + ((variation >> 12) % 5) * .015f, 1); }
        rig.RegisterDistanceMeshes(rig);return rig;
    }
    public void ValidateResidentMeshes()
    {
        foreach (var key in new[]{"belt","apron","armor","placket","shoulder-front","shoulder-back"})
        {
            bool back=key=="shoulder-back";
            var mesh=FittedRibbon(key,key=="belt"?.748f:key=="armor"?.91f:key=="apron"?.79f:key=="placket"?1.03f:.81f,key=="belt"?.775f:key=="armor"||key=="apron"?1.17f:key=="placket"?1.205f:1.20f,back);
            var arrays=mesh.SurfaceGetArrays(0);var vertices=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            for(int i=0;i<vertices.Length;i++)
            {
                var p=vertices[i];bool rear=key=="belt"?p.Z>0:back;
                float gap=MathF.Abs(p.Z-TorsoSurface(p.X,p.Y,rear,0).Z);
                if(gap<.001f || gap>.012f || !normals[i].IsFinite())
                    throw new InvalidOperationException("Clothing layer floats or intersects garment: "+key);
            }
        }
        foreach (JobType job in Enum.GetValues<JobType>()) foreach (bool child in new[] { false, true })
        {
            var resident = Resident(42, child, job);
            resident.ValidateDistanceMeshes();
            var body = resident.BodySkin.Mesh;
            // Losing the non-indexed garment leaves only collar/fold strips in the cloth surface.
            if (body.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length < 50)
            { throw new InvalidOperationException("Resident garment disappeared during mesh consolidation"); }
            if (job is JobType.Farmer or JobType.Soldier)
            {
                if (resident.Headwear?.GetParent() != resident.Head) throw new InvalidOperationException("Headwear detached from head joint");
                var hat = HeadwearShell(job == JobType.Soldier).GetAabb();
                if (hat.Position.Y > .04f || hat.End.Y < .13f) throw new InvalidOperationException("Headwear does not enclose scalp");
            }
            bool tintPreserved=false;
            for(int surface=0;surface<resident.HeadSkin.Mesh.GetSurfaceCount();surface++)
            {
                var arrays=resident.HeadSkin.Mesh.SurfaceGetArrays(surface);
                if(arrays[(int)Mesh.ArrayType.Color].VariantType==Variant.Type.Nil)continue;
                foreach(var color in arrays[(int)Mesh.ArrayType.Color].AsColorArray())if(color.G<.975f)tintPreserved=true;
            }
            if(!tintPreserved)throw new InvalidOperationException("Facial tint lost during mesh consolidation");
            if(resident.HandSkins[1].MaterialOverride!=null||resident.HandSkins[1].Mesh.GetSurfaceCount()!=2)throw new InvalidOperationException("Nails lost their skin binding or material");
            if(resident.Grip?.GetParent()!=resident.Hands[1]) throw new InvalidOperationException("Tool grip bypasses wrist");
            foreach(var pose in new[]{(true,false,false),(false,true,false),(false,false,true)})
            {
                resident.Pose(.2f,pose.Item1,pose.Item2,pose.Item3,false,0);
                if(!resident.Hands[1].Transform.IsFinite() || !resident.Knees[0].Transform.IsFinite()) throw new InvalidOperationException("Invalid wrist pose");
            }
            resident.Free();
        }
        ValidateHandling();
        GD.Print("RESIDENT_FIT_PASS: scalp enclosure, garment gaps, wrists and fingers across jobs/ages");
    }
}
