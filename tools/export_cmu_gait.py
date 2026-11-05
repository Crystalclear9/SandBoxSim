"""Export contact-aligned walk curves from the CMU Graphics Lab ASF/AMC data."""
import argparse
import hashlib
import json
import math
from pathlib import Path

from export_cmu_arm_motion import forward, mm, mv, parse_amc, parse_asf, sub, tr, unit


# Zero-based AMC indices: heel contact, toe-off, next heel contact.
# Contacts were inspected in FK using the toe trajectory and ankle reversal.
CLIPS = {
    '07': ((57, 139, 187), (124, 202, 253),
           'c29414591d76a34885bff0b04a2666bf9659f869751a2549591c55277cf95b04',
           '3ca8e8b834481c97c32e9dd555f8c9023e33b6c52d93c9d556c83570204bf3f2'),
    '08': ((83, 153, 201), (141, 213, 263),
           'bcef1f4ec4741c1790ad488504fb0d46f2bf8409f75c7b9c88955f03c39852c9',
           '419bd4d87d873de13a57791f12008b107acc4a24abbc678332423390921d082c'),
}
SAMPLES = 48
# The runtime splits each limb's phase at its own support duration, then
# remaps both phases to this stored ratio. Footfall and authored gait agree.
SUPPORT_SAMPLES = 28


def sample(values, time):
    index = int(time)
    weight = time - index
    return values[index] * (1 - weight) + values[index + 1] * weight


def wrap_channel(values, times):
    start, end = times[0], times[-1]
    # Remove end-to-end drift before closing the loop, then remove its mean.
    drift = sample(values, end) - sample(values, start)
    result = [sample(values, t) - drift * (t - start) / (end - start) for t in times[:-1]]
    mean = sum(result) / len(result)
    return [v - mean for v in result]


def normalized(values, amplitude=None):
    scale = max(abs(v) for v in values) if amplitude is None else amplitude
    if scale <= 1e-6:
        raise ValueError('Degenerate gait channel')
    return [round(v / scale, 6) for v in values]


def travel_frame(poses, start, end):
    dx, _, dz = sub(poses[end][1]['root'], poses[start][1]['root'])
    length = math.hypot(dx, dz)
    return dx / length, dz / length, length


def limb_channels(poses, side, contacts):
    start, toe, end = contacts
    times = [start + (toe - start) * i / SUPPORT_SAMPLES for i in range(SUPPORT_SAMPLES)]
    times += [toe + (end - toe) * i / (SAMPLES - SUPPORT_SAMPLES) for i in range(SAMPLES - SUPPORT_SAMPLES + 1)]
    fx, fz, stride = travel_frame(poses, start, end)
    swing, pitch, flex, spread, lift, sole, ankles = [], [], [], [], [], [], []
    for rotation, point in poses:
        thorax = tr(rotation['thorax'])
        upper = mv(thorax, unit(sub(point[side + 'humerus'], point[side + 'clavicle'])))
        lower = mv(thorax, unit(sub(point[side + 'radius'], point[side + 'humerus'])))
        pitch.append(math.atan2(upper[2], -upper[1]))
        flex.append(math.acos(max(-1, min(1, sum(x * y for x, y in zip(upper, lower))))))
        spread.append(upper[0])
        ankle = point[side + 'tibia']
        x, y, z = sub(point[side + 'foot'], ankle)
        sole.append(math.atan2(y, fx * x + fz * z))
        ankles.append((fx * ankle[0] + fz * ankle[2], -fz * ankle[0] + fx * ankle[2]))
        lift.append(ankle[1])
    first, last = lift[toe], lift[end]
    path = []
    longitudinal, lateral = zip(*ankles)
    advance = longitudinal[end] - longitudinal[toe]
    for i in range(SAMPLES - SUPPORT_SAMPLES + 1):
        u = i / (SAMPLES - SUPPORT_SAMPLES)
        frame = toe + (end - toe) * u
        height = sample(lift, frame) - (first * (1 - u) + last * u)
        swing.append(max(0, height))
        forward = (sample(longitudinal, frame) - longitudinal[toe]) / advance
        sideways = (sample(lateral, frame) - lateral[toe] * (1 - u) - lateral[end] * u) / stride
        path.append((round(forward, 6), round(sideways, 6)))
    # The skeleton's foot bone is tilted even when the shoe is flat. Keep
    # the measured relative ankle motion and remove that reference tilt.
    neutral = sum(sole[start + int((toe - start) * t)] for t in (.31, .40, .49)) / 3
    drift = sole[end] - sole[start]
    rocker = [round(sample(sole, t) - neutral - drift * (t - start) / (end - start), 6) for t in times[:-1]]
    return (wrap_channel(pitch, times), wrap_channel(flex, times), normalized(swing),
            wrap_channel(spread, times), path, rocker)


