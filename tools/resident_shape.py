"""Bounded adult soft-tissue art direction in canonical metres.

These are character design choices, not anatomical measurements. Joint helper
vertices stay fixed; the same smooth field carries skin and clothing helpers.
"""
import math


def smooth(a,b,x):
    t=max(0.,min(1.,(x-a)/(b-a)))
    return t*t*t*(10+t*(-15+6*t))


def soft_contour(point):
    x,y,z=point
    side=1 if x>=0 else -1
    bell=lambda h,w:math.exp(-((y-h)/w)**2)
    lateral=smooth(.045,.095,abs(x))
    # A broad pelvic shelf and gentle waist transition establish the large
    # silhouette. The former negative pelvic offset erased most frame contrast.
    dx=side*lateral*(.0005*bell(1.02,.070)+.006*bell(.80,.100))
    front=smooth(.030,.085,-z)
    chest=math.exp(-((abs(x)-.055)/.075)**2)
    # Redistribute the anterior thoracic surface upward, rather than adding
    # volume to its lower pole. Keep a broad transition into the upper abdomen.
    dy=.016*front*chest*bell(1.125,.060)*(1-smooth(1.20,1.25,y))
    dz=front*chest*(-.004*bell(1.165,.055)+.003*bell(1.080,.040))
    dz+=smooth(.008,.035,z)*.0025*bell(1.015,.075)
    # A broad lateral-thigh transition; no displacement of knees/ankles or
    # the medial join. Torso and thigh envelopes overlap continuously.
    thigh=bell(.635,.080)*smooth(.090,.135,abs(x))
    dx-=side*.0025*thigh
    return x+dx,y+dy,z+dz


def apply_soft_contours(points,groups,scale):
    def mean(name):
        ids=[i for a,b in groups[name] for i in range(a,b+1)]
        return [sum(points[i][j] for i in ids)/len(ids) for j in range(3)]
    shoulder=mean('joint-l-shoulder')
    pinned={i for name,ranges in groups.items() if name.startswith('joint-') for a,b in ranges for i in range(a,b+1)}
    result=[]
    for i,p in enumerate(points):
        q=(p[0]*scale,1.23+(p[1]-shoulder[1])*scale,-(p[2]-shoulder[2])*scale)
        # The source arms may be alongside the waist in an A-pose. A field
        # bounded to the torso/thigh lateral region leaves those limbs intact.
        weight=1-smooth(.17,.21,abs(q[0]))
        if i in pinned or weight==0:
            result.append(p[:]);continue
        fitted=soft_contour(q)
        offset=[(b-a)*weight for a,b in zip(q,fitted)]
        if math.sqrt(sum(v*v for v in offset))>.018:raise ValueError('Thoracic contour exceeds its 18 mm source bound')
        result.append([p[0]+offset[0]/scale,p[1]+offset[1]/scale,p[2]-offset[2]/scale])
    return result
