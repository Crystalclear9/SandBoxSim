"""Execute trusted agents' source experiments against the real C# sandbox. No Git mutations."""
from __future__ import annotations
import argparse
import hashlib
import json
import os
import secrets
import shutil
import socket
import subprocess
import sys
import time
from pathlib import Path, PurePosixPath
from rsi_benchmark import Runner, read, save, sha, terminate
from online import Client, launch, ready
from efficiency import evaluate, validate_report, episode

ROOT=Path(__file__).resolve().parents[1]


def editable_path(value):
    if not isinstance(value,str) or '\\' in value:raise ValueError('Use repository-relative forward-slash paths')
    path=PurePosixPath(value)
    if path.is_absolute() or '..' in path.parts or ':' in value or not value.startswith('src/SandBoxSim.Core/') or path.suffix!='.cs':
        raise ValueError('Only existing Core .cs source files may be edited')
    return value


def source_manifest(workspace):
    return {p.relative_to(workspace).as_posix():sha(p) for p in sorted(workspace.rglob('*'))
            if p.is_file() and not any(part in ('bin','obj') for part in p.relative_to(workspace).parts)}


def snapshot(target):
    files=subprocess.run(['git','ls-files','--cached','--others','--exclude-standard','-z'],cwd=ROOT,capture_output=True,check=True).stdout.decode().split('\0')
    for name in sorted(set(files)):
        if name and (name.startswith(('src/SandBoxSim.Core/','src/SandBoxSim.Console/','src/SandBoxSim.Tests/','config/'))
                     or name in ('Directory.Build.props','NuGet.Config','global.json')):
            source=ROOT/name
            if source.is_symlink():raise ValueError('Symlink source is unsupported')
            if not source.is_file():continue
            destination=target/name;destination.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,destination)
    return source_manifest(target)


def apply_edits(workspace, edits, allowed):
    if not isinstance(edits,list) or not 1<=len(edits)<=16:raise ValueError('Return 1-16 file edits')
    changes={}
    for edit in edits:
        name=editable_path(edit['path'])
        if name not in allowed or name in changes:raise ValueError('Unlisted or duplicate edit')
        path=workspace/name
        if not path.is_file() or path.is_symlink() or sha(path)!=edit['baseSha256']:raise ValueError('Edit source hash mismatch')
        content=edit['content']
        if not isinstance(content,str) or len(content.encode('utf-8'))>262144:raise ValueError('Source exceeds 256 KiB')
        if content==path.read_text(encoding='utf-8'):raise ValueError('No-op source edit')
        changes[name]=content
    # Validate the complete proposal before writing any file.
    for name,content in changes.items():(workspace/name).write_bytes(content.encode('utf-8'))
    return {name:sha(workspace/name) for name in changes}


def command(argv, cwd, folder, timeout):
    started=time.perf_counter();log=folder/'command.log'
    with log.open('wb') as handle:
        process=subprocess.Popen(argv,cwd=cwd,stdout=handle,stderr=subprocess.STDOUT,
                                 start_new_session=os.name!='nt',creationflags=subprocess.CREATE_NO_WINDOW if os.name=='nt' else 0)
        try:process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:terminate(process)
    result={'argv':argv,'exitCode':process.returncode,'wallSeconds':time.perf_counter()-started,'logSha256':sha(log)}
    save(folder/'execution.json',result)
    if process.returncode:raise ValueError('Command failed; '+log.read_text(encoding='utf-8',errors='replace')[-5000:])
    return result


def build_and_test(dotnet, workspace, logs, filters, seconds):
    executions=[]
    for i,project in enumerate(('SandBoxSim.Console','SandBoxSim.Tests')):
        folder=logs/f'build-{i}';folder.mkdir(parents=True)
        executions.append(command([dotnet,'build',str(workspace/f'src/{project}/{project}.csproj'),'-c','Release',
                                   '-p:UseSharedCompilation=false','--nologo'],workspace,folder,seconds))
    for i,selection in enumerate(filters):
        folder=logs/f'test-{i}';folder.mkdir(parents=True)
        executions.append(command([dotnet,str(workspace/'src/SandBoxSim.Tests/bin/Release/net8.0/SandBoxSim.Tests.dll'),
                                   '--filter',selection],workspace,folder,seconds))
    return workspace/'src/SandBoxSim.Console/bin/Release/net8.0/SandBoxSim.Console.dll', executions


