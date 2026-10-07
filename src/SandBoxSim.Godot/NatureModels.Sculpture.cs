using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private Mesh SculptedHead(bool hair,int sides=80,int rings=32)
    {
        using var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles); surface.SetSmoothGroup(0);
        float[] levels={-.50f,-.45f,-.35f,-.22f,-.06f,.10f,.22f,.34f,.43f,.49f,.50f};
        float[] widths={.135f,.25f,.355f,.42f,.465f,.48f,.48f,.435f,.37f,.16f,.025f};
        float[] depths={.22f,.31f,.375f,.425f,.46f,.47f,.465f,.425f,.35f,.16f,.025f};
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
            float end=hair ? 1.67f+MathF.Max(0,MathF.Sin(angle))*.18f-front*(.51f+.12f*MathF.Cos(angle+.4f))+.028f*MathF.Sin(angle*5) : MathF.PI;
            float latitude=row*end/rings,y=MathF.Cos(latitude)*.5f;
            float depth=Profile(y,depths);
            var p=new Vector3(MathF.Cos(angle)*Profile(y,widths),y,MathF.Sin(angle)*depth);
            if(hair)
            {
                float swept = angle + latitude*.65f;
                float locks = .004f*MathF.Sin(swept*25) + .002f*MathF.Sin(swept*43);
                p*=1.025f+locks*MathF.Sin(latitude); p.X+=.016f*MathF.Sin(latitude*2);
                p.Y+=.015f*MathF.Sin(latitude)*MathF.Cos(angle+.6f);
            }
            else if(p.Z<0)
            {
                // A flatter facial plane, rounded chin, nasal bridge/tip and recessed eye sockets.
                p.Z=Mathf.Lerp(p.Z,-depth*.94f,MathF.Pow(front,8)*.45f);
                float bridge=MathF.Exp(-p.X*p.X*540-MathF.Pow(p.Y+.015f,2)*115);
                float tip=MathF.Exp(-p.X*p.X*520-MathF.Pow(p.Y+.14f,2)*420);
                float socket=MathF.Exp(-MathF.Pow(MathF.Abs(p.X)-.19f,2)*250-MathF.Pow(p.Y-.045f,2)*380);
                float lip=MathF.Exp(-p.X*p.X*120-MathF.Pow(p.Y+.275f,2)*300);
                float cheek = MathF.Exp(-MathF.Pow(MathF.Abs(p.X)-.27f,2)*90-MathF.Pow(p.Y+.06f,2)*130);
                float brow = MathF.Exp(-MathF.Pow(MathF.Abs(p.X)-.18f,2)*170-MathF.Pow(p.Y-.17f,2)*190);
                float ala = MathF.Exp(-MathF.Pow(MathF.Abs(p.X)-.082f,2)*380-MathF.Pow(p.Y+.15f,2)*280);
                p.Z-=bridge*.050f+tip*.062f+ala*.022f+lip*.015f+cheek*.014f+brow*.014f-socket*.018f;
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
            Vertex(new(0,.51f,0)); Vertex(Point(0,side)); Vertex(Point(0,side+1));
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
ALBEDO = dye.rgb * (0.97 + warp * 0.035 + folds);
ROUGHNESS = 0.94; SPECULAR = 0.18;
}" };
        var material = new ShaderMaterial { Shader = _fabricShader }; material.SetShaderParameter("dye", new Color(color)); return material;
    }
}
