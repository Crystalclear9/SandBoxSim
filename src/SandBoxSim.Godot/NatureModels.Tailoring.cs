using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    private const int NeckOpeningSegments=64;

    private Mesh SewnButton()=>Loft("sewn-button",new[]{
        new Vector4(-.0038f,0,.0001f,.0001f),new(-.0032f,0,.0038f,.0038f),
        new(-.0020f,0,.0048f,.0048f),new(0,0,.0048f,.0048f),
        new(.0010f,0,.0038f,.0038f),new(.0012f,0,.0001f,.0001f)},24);
    private Mesh FabricFacing(string key,Vector3[] profile)
    {
        if(_meshes.TryGetValue(key,out var cached))return cached;
        const int sides=48;
        using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);surface.SetSmoothGroup(0);
        var center=Vector3.Zero;foreach(var p in profile)center+=p;center/=profile.Length;
        Vector3 Point(int row,int column)
        {
            float v=row/4f;int i=(int)v%profile.Length;float t=v-MathF.Floor(v);
            var previous=profile[(i+profile.Length-1)%profile.Length];var start=profile[i];
            var end=profile[(i+1)%profile.Length];var next=profile[(i+2)%profile.Length];
            var p=start.CubicInterpolate(end,previous,next,t);float a=column*MathF.Tau/sides;
            float neckline=key=="turned-neckline"?.009f*MathF.Pow(MathF.Max(0,-MathF.Sin(a)),4):0;
            float throat=key=="turned-neckline"?.021f*MathF.Pow(MathF.Max(0,-MathF.Sin(a)),4):0;
            return new(MathF.Cos(a)*p.X,p.Y-neckline,MathF.Sin(a)*p.Z-throat);
        }
        void Triangle(int ra,int ca,int rb,int cb,int rc,int cc)
        {
            var a=Point(ra,ca);var b=Point(rb,cb);var c=Point(rc,cc);
            Vector3 Outward(int row,int column)
            {
                float angle=column*MathF.Tau/sides;
                return Point(row,column)-new Vector3(MathF.Cos(angle)*center.X,center.Y,MathF.Sin(angle)*center.Z);
            }
            EquipmentTriangle(surface,a,b,c,Outward(ra,ca)+Outward(rb,cb)+Outward(rc,cc));
        }
        for(int row=0;row<profile.Length*4;row++)for(int col=0;col<sides;col++)
        {Triangle(row,col,row+1,col,row,col+1);Triangle(row,col+1,row+1,col,row+1,col+1);}
        surface.GenerateNormals();surface.Index();var mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }
    private Mesh RolledCuff()=>FittedFigure
        // One compact turn over the sewn sleeve edge, without the second
        // outer ridge that made the narrow adult sleeve look like two cuffs.
        ?FabricFacing("female-turned-cuff",new[]{
            new Vector3(.045f,-.270f,.043f),new(.0475f,-.267f,.0458f),new(.0488f,-.262f,.0468f),
            new(.0476f,-.254f,.0455f),new(.0455f,-.246f,.0438f),new(.0440f,-.2405f,.0424f),
            new(.0435f,-.244f,.0415f),new(.0435f,-.266f,.0415f)})
        :FabricFacing("rolled-cuff",new[]{
            new Vector3(.045f,-.270f,.043f),new(.048f,-.266f,.046f),new(.049f,-.260f,.047f),
            new(.047f,-.254f,.045f),new(.048f,-.248f,.046f),new(.047f,-.241f,.045f),
            new(.0435f,-.242f,.0415f),new(.0435f,-.266f,.0415f)});
    private Mesh TurnedBootRim()
    {
        return FittedFigure?FabricFacing("contoured-boot-rim",new[]{
            new Vector3(.0478f,-.034f,.0451f),new(.0490f,-.030f,.0462f),new(.0488f,-.023f,.0460f),
            new(.0473f,-.018f,.0445f),new(.0455f,-.019f,.0428f),new(.0457f,-.031f,.0431f)})
            :FabricFacing("turned-boot-rim:False",new[]{
            new Vector3(.050f,-.047f,.047f),new(.053f,-.039f,.050f),new(.051f,-.028f,.048f),
            new(.047f,-.024f,.044f),new(.045f,-.028f,.042f),new(.046f,-.042f,.043f)});
    }
    private Mesh TurnedNeckline()
    {
        string key="sewn-neck-binding:"+_garmentCut;
        if(_meshes.TryGetValue(key,out var cached))return cached;
        // A narrow folded facing follows the actual shirt opening. The old
        // independently sized elliptical ring overhung the front and sides.
        // This closed cross-section has a short opening at the placket;
        // neither its end caps nor its inner wall span the wearer's throat.
        const int sides=96;
        var section=new Vector2[]{new(0,-.003f),new(.0018f,-.0015f),new(.0024f,.002f),
            new(.0014f,.005f),new(-.0008f,.0052f),new(-.0014f,.001f)};
        using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);surface.SetSmoothGroup(0);
        Vector3 Point(int row,int column)
        {
            float angle=-MathF.PI/2+.045f+(MathF.Tau-.090f)*column/sides;
            var seam=GarmentPoint(1,angle);var radial=new Vector3(MathF.Cos(angle),0,MathF.Sin(angle));
            var profile=section[row%section.Length];
            return seam+radial*profile.X+Vector3.Up*profile.Y;
        }
        for(int row=0;row<section.Length;row++)for(int col=0;col<sides;col++)
        {
            var a=Point(row,col);var b=Point(row+1,col);var c=Point(row,col+1);var d=Point(row+1,col+1);
            var outward=((a+b+c+d)*.25f-(GarmentPoint(1,-MathF.PI/2+.045f+(MathF.Tau-.090f)*(col+.5f)/sides)+Vector3.Up*.001f)).Normalized();
            EquipmentTriangle(surface,a,b,c,outward);EquipmentTriangle(surface,c,b,d,outward);
        }
        foreach(int col in new[]{0,sides})
        {
            Vector3 center=Vector3.Zero;for(int row=0;row<section.Length;row++)center+=Point(row,col);center/=section.Length;
            for(int row=0;row<section.Length;row++)EquipmentTriangle(surface,center,Point(row,col),Point(row+1,col),col==0?Vector3.Left:Vector3.Right);
        }
        surface.GenerateNormals();surface.Index();var mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }
    private static List<(int From,int To)> NeckOpeningEdges(Vector3[] points,int[] indices)
    {
        var exposed=new Dictionary<(int,int),(int,int)>();
        void Count(int a,int b)
        {
            if(MathF.Min(points[a].Y,points[b].Y)<1.25f||MathF.Max(MathF.Abs(points[a].X),MathF.Abs(points[b].X))>.10f)return;
            var key=a<b?(a,b):(b,a);
            if(!exposed.Remove(key))exposed.Add(key,(a,b));
        }
        for(int i=0;i<indices.Length;i+=3)
        {
            Count(indices[i],indices[i+1]);
            Count(indices[i+1],indices[i+2]);
            Count(indices[i+2],indices[i]);
        }
        return new(exposed.Values);
    }

    private const float FemaleNeckFacingDepth=.0045f;
    private const float FemaleNeckRollRise=.00035f;
    private float FemaleNecklineHeight(float angle)
    {
        float depth=MathF.Sin(angle);
        float front=MathF.Max(0,-depth),rear=MathF.Max(0,depth);
        float cut=_garmentCut==2?.004f:_garmentCut==1?.001f:0;
        return 1.281f-.0035f*front*front+.009f*rear*rear+cut*front;
    }

    private void ShapeFemaleNeckline(Vector3[] points,int[] indices,Vector2[] pattern)
    {
        // Build the collar slope from the actual sewn opening. Let the near
        // yoke share that change instead of bending just the boundary ring.
        var opening=NeckOpeningEdges(points,indices);
        if(opening.Count!=NeckOpeningSegments)throw new InvalidOperationException("Cannot shape an incomplete neck opening");
        var rim=new (float Angle,float Radius,float Height,float Offset)[opening.Count];
        for(int i=0;i<rim.Length;i++)
        {
            var point=points[opening[i].From];
            float angle=MathF.Atan2(point.Z,point.X);
            rim[i]=(angle,new Vector2(point.X,point.Z).Length(),point.Y,FemaleNecklineHeight(angle)-point.Y);
        }
        Array.Sort(rim,(a,b)=>a.Angle.CompareTo(b.Angle));
        // Round the diagonal quarters of the actual opening. The sewn ring
        // is not evenly spaced in angle after fitting, so sample the profile
        // at fixed angular offsets rather than averaging vertex indices.
        (int Lo,int Hi,float Blend) RimSpan(float angle)
        {
            angle=Mathf.Wrap(angle,-Mathf.Pi,Mathf.Pi);
            int next=0;
            while(next<rim.Length&&rim[next].Angle<angle)next++;
            int lo=(next+rim.Length-1)%rim.Length,hi=next%rim.Length;
            float a=rim[lo].Angle,b=rim[hi].Angle;
            return (lo,hi,Mathf.PosMod(angle-a,Mathf.Tau)/Mathf.PosMod(b-a,Mathf.Tau));
        }
        float RadiusAt(float angle)
        {
            var (lo,hi,t)=RimSpan(angle);
            return Mathf.Lerp(rim[lo].Radius,rim[hi].Radius,t);
        }
        var curve=new float[rim.Length];
        for(int i=0;i<rim.Length;i++)
        {
            float angle=rim[i].Angle;
            float averaged=.40f*rim[i].Radius+.22f*(RadiusAt(angle-.19f)+RadiusAt(angle+.19f))
                +.08f*(RadiusAt(angle-.38f)+RadiusAt(angle+.38f));
            curve[i]=Math.Clamp(averaged-rim[i].Radius,-.003f,.0045f);
        }
        var rest=(Vector3[])points.Clone();
        var clavicularPanel=new float[points.Length];
        for(int i=0;i<points.Length;i++)
        {
            var point=points[i];
            if(pattern[i].X>=0||point.Y<1.22f)continue;
            float angle=MathF.Atan2(point.Z,point.X);
            var (lo,hi,t)=RimSpan(angle);
            var a=rim[lo];var b=rim[hi];
            float height=Mathf.Lerp(a.Height,b.Height,t);
            float radius=Mathf.Lerp(a.Radius,b.Radius,t);
            var radial=new Vector2(point.X,point.Z);
            float distance=new Vector2(point.Y-height,radial.Length()-radius).Length();
            float follow=1-TorsoRamp(.004f,.073f,distance);
            var ray=radial.Normalized();
            float front=MathF.Max(0,-ray.Y);
            // The collar eases back towards the throat. A flatter taper
            // under the front edge replaces the last standing lip.
            float side=ray.X*ray.X;
            float rolledInset=.0017f+.0023f*front*front+.0022f*side;
            var inset=ray*(rolledInset-Mathf.Lerp(curve[lo],curve[hi],t))*follow;
            float rimOffset=Mathf.Lerp(a.Offset,b.Offset,t);
            float spread=MathF.Max(0,radial.Length()-radius);
            var fitted=point+new Vector3(-inset.X,rimOffset*follow,-inset.Y);
            // Give the shoulder-facing cloth a graded fall from the sewn rim.
            // Soften the high spots of the yoke without flattening the lower
            // cloth, changing the opening itself, or lowering the placket.
            // Continue the fall across the whole shoulder-facing panel.
            // Ending the blend halfway to the sleeve left a flat outer shelf.
            float yoke=TorsoRamp(.002f,.015f,spread)*(1-TorsoRamp(.080f,.125f,spread))*(1-front*front);
            float fall=height+rimOffset-spread*Mathf.Lerp(.24f,.52f,side);
            float excess=MathF.Max(0,fitted.Y-fall);
            fitted.Y-=MathF.Min(.012f,excess)*yoke;
            points[i]=fitted;
            float lateral=MathF.Abs(point.X);
            // Round the cloth below the sides of the opening. Keep the rim
            // itself and the far shoulder as the two fixed supports.
            clavicularPanel[i]=TorsoRamp(.005f,.017f,distance)*(1-TorsoRamp(.048f,.078f,distance))
                *TorsoRamp(.034f,.063f,lateral)*(1-TorsoRamp(.140f,.185f,lateral))
                *TorsoRamp(1.221f,1.249f,point.Y);
        }
        for(int i=0;i<points.Length;i++)
        {
            // Round the shared shoulder crest on both sides of the sewn seam.
            // Fairing the torso alone left the adjacent sleeve cap standing up.
            var point=rest[i];
            float crest=TorsoRamp(.080f,.115f,MathF.Abs(point.X))
                *(1-TorsoRamp(.180f,.225f,MathF.Abs(point.X)))
                *TorsoRamp(1.215f,1.248f,point.Y)
                *(1-TorsoRamp(.060f,.125f,MathF.Max(0,pattern[i].X)));
            clavicularPanel[i]=MathF.Max(clavicularPanel[i],crest);
        }
        FairRestSurface(points,indices,clavicularPanel,.007f,roundRelief:true);
        foreach(var edge in opening)
        {
            var point=points[edge.From];
            if(MathF.Abs(point.Y-FemaleNecklineHeight(MathF.Atan2(point.Z,point.X)))>.0002f)
                throw new InvalidOperationException("Sewn rim does not follow its continuous collar slope");
        }
        for(int i=0;i<indices.Length;i+=3)
        {
            int a=indices[i],b=indices[i+1],c=indices[i+2];
            var before=(rest[b]-rest[a]).Cross(rest[c]-rest[a]);
            var after=(points[b]-points[a]).Cross(points[c]-points[a]);
            if(before.LengthSquared()>1e-20f&&before.Dot(after)<=0)
                throw new InvalidOperationException("Shaped collar reverses a sewn triangle");
        }
    }

    private static void FoldFemaleNeckline(Godot.Collections.Array arrays,ref Vector3[] points,ref Vector2[] pattern)
    {
        var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        var edges=NeckOpeningEdges(points,indices);
        var rim=edges.ConvertAll(edge=>edge.From);
        rim.Sort();
        if(rim.Count!=NeckOpeningSegments||new HashSet<int>(rim).Count!=rim.Count)
            throw new InvalidOperationException("Female garment lost the single sewn throat opening");
        int outerCount=points.Length,innerCount=outerCount+2*rim.Count;
        var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
        var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
        Array.Resize(ref points,innerCount);
        Array.Resize(ref pattern,innerCount);
        Array.Resize(ref bones,innerCount*4);
        Array.Resize(ref weights,innerCount*4);
        var returned=new Dictionary<int,int>(rim.Count);
        for(int i=0;i<rim.Count;i++)
        {
            int a=rim[i],b=outerCount+i,c=b+rim.Count;
            var point=points[a];
            var inward=new Vector3(-point.X,0,-point.Z).Normalized();
            points[b]=point+inward*.0022f+Vector3.Up*FemaleNeckRollRise;
            points[c]=point+inward*.0038f-Vector3.Up*FemaleNeckFacingDepth;
            pattern[b]=pattern[c]=pattern[a];
            // The turn moves with the neck opening, clear of the arm-driven
            // clavicle panel. Pin the outer seam and both layers together.
            Array.Clear(bones,a*4,4);
            Array.Clear(weights,a*4,4);
            weights[a*4]=1;
            Array.Copy(bones,a*4,bones,b*4,4);
            Array.Copy(bones,a*4,bones,c*4,4);
            Array.Copy(weights,a*4,weights,b*4,4);
            Array.Copy(weights,a*4,weights,c*4,4);
            returned.Add(a,b);
        }
        var sewn=new List<int>(indices.Length+12*rim.Count);
        sewn.AddRange(indices);
        foreach(var (a,b) in edges)
        {
            int aFold=returned[a],bFold=returned[b];
            sewn.AddRange(new[]{b,a,aFold,b,aFold,bFold,
                bFold,aFold,aFold+rim.Count,bFold,aFold+rim.Count,bFold+rim.Count});
        }
        arrays[(int)Mesh.ArrayType.Index]=sewn.ToArray();
        arrays[(int)Mesh.ArrayType.Bones]=bones;
        arrays[(int)Mesh.ArrayType.Weights]=weights;
    }

    private static void ValidateFoldedNeckline(ResidentRig male,ResidentRig female)
    {
        var cloth=female.DetailedBodyMesh.SurfaceGetArrays(0);
        var p=cloth[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var indices=cloth[(int)Mesh.ArrayType.Index].AsInt32Array();
        int original=male.DetailedBodyMesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length;
        int count=(p.Length-original)/2;
        var opening=NeckOpeningEdges(p,indices);
        if(count!=NeckOpeningSegments||opening.Count!=count||opening.Exists(e=>e.From<original+count||e.To<original+count))
            throw new InvalidOperationException("Folded neckline has a detached facing or a sealed throat");
        var weights=cloth[(int)Mesh.ArrayType.Weights].AsFloat32Array();
        var bones=cloth[(int)Mesh.ArrayType.Bones].AsInt32Array();
        for(int i=original;i<original+count;i++)
        {
            int inner=i+count;
            if(MathF.Abs(p[i].Y-p[inner].Y-(FemaleNeckFacingDepth+FemaleNeckRollRise))>.00001f)
                throw new InvalidOperationException("Folded neckline loses its turned-in depth");
            if(bones[i*4]!=0||weights[i*4]!=1)
                throw new InvalidOperationException("Folded neckline responds to arm movement");
            for(int j=0;j<4;j++)
                if(weights[i*4+j]!=weights[inner*4+j]||bones[i*4+j]!=bones[inner*4+j])
                    throw new InvalidOperationException("The two sides of the neckline fold use different skin weights");
        }
        GD.Print($"FOLDED_NECKLINE_PASS: {count} joined edge pairs, one open throat, shared skin weights and rounded return in the continuous garment");
    }

    private static int PlacketButtonCount(int cut)=>cut==2?5:cut==0?2:3;
    private static float PlacketButtonHeight(int cut,int row)=>1.258f-row*(cut==2?.046f:cut==0?.039f:.034f);

    private static void ValidateFittedPlacket(ResidentRig rig,int cut,Vector3[] points,int[] indices,
        Vector3[]? posedCloth=null,Transform3D[]? pose=null)
    {
        var data=rig.PlacketButtons!.Mesh.SurfaceGetArrays(0);
        var buttons=data[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var bones=data[(int)Mesh.ArrayType.Bones].AsInt32Array();
        var weights=data[(int)Mesh.ArrayType.Weights].AsFloat32Array();
        for(int row=0;row<PlacketButtonCount(cut);row++)
        {
            float y=PlacketButtonHeight(cut,row),front=float.NegativeInfinity,back=float.PositiveInfinity;
            if(pose!=null&&ResidentRig.PelvicClothWeight(y)==0)continue;
            for(int i=0;i<buttons.Length;i++)
            {
                var point=buttons[i];
                if(MathF.Abs(point.Y-y)>.005f)continue;
                float z=GarmentDepth(points,indices,point.X,point.Y,false,out int triangle,out var coordinates);
                float gap=z-point.Z;
                if(pose!=null&&posedCloth!=null&&triangle>=0)
                {
                    int a=indices[triangle],b=indices[triangle+1],c=indices[triangle+2];
                    var restNormal=(points[b]-points[a]).Cross(points[c]-points[a]);
                    var movedNormal=(posedCloth[b]-posedCloth[a]).Cross(posedCloth[c]-posedCloth[a]).Normalized();
                    if(restNormal.Z>0)movedNormal=-movedNormal;
                    var panel=posedCloth[a]*coordinates.X+posedCloth[b]*coordinates.Y+posedCloth[c]*coordinates.Z;
                    point=Vector3.Zero;
                    for(int j=0;j<4;j++)point+=pose[bones[i*4+j]]*buttons[i]*weights[i*4+j];
                    gap=movedNormal.Dot(point-panel);
                }
                if(!float.IsFinite(gap)||MathF.Abs(gap)>.008f)
                    throw new InvalidOperationException($"Fitted placket button loses front-panel contact: cut={cut}, row={row}, y={point.Y:F3}, gap={gap:F4}");
                front=MathF.Max(front,gap);
                back=MathF.Min(back,gap);
            }
            if(front<.0005f||back>-.0005f)throw new InvalidOperationException("Fitted placket button has no visible face or embedded base");
        }
        if(rig.DetailedBodyMesh.SurfaceGetMaterial(0) is not ShaderMaterial near||
            rig.DistantBodyMesh.SurfaceGetMaterial(0) is not ShaderMaterial far||
            near.GetShaderParameter("garment_cut").AsInt32()!=cut||far.GetShaderParameter("garment_cut").AsInt32()!=cut)
            throw new InvalidOperationException("The fitted placket changes cut with detail level");
        if(pose==null)
        {
            rig.SetHandDetail(false);
            bool staticButtons=rig.PlacketButtons.Skin==null&&rig.PlacketButtons.Skeleton.IsEmpty;
            rig.SetHandDetail(true);
            if(!staticButtons||rig.PlacketButtons.Skin!=rig.DetailedBodySkin||rig.PlacketButtons.Skeleton!=rig.BodySkin.Skeleton)
                throw new InvalidOperationException("The placket and cloth use different bindings after detail switching");
        }
        GD.Print(pose==null
            ?$"FITTED_PLACKET_PASS: cut={cut}, {PlacketButtonCount(cut)} buttons seated in the rest front panel, same cut at both detail levels"
            :$"FITTED_PLACKET_BEND_PASS: cut={cut}, lower closure remains seated on the posed front panel");
    }

    private void AddFittedPlacketButtons(ResidentRig rig,FemaleBuild build)
    {
        string key="fitted-placket-buttons:"+_garmentCut+":"+build;
        if(!_meshes.TryGetValue(key,out var mesh))
        {
            var cloth=rig.DetailedBodyMesh.SurfaceGetArrays(0);
            var clothPoints=cloth[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var clothIndices=cloth[(int)Mesh.ArrayType.Index].AsInt32Array();
            var clothBones=cloth[(int)Mesh.ArrayType.Bones].AsInt32Array();
            var clothWeights=cloth[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            float PelvicInfluence(int vertex)
            {
                float influence=0;
                for(int slot=0;slot<4;slot++)
                    if(clothBones[vertex*4+slot]==3)influence+=clothWeights[vertex*4+slot];
                return influence;
            }
            var button=SewnButton().SurfaceGetArrays(0);
            var points=button[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals=button[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var indices=button[(int)Mesh.ArrayType.Index].AsInt32Array();
            var axis=new Basis(Vector3.Right,MathF.PI/2);
            using var surface=new SurfaceTool();
            surface.Begin(Mesh.PrimitiveType.Triangles);
            surface.SetMaterial(ResidentMaterial("#514237"));
            for(int row=0;row<PlacketButtonCount(_garmentCut);row++)
            {
                const float x=.002f;
                float y=PlacketButtonHeight(_garmentCut,row);
                float z=GarmentDepth(clothPoints,clothIndices,x,y,false,out int triangle,out var coordinates);
                if(triangle<0)throw new InvalidOperationException("Fitted placket button misses the final front panel");
                float pelvis=coordinates.X*PelvicInfluence(clothIndices[triangle])
                    +coordinates.Y*PelvicInfluence(clothIndices[triangle+1])
                    +coordinates.Z*PelvicInfluence(clothIndices[triangle+2]);
                var anchor=new Vector3(x,y,z+.0007f);
                foreach(int i in indices)
                {
                    var point=anchor+axis*points[i];
                    surface.SetBones(new[]{0,3,0,0});
                    surface.SetWeights(new[]{1-pelvis,pelvis,0f,0f});
                    surface.SetNormal(axis*normals[i]);
                    surface.AddVertex(point);
                }
            }
            surface.Index();
            _meshes[key]=mesh=WithScreenLods(surface.Commit());
        }
        rig.PlacketButtons=new MeshInstance3D {Name="FittedPlacketButtons",Mesh=mesh,
            Skin=rig.DetailedBodySkin,Skeleton=new NodePath("../ShoulderSkeleton")};
        rig.Torso.AddChild(rig.PlacketButtons);
    }
    private Material CollarMaterial(string cloth)
    {
        string key="folded-collar:"+cloth;if(_residentMaterials.TryGetValue(key,out var cached))return cached;
        var material=Fabric("#"+new Color(cloth).Darkened(.055f).ToHtml(false));_residentMaterials[key]=material;return material;
    }
    private Mesh CollarLeaf(float side)
    {
        string key="fitted-collar:"+side+":"+_garmentCut;
        if(_meshes.TryGetValue(key,out var cached))return cached;
        using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);surface.SetSmoothGroup(0);
        Vector3 Point(int row,int column,bool back=false)
        {
            float v=row/8f,u=column/8f;
            float inner=Mathf.Lerp(.017f,.028f,v),outer=Mathf.Lerp(.075f,.033f,v)+MathF.Sin(v*MathF.PI)*.012f;
            float x=side*Mathf.Lerp(inner,outer,u),y=Mathf.Lerp(1.255f,1.185f,v)+u*.007f*(1-v);
            return TorsoSurface(x,y,false,(back?.001f:.003f)+.0025f*MathF.Sin(u*MathF.PI)*MathF.Sin(v*MathF.PI));
        }
        foreach(bool back in new[]{false,true})for(int r=0;r<8;r++)for(int c=0;c<8;c++)
        {
            var a=Point(r,c,back);var b=Point(r,c+1,back);var d=Point(r+1,c,back);var e=Point(r+1,c+1,back);
            EquipmentTriangle(surface,a,b,d,back?Vector3.Back:Vector3.Forward);EquipmentTriangle(surface,b,e,d,back?Vector3.Back:Vector3.Forward);
        }
        foreach(int r in new[]{0,8})for(int c=0;c<8;c++)
        {
            var a=Point(r,c);var b=Point(r,c+1);var d=Point(r,c,true);var e=Point(r,c+1,true);
            EquipmentTriangle(surface,a,b,d,r==0?Vector3.Up:Vector3.Down);EquipmentTriangle(surface,b,e,d,r==0?Vector3.Up:Vector3.Down);
        }
        foreach(int c in new[]{0,8})for(int r=0;r<8;r++)
        {
            var a=Point(r,c);var b=Point(r+1,c);var d=Point(r,c,true);var e=Point(r+1,c,true);
            var outward=new Vector3((c==0?-1:1)*side,0,0);
            EquipmentTriangle(surface,a,b,d,outward);EquipmentTriangle(surface,b,e,d,outward);
        }
        surface.GenerateNormals();surface.Index();var mesh=surface.Commit();_meshes[key]=mesh;return mesh;
    }
    private void AddResidentTailoring(ResidentRig rig,string cloth,string skin,JobType job)
    {
        if(BodyFrame==1)
        {
            Sculpt(rig.Torso,FemaleBeltOverlap(),Vector3.Zero,ResidentMaterial("#604c3b"));
            Detail(rig.Torso,"box",TorsoSurface(-.010f,.7615f+BeltRise,false,.007f),new(.009f,.022f,.006f),"#48372d");
        }
        else Detail(rig.Torso,"box",TorsoSurface(.021f,.737f+BeltRise,false,.006f),new(.033f,.075f,.012f),"#514237");
        // Short stitches follow the actual folded surface instead of spanning
        // the chest as two loose, contrasting cords.
        string thread="#"+new Color(cloth).Darkened(.14f).ToHtml(false);
        // A subdued rear-yoke stitch follows the same shirt surface used by
        // the backpack and shoulder skin; no detached trim across the back.
        if(!FittedFigure)for(int stitch=0;stitch<48;stitch++)
        {
            float x0=Mathf.Lerp(-.15f,.15f,stitch/48f),x1=x0+.0038f;
            float Y(float x)=>1.155f+.018f*(1-MathF.Pow(x/.15f,2));
            FineBeam(rig.Torso,TorsoSurface(x0,Y(x0),true,.0016f),
                TorsoSurface(x1,Y(x1),true,.0016f),.00045f,thread);
        }
        if(!FittedFigure)foreach(float side in new[]{-1f,1f})for(int stitch=0;stitch<24;stitch++)
        {
            float y=Mathf.Lerp(PlacketBottom+.006f,PlacketTop-.006f,stitch/23f);
            FineBeam(rig.Torso,TorsoSurface(side*.007f,y,false,.0047f),
                TorsoSurface(side*.007f,y+.0035f,false,.0047f),.00055f,thread);
        }
        if(!FittedFigure&&(job is JobType.Gatherer or JobType.Hunter or JobType.Trader))
        {
            Sculpt(rig.Torso,FittedRibbon("shoulder-front",.81f,1.20f),Vector3.Zero,LeatherSurface("#665141"));
            Sculpt(rig.Torso,FittedRibbon("shoulder-back",.81f,1.20f,true),Vector3.Zero,LeatherSurface("#665141"));
            var pouch=rig.SatchelMount=new Node3D {Name="SatchelMount",Position=new(.23f,.80f,-.008f)};
            rig.Torso.AddChild(pouch);
            Sculpt(pouch,SatchelBody(),Vector3.Zero,LeatherSurface("#786346"),new(-MathF.PI/2,0,0));
            Sculpt(pouch,SatchelFlap(),Vector3.Zero,LeatherSurface("#786346"));
            Detail(pouch,"sphere",new(0,.030f,SatchelDepth(0,.030f)-.005f),new(.010f,.010f,.008f),"#514237");
        }
    }

    private Mesh FemaleBeltOverlap()
    {
        string key="female-belt-overlap:"+_garmentCut;
        if(_meshes.TryGetValue(key,out var cached))return cached;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);surface.SetSmoothGroup(0);
        void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 outward)
        {
            EquipmentTriangle(surface,a,b,c,outward);EquipmentTriangle(surface,b,d,c,outward);
        }
        Vector3 Edge(int section,float side,bool outer)
        {
            float t=section/12f;
            float x=Mathf.Lerp(-.059f,.025f,t);
            float halfWidth=Mathf.Lerp(.0028f,.0070f,Mathf.SmoothStep(0,.16f,t));
            return TorsoSurface(x,.7615f+BeltRise+side*halfWidth,false,outer?.0078f:.0040f);
        }
        for(int section=0;section<12;section++)
        {
            int next=section+1;
            Quad(Edge(section,-1,true),Edge(next,-1,true),Edge(section,1,true),Edge(next,1,true),Vector3.Forward);
            Quad(Edge(section,-1,false),Edge(next,-1,false),Edge(section,1,false),Edge(next,1,false),Vector3.Back);
            Quad(Edge(section,1,true),Edge(next,1,true),Edge(section,1,false),Edge(next,1,false),Vector3.Up);
            Quad(Edge(section,-1,true),Edge(next,-1,true),Edge(section,-1,false),Edge(next,-1,false),Vector3.Down);
        }
        Quad(Edge(0,-1,true),Edge(0,1,true),Edge(0,-1,false),Edge(0,1,false),Vector3.Left);
        Quad(Edge(12,-1,true),Edge(12,1,true),Edge(12,-1,false),Edge(12,1,false),Vector3.Right);
        surface.GenerateNormals();surface.Index();return _meshes[key]=surface.Commit();
    }
    private void ValidateGarmentCuts()
    {
        int previous=_garmentCut;
        var meshes=new System.Collections.Generic.HashSet<Mesh>();
        for(int cut=0;cut<3;cut++)
        {
            SelectGarmentCut(cut);var mesh=Garment();meshes.Add(mesh);
            if(MathF.Abs(mesh.GetAabb().Position.Y-GarmentBottom)>.00001f||mesh!=Garment())
                throw new InvalidOperationException("Garment cut loses hem length or cache");
            var arrays=mesh.SurfaceGetArrays(0);var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            for(int i=0;i<points.Length;i++)if(!points[i].IsFinite()||!normals[i].IsFinite())
                throw new InvalidOperationException("Garment facing has invalid geometry");
            float seamBelt=FinishedBeltHeight(0),seamHem=SourceHemHeight(0);
            var panelReference=GarmentFrameReference();
            var maleSupport=FrameRadii("trunk-masculine-cage.json",MaleTrunkHash);
            var femaleSupport=FrameRadii("trunk-feminine-cage.json",FemaleTrunkHash);
            foreach(float angle in new[]{-MathF.Tau,-.000001f,-.00000001f,0,.00000001f,.000001f,MathF.Tau,2*MathF.Tau})
            {
                float belt=FinishedBeltHeight(angle),hem=SourceHemHeight(angle);
                if(!float.IsFinite(belt)||!float.IsFinite(hem)||MathF.Abs(belt-seamBelt)>.00001f||MathF.Abs(hem-seamHem)>.00001f)
                    throw new InvalidOperationException("Tailored belt or sewn hem breaks at the angular seam");
                foreach(float y in new[]{GarmentReferenceLow,TrunkLow,1.04f,TrunkHigh})
                {
                    if(MathF.Abs(GarmentReferenceSample(panelReference,y,angle)-GarmentReferenceSample(panelReference,y,0))>.00001f)
                        throw new InvalidOperationException("Sewn panel reference breaks at the angular seam");
                    foreach(var source in new[]{maleSupport,femaleSupport})
                        if(MathF.Abs(TrunkSample(source,y,angle)-TrunkSample(source,y,0))>.00001f)
                            throw new InvalidOperationException("Source trunk support breaks at the angular seam");
                }
            }
            foreach(var key in new[]{"belt","apron","armor","placket","shoulder-front","shoulder-back"})
            {
                bool rear=key=="shoulder-back";
                var layer=FittedRibbon(key,key=="belt"?.748f:key=="armor"?.91f:key=="apron"?.79f:key=="placket"?PlacketBottom:.81f,
                    key=="belt"?.775f:key=="armor"||key=="apron"?1.17f:key=="placket"?PlacketTop:1.20f,rear);
                foreach(var p in layer.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                {
                    float gap=MathF.Abs(p.Z-TorsoSurface(p.X,p.Y,key=="belt"?p.Z>0:rear,0).Z);
                    if(gap<.001f||gap>.012f)throw new InvalidOperationException("Clothing layer uses a different garment cut");
                }
            }
        }
        if(meshes.Count!=3)throw new InvalidOperationException("Garment cuts share one silhouette");
        foreach(var mesh in new[]{RolledCuff(),TurnedNeckline()})
        {
            var data=mesh.SurfaceGetArrays(0);var points=data[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals=data[(int)Mesh.ArrayType.Normal].AsVector3Array();
            for(int i=0;i<points.Length;i++)if(!points[i].IsFinite()||!normals[i].IsFinite())
                throw new InvalidOperationException("Folded neckline or cuff has invalid geometry");
        }
        ValidateNeckBinding();
        SelectGarmentCut(previous);
        GD.Print("GARMENT_CUTS_PASS: three cached silhouettes, sewn hems, folded cuffs/neckline and fitted layers per cut");
        GD.Print("TAILORING_SEAM_PASS: three cuts, two source bodies, signed near-zero/whole-turn angles, belt/hem/panel/support continuity including final rows");
    }
    private void ValidateNeckBinding()
    {
        // Measure the final triangles against the same seam that the baked
        // shirt exposes. This catches detached trim and accidental throat caps.
        var mesh=TurnedNeckline();var arrays=mesh.SurfaceGetArrays(0);
        var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        foreach(var point in points)
        {
            float best=float.PositiveInfinity;
            for(int sample=0;sample<=384;sample++)best=MathF.Min(best,point.DistanceTo(GarmentPoint(1,sample*MathF.Tau/384)));
            if(best>.0065f||point.X*point.X+point.Z*point.Z<.003f)
                throw new InvalidOperationException("Neck binding detaches from the shirt or caps the throat");
        }
        foreach(var normal in normals)if(!normal.IsFinite()||MathF.Abs(normal.Length()-1)>.01f)
            throw new InvalidOperationException("Neck binding has invalid normals");
        if(mesh!=TurnedNeckline()||arrays[(int)Mesh.ArrayType.Index].AsInt32Array().Length/3>1200)
            throw new InvalidOperationException("Neck binding loses its cache or detail budget");
        GD.Print("NECK_BINDING_PASS: actual open shirt seam, <=6.5 mm support, open throat, finite normals and cached detail budget");
    }
}
