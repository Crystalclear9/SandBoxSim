"""Fetch/verify the pinned CC0 Blender body bundle inside the project only."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import urllib.request
import zipfile

ROOT=Path(__file__).resolve().parents[1]
MANIFEST=ROOT/'src/SandBoxSim.Godot/assets/models/human/reference-shape-sources.json'
DESTINATION=ROOT/'.tools/sources/blender-assets/human-base-meshes-v1.4.1.blend'


class Ranges(io.RawIOBase):
    def __init__(self,url,size):self.url,self.size,self.position=url,size,0
    def seekable(self):return True
    def tell(self):return self.position
    def seek(self,offset,whence=0):
        p=offset if whence==0 else self.position+offset if whence==1 else self.size+offset
        if not 0<=p<=self.size:raise ValueError('Archive seek outside pinned source')
        self.position=p;return p
    def read(self,count=-1):
        if count<0:count=self.size-self.position
        if count==0:return b''
        if count>64*1024*1024:raise ValueError('Unexpected source range size')
        end=min(self.size,self.position+count)-1
        url=self.url+f'?range={self.position}-{end}'
        request=urllib.request.Request(url,headers={'Range':f'bytes={self.position}-{end}','User-Agent':'SandBoxSim CC0 source fetch'})
        with urllib.request.urlopen(request,timeout=90) as response:
            data=response.read(count+1)
            if response.status!=206 or response.headers.get('Content-Range')!=f'bytes {self.position}-{end}/{self.size}':
                raise ValueError('Server did not return the exact archive range')
        if len(data)!=end-self.position+1:raise ValueError('Truncated source archive range')
        self.position+=len(data);return data


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--verify',action='store_true');a=parser.parse_args()
    m=json.loads(MANIFEST.read_text(encoding='utf-8'));s=m['source']
    if m['license']!='CC0-1.0' or not s['url'].startswith('https://download.blender.org/demo/asset-bundles/human-base-meshes/'):
        raise ValueError('Unexpected body reference source')
    def verified(raw):return len(raw)==s['memberBytes'] and hashlib.sha256(raw).hexdigest()==s['sha256']
    if DESTINATION.is_file() and verified(DESTINATION.read_bytes()):
        print('BODY_REFERENCE_VERIFIED: pinned CC0 official bundle, project-local cache');return
    if a.verify:raise ValueError('Body reference bundle missing or changed')
    with zipfile.ZipFile(Ranges(s['url'],s['archiveBytes'])) as archive:
        member=archive.getinfo(s['member'])
        if member.file_size!=s['memberBytes'] or member.CRC!=s['memberCrc32']:raise ValueError('Changed archive member')
        raw=archive.read(member)
    if not verified(raw):raise ValueError('Changed body source bytes')
    DESTINATION.parent.mkdir(parents=True,exist_ok=True);DESTINATION.write_bytes(raw)
    print('BODY_REFERENCE_FETCHED:',len(raw),'verified bytes in project cache')


if __name__=='__main__':main()
