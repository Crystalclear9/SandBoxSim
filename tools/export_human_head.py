"""Export the default CC0 anatomical male head; keep the historical CLI name."""
import runpy
import sys
from pathlib import Path

if '--' not in sys.argv:
    raise ValueError('Pass -- --output DIRECTORY')
sys.argv.extend(['--kind', 'masculine'])
runpy.run_path(str(Path(__file__).with_name('export_human_variants.py')), run_name='__main__')