def available_port():
    with socket.socket() as sock:
        sock.bind(('127.0.0.1',0));return sock.getsockname()[1]


def pair(reference, candidate, suite, repeats, folder):
    processes=[];clients=[];handles=[];started=time.perf_counter()
    try:
        for name,assembly in (('reference',reference),('candidate',candidate)):
            token=secrets.token_hex(24);port=available_port();handle=(folder/f'{name}-service.log').open('wb');handles.append(handle)
            process=launch(assembly,port,token,handle);processes.append(process)
            client=Client(f'http://127.0.0.1:{port}',token);clients.append(client);ready(client,process)
        report=evaluate(clients[0],clients[1],suite,repeats)
        if report['correct']:validate_report(report)
        save(folder/'report.json',report)
        return report
    finally:
        for client in clients:client.close()
        for process in processes:
            if process.poll() is None:
                process.terminate()
                try:process.wait(timeout=10)
                except subprocess.TimeoutExpired:terminate(process)
        for handle in handles:handle.close()
        save(folder/'cost.json',{'wallSeconds':time.perf_counter()-started,'scope':'Service launch, ready, paired episodes and release'})


def suites(config):
    development=config['developmentSuite'];heldout=config['heldoutSuite']
    if development.get('split')!='development' or heldout.get('split')!='heldout':raise ValueError('Explicit development and heldout splits required')
    ds=[c['seed'] for c in development['cases']];hs=[c['seed'] for c in heldout['cases']]
    if len(set(ds+hs))!=len(ds+hs):raise ValueError('World seeds must be unique and split-disjoint')
    return development,heldout


def profile(reference, suite, folder):
    folder.mkdir();token=secrets.token_hex(24);port=available_port();client=Client(f'http://127.0.0.1:{port}',token)
    with (folder/'service.log').open('wb') as log:
        process=launch(reference,port,token,log)
        try:
            ready(client,process)
            rows=[episode(client,case) for case in suite['cases']]
            result={'scope':'One cold reference episode per public development world; diagnostic only, not statistically stable',
                    'cases':[{'case':i,'completeEpisodeMs':r['wallMs'],'simulationStepMs':sum(r['stepMs']),
                              'pathQueryMs':r['pathMs'],'setupAndReleaseMs':r['setupAndReleaseMs'],
                              'httpCalls':r['httpCalls'],'measuredTicks':r['measuredTicks']} for i,r in enumerate(rows)]}
            save(folder/'profile.json',result);return result
        finally:
            client.close()
            if process.poll() is None:
                process.terminate()
                try:process.wait(timeout=10)
                except subprocess.TimeoutExpired:terminate(process)


