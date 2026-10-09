using System;
namespace SandBoxSim.Client;
public partial class WorldView3D
{
    private static float TerrainNoise(float x,float y,int seed)
    {
        int ix=(int)MathF.Floor(x),iy=(int)MathF.Floor(y);float u=x-ix,v=y-iy;
        u=u*u*(3-2*u);v=v*v*(3-2*v);
        float At(int a,int b)
        {
            uint h=unchecked((uint)(a*374761393+b*668265263+seed*1274126177));
            h=(h^(h>>13))*1274126177u;h^=h>>16;return (h&65535)/65535f;
        }
        return (At(ix,iy)*(1-u)+At(ix+1,iy)*u)*(1-v)+(At(ix,iy+1)*(1-u)+At(ix+1,iy+1)*u)*v;
    }
    private static float NaturalRelief(float x,float y,int seed)
        =>(TerrainNoise(x*.71f,y*.71f,seed)-.5f)*.046f
          +(TerrainNoise(x*1.83f+17,y*1.83f-9,seed)-.5f)*.019f;
}
