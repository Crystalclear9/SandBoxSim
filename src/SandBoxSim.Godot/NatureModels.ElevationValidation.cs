using System;
using System.Collections.Generic;
using Godot;
using SandBoxSim.Core.Agents;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    private void ValidateElevationSurface()
    {
        int poses=0,hingeSamples=0;float minimumRadius=1,maximumSleeveRadius=0,maximumGirdleError=0,minimumHingeAlignment=1,maximumEdgeStretch=0;
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
                var edges=new HashSet<(int A,int B)>();var indices=arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                for(int i=0;i<indices.Length;i+=3)for(int j=0;j<3;j++)
                {
                    int a=indices[i+j],b=indices[i+(j+1)%3];
                    if(points[a].Y>1&&points[b].Y>1&&points[a].DistanceSquaredTo(points[b])>1e-10f)edges.Add(a<b?(a,b):(b,a));
                }
                var posed=new Vector3[points.Length];
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
                        posed[i]=point;
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
                    foreach(var edge in edges)
                    {
                        float stretch=posed[edge.A].DistanceTo(posed[edge.B])/points[edge.A].DistanceTo(points[edge.B]);
                        maximumEdgeStretch=MathF.Max(maximumEdgeStretch,stretch);
                        if(stretch>4.5f)throw new InvalidOperationException($"Raised shoulder tears an actual garment edge: identity={identity}, side={side}, plane={plane}, frame={frame}, stretch={stretch}, seam={seams[edge.A]}/{seams[edge.B]}");
                    }
                    float actualElevation=MathF.Acos(Math.Clamp((-rig.Arms[side].Basis.Y).Dot(Vector3.Down),-1,1));
                    var expectedGirdle=rig.ShoulderGirdle(side,-rig.Arms[side].Basis.Y);
                    float girdleError=rig.Arms[side].Position.DistanceTo(expectedGirdle.Origin);
                    maximumGirdleError=MathF.Max(maximumGirdleError,girdleError);
                    if(girdleError>.002f)throw new InvalidOperationException($"Shoulder does not follow the humeral girdle arc: side={side}, plane={plane}, frame={frame}, error={girdleError}, upper={-rig.Arms[side].Basis.Y}, actual={rig.Arms[side].Position}, expected={expectedGirdle.Origin}");
                    // Worst midpoint between neighbouring rotations: unlike
                    // checking individual bones, this detects LBS collapse.
                    var rotation=rig.Arms[side].Quaternion;
                    var axis=new Vector3(rotation.X,rotation.Y,rotation.Z).Normalized();
                    var radial=axis.Cross(MathF.Abs(axis.Y)<.9f?Vector3.Up:Vector3.Right).Normalized();
                    for(int span=0;span<ResidentRig.SleeveSpans;span++)
                    {
                        var platform=expectedGirdle.Basis.GetRotationQuaternion();
                        var a=ResidentRig.SleeveRotation(platform,rotation,(float)span/ResidentRig.SleeveSpans);
                        var b=ResidentRig.SleeveRotation(platform,rotation,(float)(span+1)/ResidentRig.SleeveSpans);
                        float radius=((a*radial+b*radial)*.5f).Length();
                        minimumRadius=MathF.Min(minimumRadius,radius);
                        if(radius<.94f)throw new InvalidOperationException($"Raised sleeve loses rotational radius: identity={identity}, side={side}, plane={plane}, frame={frame}, radius={radius}, interval={span}, arm={rotation}, platform={platform}");
                    }
                    poses++;
                }
            }
            finally{rig.Free();}
        }
        GD.Print($"ELEVATION_SURFACE_PASS: poses={poses}, two adult frames, both arms, 0–135 degrees, three planes, raise/lower, minimum interval radius={minimumRadius:F4}, mid-sleeve radial extent={maximumSleeveRadius:F4} m, girdle error={maximumGirdleError:F5} m, finite skin and attached cuffs");
        GD.Print($"SIGNED_ELBOW_FRAME_PASS: settled raised samples={hingeSamples}, minimum flexion-axis alignment={minimumHingeAlignment:F4}; no unsigned hinge flip");
        GD.Print($"ELEVATION_EDGE_PASS: actual morphed/skinned shoulder edges, maximum stretch={maximumEdgeStretch:F3}, both arms and three elevation planes");
    }
}
