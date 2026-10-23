"""Measure the visible eye aperture against the actual exported skin surface."""
import math
from mathutils import Vector
from mathutils.bvhtree import BVHTree


def eye_apertures(head, eyes):
    def tree(surface):
        values=surface['positions']; points=[Vector(values[i:i+3]) for i in range(0,len(values),3)]
        indices=surface['indices']
        return BVHTree.FromPolygons(points,[indices[i:i+3] for i in range(0,len(indices),3)],all_triangles=True),points
    skin,_=tree(head)
    def depth(bvh,x,y):
        hit,_,_,_=bvh.ray_cast(Vector((x,y,-.35)),Vector((0,0,1)),.6)
        return hit.z if hit is not None else None
    result=[]
    for eye in eyes:
        globe,points=tree(eye)
        x0,x1=min(p.x for p in points),max(p.x for p in points)
        y0,y1=min(p.y for p in points),max(p.y for p in points)
        def visible(x,y):
            a,b=depth(globe,x,y),depth(skin,x,y)
            return a is not None and (b is None or a<b-.00004)
        def interval(x):
            ys=[y0+(y1-y0)*i/160 for i in range(161)]
            found=[i for i,y in enumerate(ys) if visible(x,y)]
            if not found:return None
            lo,hi=found[0],found[-1]
            def refine(a,b,lower):
                for _ in range(14):
                    mid=(a+b)*.5
                    if visible(x,mid)==lower:b=mid
                    else:a=mid
                return (a+b)*.5
            low=refine(ys[max(0,lo-1)],ys[lo],True)
            high=refine(ys[hi],ys[min(160,hi+1)],False)
            return low,high
        xs=[x0+(x1-x0)*(i+.5)/128 for i in range(128)]
        valid=[x for x in xs if interval(x)]
        if not valid:raise ValueError('Source eye is completely occluded by the exported face')
        left,right=min(valid),max(valid)
        upper,lower=[],[]
        for i in range(49):
            x=left+(right-left)*i/48
            low,high=interval(x)
            if i in (0,48):low=high=(low+high)*.5
            for y,dest in ((high,upper),(low,lower)):
                z=depth(globe,x,y)
                if z is None:z=depth(skin,x,y)
                dest.append([round(x,7),round(y,7),round(z,7)])
        if max(u[1]-l[1] for u,l in zip(upper,lower))<.002:
            raise ValueError('Source eye aperture is degenerate')
        result.append({'upper':upper,'lower':lower})
    return result
