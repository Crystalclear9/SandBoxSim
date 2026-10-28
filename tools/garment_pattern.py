"""Build a sewn shirt from a sampled torso pattern, without a voxel union.

The two armholes share vertex indices with their sleeves. Neck, hem and cuffs
are open boundaries; no hidden solid caps or disconnected shoulder patches.
Coordinates are in the client's proportioned rest space (metres, Y up).
"""
import math


def torso_shoulder_weight(point, shoulder):
    """Local side-chest support; neckline, centre front and belt stay fixed.

    A sleeve's lower armhole is already below the shoulder joint in bind
    space. Pinning it to the chest creates a sheet when the upper arm lifts.
    Use a restrained, short support field: moving the whole side chest with
    the arm creates a wing even if the shared seam remains connected.
    """
    def smooth(a,b,x):
        t=min(1,max(0,(x-a)/(b-a)))
        return t*t*(3-2*t)
    x,y,_=point
    along=shoulder-y
    return .38*smooth(.075,.205,abs(x))*smooth(-.020,.035,along)*(1-smooth(.045,.175,along))


def build_pattern(data, include_details=False):
    if data['schemaVersion'] != 2:
        raise ValueError('Expected sampled garment pattern schema 2')
    rows, cols = data['rings'], data['sides']
    values = data['positions']
    if len(values) != (rows+1)*cols*3 or cols % 4:
        raise ValueError('Invalid pattern dimensions')
    points = [tuple(values[i:i+3]) for i in range(0,len(values),3)]
    shoulder = data['shoulder']
    weights=[]
    for p in points:
        w=torso_shoulder_weight(p,shoulder)
        weights.append((1-w,w if p[0]<0 else 0.,w if p[0]>=0 else 0.))
    # Metres along the sleeve and across its underarm seam. Non-sleeve
    # vertices use a negative sentinel, so trimming cannot mark the torso.
    details = [(-1.,0.) for _ in points]
    faces = []
    sleeve_free=[]
    idx = lambda r,c: r*cols+c%cols
    # The armhole reaches the shoulder slope, leaving the neckline untouched.
    low = min(range(rows),key=lambda r:abs(points[idx(r,0)][1]-(shoulder-.085)))
    high = min(range(rows),key=lambda r:abs(points[idx(r,0)][1]-(shoulder+.047)))
    half = 3
    def in_hole(r,c):
        return low <= r < high and any((c-center)%cols < half or (center-c)%cols <= half for center in (0,cols//2))
    for r in range(rows):
        for c in range(cols):
            if not in_hole(r,c):
                faces.append((idx(r,c),idx(r+1,c),idx(r+1,c+1),idx(r,c+1)))
    panel_normals=[[0.,0.,0.] for _ in points]
    for face in faces:
        n=[sum(points[a][(j+1)%3]*points[b][(j+2)%3]-points[a][(j+2)%3]*points[b][(j+1)%3] for a,b in zip(face,face[1:]+face[:1])) for j in range(3)]
        for i in face:
            for j in range(3):panel_normals[i][j]+=n[j]
    for center,side,bone in ((0,1,2),(cols//2,-1,1)):
        # Clockwise boundary in the unwrapped torso panel.
        loop = ([idx(low,c) for c in range(center-half,center+half)] +
                [idx(r,center+half) for r in range(low,high)] +
                [idx(high,c) for c in range(center+half,center-half,-1)] +
                [idx(r,center-half) for r in range(high,low,-1)])
        anchor = [points[i] for i in loop]
        phases = [math.atan2(p[2]/.085,(p[1]-(shoulder-.018))/.066) for p in anchor]
        for i,phase in zip(loop,phases):
            details[i]=(0.,abs(math.remainder(phase-math.pi,math.tau))*.045)
        tangents=[]
        for index,p in zip(loop,anchor):
            r,c=divmod(index,cols)
            rel=(c-center+cols//2)%cols-cols//2
            outside=[]
            if r==low:outside.append(points[idx(r-1,c)])
            if r==high:outside.append(points[idx(r+1,c)])
            if rel==-half:outside.append(points[idx(r,c-1)])
            if rel==half:outside.append(points[idx(r,c+1)])
            direction=tuple(sum(p[j]-q[j] for q in outside) for j in range(3))
            normal=panel_normals[index];n2=sum(v*v for v in normal)
            projection=sum(direction[j]*normal[j] for j in range(3))/n2
            direction=tuple(direction[j]-normal[j]*projection for j in range(3))
            length=math.sqrt(sum(q*q for q in direction))
            speed=.06+.08*min(1,max(0,(p[1]-(shoulder-.085))/.132))
            tangents.append(tuple(q/length*speed for q in direction))
        prev = loop
        for row in range(1,19):
            t=row/18
            # A falling sleeve keeps each armhole point's vertical offset
            # until it gradually joins the hanging tube. The old common
            # horizontal target squeezed the whole cap into its first third,
            # producing a hard shelf at the shoulder and a swollen lower ring.
            cx=side*(.202+.004*math.sin(math.pi*t))
            cy=shoulder-.263*t
            rx=.044*(1-t)+.043*t+.0030*math.sin(math.pi*t)**2
            rz=.044*(1-t)+.042*t+.0025*math.sin(math.pi*t)**2
            blend=min(1,t/.53);blend=blend**3*(10-15*blend+6*blend**2)
            ring=[]
            for p,phase,direction in zip(anchor,phases,tangents):
                target=(cx+side*rx*math.cos(phase),cy,rz*math.sin(phase))
                q=(p[0]*(1-blend)+target[0]*blend,
                   cy+(p[1]-shoulder)*(1-t)**3,
                   p[2]*(1-blend)+target[2]*blend)
                # Extend the neighbouring torso panel's actual tangent across
                # the shared seam. A zero horizontal derivative here creates
                # a vertical ledge even when averaged normals look smooth.
                # This Hermite correction leaves both cuff value and its
                # derivative unchanged; the underarm may turn upward a few mm.
                correction=t*(1-t)**3
                current=(0.,-.263-3*(p[1]-shoulder),0.)
                q=tuple(q[i]+correction*(direction[i]-current[i]) for i in range(3))
                # Two broad diagonal troughs fall from the armhole. A small
                # compression fold is held by the rolled cuff. Both end at
                # actual supports; there is no repeating all-over ripple.
                envelope=math.sin(math.pi*t)**2
                def trough(center,width):
                    d=math.remainder(phase-center,math.tau)
                    return math.exp(-(d/width)**2)
                fall=-.0036*trough(-1.08+.72*t,.34)+.0020*trough(-.62+.72*t,.42)
                fall+=-.0027*trough(1.62-.54*t,.39)+.0015*trough(2.07-.54*t,.46)
                cuff=.0025*math.exp(-((t-.80)/.13)**2)*(trough(2.35,.55)-.65*trough(2.90,.32))
                fold=envelope*(fall+cuff)
                q=(q[0]+side*fold*math.cos(phase),q[1],q[2]+fold*math.sin(phase))
                # Keep the first two rings on the actual panel tangent. The
                # previous asymptotic Hermite derivative was not sufficient
                # at finite ring spacing on the clavicular/front armhole.
                root_blend=min(1,max(0,(t-.11)/.22));root_blend=root_blend*root_blend*(3-2*root_blend)
                q=tuple((p[i]+direction[i]*t)*(1-root_blend)+q[i]*root_blend for i in range(3))
                ring.append(len(points));points.append(q)
                if row<16:sleeve_free.append(len(points)-1)
                details.append((t*.263,abs(math.remainder(phase-math.pi,math.tau))*(rx+rz)*.5))
                # The seam stays anchored to the torso while the hanging
                # fabric takes the shoulder rotation. Its lower, already
                # axillary points share the side chest's local shoulder pull.
                # Upper cap support is lower; cuff weights remain exactly 1.
                # The first two underarm rows turn around the shared seam.
                # Do not ramp bone influence through that compressed fold;
                # start only after the sleeve has separated from the chest.
                w=min(1,max(0,(t-.12)/.48));w=w*w*(3-2*w)
                w=1-(1-w)*(1-torso_shoulder_weight(q,shoulder))
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
            ring.append(len(points));points.append((x*.984,y+rise,z*.984));weights.append((1.,0.,0.));details.append((-1.,0.))
        for c in range(cols):
            k=(c+1)%cols;faces.append((prev[c],prev[k],ring[k],ring[c]))
        prev=ring
    # Harmonic support follows physical edge lengths, not row numbers. Short
    # folded edges then cannot receive a large arbitrary bone-weight jump.
    # The shared armhole is fixed to the torso support field; the distal sleeve
    # remains rigidly attached to the upper arm/cuff.
    adjacency=[set() for _ in points]
    for face in faces:
        for a in face:
            for b in face:
                if a!=b:adjacency[a].add(b)
    field=[w[1]+w[2] for w in weights]
    neighbours=[]
    for i in sleeve_free:
        adjacent=[(j,1/max(1e-8,sum((points[i][k]-points[j][k])**2 for k in range(3)))) for j in adjacency[i]]
        total=sum(w for j,w in adjacent);neighbours.append((i,[(j,w/total) for j,w in adjacent]))
    for _ in range(2400):
        updates=[(i,sum(field[j]*w for j,w in adjacent)) for i,adjacent in neighbours]
        error=max(abs(field[i]-value) for i,value in updates)
        for i,value in updates:field[i]=value
        if error<.000002:break
    # Bound the spatial gradient as well as the average: even a harmonic
    # field can change too quickly across a tiny fold next to a fixed seam.
    constraints=[(i,[(j,5.5*math.dist(points[i],points[j])) for j in adjacency[i]]) for i in sleeve_free]
    for _ in range(1200):
        updates=[]
        for i,adjacent in constraints:
            low=max(field[j]-gap for j,gap in adjacent);high=min(field[j]+gap for j,gap in adjacent)
            value=(low+high)*.5 if low>high else min(high,max(low,field[i]))
            updates.append((i,(field[i]+value)*.5))
        error=max(abs(field[i]-value) for i,value in updates)
        for i,value in updates:field[i]=value
        if error<.0000001:break
    for i in sleeve_free:
        w=field[i];weights[i]=(1-w,w if points[i][0]<0 else 0.,w if points[i][0]>=0 else 0.)
    # Remove vertices inside the cut-out armholes; there are no unused points.
    used=sorted({i for f in faces for i in f});mapping={v:i for i,v in enumerate(used)}
    result=([points[i] for i in used],[tuple(mapping[i] for i in f) for f in faces],[weights[i] for i in used])
    return (*result,[details[i] for i in used]) if include_details else result