def body_channels(poses, start, end):
    s, c, _ = travel_frame(poses, start, end)
    travel = [[c, 0, -s], [0, 1, 0], [s, 0, c]]
    rows = []
    for rotation, point in poses:
        def yxz(name):
            m = mm(travel, rotation[name])
            # Re-express the source in the game's -Z-forward convention.
            return math.asin(max(-1, min(1, m[1][2]))), math.atan2(m[0][2], m[2][2]), math.atan2(-m[1][0], m[1][1])
        pelvis_pitch, yaw, roll = yxz('root')
        chest = yxz('thorax')
        x, y, z = mv(travel, point['root'])
        rows.append((yaw, roll, y, *chest, -x, -z, pelvis_pitch))
    times = [start + (end - start) * i / SAMPLES for i in range(SAMPLES + 1)]
    return [normalized(wrap_channel(channel, times)) for channel in zip(*rows)]


def export(source):
    clips = []
    for subject, (left, right, asf_hash, amc_hash) in CLIPS.items():
        folder = source / f'cmu-{subject}'
        hashes = {f'{subject}.asf': asf_hash, f'{subject}_01.amc': amc_hash}
        for file, expected in hashes.items():
            if hashlib.sha256((folder / file).read_bytes()).hexdigest() != expected:
                raise ValueError(f'Changed CMU source: {file}')
        bones, tree = parse_asf((folder / f'{subject}.asf').read_text(encoding='utf-8'))
        frames = parse_amc((folder / f'{subject}_01.amc').read_text(encoding='utf-8'))
        poses = [forward(bones, tree, frame) for frame in frames]
        limbs = [limb_channels(poses, side, contact) for side, contact in zip('lr', (left, right))]
        pitch_scale = max(abs(v) for pitch, *_ in limbs for v in pitch)
        flex_scale = max(abs(v) for _, flex, *_ in limbs for v in flex)
        clips.append({
            'source': f'CMU Graphics Lab {subject}_01, Walk',
            'sourceUrl': f'https://mocap.cs.cmu.edu/search.php?subjectnumber={int(subject)}',
            'sourceSha256': hashes, 'fps': 120, 'contacts': [left, right],
            'body': list(zip(*body_channels(poses, left[0], left[-1]))),
            'arms': [list(zip(normalized(pitch, pitch_scale), normalized(flex, flex_scale), normalized(spread)))
                     for pitch, flex, _, spread, _, _ in limbs],
            'swingHeight': [limb[2] for limb in limbs],
            'swingPath': [limb[4] for limb in limbs],
            'solePitch': [limb[5] for limb in limbs],
        })
    return {'schemaVersion': 2, 'license': 'LicenseRef-CMU-Mocap',
            'bodyLayout': 'pelvisYaw,pelvisRoll,hipHeight,chestPitch,chestYaw,chestRoll,lateralHip,foreAftHip,pelvisPitch',
            'armLayout': 'upperPitch,elbowFlex,upperLateral',
            'swingPathLayout': 'progress,sideOffsetInStrideLengths', 'solePitchUnit': 'rad',
            'cycleSamples': SAMPLES, 'supportSamples': SUPPORT_SAMPLES, 'clips': clips}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    args.output.write_text(json.dumps(export(args.source), separators=(',', ':')) + '\n', encoding='utf-8', newline='\n')
    print('CMU_GAIT_EXPORTED', hashlib.sha256(args.output.read_bytes()).hexdigest())
