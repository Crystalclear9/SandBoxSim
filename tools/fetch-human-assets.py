"""Fetch only the pinned CC0 resident inputs into the project's .tools folder.

The large system-assets ZIP is read with HTTP ranges, never downloaded whole.
Existing verified inputs are reused. --verify performs no network requests.
"""
import argparse
import hashlib
import io
import json
from pathlib import Path
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT/'src/SandBoxSim.Godot/assets/models/human/head-sources.json'
DESTINATION = ROOT/'.tools/sources/makehuman'


def request(url, headers=None, limit=20_000_000):
    if not url.startswith(('https://raw.githubusercontent.com/makehumancommunity/',
                           'https://files2.makehumancommunity.org/asset_packs/')):
        raise ValueError('Unexpected source host')
    req = urllib.request.Request(url, headers={'User-Agent':'SandBoxSim resident assets', **(headers or {})})
    with urllib.request.urlopen(req, timeout=40) as response:
        data = response.read(limit+1)
        if len(data)>limit:
            raise ValueError('Unexpected source size')
        return data, response.status, response.headers


class ArchiveRanges(io.RawIOBase):
    def __init__(self, url, size):
        self.url, self.size, self.position = url, size, 0

    def seekable(self):
        return True

    def tell(self):
        return self.position

    def seek(self, offset, whence=0):
        position = offset if whence==0 else self.position+offset if whence==1 else self.size+offset
        if position<0 or position>self.size:
            raise ValueError('Archive seek outside file')
        self.position = position
        return position

    def read(self, count=-1):
        if count<0:
            count = self.size-self.position
        if count==0:
            return b''
        end = min(self.size, self.position+count)-1
        data, status, headers = request(self.url, {'Range':f'bytes={self.position}-{end}'})
        expected = f'bytes {self.position}-{end}/{self.size}'
        if status!=206 or headers.get('Content-Range')!=expected or len(data)!=end-self.position+1:
            raise ValueError('Server did not return the requested archive range')
        self.position += len(data)
        return data


def safe_path(name):
    path = (DESTINATION/name).resolve()
    if DESTINATION.resolve() not in path.parents:
        raise ValueError('Source path escaped project cache')
    return path


def verified(data, record):
    return len(data)==record['bytes'] and hashlib.sha256(data).hexdigest()==record['sha256']


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--verify', action='store_true')
    options = parser.parse_args()
    manifest = json.loads(MANIFEST.read_text(encoding='utf-8'))
    if manifest['license']!='CC0-1.0':
        raise ValueError('Unexpected resident asset license')
    pending = []
    for name, record in manifest['files'].items():
        path = safe_path(name)
        if path.is_file() and verified(path.read_bytes(),record):
            continue
        if options.verify:
            raise ValueError('Missing or changed source: '+name)
        pending.append((name, record))
    archive = None
    try:
        for name, record in pending:
            if 'url' in record:
                data, _, _ = request(record['url'])
            else:
                if archive is None:
                    archive = zipfile.ZipFile(ArchiveRanges(manifest['archiveUrl'],manifest['archiveBytes']))
                member = archive.getinfo(record['archiveMember'])
                if member.file_size!=record['bytes']:
                    raise ValueError('Archive source size changed: '+name)
                data = archive.read(member)  # zipfile also checks each member CRC.
            if not verified(data,record):
                raise ValueError('Source checksum mismatch: '+name)
            path = safe_path(name)
            path.parent.mkdir(parents=True,exist_ok=True)
            path.write_bytes(data)
            print('FETCHED',name,len(data),flush=True)
    finally:
        if archive is not None:
            archive.close()
    print('HUMAN_SOURCES_VERIFIED:',len(manifest['files']),'CC0 inputs; project-local cache')


if __name__=='__main__':
    main()
