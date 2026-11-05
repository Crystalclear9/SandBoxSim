"""Retarget selected CMU standing action recordings into compact motion curves."""
import argparse
import hashlib
import json
import math
from pathlib import Path

from export_cmu_arm_motion import forward, mm, mv, parse_amc, parse_asf, sub, tr


SAMPLES = 64
SOURCES = {
    '02_06': ('c9f5ff45b4437b279f58b95dacf017afd3135373096274df69436a9354d796cf',
              '410294c104bf6c4cbcc7f5757e8a24d012beca0090ab96779b7b1611d5a3fdc1'),
    '18_08': ('1b85e296d50b0c1bbc87b154486ea5192ec4d99748e7bd9e9fb0419a0be8214b',
              '0c6295c0d06385ca51b500f82365f601d2314055825527ffbf537f78410407a4'),
    '13_09': ('eb33390bcbccb739d7622bd583fd9be55020868f68689aa6f2060ad688df10dc',
              'd4cf7d478b417e7323358849638d7311f31f6cbd392f5e1a759bb2d3548bf09b'),
}
# Zero-based source frames. Use only the reach and recovery around the
# selected bend, leaving out the subsequent overhead arm display.
PICKUP = ((18, 259), (1014, 1242))
SOCIAL = ((0, 95), (1182, 1384))
DRINK = (128, 225, 330, 480)


def capture(root, trial):
    subject = trial[:2]
    folder = root / f'cmu-{subject}'
    names = (f'{subject}.asf', f'{trial}.amc')
    files = [(folder / name).read_bytes() for name in names]
    hashes = tuple(hashlib.sha256(file).hexdigest() for file in files)
    if hashes != SOURCES[trial]:
        raise ValueError(f'Changed capture source: {trial}')
    bones, tree = parse_asf(files[0].decode('utf-8'))
    poses = [forward(bones, tree, frame) for frame in parse_amc(files[1].decode('utf-8'))]
    return poses, dict(zip(names, hashes))


def motion_channels(poses, first, last, extract, remap=lambda t: t):
    start_frame, _ = poses[first]
    root = start_frame['root']
    heading = math.atan2(root[0][2], root[2][2])
    c, s = math.cos(heading), math.sin(heading)
    level = [[c, 0, -s], [0, 1, 0], [s, 0, c]]
    source = [extract(level, *pose) for pose in poses[first:last + 1]]
    result = []
    for row in range(SAMPLES + 1):
        t = remap(row / SAMPLES)
        position = t * (last - first)
        index = min(int(position), len(source) - 2)
        u = position - index
        # Close each phrase by removing the end pose drift. A short taper
        # keeps both its value and tangent quiet across the repeated seam.
        ramp = min(1, min(t, 1 - t) / .10)
        edge = ramp * ramp * (3 - 2 * ramp)
        result.append(tuple((a + (b - a) * u - (x + (y - x) * t)) * edge
                            for a, b, x, y in zip(source[index], source[index + 1], source[0], source[-1])))
    return list(zip(*result))


def pitch(m):
    return math.asin(max(-1, min(1, m[1][2])))


def pickup_channels(level, rotation, point):
    chest = mm(level, rotation['thorax'])
    head = mm(tr(rotation['thorax']), rotation['head'])
    left = mv(tr(rotation['thorax']), sub(point['lwrist'], point['thorax']))
    hip = mv(level, point['root'])
    shoulder_yaw = math.atan2(chest[2][0], chest[0][0])
    return -pitch(chest), -hip[1], -hip[0], -point['rhand'][1], left[2], pitch(head), shoulder_yaw


def social_channels(level, rotation, point):
    chest = mm(level, rotation['thorax'])
    head = mm(tr(rotation['thorax']), rotation['head'])
    l, r = [mv(tr(rotation['thorax']), sub(point[side + 'wrist'], point['thorax'])) for side in 'lr']
    return (pitch(chest), math.atan2(chest[0][2], chest[2][2]), math.atan2(-chest[1][0], chest[1][1]),
            pitch(head), l[0], l[1], l[2], -r[0], r[1], r[2])


def drink_channels(level, rotation, _point):
    return pitch(mm(tr(rotation['thorax']), rotation['head'])), pitch(mm(level, rotation['thorax']))


def normalized(channel, shared_scale=None, positive=False):
    scale = max(abs(v) for v in channel) if shared_scale is None else shared_scale
    return [round(max(0, v / scale) if positive else v / scale, 6) if scale > 1e-6 else 0 for v in channel]


def export(root):
    pickups, p_hash = capture(root, '02_06')
    socials, s_hash = capture(root, '18_08')
    drinks, d_hash = capture(root, '13_09')
    pickup = [list(zip(*(normalized(values, positive=i in (0, 1, 3, 4))
                         for i, values in enumerate(motion_channels(pickups, *window, pickup_channels)))))
              for window in PICKUP]
    social = []
    for window in SOCIAL:
        values = motion_channels(socials, *window, social_channels)
        lift = max(abs(v) for i in (5, 8) for v in values[i])
        social.append(list(zip(*(normalized(channel, lift if i >= 4 else None, i in (5, 8))
                                 for i, channel in enumerate(values)))))
    first, contact, tilt, last = DRINK
    phase_marks = (0, .41, .59, 1)
    source_marks = tuple((frame - first) / (last - first) for frame in DRINK)

    def drink_phase(t):
        i = next(i for i in range(3) if t <= phase_marks[i + 1])
        u = (t - phase_marks[i]) / (phase_marks[i + 1] - phase_marks[i])
        return source_marks[i] + (source_marks[i + 1] - source_marks[i]) * u

    drink = list(zip(*(normalized(values) for values in motion_channels(drinks, first, last, drink_channels, drink_phase))))
    return {'schemaVersion': 1, 'license': 'LicenseRef-CMU-Mocap', 'fps': 120, 'samples': SAMPLES + 1,
            'pickup': {'source': '02_06', 'frames': PICKUP, 'sourceSha256': p_hash, 'curves': pickup},
            'social': {'source': '18_08', 'frames': SOCIAL, 'sourceSha256': s_hash, 'curves': social},
            'drink': {'source': '13_09', 'frames': DRINK, 'sourceSha256': d_hash, 'curves': [drink]}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.write_text(json.dumps(export(args.source), separators=(',', ':')) + '\n', encoding='utf-8', newline='\n')
    print('CMU_ACTIONS_EXPORTED', hashlib.sha256(args.output.read_bytes()).hexdigest())
