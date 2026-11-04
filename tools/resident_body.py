"""Verified, shared adult shape recipe for torso, arm and trouser exporters.

Values are art-directed source blend weights, not population measurements or
sex-classification thresholds. Faces and hands have their own source pipeline.
"""
import hashlib
import json
from pathlib import Path

ASSETS = Path(__file__).resolve().parents[1]/'src/SandBoxSim.Godot/assets/models/human'


def source_manifest():
    files=json.loads((ASSETS/'head-sources.json').read_text(encoding='utf-8'))['files']
    reference=ASSETS/'reference-shape-sources.json'
    if reference.exists():files.update(json.loads(reference.read_text(encoding='utf-8'))['files'])
    return files


def read_source(source, name):
    record = source_manifest()[name]
    path=ASSETS/name if name in ('reference-shape-male.json','reference-shape-female.json') else Path(source)/name
    raw = path.read_bytes()
    if len(raw) != record['bytes'] or hashlib.sha256(raw).hexdigest() != record['sha256']:
        raise ValueError('Changed resident source: '+name)
    return raw.decode('utf-8')


def limb_weights(source,roots):
    """Semantic source regions include every descendant, including metacarpals."""
    bones=json.loads(read_source(source,'rig/default.mhskel'))['bones']
    selected=set(roots)
    if not selected.issubset(bones):raise ValueError('Missing source limb root')
    while True:
        expanded=selected|{name for name,bone in bones.items() if bone.get('parent') in selected}
        if expanded==selected:break
        selected=expanded
    result={}
    for name,entries in json.loads(read_source(source,'rig/default_weights.mhw'))['weights'].items():
        if name in selected:
            for index,weight in entries:result[index]=result.get(index,0)+weight
    return result


def recipe(frame):
    if frame not in ('male', 'female'):
        raise ValueError('Unknown adult frame')
    return json.loads((ASSETS/'body-profiles.json').read_text(encoding='utf-8'))['frames'][frame]


def body_metric_scale(source):
    """One similarity scale: the baseline shoulder/hip span is 0.43 m.

    Independent X/Y/Z scales formerly broadened the anatomy relative to its
    height. Use the same reference for both adults and all support exporters.
    """
    points=body_points(source,'male',baseline=True)
    groups=json.loads(read_source(source,'basemesh_vertex_groups.json'))
    def height(name):
        ids=[i for a,b in groups[name] for i in range(a,b+1)]
        return sum(points[i][1] for i in ids)/len(ids)
    return .43/(height('joint-l-shoulder')-height('joint-l-upper-leg'))


def body_points(source, frame, *, baseline=False):
    points = [list(map(float, line.split()[1:4])) for line in read_source(source, 'base.obj').splitlines() if line.startswith('v ')]
    targets = {f'caucasian-{frame}-young.target': 1.}
    if not baseline:
        targets.update(recipe(frame)['targets'])
    for name, weight in targets.items():
        if not 0 < weight <= 1:
            raise ValueError('Invalid body target weight')
        text=read_source(source,name)
        if name.startswith('reference-shape-'):
            data=json.loads(text)
            if data['schemaVersion']!=1 or data['license']!='CC0-1.0' or len(data['deltas'])!=len(points)*3:
                raise ValueError('Invalid reference body deformation')
            for i,p in enumerate(points):
                for axis in range(3):p[axis]+=data['deltas'][i*3+axis]*weight
            continue
        for line in text.splitlines():
            fields = line.split()
            if len(fields) == 4 and not line.startswith('#'):
                p = points[int(fields[0])]
                for axis in range(3):
                    p[axis] += float(fields[axis+1])*weight
    if not baseline and recipe(frame).get('surfaceStyle'):
        if recipe(frame)['surfaceStyle']!='balanced-silhouette-v6':raise ValueError('Unknown body surface style')
        from resident_shape import apply_soft_contours
        points=apply_soft_contours(points,json.loads(read_source(source,'basemesh_vertex_groups.json')),body_metric_scale(source))
    return points


def body_provenance(frame):
    raw = (ASSETS/'body-profiles.json').read_bytes().replace(b'\r\n', b'\n')
    manifest = source_manifest()
    names = ('base.obj', 'basemesh_vertex_groups.json', 'caucasian-male-young.target', f'caucasian-{frame}-young.target', *recipe(frame)['targets'])
    return dict(profile=json.loads(raw)['profile'], profileSha256=hashlib.sha256(raw).hexdigest(),
                coordinateFrame='uniform-baseline-shoulder-hip-v1',
                targets=recipe(frame)['targets'], surfaceStyle=recipe(frame).get('surfaceStyle'), sourceSha256={n: manifest[n]['sha256'] for n in names})
