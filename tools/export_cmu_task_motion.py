"""Export body coordination for turns and cultivation from CMU ASF/AMC."""
import argparse
import hashlib
import json
import math
from pathlib import Path

from export_cmu_actions import motion_channels, normalized, pitch
from export_cmu_arm_motion import forward, mm, mv, parse_amc, parse_asf, sub, tr


SOURCES = {
    '47_01': ('58d5d204a5c60ade4c7df08af19c049b43c5d0b484546494a44129e71abc606b',
              '2a3768d612a1cf9e2532c83c8d8415b1568b68dfcfe9508db5404db03b227a67'),
    '80_05': ('5fd77816c7506adcffe141fb4ce679aaf75102d1857e0f2da00e8dec2c49f7cc',
              '24a89fd99741c548f6965fc44e7eeef8c35231398a783fd6fda21a8f00b4b4f2'),
}


def capture(root, trial):
    subject = trial[:2]
    names = (f'{subject}.asf', f'{trial}.amc')
    files = [(root / f'cmu-{subject}' / name).read_bytes() for name in names]
    hashes = tuple(hashlib.sha256(file).hexdigest() for file in files)
    if hashes != SOURCES[trial]:
        raise ValueError(f'Changed capture source: {trial}')
    bones, tree = parse_asf(files[0].decode('utf-8'))
    return [forward(bones, tree, frame) for frame in parse_amc(files[1].decode('utf-8'))], dict(zip(names, hashes))


def yaw(basis):
    return math.atan2(basis[0][2], basis[2][2])


def roll(basis):
    return math.atan2(-basis[1][0], basis[1][1])


def turn_channels(_level, rotation, points):
    chest = mm(tr(rotation['root']), rotation['thorax'])
    head = mm(tr(rotation['thorax']), rotation['head'])
    arms = [mv(tr(rotation['thorax']), sub(points[s + 'wrist'], points[s + 'clavicle']))[2] for s in 'lr']
    return yaw(head), yaw(chest), roll(chest), *arms


def dig_channels(level, rotation, points):
    chest = mm(level, rotation['thorax'])
    head = mm(tr(rotation['thorax']), rotation['head'])
    return pitch(chest), yaw(mm(tr(rotation['root']), rotation['thorax'])), pitch(head), mv(level, points['root'])[2]


def export(root):
    turn, turn_hash = capture(root, '47_01')
    dig, dig_hash = capture(root, '80_05')
    turn_frames = (208, 432)
    dig_frames = ((319, 348, 387, 421, 474), (651, 687, 729, 760, 824))
    phases = (0, .29, .54, .78, 1)
    def retime(marks, time):
        segment = next(i for i in range(4) if time <= phases[i + 1])
        u = (time - phases[segment]) / (phases[segment + 1] - phases[segment])
        frame = marks[segment] + (marks[segment + 1] - marks[segment]) * u
        return (frame - marks[0]) / (marks[-1] - marks[0])

    turn_rows = list(zip(*(normalized(values) for values in motion_channels(turn, *turn_frames, turn_channels))))
    dig_rows = [list(zip(*(normalized(values) for values in motion_channels(dig, marks[0], marks[-1], dig_channels,
                 lambda time: retime(marks, time))))) for marks in dig_frames]
    return {'schemaVersion': 1, 'license': 'LicenseRef-CMU-Mocap',
            'turn': {'source': '47_01', 'fps': 120, 'frames': turn_frames, 'sourceSha256': turn_hash, 'curves': [turn_rows]},
            'dig': {'source': '80_05', 'fps': 60, 'frames': dig_frames, 'sourceSha256': dig_hash, 'curves': dig_rows}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.write_text(json.dumps(export(args.source), separators=(',', ':')) + '\n', encoding='utf-8', newline='\n')
    print('CMU_TASKS_EXPORTED', hashlib.sha256(args.output.read_bytes()).hexdigest())
