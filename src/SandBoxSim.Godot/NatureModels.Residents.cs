using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private int _garmentCut;
    private const float PlacketBottom=1.03f,PlacketTop=1.267f;
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
    // Shared by source sampling, clothing attachments and distant meshes.
    // Keep profiles outside the point sampler to avoid per-vertex allocations.
    private static readonly float[][] GarmentHeightProfiles={
        new[]{.60f,.635f,.73f,.82f,.97f,1.10f,1.19f,1.24f,1.28f},
        new[]{.63f,.665f,.73f,.82f,.97f,1.10f,1.19f,1.24f,1.28f},
        new[]{.535f,.570f,.73f,.82f,.97f,1.10f,1.19f,1.24f,1.28f}};
    private static readonly float[][] GarmentWidthProfiles={
        new[]{.180f,.184f,.168f,.153f,.158f,.165f,.171f,.145f,.067f},
        new[]{.173f,.178f,.168f,.153f,.158f,.165f,.171f,.145f,.067f},
        new[]{.184f,.188f,.171f,.157f,.161f,.168f,.174f,.147f,.067f}};
    private static readonly float[][] GarmentDepthProfiles={
        new[]{.114f,.118f,.108f,.101f,.108f,.111f,.103f,.084f,.084f},
        new[]{.114f,.118f,.108f,.101f,.108f,.111f,.103f,.084f,.084f},
        new[]{.128f,.132f,.113f,.105f,.111f,.115f,.106f,.085f,.084f}};
    private Vector3 GarmentPoint(float fraction,float angle)
    {
        var height=GarmentHeightProfiles[_garmentCut];
        var width=GarmentWidthProfiles[_garmentCut];
        var depth=GarmentDepthProfiles[_garmentCut];
        float y=Mathf.Lerp(height[0],height[^1],fraction);
        float Profile(float[] values)
        {
            int i=0;while(i<height.Length-2 && y>height[i+1])i++;
            float t=(y-height[i])/(height[i+1]-height[i]);int a=Math.Max(0,i-1),b=Math.Min(height.Length-1,i+2);
            float m0=(values[i+1]-values[a])/(height[i+1]-height[a])*(height[i+1]-height[i]);
            float m1=(values[b]-values[i])/(height[b]-height[i])*(height[i+1]-height[i]);
            float value=(2*t*t*t-3*t*t+1)*values[i]+(t*t*t-2*t*t+t)*m0+(-2*t*t*t+3*t*t)*values[i+1]+(t*t*t-t*t)*m1;
            return value;
        }
        float belt=1-MathF.Exp(-MathF.Pow((y-.765f)*22,2));
        float hem=Math.Clamp((.80f-y)/(.80f-GarmentBottom),0,1);
        float front=MathF.Pow(MathF.Max(0,-MathF.Sin(angle)),4);
        float tension=MathF.Exp(-MathF.Pow((y-.82f)/.08f,2));
        float fold=1+hem*belt*(.030f*MathF.Cos(angle*3+(y-.6f)*5)+.018f*MathF.Cos(angle*7-.4f)+.009f*MathF.Cos(angle*13+y*7));
        // Cloth gathers below the belt; diagonal compression folds radiate from it.
        fold+=front*tension*.025f*MathF.Sin((y-.76f)*70+MathF.Abs(MathF.Cos(angle))*10);
        // Tension follows the two armholes and belt, rather than forming
        // evenly spaced ribs over the entire chest.
        float armhole=MathF.Exp(-MathF.Pow((y-1.145f)/.10f,2))*MathF.Pow(MathF.Abs(MathF.Cos(angle)),3);
        fold+=armhole*.016f*MathF.Sin(y*65+MathF.Sin(angle)*5);
        // A few diagonal tension folds terminate at the belt and armhole.
        // Millimetre-scale displacement is sampled into the sewn surface;
        // attachments use this same sampler, so the belt cannot float above it.
        float waist=MathF.Exp(-MathF.Pow((y-.90f)/.095f,2));
        float diagonal=MathF.Sin((y-.81f)*58+MathF.Cos(angle)*6.0f+.7f);
        fold+=waist*front*.027f*diagonal;
        float backPleat=MathF.Pow(MathF.Max(0,MathF.Sin(angle)),6)*MathF.Exp(-MathF.Pow((y-1.05f)/.14f,2));
        fold+=backPleat*.018f*MathF.Cos(angle*11+y*16);
        // The shirt rests on a sternum and shoulder blades rather than an
        // inflated circular trunk; fabric falls away from those support points.
        float shoulderBlade=MathF.Exp(-MathF.Pow((y-1.15f)/.07f,2))*MathF.Pow(MathF.Max(0,MathF.Sin(angle)),4);
        float sternum=MathF.Exp(-MathF.Pow((y-1.115f)/.10f,2))*MathF.Pow(MathF.Max(0,-MathF.Sin(angle)),12);
        float vent=_garmentCut==2?hem*(.075f*MathF.Pow(MathF.Max(0,-MathF.Sin(angle)),16)+.040f*MathF.Pow(MathF.Max(0,MathF.Sin(angle)),16)):0;
        float chest=MathF.Exp(-MathF.Pow((y-1.10f)/.10f,2));
        float volume=chest*front*.002f+shoulderBlade*.003f-sternum*.003f;
        float neckline=MathF.Exp(-MathF.Pow((y-1.28f)/.022f,2))*front*.009f;
        return new Vector3(MathF.Sign(MathF.Cos(angle))*MathF.Pow(MathF.Abs(MathF.Cos(angle)),.96f)*Profile(width)*fold,y+vent-neckline,MathF.Sign(MathF.Sin(angle))*MathF.Pow(MathF.Abs(MathF.Sin(angle)),.96f)*(Profile(depth)*fold+volume));
    }
    private Mesh Garment()
    {
        string key="tunic:"+_garmentCut;
        if (_meshes.TryGetValue(key, out var existing)) { return existing; }
        const int sides = 64, rings=40;
        var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles); surface.SetSmoothGroup(0);
        Vector3 Point(int ring,int side)=>GarmentPoint(ring/(float)rings,side*MathF.Tau/sides);
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
        _faceVariant=(int)((variation>>8)%8)+(child?8:0);_facePoints=null;_faceIndices=null;
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
        Sculpt(rig.Torso,FittedRibbon("placket",PlacketBottom,PlacketTop),Vector3.Zero,CollarMaterial(cloth));
        for (int button = 0; button < 3; button++)
        {
            var anchor=TorsoSurface(.006f,1.237f-button*.067f,false,.0012f);
            Sculpt(rig.Torso,SewnButton(),anchor,ResidentMaterial("#514237"));
            FineBeam(rig.Torso,anchor+new Vector3(-.0014f,-.0010f,-.0038f),anchor+new Vector3(.0014f,.0010f,-.0038f),.00055f,"#a09380");
        }
        Sculpt(rig.Torso, FittedRibbon("belt", .748f, .775f), Vector3.Zero, ResidentMaterial("#514237"));
        var buckle=TorsoSurface(.015f,.7615f,false,.009f);
        var buckleCorners=new[]{buckle+new Vector3(-.0205f,-.0155f,0),buckle+new Vector3(.0205f,-.0155f,0),buckle+new Vector3(.0205f,.0155f,0),buckle+new Vector3(-.0205f,.0155f,0)};
        for(int edge=0;edge<4;edge++)FineBeam(rig.Torso,buckleCorners[edge],buckleCorners[(edge+1)%4],.004f,"#b8a17b");
        FineBeam(rig.Torso,buckle+new Vector3(-.006f,0,-.002f),buckle+new Vector3(.0205f,0,-.002f),.003f,"#b8a17b");
        Detail(rig.Torso, "capsule", new Vector3(.23f, .7f, .075f), new Vector3(.075f, .065f, .055f), "#725b40");
        // The attributed head already includes a continuous anatomical neck.
        // A second torso-mounted neck intersects it when the head turns.
        rig.Head = new Node3D { Name = "Head", Position = new Vector3(0, 1.382f, 0) }; rig.Torso.AddChild(rig.Head);
        Detail(rig.Head, "face", Vector3.Zero, new Vector3(.177f, .220f, .195f), skin).MaterialOverride=AnatomicalFaceMaterial(skin);
        Detail(rig.Head, "hair", new Vector3(0, .008f, 0), new Vector3(.181f, .224f, .200f), hair).MaterialOverride=AuthoredHairMaterial(hair);
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
            Sculpt(arm,Loft("tailored-sleeve",SleeveProfile()),Vector3.Zero,ResidentMaterial(cloth),new(MathF.PI/2,0,0));
            var elbow = rig.Elbows[i] = new Node3D { Name = "Elbow", Position = new Vector3(0, -.28f, 0) }; arm.AddChild(elbow);
            rig.ForearmProxies[i]=Sculpt(elbow,DistantForearm(i),Vector3.Zero,ResidentMaterial(skin));
            rig.ForearmProxies[i].Visible=false;
            var hand = rig.Hands[i] = new Node3D { Name = "Hand", Position = new(0,-.255f,-.01f) }; elbow.AddChild(hand);
            rig.HandSkins[i]=new MeshInstance3D {Name="AnatomicalHand"};hand.AddChild(rig.HandSkins[i]);
            for(int digit=0;digit<4;digit++)
            {
                var finger=rig.Fingers[i,digit]=new Node3D {Name="Finger"+digit,Position=ResidentRig.FingerRoot(i,digit)}; hand.AddChild(finger);
                float lengthScale=digit==0?.92f:digit==1?1:digit==2?.96f:.86f;
                float proximal=.026f*lengthScale,middle=.018f*lengthScale,distal=.014f*lengthScale;
                var mid=rig.FingerMiddles[i,digit]=new Node3D {Name="Middle",Position=new(0,-proximal,0)};finger.AddChild(mid);
                var tip=rig.Fingertips[i,digit]=new Node3D {Name="Tip",Position=new(0,-middle,0)};mid.AddChild(tip);
            }
            var thumb=rig.Thumbs[i]=new Node3D {Name="Thumb",Position=ResidentRig.ThumbRoot(i)};hand.AddChild(thumb);
            var thumbTip=rig.ThumbTips[i]=new Node3D {Name="ThumbTip",Position=new(0,-.018f,0)};thumb.AddChild(thumbTip);
            rig.HandOpen[i]=FingerProxy(skin,false,i);rig.HandClosed[i]=FingerProxy(skin,true,i);
            rig.HandProxies[i]=new MeshInstance3D {Name="DistantFingers",Mesh=rig.HandOpen[i],Visible=false};hand.AddChild(rig.HandProxies[i]);
            Sculpt(arm,RolledCuff(),Vector3.Zero,CollarMaterial(cloth));
            var leg = rig.Legs[i] = new Node3D { Name = i == 0 ? "LeftLeg" : "RightLeg", Position = new Vector3(side * .10f, .67f, 0) }; rig.AddChild(leg);
            Sculpt(leg,Loft("resident-trouser",new[]{new Vector4(-.01f,0,.064f,.059f),new(.04f,.004f,.074f,.066f),new(.10f,.004f,.076f,.068f),new(.17f,.001f,.069f,.060f),new(.23f,-.002f,.061f,.055f),new(.28f,-.004f,.055f,.052f),new(.33f,-.004f,.050f,.048f)}),Vector3.Zero,ResidentMaterial("#645e53"),new(MathF.PI/2,0,0));
            var knee = rig.Knees[i] = new Node3D { Name = "Knee", Position = new Vector3(0, -.31f, 0) }; leg.AddChild(knee);
            Sculpt(knee,Loft("resident-boot",new[]{new Vector4(0,0,.047f,.044f),new(.035f,0,.052f,.048f),new(.085f,-.002f,.057f,.052f),new(.15f,-.005f,.052f,.048f),new(.21f,-.010f,.045f,.045f),new(.25f,-.015f,.043f,.044f),new(.28f,-.012f,.047f,.048f)}),Vector3.Zero,LeatherSurface("#514237"),new(MathF.PI/2,0,0));
            Sculpt(knee,TurnedBootRim(),Vector3.Zero,LeatherSurface("#514237"));
            var foot=rig.Feet[i]=new Node3D {Name="Foot",Position=new(0,-.295f,-.04f)};knee.AddChild(foot);
            Sculpt(foot,BootLast(false),Vector3.Zero,LeatherSurface("#423a32"));
            Sculpt(foot,BootLast(true),Vector3.Zero,LeatherSurface("#302e28"));
            Sculpt(foot,BootLaces(),Vector3.Zero,ResidentMaterial("#8c7b66"));

        }
        AddResidentEquipment(rig, job);
        AddResidentTailoring(rig, cloth, skin, job);
        ApplyResidentProportions(rig);
        rig.HeadRest=rig.Head.Position;
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
        else
        {
            float frame=HumanFamily==2?.88f:.94f;
            rig.Scale = new Vector3(frame*(1 + ((variation >> 8) % 5 - 2f) * .025f), 1.005f + ((variation >> 12) % 5) * .012f, 1);
            rig.Head.Scale*=new Vector3(1/frame,1,1);
        }
        rig.RegisterDistanceMeshes(rig);return rig;
    }
    public void ValidateResidentMeshes()
    {
        ValidateContinuousGarments();
        var speaker=Resident(17,false,JobType.Builder);var listener=Resident(17,false,JobType.Builder);
        speaker.MouthMaterial.SetShaderParameter("speech",1f);
        if(ReferenceEquals(speaker.MouthMaterial,listener.MouthMaterial)||listener.MouthMaterial.GetShaderParameter("speech").AsSingle()!=0)
            throw new InvalidOperationException("Residents share facial animation material state");
        speaker.LidMaterial.SetShaderParameter("blink",1f);
        if(ReferenceEquals(speaker.LidMaterial,listener.LidMaterial)||listener.LidMaterial.GetShaderParameter("blink").AsSingle()!=0)
            throw new InvalidOperationException("Residents share eyelid animation state");
        float mergedClosure=0;
        for(int surface=0;surface<speaker.HeadSkin.Mesh.GetSurfaceCount();surface++)
        {
            if(!speaker.HeadSkin.Mesh.SurfaceGetMaterial(surface).HasMeta("UsesSkinAtlas"))continue;
            var arrays=speaker.HeadSkin.Mesh.SurfaceGetArrays(surface);
            var deltas=arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
            foreach(var delta in deltas)mergedClosure=MathF.Max(mergedClosure,-delta.X);
        }
        if(mergedClosure<.006f||mergedClosure>.015f)throw new InvalidOperationException("Head merge lost the metric eyelid morph");
        speaker.Free();listener.Free();
        foreach (var key in new[]{"belt","apron","armor","placket","shoulder-front","shoulder-back"})
        {
            bool back=key=="shoulder-back";
            var mesh=FittedRibbon(key,key=="belt"?.748f:key=="armor"?.91f:key=="apron"?.79f:key=="placket"?PlacketBottom:.81f,key=="belt"?.775f:key=="armor"||key=="apron"?1.17f:key=="placket"?PlacketTop:1.20f,back);
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
            // The boot stays grounded while the pelvis is raised; arms retain
            // their original segment lengths and the shoulder bind follows them.
            if(MathF.Abs(resident.Legs[0].Position.Y+resident.Knees[0].Position.Y+resident.Feet[0].Position.Y-.065f)>.001f
                ||resident.Head.Position.Y-resident.Arms[0].Position.Y<.19f
                ||resident.Legs[0].Position.Y<resident.Head.Position.Y*.55f)
                throw new InvalidOperationException("Resident proportions detach feet, neck or pelvis");
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
            bool skinTangents=false,hairAtlas=false;
            for(int surface=0;surface<resident.HeadSkin.Mesh.GetSurfaceCount();surface++)
            {
                var arrays=resident.HeadSkin.Mesh.SurfaceGetArrays(surface);
                var material=resident.HeadSkin.Mesh.SurfaceGetMaterial(surface);
                if(material.HasMeta("UsesSkinAtlas"))
                {
                    var tangent=arrays[(int)Mesh.ArrayType.Tangent].AsFloat32Array();
                    if(tangent.Length!=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length*4)
                        throw new InvalidOperationException("Skin UV atlas lost its tangent frame");
                    foreach(float v in tangent)if(!float.IsFinite(v))throw new InvalidOperationException("Invalid sculpt tangent frame");
                    skinTangents=true;
                }
                if(material.HasMeta("UsesHairAtlas"))hairAtlas=true;
                if(arrays[(int)Mesh.ArrayType.Color].VariantType==Variant.Type.Nil)continue;
                foreach(var color in arrays[(int)Mesh.ArrayType.Color].AsColorArray())
                {
                    if(color.G>.5f)tintPreserved=true;

                }
            }
            if(!tintPreserved)throw new InvalidOperationException("Facial tint lost during mesh consolidation");
            if(!skinTangents||!hairAtlas)throw new InvalidOperationException("Head consolidation erased skin UV tangents or hair atlas");
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
