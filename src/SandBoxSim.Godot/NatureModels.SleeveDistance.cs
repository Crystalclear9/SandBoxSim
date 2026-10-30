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
}
