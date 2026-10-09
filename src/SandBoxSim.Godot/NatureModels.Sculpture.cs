using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private int _faceVariant;
    private Mesh SculptedHead(bool hair,int sides=80,int rings=32)
    {
        using var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles); surface.SetSmoothGroup(0);
        float[] levels={-.50f,-.45f,-.35f,-.22f,-.06f,.10f,.22f,.34f,.43f,.49f,.50f};
        float[] widths={.075f,.24f,.355f,.405f,.445f,.455f,.445f,.405f,.31f,.13f,.010f};
        float[] depths={.14f,.245f,.30f,.365f,.435f,.465f,.46f,.425f,.335f,.13f,.010f};
        float Profile(float y,float[] values)
        {
            for(int i=0;i<levels.Length-1;i++) if(y<=levels[i+1])
            {
                float t=Math.Clamp((y-levels[i])/(levels[i+1]-levels[i]),0,1);
                float span=levels[i+1]-levels[i];int previous=Math.Max(0,i-1),next=Math.Min(levels.Length-1,i+2);
                float m0=(values[i+1]-values[previous])/(levels[i+1]-levels[previous])*span;
                float m1=(values[next]-values[i])/(levels[next]-levels[i])*span;
                return MathF.Max(.02f,(2*t*t*t-3*t*t+1)*values[i]+(t*t*t-2*t*t+t)*m0+(-2*t*t*t+3*t*t)*values[i+1]+(t*t*t-t*t)*m1);
            }
            return values[^1];
        }
        Vector3 Point(int row,int col)
        {
            float angle=col*MathF.Tau/sides,front=MathF.Max(0,-MathF.Sin(angle));
            float direction=_faceVariant%2==0?1:-1;
            int style=_faceVariant%3;
            float end=hair ? 1.72f+MathF.Max(0,MathF.Sin(angle))*.18f-front*(.64f+.08f*MathF.Cos(angle+direction*.5f))+.010f*MathF.Sin(angle*7) : MathF.PI;
            float latitude=row*end/rings,y=hair?MathF.Cos(latitude)*.5f:.5f-row/(float)rings;
            float jaw=1+((_faceVariant%4)-1.5f)*.045f;
            float browWidth=1+((_faceVariant/4)-.5f)*.075f;
            float depth=Profile(y,depths)*(1+((_faceVariant%3)-1)*.045f);
            float faceBlend=Math.Clamp((y+.35f)/.47f,0,1);faceBlend=faceBlend*faceBlend*(3-2*faceBlend);
            var p=new Vector3(MathF.Cos(angle)*Profile(y,widths)*Mathf.Lerp(jaw,browWidth,faceBlend),y,MathF.Sin(angle)*depth);
            if(hair)
            {
                float swept = angle + direction*latitude*(style==1?.52f:.90f);
                float locks = (style==2?.007f:.016f)*MathF.Sin(swept*(style==1?14:19)) + .003f*MathF.Sin(swept*37);
                float part=MathF.Exp(-MathF.Pow((MathF.Cos(angle)-direction*.28f)*12,2))*MathF.Pow(front,3);
                float crown=style==2?.012f:.028f;
                p*=1+crown+locks*MathF.Sin(latitude)-part*.024f; p.X+=.012f*MathF.Sin(latitude*2);
                p.Y+=(style==2?.006f:.022f)*MathF.Sin(latitude)*MathF.Cos(angle+direction*.6f);
                p.X+=((_faceVariant%2)==0?1:-1)*.022f*MathF.Sin(latitude);
                p=WrapHumanHair(p,angle);
            }
            else if(p.Z<0)
            {
                // A flatter facial plane, rounded chin, nasal bridge/tip and recessed eye sockets.
                p.Z=Mathf.Lerp(p.Z,-depth*.94f,MathF.Pow(front,8)*.45f);
                float bridge=MathF.Exp(-p.X*p.X*510-MathF.Pow(p.Y+.015f,2)*72);
                float tip=MathF.Exp(-p.X*p.X*430-MathF.Pow(p.Y+.14f,2)*370);
                float socket=MathF.Exp(-MathF.Pow(MathF.Abs(p.X)-.19f,2)*250-MathF.Pow(p.Y-.045f,2)*380);
                float lip=MathF.Exp(-p.X*p.X*120-MathF.Pow(p.Y+.275f,2)*300);
                float cheek = MathF.Exp(-MathF.Pow(MathF.Abs(p.X)-.27f,2)*120-MathF.Pow(p.Y+.03f,2)*175);
                float brow = MathF.Exp(-MathF.Pow(MathF.Abs(p.X)-.18f,2)*170-MathF.Pow(p.Y-.17f,2)*190);
                float ala = MathF.Exp(-MathF.Pow(MathF.Abs(p.X)-.082f,2)*380-MathF.Pow(p.Y+.15f,2)*280);
                float chin=MathF.Exp(-p.X*p.X*65-MathF.Pow(p.Y+.405f,2)*390);
                float philtrum=MathF.Exp(-p.X*p.X*1200-MathF.Pow(p.Y+.225f,2)*850);
                float nostril=MathF.Exp(-MathF.Pow(MathF.Abs(p.X)-.058f,2)*1500-MathF.Pow(p.Y+.174f,2)*2200);
                float temple=MathF.Exp(-MathF.Pow(MathF.Abs(p.X)-.37f,2)*220-MathF.Pow(p.Y-.12f,2)*130);
                float eyeCrease=MathF.Exp(-MathF.Pow(MathF.Abs(p.X)-.19f,2)*280-MathF.Pow(p.Y-.092f,2)*2600);
                p.Z+=philtrum*.006f-chin*.030f+nostril*.006f+temple*.012f+eyeCrease*.007f;
                p.Z-=bridge*(.050f+(_faceVariant%3)*.004f)+tip*(.071f+(_faceVariant%4)*.005f)+ala*.030f+lip*.013f+cheek*.030f+brow*.017f-socket*.025f;
            }
            return p;
        }
        void Vertex(Vector3 p)
        {
            surface.SetUV(new(.5f+p.X,.5f-p.Y));
            float cheek=hair?0:MathF.Exp(-MathF.Pow(MathF.Abs(p.X)-.28f,2)*100-MathF.Pow(p.Y+.06f,2)*170)*MathF.Max(0,-p.Z)*1.8f;
            float nostril=hair?0:MathF.Exp(-MathF.Pow(MathF.Abs(p.X)-.060f,2)*1100-MathF.Pow(p.Y+.165f,2)*950)*MathF.Max(0,-p.Z)*1.7f;
            surface.SetColor(new Color(1-nostril*.22f,.99f-cheek*.045f-nostril*.23f,.98f-cheek*.075f-nostril*.22f));surface.AddVertex(p);
        }
        for (int row = 0; row < rings; row++) for (int col = 0; col < sides; col++)
        {
            var a = Point(row, col); var b = Point(row, col + 1); var c = Point(row + 1, col); var d = Point(row + 1, col + 1);
            foreach (var p in new[] { a, c, b, b, c, d }) { Vertex(p); }
        }
        for(int side=0;side<sides;side++)
        {
            Vertex(hair?WrapHumanHair(new(0,.51f,0),0,true):new(0,.51f,0)); Vertex(Point(0,side)); Vertex(Point(0,side+1));
            if(!hair)
            {
                Vertex(new(0,-.512f,0));Vertex(Point(rings,side+1));Vertex(Point(rings,side));
            }
        }
        surface.GenerateNormals(); surface.Index(); return surface.Commit();
    }
    private Shader? _fabricShader;
    private Material Fabric(string color)
    {
        _fabricShader ??= new Shader { Code = @"shader_type spatial;
uniform vec4 dye : source_color;
varying vec3 cloth_position;
void vertex(){ cloth_position = VERTEX; }
void fragment(){
float warp = sin(cloth_position.y * 620.0) * sin((cloth_position.x + cloth_position.z) * 610.0);
float footprint = max(length(dFdx(cloth_position)), length(dFdy(cloth_position))) * 620.0;
warp /= 1.0 + footprint * footprint;
float folds = sin(cloth_position.y * 37.0 + cloth_position.x * 13.0) * 0.025;
ALBEDO = dye.rgb * (0.97 + warp * 0.016 + folds);
ROUGHNESS = 0.86; SPECULAR = 0.22;
}" };
        var material = new ShaderMaterial { Shader = _fabricShader }; material.SetShaderParameter("dye", new Color(color)); return material;
    }
}
