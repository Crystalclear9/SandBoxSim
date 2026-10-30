using System;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void ValidateElevationSurface()
    {
        int poses=0,hingeSamples=0;float minimumRadius=1,maximumSleeveRadius=0,maximumGirdleError=0,minimumHingeAlignment=1;
        foreach(int identity in new[]{17,55})
        foreach(float plane in new[]{0f,30f,75f})
        foreach(int side in new[]{0,1})
        {
            var rig=Resident(identity,false,JobType.None);
            try
            {
                var arrays=rig.DetailedBodyMesh.SurfaceGetArrays(0);
                var points=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var bones=arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
                var weights=arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                var seams=arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
                // Both directions, three planes; feed the displayed solver,
                // then inspect the actual GPU palette and fitted mesh data.
                for(int frame=0;frame<=180;frame++)
                {
                    float degrees=135*(1-MathF.Cos(MathF.Tau*frame/180))*.5f;
                    rig.StudyArmElevation(1f/60,Mathf.DegToRad(degrees),Mathf.DegToRad(plane),side);
                    ValidateSleeveSupport(rig,side);
                    var corrected=CorrectedSleeveSource(rig,points);
                    if(frame>=70&&frame<=120)
                    {
                        var upper=-rig.Arms[side].Basis.Y;
                        var lower=(rig.Arms[side].Basis*rig.Elbows[side].Basis*rig.Hands[side].Position).Normalized();
                        var hinge=upper.Cross(lower).Normalized();
                        float alignment=rig.Arms[side].Basis.X.Dot(hinge);
                        minimumHingeAlignment=MathF.Min(minimumHingeAlignment,alignment);hingeSamples++;
                        if(alignment<.90f)throw new InvalidOperationException($"Raised arm's signed flexion axis does not match its elbow plane: side={side}, plane={plane}, alignment={alignment}");
                    }
                    var transforms=new Transform3D[ResidentRig.SupportedBodyBones];
                    for(int bone=0;bone<transforms.Length;bone++)
                        transforms[bone]=rig.ShoulderSkeleton.GetBonePose(bone)*rig.DetailedBodySkin.GetBindPose(bone);
                    for(int i=0;i<points.Length;i++)
                    {
                        var point=Vector3.Zero;
                        for(int j=0;j<4;j++)point+=(transforms[bones[i*4+j]]*corrected[i])*weights[i*4+j];
                        if(!point.IsFinite())throw new InvalidOperationException("Non-finite raised sleeve");
                        if((points[i].X<0)==(side==0)&&seams[i].X>.08f&&seams[i].X<.20f)
                        {
                            var offset=point-rig.Arms[side].Position;var direction=-rig.Arms[side].Basis.Y;
                            float extent=(offset-direction*offset.Dot(direction)).Length();
                            maximumSleeveRadius=MathF.Max(maximumSleeveRadius,extent);
                            if(extent>.11f)throw new InvalidOperationException($"Raised mid-sleeve bows away from the upper arm: radius={extent}");
                        }
                        if(seams[i].X>.2629f&&point.DistanceTo(transforms[points[i].X<0?1:2]*points[i])>.000002f)
                            throw new InvalidOperationException("Raised sleeve cuff detaches from the displayed arm");
                    }
                    float actualElevation=MathF.Acos(Math.Clamp((-rig.Arms[side].Basis.Y).Dot(Vector3.Down),-1,1));
                    float expectedLift=.020f*Mathf.SmoothStep(Mathf.DegToRad(30),Mathf.DegToRad(105),actualElevation);
                    float girdleError=MathF.Abs(rig.Arms[side].Position.Y-rig.ShoulderRest(side).Y-expectedLift);
                    maximumGirdleError=MathF.Max(maximumGirdleError,girdleError);
                    if(girdleError>.002f)throw new InvalidOperationException("Shoulder lift follows the wrist instead of the upper arm");
                    // Worst midpoint between neighbouring rotations: unlike
                    // checking individual bones, this detects LBS collapse.
                    var rotation=rig.Arms[side].Quaternion;
                    var axis=new Vector3(rotation.X,rotation.Y,rotation.Z).Normalized();
                    var radial=axis.Cross(MathF.Abs(axis.Y)<.9f?Vector3.Up:Vector3.Right).Normalized();
                    for(int span=0;span<ResidentRig.SleeveSpans;span++)
                    {
                        var a=Quaternion.Identity.Slerp(rotation,(float)span/ResidentRig.SleeveSpans);
                        var b=Quaternion.Identity.Slerp(rotation,(float)(span+1)/ResidentRig.SleeveSpans);
                        float radius=((a*radial+b*radial)*.5f).Length();
                        minimumRadius=MathF.Min(minimumRadius,radius);
                        if(radius<.94f)throw new InvalidOperationException("Raised sleeve loses excessive rotational radius");
                    }
                    poses++;
                }
            }
            finally{rig.Free();}
        }
        GD.Print($"ELEVATION_SURFACE_PASS: poses={poses}, two adult frames, both arms, 0–135 degrees, three planes, raise/lower, minimum interval radius={minimumRadius:F4}, mid-sleeve radial extent={maximumSleeveRadius:F4} m, girdle error={maximumGirdleError:F5} m, finite skin and attached cuffs");
        GD.Print($"SIGNED_ELBOW_FRAME_PASS: settled raised samples={hingeSamples}, minimum flexion-axis alignment={minimumHingeAlignment:F4}; no unsigned hinge flip");
    }
}