def run(config, source, output, dotnet):
    started=time.perf_counter();development,heldout=suites(config)
    allowed=[editable_path(p) for p in config['editableFiles']]
    if not allowed or len(set(allowed))!=len(allowed):raise ValueError('Unique editableFiles required')
    attempts=config.get('attempts',2);repeats=config.get('repeats',3);filters=config.get('testFilters',['AgentTests','ResponsivenessTests'])
    if type(attempts)is not int or not 1<=attempts<=8 or type(repeats)is not int or not 3<=repeats<=20:raise ValueError('Use 1-8 attempts and 3-20 paired repeats')
    if not filters or not all(isinstance(f,str) and f for f in filters):raise ValueError('Nonempty semantic test filters required')
    command_seconds=config.get('commandSeconds',120)
    if not isinstance(command_seconds,(int,float)) or isinstance(command_seconds,bool) or not 1<=command_seconds<=600:raise ValueError('Invalid commandSeconds')
    output=Path(output).resolve();output.mkdir(parents=True,exist_ok=False)
    baseline=output/'reference';baseline.mkdir();initial=snapshot(baseline)
    if any(p not in initial for p in allowed):raise ValueError('Editable source missing from snapshot')
    save(output/'source-manifest.json',initial);save(output/'config.json',config)
    agent=output/'agent.py';shutil.copyfile(source/config['agent'],agent)
    budget={'attempts':attempts,'callSeconds':config.get('agentSeconds',30),'taskSeconds':config.get('agentSeconds',30)}
    runner=Runner(output,budget,config.get('envAllowlist',[]));feedback=[];records=[];selected=None;best=float('-inf')
    try:
        reference,_=build_and_test(dotnet,baseline,output/'reference-logs',filters,command_seconds)
        development_profile=profile(reference,development,output/'development-profile')
        for attempt in range(attempts):
            candidate=output/f'candidate-{attempt}';candidate.mkdir()
            for name in initial:
                dest=candidate/name;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(baseline/name,dest)
            folder=output/f'attempt-{attempt}';folder.mkdir();begin=time.perf_counter()
            request={'schemaVersion':1,'mode':'research','attempt':attempt,
                     'goal':'Optimize real sandbox runtime while preserving exact world trajectories and independent path semantics',
                     'sourceWorkspace':str(baseline),'sourceWorkspaceContract':'Inspect this source snapshot; return edits through JSON only. Trusted local process, not OS isolation.',
                     'files':[{ 'path':p,'baseSha256':initial[p],'content':(baseline/p).read_text(encoding='utf-8')} for p in allowed],
                     'developmentSuite':development,'developmentProfile':development_profile,'feedback':feedback,
                     'replyFormat':{'hypothesis':'explain expected mechanism and semantic risks','edits':[{'path':'existing allowed file','baseSha256':'supplied hash','content':'full UTF-8 source'}]}}
            reply,execution=runner.invoke(agent,request)
            record={'attempt':attempt,'call':execution,'status':'rejected'}
            try:
                if execution['error']:raise ValueError(execution['error'])
                if not isinstance(reply.get('hypothesis'),str) or not reply['hypothesis'].strip():raise ValueError('Research hypothesis required')
                record['hypothesis']=reply['hypothesis'];record['changes']=apply_edits(candidate,reply['edits'],allowed)
                candidate_assembly,_=build_and_test(dotnet,candidate,folder/'logs',filters,command_seconds)
                current=source_manifest(candidate)
                if set(current)!=set(initial) or any(current[p]!=initial[p] for p in initial if p not in record['changes']):raise ValueError('Unexpected source mutation')
                report=pair(reference,candidate_assembly,development,repeats,folder)
                record['status']='development-correct' if report['correct'] else 'development-incorrect'
                record['summary']=report['summary'];record['failures']=report['failures']
                record['assemblySha256']=sha(candidate_assembly)
                record['coreSha256']=sha(candidate_assembly.with_name('SandBoxSim.Core.dll'))
                if report['correct'] and report['summary']['medianSpeedup']>best:
                    best=report['summary']['medianSpeedup'];selected=(attempt,candidate_assembly)
            except (ValueError,KeyError,TypeError,OSError,subprocess.SubprocessError) as e:record['error']=str(e)
            record['wallSeconds']=time.perf_counter()-begin;save(folder/'result.json',record);records.append(record)
            feedback.append({k:record[k] for k in ('attempt','status','summary','failures','error','hypothesis') if k in record})
            # Never provide private suite or results to another agent call.
        private=None
        if selected:
            folder=output/'heldout';folder.mkdir();private=pair(reference,selected[1],heldout,repeats,folder)
        observed={'kind':'real-sandbox-research-v1','fixtureOnly':config.get('fixtureOnly',False),'agentSha256':sha(agent),
                  'referenceManifestSha256':sha(output/'source-manifest.json'),'attempts':records,'agentCalls':runner.calls,
                  'referenceAssemblySha256':sha(reference),'referenceCoreSha256':sha(reference.with_name('SandBoxSim.Core.dll')),
                  'controllerSha256':sha(__file__),
                  'selectedAttempt':selected[0] if selected else None,'heldoutCorrect':bool(private and private['correct']),
                  'heldoutImprovementDetected':bool(private and private['correct'] and private['summary']['improvementDetected']),
                  'wallSeconds':time.perf_counter()-started,'costScope':'End-to-end controller including snapshot, agents, builds, tests and paired real services',
                  'recursiveEvidenceSupported':False,'scope':'Real code research loop; does not independently establish recursive improver capability'}
        final=source_manifest(baseline)
        if final!=initial:raise ValueError('Reference source changed during research')
        if any(sha(ROOT/p)!=expected for p,expected in initial.items()):raise ValueError('Main checkout source changed during research')
        save(output/'result.json',observed)
        return observed
    except Exception as e:
        save(output/'failure.json',{'error':str(e),'attempts':records,'wallSeconds':time.perf_counter()-started});raise


