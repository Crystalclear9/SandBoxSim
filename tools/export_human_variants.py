"""Compatibility entry point for the textured MakeHuman resident exporter."""
from pathlib import Path
import runpy
import sys

arguments=sys.argv[sys.argv.index('--')+1:]
if '--kind' in arguments:
    i=arguments.index('--kind')+1
    if arguments[i]=='scan':arguments[i]='neutral'
if '--source' not in arguments:
    arguments+=['--source',str(Path(__file__).resolve().parents[1]/'.tools/sources/makehuman')]
sys.argv=[__file__,'--']+arguments
runpy.run_path(str(Path(__file__).with_name('export_resident_faces.py')),run_name='__main__')
