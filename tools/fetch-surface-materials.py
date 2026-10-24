"""Fetch the small, attributed CC0 surface library into this repository only.

Run from any directory with Python 3.10+. Upstream originals and API responses
stay in .tools/sources/surface-materials; verified runtime maps go into assets.
"""
from concurrent.futures import ThreadPoolExecutor
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import struct
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ('leafy_grass', 'brown_mud_leaves_01', 'wood_planks_grey', 'plastered_wall_03', 'coast_sand_01', 'rock_face_03', 'reed_roof_03', 'rough_linen')
SOURCE = ROOT / '.tools/sources/surface-materials'
OUTPUT = ROOT / 'src/SandBoxSim.Godot/assets/materials/scanned'


def get(url):
    if not url.startswith(('https://api.polyhaven.com/', 'https://dl.polyhaven.org/')):
        raise ValueError('Unexpected asset host')
    request = urllib.request.Request(url, headers={'User-Agent': 'SandBoxSim asset maintenance'})
    with urllib.request.urlopen(request, timeout=60) as response:
        return response.read()


def fetch(asset):
    folder = SOURCE / asset
    folder.mkdir(parents=True, exist_ok=True)
    info_raw = get('https://api.polyhaven.com/info/' + asset)
    files_raw = get('https://api.polyhaven.com/files/' + asset)
    (folder / 'info.json').write_bytes(info_raw)
    (folder / 'files.json').write_bytes(files_raw)
    info, files = json.loads(info_raw), json.loads(files_raw)
    record = {'url': 'https://polyhaven.com/a/' + asset, 'authors': info['authors'],
              'dimensionsMillimeters': info.get('dimensions'), 'maps': {}}
    for kind, output_key in (('Diffuse', 'diffuse'), ('nor_gl', 'normal'), ('Rough', 'roughness')):
        if asset == 'coast_sand_01' and kind != 'Diffuse':
            continue
        entry = files[kind]['1k']['png' if kind == 'nor_gl' else 'jpg']
        name = entry['url'].rsplit('/', 1)[-1]
        if Path(name).name != name:
            raise ValueError('Invalid asset filename')
        path = folder / name
        data = path.read_bytes() if path.exists() else get(entry['url'])
        if len(data) != entry['size'] or hashlib.md5(data).hexdigest() != entry['md5']:
            raise ValueError('Upstream map integrity mismatch: ' + asset + '/' + name)
        path.write_bytes(data)
        if kind == 'nor_gl':
            record['pixelSize'] = list(struct.unpack('>II', data[16:24]))
        record['maps'][output_key] = {'path': asset + '/' + name, 'url': entry['url'],
                                     'sha256': hashlib.sha256(data).hexdigest(), 'bytes': len(data)}
    return asset, record


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--verify', action='store_true', help='Check committed map bytes without network access')
    options = parser.parse_args()
    if options.verify:
        manifest = json.loads((OUTPUT / 'manifest.json').read_text(encoding='utf-8'))
        if manifest['license'] != 'CC0-1.0' or set(manifest['assets']) != set(ASSETS):
            raise ValueError('Unexpected material library')
        count = 0
        for record in manifest['assets'].values():
            for entry in record['maps'].values():
                path = (OUTPUT / entry['path']).resolve()
                if OUTPUT.resolve() not in path.parents:
                    raise ValueError('Map escaped material directory')
                data = path.read_bytes()
                if len(data) != entry['bytes'] or hashlib.sha256(data).hexdigest() != entry['sha256']:
                    raise ValueError('Runtime map integrity mismatch: ' + str(path))
                count += 1
        print('SURFACE_MATERIALS_VERIFIED:', count, 'maps; no network access')
        return
    with ThreadPoolExecutor(max_workers=4) as workers:
        records = dict(workers.map(fetch, ASSETS))
    # Do not publish a partially downloaded material library.
    for asset, record in records.items():
        destination = OUTPUT / asset
        destination.mkdir(parents=True, exist_ok=True)
        for entry in record['maps'].values():
            name = Path(entry['path']).name
            shutil.copyfile(SOURCE / asset / name, destination / name)
    manifest = {'schemaVersion': 1, 'license': 'CC0-1.0',
                'licenseUrl': 'https://polyhaven.com/license', 'assets': records}
    (OUTPUT / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    credits = ['Poly Haven scanned surface materials — CC0 1.0',
               'License: https://polyhaven.com/license',
               'Original map bytes retained. Godot applies mipmaps/compression at import.', '']
    for asset, record in records.items():
        credits.append(asset + ' — ' + ', '.join(record['authors']) + ' — ' + record['url'])
    (OUTPUT / 'LICENSE.txt').write_text('\n'.join(credits) + '\n', encoding='utf-8')
    print('SURFACE_MATERIALS_FETCHED:', len(records), 'materials,', sum(len(r['maps']) for r in records.values()), 'verified maps')


if __name__ == '__main__':
    main()