def verify(folder):
    folder=Path(folder);result=read(folder/'result.json');config=read(folder/'config.json');initial=read(folder/'source-manifest.json')
    if result['controllerSha256']!=sha(__file__) or result['agentSha256']!=sha(folder/'agent.py') or result['referenceManifestSha256']!=sha(folder/'source-manifest.json'):
        raise ValueError('Research source identity changed')
    if source_manifest(folder/'reference')!=initial:raise ValueError('Reference snapshot changed')
    def check_commands(logs):
        required=['build-0','build-1']+[f'test-{i}' for i in range(len(config.get('testFilters',['AgentTests','ResponsivenessTests'])))]
        for name in required:
            execution=logs/name/'execution.json';value=read(execution)
            if value['exitCode']!=0 or value['logSha256']!=sha(execution.with_name('command.log')):raise ValueError('Build or semantic check changed')
    check_commands(folder/'reference-logs')
    reference=folder/'reference/src/SandBoxSim.Console/bin/Release/net8.0/SandBoxSim.Console.dll'
    if sha(reference)!=result['referenceAssemblySha256'] or sha(reference.with_name('SandBoxSim.Core.dll'))!=result['referenceCoreSha256']:
        raise ValueError('Reference binary changed')
    if len(result['attempts'])!=config.get('attempts',2) or result['agentCalls']!=len(result['attempts']):raise ValueError('Research attempt count changed')
    history=[]
    for i,record in enumerate(result['attempts']):
        if record!=read(folder/f'attempt-{i}/result.json') or record['attempt']!=i:raise ValueError('Attempt result changed')
        call=folder/'calls'/str(record['call']['call']);request=read(call/'input.json')
        if read(call/'execution.json')!=record['call'] or sha(call/'agent.py')!=result['agentSha256']:raise ValueError('Agent call identity changed')
        if request['mode']!='research' or request['attempt']!=i or request['developmentSuite']!=config['developmentSuite'] or request['feedback']!=history:
            raise ValueError('Research request or feedback changed')
        if 'heldoutSuite' in request or 'heldoutResults' in request:raise ValueError('Heldout leaked into research')
        expected_files=[{'path':p,'baseSha256':initial[p],'content':(folder/'reference'/p).read_text(encoding='utf-8')} for p in config['editableFiles']]
        if request['files']!=expected_files:raise ValueError('Agent source context changed')
        if request['developmentProfile']!=read(folder/'development-profile/profile.json'):raise ValueError('Development profile changed')
        history.append({k:record[k] for k in ('attempt','status','summary','failures','error','hypothesis') if k in record})
        if record['status'] in ('development-correct','development-incorrect'):
            reply=read(call/'stdout.txt');expected=dict(initial)
            for edit in reply['edits']:
                name=editable_path(edit['path'])
                if name not in config['editableFiles'] or edit['baseSha256']!=initial[name]:raise ValueError('Proposal source identity changed')
                expected[name]=hashlib.sha256(edit['content'].encode('utf-8')).hexdigest()
            if source_manifest(folder/f'candidate-{i}')!=expected:raise ValueError('Candidate does not match agent proposal')
            assembly=folder/f'candidate-{i}/src/SandBoxSim.Console/bin/Release/net8.0/SandBoxSim.Console.dll'
            if sha(assembly)!=record['assemblySha256'] or sha(assembly.with_name('SandBoxSim.Core.dll'))!=record['coreSha256']:raise ValueError('Candidate binary changed')
            report=read(folder/f'attempt-{i}/report.json')
            if report['correct']:validate_report(report)
            if report['suite']!=config['developmentSuite'] or report['summary']!=record['summary']:raise ValueError('Development selection changed')
            if report['candidateIdentity']['assemblySha256']!=record['assemblySha256'] or report['candidateIdentity']['coreSha256']!=record['coreSha256']:raise ValueError('Measured binary identity mismatch')
            check_commands(folder/f'attempt-{i}/logs')
            if report['referenceIdentity']['assemblySha256']!=result['referenceAssemblySha256'] or report['referenceIdentity']['coreSha256']!=result['referenceCoreSha256']:
                raise ValueError('Measured reference identity mismatch')
    correct=[r for r in result['attempts'] if r['status']=='development-correct']
    selected=max(correct,key=lambda r:r['summary']['medianSpeedup'])['attempt'] if correct else None
    if selected!=result['selectedAttempt']:raise ValueError('Selection used something other than development score')
    if selected is not None:
        report=read(folder/'heldout/report.json')
        if report['correct']:validate_report(report)
        record=result['attempts'][selected]
        if report['suite']!=config['heldoutSuite'] or report['candidateIdentity']['assemblySha256']!=record['assemblySha256']:raise ValueError('Heldout candidate changed')
        if report['candidateIdentity']['coreSha256']!=record['coreSha256'] or report['referenceIdentity']['assemblySha256']!=result['referenceAssemblySha256'] or report['referenceIdentity']['coreSha256']!=result['referenceCoreSha256']:
            raise ValueError('Heldout binary identity mismatch')
        if report['correct']!=result['heldoutCorrect'] or bool(report['correct'] and report['summary']['improvementDetected'])!=result['heldoutImprovementDetected']:raise ValueError('Heldout conclusion changed')
    if result['recursiveEvidenceSupported'] is not False:raise ValueError('A source research loop is not recursive evidence')
    return {'protocolValidated':True,'agentCalls':result['agentCalls'],'heldoutCorrect':result['heldoutCorrect'],'heldoutImprovementDetected':result['heldoutImprovementDetected']}


def main():
    parser=argparse.ArgumentParser(description=__doc__);sub=parser.add_subparsers(dest='command',required=True)
    run_args=sub.add_parser('run');run_args.add_argument('config',type=Path);run_args.add_argument('--output',type=Path,required=True)
    private=Path.home()/'.sandboxsim-tool/net8'/('dotnet.exe' if os.name=='nt' else 'dotnet')
    run_args.add_argument('--dotnet',default=str(private) if private.is_file() else shutil.which('dotnet'))
    check=sub.add_parser('verify');check.add_argument('folder',type=Path)
    args=parser.parse_args()
    try:
        if args.command=='verify':print(json.dumps(verify(args.folder)));return 0
        if not args.dotnet:raise ValueError('Install .NET 8 SDK or supply --dotnet')
        result=run(read(args.config),args.config.resolve().parent,args.output,args.dotnet)
        print(json.dumps({k:result[k] for k in ('selectedAttempt','agentCalls','heldoutCorrect','heldoutImprovementDetected','recursiveEvidenceSupported')}));return 0
    except (ValueError,KeyError,TypeError,OSError,subprocess.SubprocessError) as e:print(json.dumps({'error':str(e)}),file=sys.stderr);return 2


if __name__=='__main__':sys.exit(main())
