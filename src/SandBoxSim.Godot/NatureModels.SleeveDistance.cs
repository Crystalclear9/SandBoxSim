using System;
using System.Collections.Generic;
using Godot;

namespace SandBoxSim.Client;

internal sealed partial class NatureModels
{
    // Rest-space geodesic distance from the shared armhole. Pattern UVs are
    // construction coordinates: a folded cap can put distant UV rows only a
    // millimetre apart. Using them directly for corrective strength tears it.
    // Solve once on the fitted cached mesh, never during animation.
    private static float[] SleeveDistances(Vector3[] points,int[] indices,Vector2[] pattern)
    {
        var neighbours=new List<int>[points.Length];
        for(int i=0;i<points.Length;i++)neighbours[i]=new();
        for(int i=0;i<indices.Length;i+=3)
            for(int j=0;j<3;j++)
            {
                int a=indices[i+j],b=indices[i+(j+1)%3];
                neighbours[a].Add(b);neighbours[b].Add(a);
            }
        var distance=new float[points.Length];Array.Fill(distance,float.PositiveInfinity);
        var queue=new PriorityQueue<int,float>();
        for(int i=0;i<points.Length;i++)if(pattern[i].X<=0){distance[i]=0;queue.Enqueue(i,0);}
        while(queue.TryDequeue(out int a,out float travelled))
        {
            if(travelled>distance[a])continue;
            foreach(int b in neighbours[a])
            {
                float next=travelled+points[a].DistanceTo(points[b]);
                if(next>=distance[b])continue;
                distance[b]=next;queue.Enqueue(b,next);
            }
        }
        for(int i=0;i<distance.Length;i++)
            if(!float.IsFinite(distance[i]))throw new InvalidOperationException("Sleeve has no path to its sewn armhole");
        return distance;
    }
    private static float[] GirdleWeights(Vector3[] points,int[] indices,Vector2[] pattern)
    {
        var neighbours=new HashSet<int>[points.Length];for(int i=0;i<points.Length;i++)neighbours[i]=new();
        for(int i=0;i<indices.Length;i+=3)for(int j=0;j<3;j++)
        {int a=indices[i+j],b=indices[i+(j+1)%3];neighbours[a].Add(b);neighbours[b].Add(a);}
        var distance=new float[points.Length];Array.Fill(distance,float.PositiveInfinity);
        var queue=new PriorityQueue<int,float>();
        for(int a=0;a<points.Length;a++)if(pattern[a].X<=0)
            foreach(int b in neighbours[a])if(pattern[b].X>0){distance[a]=0;queue.Enqueue(a,0);break;}
        while(queue.TryDequeue(out int a,out float travelled))
        {
            if(travelled>distance[a])continue;
            foreach(int b in neighbours[a])
            {
                if(pattern[b].X>0)continue;
                float next=travelled+points[a].DistanceTo(points[b]);
                if(next>=distance[b])continue;distance[b]=next;queue.Enqueue(b,next);
            }
        }
        var result=new float[points.Length];
        for(int i=0;i<points.Length;i++)
        {
            // Shoulder panels move, the neck opening, sternum and waist do
            // not. Distance is on the sewn surface, not a Y-only chest mask.
            result[i]=pattern[i].X>0?1:(1-Mathf.SmoothStep(0,.16f,distance[i]))
                *Mathf.SmoothStep(.04f,.105f,MathF.Abs(points[i].X))*Mathf.SmoothStep(.96f,1.075f,points[i].Y);
        }
        return result;
    }
    private static float[] SleeveInfluences(Vector3[] points,int[] indices,Vector2[] pattern,float[] seamDistance)
    {
        var neighbours=new HashSet<int>[points.Length];for(int i=0;i<points.Length;i++)neighbours[i]=new();
        for(int i=0;i<indices.Length;i+=3)for(int j=0;j<3;j++)
        {int a=indices[i+j],b=indices[i+(j+1)%3];neighbours[a].Add(b);neighbours[b].Add(a);}
        var cuff=new float[points.Length];Array.Fill(cuff,float.PositiveInfinity);
        var queue=new PriorityQueue<int,float>();
        for(int i=0;i<points.Length;i++)if(pattern[i].X>=.2629f){cuff[i]=0;queue.Enqueue(i,0);}
        while(queue.TryDequeue(out int a,out float travelled))
        {
            if(travelled>cuff[a])continue;
            foreach(int b in neighbours[a])
            {
                if(pattern[b].X<0)continue;
                float next=travelled+points[a].DistanceTo(points[b]);
                if(next>=cuff[b])continue;cuff[b]=next;queue.Enqueue(b,next);
            }
        }
        var value=new float[points.Length];
        for(int i=0;i<value.Length;i++)if(pattern[i].X>0)
            value[i]=pattern[i].X>=.2629f?1:seamDistance[i]/(seamDistance[i]+cuff[i]);
        // Harmonic interpolation is solved only once per fitted garment.
        // Edge-length weights keep the dense cap rings from behaving like
        // equal-length pattern rows. Both sewn boundary and cuff are exact.
        for(int iteration=0;iteration<360;iteration++)
            for(int i=0;i<value.Length;i++)if(pattern[i].X>0&&pattern[i].X<.2629f)
            {
                double sum=0,total=0;foreach(int j in neighbours[i])
                {float w=1/MathF.Max(.00001f,points[i].DistanceTo(points[j]));sum+=value[j]*w;total+=w;}
                value[i]=(float)(sum/total);
            }
        return value;
    }
}
