using System;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private float _femaleWaistHeight;
    private readonly float[] _femaleBeltRise=new float[3];

    // Find the natural waist on the same source envelope that supports the
    // clothing. This is a character-specific fitting guide, not a body ideal.
    private float FemaleWaistHeight
    {
        get
        {
            if(_femaleWaistHeight>0)return _femaleWaistHeight;
            var source=FemaleThoraxSupport();float minimum=float.PositiveInfinity,height=0;
            for(int row=0;row<=20;row++)
            {
                float y=.96f+row*.005f,area=0;
                for(int col=0;col<TrunkColumns;col++)
                    area+=TrunkSample(source,y,col*Mathf.Tau/TrunkColumns)
                        *TrunkSample(source,y,(col+1)*Mathf.Tau/TrunkColumns);
                if(area<minimum){minimum=area;height=y;}
            }
            if(!float.IsFinite(minimum)||height<=.96f||height>=1.06f)
                throw new InvalidOperationException("Female waist guide escapes the source torso bracket");
            return _femaleWaistHeight=height;
        }
    }

    private float BeltRise
    {
        get
        {
            if(!FittedFigure)return 0;
            if(_femaleBeltRise[_garmentCut]>0)return _femaleBeltRise[_garmentCut];
            float target=FemaleWaistHeight,low=.04f,high=.18f;
            float Height(float rise)=>TailoredPanelHeight(new Vector3(.10f,ResidentRig.BodyY(.7615f+rise),0));
            if(Height(low)>=target||Height(high)<=target)
                throw new InvalidOperationException("Clothing cut cannot reach its anatomical waist guide");
            // Invert the finished panel map separately for each cut. One fixed
            // source rise placed all three belts above the actual waist.
            for(int i=0;i<24;i++)
            {
                float middle=(low+high)*.5f;
                if(Height(middle)<target)low=middle;else high=middle;
            }
            return _femaleBeltRise[_garmentCut]=(low+high)*.5f;
        }
    }

    private void ValidateNaturalWaistBelts()
    {
        int previousFace=_faceVariant,previousCut=_garmentCut;
        try
        {
            _faceVariant=5;
            for(int cut=0;cut<3;cut++)
            {
                SelectGarmentCut(cut);float centre=.7615f+BeltRise;
                var ribbon=ProportionedMesh(FittedRibbon("belt",centre-.009f,centre+.009f),Transform3D.Identity,true);
                var mesh=FrameMesh(ribbon,Transform3D.Identity,true);
                var points=mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                float low=float.PositiveInfinity,high=float.NegativeInfinity;int count=0;
                foreach(var p in points)if(MathF.Abs(p.X)<.001f)
                {low=MathF.Min(low,p.Y);high=MathF.Max(high,p.Y);count++;}
                if(count<4||MathF.Abs((low+high)*.5f-FemaleWaistHeight)>.002f||high-low<.008f)
                    throw new InvalidOperationException($"Finished belt misses the source waist or loses its band width: cut={cut}, samples={count}, low/high={low}/{high}, target={FemaleWaistHeight}, rise={BeltRise}");
                GD.Print($"NATURAL_WAIST_BELT_PASS: cut={cut}, actual band centre={(low+high)*.5f:F4} m, source waist={FemaleWaistHeight:F4} m; cached inverse panel fit, shared gather/mount coordinates");
            }
        }
        finally{_faceVariant=previousFace;SelectGarmentCut(previousCut);_facePoints=null;_faceIndices=null;}
    }
}
