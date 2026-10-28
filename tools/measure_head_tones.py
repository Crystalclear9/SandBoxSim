"""Measure linear forehead luminance in the pinned source head atlases.

Reads assets only; prints calibration values for the runtime skin shader.
Requires Pillow. UVs are the runtime top-left image coordinates.
"""
import json
from pathlib import Path
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]/'src/SandBoxSim.Godot/assets/models/human'

def measure(name):
    data=json.loads((ROOT/(name+'.mesh.json')).read_text(encoding='utf-8'))
    mesh=data['head'];p=mesh['positions'];uv=mesh['uv'];ids=mesh['indices']
    image=Image.open(ROOT/data['albedo']['file']).convert('RGB')
    total=weight=0.;count=0
    def linear(v):
        v=v/255
        return v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4
    for i in range(0,len(ids),3):
        a,b,c=ids[i:i+3];points=[p[j*3:j*3+3] for j in (a,b,c)]
        x,y,z=[sum(t[k] for t in points)/3 for k in range(3)]
        if not(abs(x)<.040 and .040<y<.085 and z<-.035):continue
        u=sum(uv[j*2] for j in (a,b,c))/3;v=sum(uv[j*2+1] for j in (a,b,c))/3
        rgb=image.getpixel((min(image.width-1,max(0,int(u*image.width))),min(image.height-1,max(0,int(v*image.height)))))
        ab=[points[1][k]-points[0][k] for k in range(3)];ac=[points[2][k]-points[0][k] for k in range(3)]
        area=sum((ab[(k+1)%3]*ac[(k+2)%3]-ab[(k+2)%3]*ac[(k+1)%3])**2 for k in range(3))**.5
        total+=sum(linear(value)*channel for value,channel in zip(rgb,(.2126,.7152,.0722)))*area
        weight+=area;count+=1
    if count<20 or weight<=0:raise ValueError('Missing source forehead region: '+name)
    return {'linearLuminance':total/weight,'triangles':count}

if __name__=='__main__':
    measurements={name:measure(name) for name in ('human-head','human-scan','human-feminine')}
    reference=measurements['human-head']['linearLuminance']
    for value in measurements.values():value['gain']=max(.7,min(1.4,reference/value['linearLuminance']))
    print(json.dumps(measurements,indent=2))
