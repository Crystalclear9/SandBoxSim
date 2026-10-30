"""Convert one attributed CMU ASF/AMC capture to body-relative arm directions.

No third-party packages. Root translation, noisy fingers/thumbs and body size
are deliberately excluded; the client retargets directions to its own limbs.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path


def mm(a,b):return [[sum(a[i][k]*b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]
def mv(a,v):return tuple(sum(a[i][k]*v[k] for k in range(3)) for i in range(3))
def tr(a):return list(map(list,zip(*a)))
def sub(a,b):return tuple(x-y for x,y in zip(a,b))
def unit(v):
    length=math.sqrt(sum(x*x for x in v))
    if length<1e-9:raise ValueError('Zero capture direction')
    return tuple(x/length for x in v)
def euler(values):
    x,y,z=map(math.radians,values);cx,sx=math.cos(x),math.sin(x);cy,sy=math.cos(y),math.sin(y);cz,sz=math.cos(z),math.sin(z)
    return mm(mm([[cz,-sz,0],[sz,cz,0],[0,0,1]],[[cy,0,sy],[0,1,0],[-sy,0,cy]]),[[1,0,0],[0,cx,-sx],[0,sx,cx]])


def parse_asf(text):
    bones={};children={};section='';bone=None
    for line in text.splitlines():
        parts=line.split()
        if not parts or parts[0].startswith('#'):continue
        if parts[0].startswith(':'):section=parts[0];continue
        if section==':bonedata':
            if parts[0]=='begin':bone={'dof':[]}
            elif parts[0]=='end':bones[bone['name']]=bone
            elif parts[0]=='name':bone['name']=parts[1]
            elif parts[0]=='direction':bone['direction']=tuple(map(float,parts[1:]))
            elif parts[0]=='length':bone['length']=float(parts[1])
            elif parts[0]=='axis':
                if parts[4]!='XYZ':raise ValueError('Unsupported ASF axis order')
                bone['axis']=euler(map(float,parts[1:4]))
            elif parts[0]=='dof':bone['dof']=parts[1:]
        elif section==':hierarchy' and parts[0] not in ('begin','end'):children[parts[0]]=parts[1:]
    return bones,children


def parse_amc(text):
    result=[];frame=None
    for line in text.splitlines():
        parts=line.split()
        if not parts or parts[0][0] in '#:':continue
        if parts[0].isdigit():
            if int(parts[0])!=len(result)+1:raise ValueError('Non-contiguous AMC frames')
            frame={};result.append(frame)
        else:frame[parts[0]]=list(map(float,parts[1:]))
    return result


def forward(bones,children,frame):
    rotations={'root':euler(frame['root'][3:])};points={'root':tuple(frame['root'][:3])}
    def walk(parent):
        for name in children.get(parent,[]):
            bone=bones[name];channels=dict(zip(bone['dof'],frame.get(name,[])))
            rotation=euler([channels.get('r'+axis,0) for axis in 'xyz']);c=bone['axis']
            rotations[name]=mm(mm(mm(rotations[parent],c),rotation),tr(c))
            delta=mv(rotations[name],bone['direction'])
            points[name]=tuple(points[parent][i]+bone['length']*delta[i] for i in range(3));walk(name)
    walk('root');return rotations,points


def export(folder):
    hashes={'143.asf':'6d7207da3bb9eceeaa32f23fb641b69f5836d11242b00c052e7d64f0cbe20a35',
            '143_30.amc':'ffd7b2b3344cf41eef16a3b9b2910424dc27c24ac1f4d6c3b68b6c719e7b22e3'}
    for name,digest in hashes.items():
        if hashlib.sha256((folder/name).read_bytes()).hexdigest()!=digest:raise ValueError('Changed capture source: '+name)
    bones,children=parse_asf((folder/'143.asf').read_text(encoding='utf-8'))
    frames=parse_amc((folder/'143_30.amc').read_text(encoding='utf-8'));samples=[]
    def mapped(v):return (-v[0],v[1],-v[2])
    for frame in frames:
        rotations,points=forward(bones,children,frame);body=tr(rotations['thorax']);sample=[]
        for side in ('l','r'):
            for a,b in [(side+'clavicle',side+'humerus'),(side+'humerus',side+'radius')]:
                sample.extend(round(x,7) for x in unit(mapped(mv(body,sub(points[b],points[a])))))
        # Body rotation relative to the root, re-expressed after a 180° Y
        # coordinate change. Store basis columns; no Euler-order ambiguity.
        chest=mm(tr(rotations['root']),rotations['thorax']);sign=(-1,1,-1)
        sample.extend(round(chest[i][j]*sign[i]*sign[j],7) for j in range(3) for i in range(3))
        # Wrist pronation is measured; finger/thumb channels are not. The
        # hand's ASF rest axes identify its long axis and dorsal normal.
        for side in ('l','r'):
            palm=mm(mm(body,rotations[side+'wrist']),bones[side+'hand']['axis'])
            sample.extend(round(palm[i][j]*sign[i],7) for j in range(3) for i in range(3))
        samples.append(sample)
    return {'schemaVersion':1,'source':'CMU Graphics Lab 143_30, Stretch And Yawn','sourceUrl':'http://mocap.cs.cmu.edu/search.php?subjectnumber=143',
            'license':'LicenseRef-CMU-Mocap','acknowledgment':'The data used in this project was obtained from mocap.cs.cmu.edu. The database was created with funding from NSF EIA-0196217.',
            'sourceSha256':hashes,'fps':120,'frameCount':len(samples),'layout':'leftUpper,leftLower,rightUpper,rightLower,chestBasisColumns,leftWristBasisColumns,rightWristBasisColumns','samples':samples}


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--source',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);args=parser.parse_args()
    data=export(args.source);args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(data,separators=(',',':'))+'\n',encoding='utf-8',newline='\n')
    print('CMU_ARM_MOTION_EXPORTED',data['frameCount'],hashlib.sha256(args.output.read_bytes()).hexdigest())
