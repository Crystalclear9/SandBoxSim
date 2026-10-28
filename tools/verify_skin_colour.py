"""Verify production skin calibration and distant head colour on a real GPU.

Requires a built Debug client, Godot .NET and a graphical display. Logs stay
in the requested project-local output folder; no screenshots are generated.
"""
import argparse
import json
import os
from pathlib import Path
import uuid
from evaluate import ROOT, engine_path, validate_build, wait_for_engine
from local_environment import tool_environment


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--engine')
    parser.add_argument('--output',type=Path)
    args=parser.parse_args()
    output=(args.output or ROOT/'.tools/tmp'/('skin-colour-'+uuid.uuid4().hex[:12])).resolve()
    if not output.is_relative_to(ROOT/'.tools/tmp'):
        parser.error('Output must stay inside the project .tools/tmp directory')
    if output.exists() and any(output.iterdir()):
        parser.error('Output directory must be empty or new')
    validate_build(ROOT,ROOT/'src/SandBoxSim.Godot/.godot/mono/temp/bin/Debug/SandBoxSim.Godot.dll')
    output.mkdir(parents=True,exist_ok=True)
    env=tool_environment(godot=True)
    env['PYTHONDONTWRITEBYTECODE']='1'
    for name in ('TEMP','TMP','TMPDIR'):env[name]=str(output)
    command=[engine_path(args.engine or os.environ.get('GODOT_EXE')),'--log-file',str(output/'engine.log'),
             '--path',str(ROOT/'src/SandBoxSim.Godot'),'--script',str(ROOT/'tools/verify_skin_colour.gd'),
             '--rendering-method','gl_compatibility','--resolution','800x600','--max-fps','60','--audio-driver','Dummy',
             '--','--panel=face-frames','--seed=839102','--agents=40']
    code,timed_out,log=wait_for_engine(command,ROOT,env,180)
    (output/'output.log').write_bytes(log)
    passed=code==0 and not timed_out and b'ERROR:' not in log and log.count(b'SKIN_COLOUR_RENDER_PASS')==1
    print(log.decode('utf-8','replace').strip())
    print(json.dumps(dict(passed=passed,engineExitCode=code,timedOut=timed_out,output=str(output))))
    return 0 if passed else 1


if __name__=='__main__':raise SystemExit(main())
