"""Verified, shared adult shape recipe for torso, arm and trouser exporters.

Values are art-directed source blend weights, not population measurements or
sex-classification thresholds. Faces and hands have their own source pipeline.
"""
import hashlib
import json
from pathlib import Path

ASSETS = Path(__file__).resolve().parents[1]/'src/SandBoxSim.Godot/assets/models/human'


def read_source(source, name):
    record = json.loads((ASSETS/'head-sources.json').read_text(encoding='utf-8'))['files'][name]
    raw = (Path(source)/name).read_bytes()
    if len(raw) != record['bytes'] or hashlib.sha256(raw).hexdigest() != record['sha256']:
        raise ValueError('Changed resident source: '+name)
    return raw.decode('utf-8')


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
        for line in read_source(source, name).splitlines():
            fields = line.split()
            if len(fields) == 4 and not line.startswith('#'):
                p = points[int(fields[0])]
                for axis in range(3):
                    p[axis] += float(fields[axis+1])*weight
    return points


def body_provenance(frame):
    raw = (ASSETS/'body-profiles.json').read_bytes().replace(b'\r\n', b'\n')
    manifest = json.loads((ASSETS/'head-sources.json').read_text(encoding='utf-8'))['files']
    names = ('base.obj', 'basemesh_vertex_groups.json', 'caucasian-male-young.target', f'caucasian-{frame}-young.target', *recipe(frame)['targets'])
    return dict(profile='metric-physique-v20', profileSha256=hashlib.sha256(raw).hexdigest(),
                coordinateFrame='uniform-baseline-shoulder-hip-v1',
                targets=recipe(frame)['targets'], sourceSha256={n: manifest[n]['sha256'] for n in names})
