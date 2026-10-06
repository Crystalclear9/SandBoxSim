using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private Mesh SculptedHead(bool hair)
    {
        const int sides = 48, rings = 28;
        var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles); surface.SetSmoothGroup(0);
        Vector3 Point(int row, int col)
        {
            float angle = col * MathF.Tau / sides;
            float front = MathF.Max(0, -MathF.Sin(angle));
            float end = hair ? 1.85f - front * .63f + .06f * MathF.Sin(angle * 5) : MathF.PI;
            float latitude = row * end / rings;
            var p = new Vector3(MathF.Sin(latitude) * MathF.Cos(angle), MathF.Cos(latitude), MathF.Sin(latitude) * MathF.Sin(angle)) * .5f;
            if (hair)
            {
                float strand = .006f * MathF.Sin(angle * 19 + latitude * 6);
                p *= 1.045f + strand; p.X += .018f * MathF.Sin(latitude * 2);
            }
            else
            {
                // Jaw taper, cheekbones and a continuous nose bridge; no separate ball nose.
                p.X *= .80f + .20f * Math.Clamp((p.Y + .3f) / .5f, 0, 1);
                if (p.Z < 0)
                {
                    float bridge = MathF.Exp(-p.X * p.X * 150 - (p.Y + .035f) * (p.Y + .035f) * 28);
                    float cheek = MathF.Exp(-MathF.Pow(MathF.Abs(p.X) - .24f, 2) * 90 - p.Y * p.Y * 70);
                    float socket = MathF.Exp(-MathF.Pow(MathF.Abs(p.X) - .18f, 2) * 160 - MathF.Pow(p.Y - .1f, 2) * 170);
                    p.Z -= bridge * .14f + cheek * .025f - socket * .035f;
                }
            }
            return p;
        }
        for (int row = 0; row < rings; row++) for (int col = 0; col < sides; col++)
        {
            var a = Point(row, col); var b = Point(row, col + 1); var c = Point(row + 1, col); var d = Point(row + 1, col + 1);
            foreach (var p in new[] { a, c, b, b, c, d }) { surface.AddVertex(p); }
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
