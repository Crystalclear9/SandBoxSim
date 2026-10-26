using System;
using Godot;

namespace SandBoxSim.Client;
internal sealed partial class NatureModels
{
    private Mesh ReedClump()
    {
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        for(int stem=0;stem<7;stem++)
        {
            float angle=stem*2.399963f,height=.64f+(stem%4)*.095f;
            var radial=new Vector3(MathF.Cos(angle),0,MathF.Sin(angle));
            var root=radial*(.025f+(stem%3)*.035f);
            Vector3 Center(float t)=>root+Vector3.Up*height*t+radial*.075f*t*t;
            for(int joint=0;joint<5;joint++)BotanicalStem(tool,Center(joint/5f),Center((joint+1)/5f),.0038f);
            for(int leaf=0;leaf<4;leaf++)
            {
                float t=.18f+leaf*.16f,sign=leaf%2==0?-1:1;
                var axis=(radial*sign+Vector3.Up*.24f).Normalized();
                BotanicalLeaf(tool,Center(t),axis,axis.Cross(Vector3.Up).Normalized(),.25f-leaf*.025f,.014f,.018f,.72f+leaf*.075f);
            }
            // Common reed has an open, branched panicle, not a cattail club.
            // Every branch originates on the continuous central rachis. Previously
            // the upper branch roots floated above the last culm segment.
            var shaft=new Vector3[11];var radii=new float[11];
            for(int joint=0;joint<=10;joint++)
            {float t=joint/10f;shaft[joint]=ReedRachis(stem,t);radii[joint]=.0028f*(1-.76f*t);}
            ReedTube(tool,shaft,radii);
            for(int branch=0;branch<9;branch++)
            {
                var limb=ReedPanicleBranch(stem,branch);
                float thickness=.00075f*(1-.35f*(branch+1)/10f);
                ReedTube(tool,limb,new[]{thickness,thickness*.72f,thickness*.36f});
                var direction=(limb[2]-limb[0]).Normalized();
                var across=new Vector3(-direction.Z,0,direction.X).Normalized();
                for(int spikelet=0;spikelet<3;spikelet++)
                {
                    var rootlet=ReedSpikeletRoot(limb,spikelet);
                    float length=.018f-(branch/8f)*.004f;
                    // Paired folded glumes grow from the branch, opening outwards
                    // into fine tips. Compact seed units replace broad leaf paddles.
                    foreach(int side in new[]{-1,1})
                    {
                        var axis=(direction*.38f+Vector3.Up*.72f+across*(side*.25f)).Normalized();
                        BotanicalLeaf(tool,rootlet,axis,across,length,.0018f,.0012f,
                            1.14f,2,new Color(2.12f,1.08f,.80f));
                    }
                }
            }
        }
        tool.GenerateNormals();tool.Index();return WithScreenLods(tool.Commit());
    }
    private static Vector3 ReedRachis(int stem,float t)
    {
        float angle=stem*2.399963f,height=.64f+(stem%4)*.095f;
        var radial=new Vector3(MathF.Cos(angle),0,MathF.Sin(angle));
        var crown=radial*(.025f+(stem%3)*.035f+.075f)+Vector3.Up*height;
        return crown+Vector3.Up*(.20f*t)+radial*(.028f*t*t);
    }
    private static Vector3[] ReedPanicleBranch(int stem,int branch)
    {
        float t=(branch+1)/10f,azimuth=branch*2.399963f+stem*2.399963f;
        var direction=new Vector3(MathF.Cos(azimuth),0,MathF.Sin(azimuth));
        var start=ReedRachis(stem,t);
        float spread=.082f*MathF.Sin(t*MathF.PI)*(.87f+(branch%3)*.10f);
        var tip=start+direction*spread+Vector3.Up*(.028f-.026f*t);
        return new[]{start,start.Lerp(tip,.55f)+Vector3.Up*.008f,tip};
    }
    private static Vector3 ReedSpikeletRoot(Vector3[] limb,int spikelet)
    {
        float t=(spikelet+1)/3f;
        return t<=.55f?limb[0].Lerp(limb[1],t/.55f):limb[1].Lerp(limb[2],(t-.55f)/.45f);
    }
    // Shared rings keep a bent stem continuous: independently tapered cylinders
    // would leave annular gaps at each joint. Branch roots intersect this shaft.
    private static void ReedTube(SurfaceTool tool,Vector3[] centers,float[] radii)
    {
        var rings=new Vector3[centers.Length,6];
        for(int ring=0;ring<centers.Length;ring++)
        {
            var axis=(centers[Math.Min(ring+1,centers.Length-1)]-centers[Math.Max(0,ring-1)]).Normalized();
            var across=axis.Cross(MathF.Abs(axis.Y)>.95f?Vector3.Right:Vector3.Up).Normalized();
            var up=axis.Cross(across).Normalized();
            for(int side=0;side<6;side++)
            {float a=side*MathF.Tau/6;rings[ring,side]=centers[ring]+(across*MathF.Cos(a)+up*MathF.Sin(a))*radii[ring];}
        }
        void Vertex(int ring,int side)
        {
            tool.SetColor(new(1.50f,.83f,.49f,1));tool.SetNormal(Vector3.Up);
            tool.SetUV(new(side/6f,ring/(float)(centers.Length-1)));tool.AddVertex(rings[ring,side%6]);
        }
        for(int ring=0;ring<centers.Length-1;ring++)for(int side=0;side<6;side++)
        {Vertex(ring,side);Vertex(ring+1,side);Vertex(ring,side+1);Vertex(ring,side+1);Vertex(ring+1,side);Vertex(ring+1,side+1);}
    }
    // Probe the committed mesh, not only construction parameters. The short
    // cross-axis rays must hit both sides of each shaft/branch segment, including
    // the formerly empty space above the culm. No renderer or physics is needed.
    private void ValidateReedPanicles()
    {
        var arrays=Shape("reeds").SurfaceGetArrays(0);
        var vertices=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        if(indices.Length/3>5824)throw new InvalidOperationException("Reed panicles exceed their previous triangle budget");
        int probes=0;
        void CrossSection(Vector3 center,Vector3 axis,float reach)
        {
            var direction=axis.Cross(MathF.Abs(axis.Y)>.95f?Vector3.Right:Vector3.Up).Normalized();
            foreach(int sign in new[]{-1,1})
            {
                var ray=direction*sign;var origin=center-ray*reach;bool hit=false;
                for(int i=0;i<indices.Length;i+=3)
                {
                    var a=vertices[indices[i]];var edge1=vertices[indices[i+1]]-a;var edge2=vertices[indices[i+2]]-a;
                    var h=ray.Cross(edge2);float determinant=edge1.Dot(h);
                    if(MathF.Abs(determinant)<1e-12f)continue;
                    float inverse=1/determinant;var offset=origin-a;float u=offset.Dot(h)*inverse;
                    if(u<-.0001f||u>1.0001f)continue;
                    var q=offset.Cross(edge1);float v=ray.Dot(q)*inverse;
                    if(v<-.0001f||u+v>1.0001f)continue;
                    float distance=edge2.Dot(q)*inverse;
                    // Require geometry between the origin and the center, not a
                    // remote leaf on the opposite side of a missing stem.
                    if(distance>0&&distance<reach*.995f){hit=true;break;}
                }
                if(!hit)throw new InvalidOperationException("Reed panicle has an unsupported shaft or branch in the committed mesh");
                probes++;
            }
        }
        for(int stem=0;stem<7;stem++)
        {
            for(int joint=0;joint<10;joint++)
            {
                var a=ReedRachis(stem,joint/10f);var b=ReedRachis(stem,(joint+1)/10f);
                CrossSection((a+b)*.5f,b-a,.004f);
            }
            for(int branch=0;branch<9;branch++)
            {
                var limb=ReedPanicleBranch(stem,branch);
                // Two sides of the actual shaft at the attachment node, and
                // both branch segments, must have real committed geometry.
                CrossSection(limb[0],ReedRachis(stem,(branch+1.05f)/10)-ReedRachis(stem,(branch+.95f)/10),.004f);
                for(int segment=0;segment<2;segment++)CrossSection((limb[segment]+limb[segment+1])*.5f,limb[segment+1]-limb[segment],.0014f);
                for(int spikelet=0;spikelet<3;spikelet++)
                    CrossSection(ReedSpikeletRoot(limb,spikelet),limb[2]-limb[1],.0014f);
            }
        }
        GD.Print($"REED_PANICLES_PASS: {indices.Length/3} triangles, {probes} committed-mesh shaft/branch/spikelet support probes, shared cache/LOD");
    }
    // Eleven tapered blades share one mesh across spatially culled instances.
    private static ArrayMesh MeadowTuft()
    {
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);
        for(int blade=0;blade<11;blade++)
        {
            float a=blade*2.399963f,h=.19f+(blade%5)*.041f;
            var side=new Vector3(MathF.Cos(a),0,MathF.Sin(a));
            var lean=new Vector3(-side.Z,0,side.X)*(.09f+(blade%2)*.025f);
            var root=side*(.035f+(blade%3)*.022f);
            Vector3 Point(float t,float edge)=>root+Vector3.Up*h*t+lean*t*t+side*edge*.013f*MathF.Pow(1-t,.72f);
            void Vertex(float t,float edge){tool.SetUV(new((edge+1)*.5f,t));tool.AddVertex(Point(t,edge));}
            for(int row=0;row<5;row++)
            {
                float t=row/5f,u=(row+1)/5f;
                Vertex(t,-1);Vertex(u,-1);Vertex(t,1);
                Vertex(t,1);Vertex(u,-1);Vertex(u,1);
            }
        }
        tool.GenerateNormals();tool.Index();return tool.Commit();
    }
}
