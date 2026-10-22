"""Build a sewn shirt from a sampled torso pattern, without a voxel union.

The two armholes share vertex indices with their sleeves. Neck, hem and cuffs
are open boundaries; no hidden solid caps or disconnected shoulder patches.
Coordinates are in the client's proportioned rest space (metres, Y up).
"""
import math


def build_pattern(data):
    if data['schemaVersion'] != 2:
        raise ValueError('Expected sampled garment pattern schema 2')
    rows, cols = data['rings'], data['sides']
    values = data['positions']
    if len(values) != (rows+1)*cols*3 or cols % 4:
        raise ValueError('Invalid pattern dimensions')
    points = [tuple(values[i:i+3]) for i in range(0,len(values),3)]
    weights = [(1.,0.,0.) for _ in points]
    faces = []
    idx = lambda r,c: r*cols+c%cols
    shoulder = data['shoulder']
    # The armhole reaches the shoulder slope, leaving the neckline untouched.
    low = min(range(rows),key=lambda r:abs(points[idx(r,0)][1]-(shoulder-.085)))
    high = min(range(rows),key=lambda r:abs(points[idx(r,0)][1]-(shoulder+.047)))
    half = 5
    def in_hole(r,c):
        return low <= r < high and any((c-center)%cols < half or (center-c)%cols <= half for center in (0,cols//2))
    for r in range(rows):
        for c in range(cols):
            if not in_hole(r,c):
                faces.append((idx(r,c),idx(r+1,c),idx(r+1,c+1),idx(r,c+1)))
    for center,side,bone in ((0,1,2),(cols//2,-1,1)):
        # Clockwise boundary in the unwrapped torso panel.
        loop = ([idx(low,c) for c in range(center-half,center+half)] +
                [idx(r,center+half) for r in range(low,high)] +
                [idx(high,c) for c in range(center+half,center-half,-1)] +
                [idx(r,center-half) for r in range(high,low,-1)])
        anchor = [points[i] for i in loop]
        phases = [math.atan2(p[2]/.085,(p[1]-(shoulder-.018))/.066) for p in anchor]
        prev = loop
        for row in range(1,13):
            t=row/12
            # A set-in sleeve bends downward from its sewn armhole. The crown
            # is narrow and sloped, rather than a sphere perched on the arm.
            cx=side*(.202+.004*math.sin(math.pi*t))
            cy=shoulder-.263*t
            rx=.047*(1-t)+.043*t
            rz=.046*(1-t)+.042*t
            blend=min(1,t*3);blend=blend*blend*(3-2*blend)
            ring=[]
            for p,phase in zip(anchor,phases):
                target=(cx+side*rx*math.cos(phase),cy,rz*math.sin(phase))
                q=tuple(p[i]*(1-blend)+target[i]*blend for i in range(3))
                # Broad sleeve compression follows the inside elbow, not
                # periodic bumps over the entire cloth surface.
                fold=.0024*math.exp(-((t-.76)/.19)**2)*math.sin(phase*3+t*13)
                q=(q[0]+side*fold*math.cos(phase),q[1],q[2]+fold*math.sin(phase))
                ring.append(len(points));points.append(q)
                w=min(1,t*3);w=w*w*(3-2*w)
                weights.append((1-w,w if bone==1 else 0.,w if bone==2 else 0.))
            for j in range(len(loop)):
                k=(j+1)%len(loop);faces.append((prev[j],prev[k],ring[k],ring[j]))
            prev=ring
    # Turn the skirt under into a thin inner facing. It remains open, shares
    # the visible hem vertices, and follows the merchant coat's front vents.
    prev=[idx(0,c) for c in range(cols)]
    for rise in (.002,.010):
        ring=[]
        for c in range(cols):
            x,y,z=points[idx(0,c)]
            ring.append(len(points));points.append((x*.984,y+rise,z*.984));weights.append((1.,0.,0.))
        for c in range(cols):
            k=(c+1)%cols;faces.append((prev[c],prev[k],ring[k],ring[c]))
        prev=ring
    # Remove vertices inside the cut-out armholes; there are no unused points.
    used=sorted({i for f in faces for i in f});mapping={v:i for i,v in enumerate(used)}
    return [points[i] for i in used],[tuple(mapping[i] for i in f) for f in faces],[weights[i] for i in used]
