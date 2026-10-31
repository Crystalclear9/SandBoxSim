using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    // Export evaluated geometry, rather than an idealized bone diagram. This
    // shares the gameplay solver, morph mix, skin palette and fitted clothing.
    public void ExportResidentPoseStudy(string folder)
    {
        Directory.CreateDirectory(folder);
        float[][] V(Vector3[] points)=>points.Select(p=>new[]{p.X,p.Y,p.Z}).ToArray();
        float[][] B(Basis basis)=>V(new[]{basis.X,basis.Y,basis.Z});
        foreach(int identity in new[]{17,55})
        {
            var rig=Resident(identity,false,JobType.None);
            try
            {
                var arrays=rig.DetailedBodyMesh.SurfaceGetArrays(0);
                var rest=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
                var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                var pattern=arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
                var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                foreach(int degrees in new[]{0,45,90,135})
                {
                    for(int frame=0;frame<180;frame++)rig.StudyArmElevation(1f/60,Mathf.DegToRad(degrees),Mathf.DegToRad(30));
                    var local=CorrectedSleeveSource(rig,rest);var displayed=new Vector3[rest.Length];
                    var palette=new Transform3D[ResidentRig.SupportedBodyBones];
                    for(int bone=0;bone<palette.Length;bone++)palette[bone]=rig.ShoulderSkeleton.GetBonePose(bone)*rig.DetailedBodySkin.GetBindPose(bone);
                    for(int i=0;i<rest.Length;i++)for(int j=0;j<4;j++)
                    {
                        int bone=bones[i*4+j];
                        displayed[i]+=palette[bone]*local[i]*weights[i*4+j];
                    }
                    var support=PoseShoulderSupport(rig,1,rig.Arms[1].Quaternion,rig.DisplayedGirdle(1));
                    var data=new {schemaVersion=1,space="torso-local metres, Y up",identity,degrees,planeDegrees=30,
                        rest=V(rest),rendered=V(displayed),indices,bones,weights,
                        pattern=pattern.Select(p=>new[]{p.X,p.Y}).ToArray(),
                        support=V(support),supportIndices=ShoulderSurface().Triangles,
                        upperArmBasisColumns=B(rig.Arms[1].Basis),
                        forearmBasisColumns=B(rig.Arms[1].Basis*rig.Elbows[1].Basis),
                        palmBasisColumns=B(rig.Arms[1].Basis*rig.Elbows[1].Basis*rig.Hands[1].Basis),
                        shoulder=V(new[]{rig.Arms[1].Position}),
                        elbow=V(new[]{rig.Arms[1].Transform*rig.Elbows[1].Position})};
                    File.WriteAllText(Path.Combine(folder,$"resident-{identity}-{degrees}.json"),JsonSerializer.Serialize(data));
                }
            }
            finally{rig.Free();}
        }
        GD.Print("RESIDENT_POSE_STUDY_EXPORTED: two adult frames, 0/45/90/135 degrees, actual morphed/skinned garment and anatomical support");
    }
}
