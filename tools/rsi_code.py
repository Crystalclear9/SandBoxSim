"""Real Python candidate probes with independent correctness and paired CPU/memory records.

This is trusted local execution, not protection against a malicious candidate.
"""
import hashlib, json, os, random, statistics, subprocess, sys, tempfile
from pathlib import Path

WORKER=Path(__file__).with_name('rsi_code_worker.py')
REFERENCE='''
def solve(problem):
    family=problem['family']
    if family=='resource':
        items=problem['items'];result=[]
        for x,y,kind in problem['queries']:
            candidates=[(abs(ix-x)+abs(iy-y),i) for i,(ix,iy,k) in enumerate(items) if k==kind]
            result.append(min(candidates)[1] if candidates else -1)
        return result
    if family=='routing':
        from collections import deque
        size=problem['size'];blocked=set(problem['blocked']);result=[]
        for start,goal in problem['queries']:
            excluded=blocked-{start,goal};queue=deque([start]);distance={start:0};found=-1
            while queue:
                node=queue.popleft()
                if node==goal:found=distance[node];break
                x,y=node%size,node//size
                for dx,dy in ((1,0),(-1,0),(0,1),(0,-1)):
                    nx,ny=x+dx,y+dy;nxt=ny*size+nx
                    if 0<=nx<size and 0<=ny<size and nxt not in excluded and nxt not in distance:
                        distance[nxt]=distance[node]+1;queue.append(nxt)
            result.append(found)
        return result
    world=problem['world'][:];result=[]
    for changes in problem['updates']:
        for index,value in changes:world[index]=value
        result.append([(v*2654435761 ^ (v>>3)) & 0xffffffff for v in world])
    return result
'''

def problem(task,seed):
    rng=random.Random(seed);family=task['family'];n=task['n'];q=task['q']
    profile=task.get('heldoutProfile','standard') if task.get('heldoutSeed')==seed else 'standard'
    if family=='resource':
        kinds=1 if profile=='homogeneous' else 16 if profile=='diverse' else 4
        return {'family':family,'items':[[rng.randrange(64),rng.randrange(64),rng.randrange(kinds)] for _ in range(n)],
            'queries':[[rng.randrange(64),rng.randrange(64),rng.randrange(kinds)] for _ in range(q)]}
    if family=='routing':
        size=max(8,int(n**.5));density=.38 if profile=='dense' else .02 if profile=='open' else .17
        return {'family':family,'size':size,'blocked':[i for i in range(size*size) if rng.random()<density],
            'queries':[[rng.randrange(size*size),rng.randrange(size*size)] for _ in range(q)]}
    rate=.65 if profile=='churn' else .005 if profile=='sparse' else 1/32
    return {'family':family,'world':[rng.randrange(1000) for _ in range(n)],
        'updates':[[[rng.randrange(n),rng.randrange(1000)] for _ in range(max(1,int(n*rate)))] for _ in range(q)]}

def measured(source,problems):
    if not isinstance(source,str) or len(source.encode())>65536:raise ValueError('Candidate source must be <=64 KiB')
    with tempfile.TemporaryDirectory(prefix='sandboxsim-code-') as folder:
        out=Path(folder)/'out.json';err=Path(folder)/'err.txt'
        with out.open('wb') as stdout,err.open('wb') as stderr:
            process=subprocess.Popen([sys.executable,str(WORKER)],stdin=subprocess.PIPE,stdout=stdout,stderr=stderr,cwd=folder,
                start_new_session=os.name!='nt',creationflags=subprocess.CREATE_NO_WINDOW if os.name=='nt' else 0)
            try:process.communicate(json.dumps({'source':source,'problems':problems},allow_nan=False).encode(),timeout=8)
            except subprocess.TimeoutExpired:
                if os.name=='nt':subprocess.run(['taskkill','/PID',str(process.pid),'/T','/F'],capture_output=True)
                else:
                    import signal
                    os.killpg(process.pid,signal.SIGKILL)
                process.wait();raise ValueError('Candidate execution exceeded 8 seconds')
        if process.returncode:raise ValueError('Candidate execution failed: '+err.read_text(encoding='utf-8')[-1000:])
        if out.stat().st_size>65536:raise ValueError('Candidate output exceeded 64 KiB')
        result=json.loads(out.read_text(encoding='utf-8'))
        if len(result['rows'])!=len(problems):raise ValueError('Incomplete candidate measurements')
        return result['rows']

def evaluate(task,proposal,seed):
    source=proposal['source'];payloads=[]
    # Different inputs in every measured call discourage identical-input memoization.
    for replicate in range(7):
        # All families use the original private profile but independently generated inputs.
        shifted=dict(task);shifted['heldoutSeed']=seed+replicate*104729 if seed==task['heldoutSeed'] else task['heldoutSeed']
        instance=problem(shifted,seed+replicate*104729);payloads.append(instance)
    scope={};exec(REFERENCE,scope)
    expected=[hashlib.sha256(json.dumps(scope['solve'](p),separators=(',',':')).encode()).hexdigest() for p in payloads]
    # Alternate full process order by seed; all candidate and reference calls receive the same payloads.
    if seed%2:actual=measured(source,payloads);baseline=measured(REFERENCE,payloads)
    else:baseline=measured(REFERENCE,payloads);actual=measured(source,payloads)
    correct=all(a['digest']==b['digest']==e for a,b,e in zip(actual,baseline,expected))
    ratios=[max(0,b['cpuNanoseconds']-b['cpuQuantumNanoseconds'])/(a['cpuNanoseconds']+a['cpuQuantumNanoseconds']) for a,b in zip(actual,baseline)]
    ratio=statistics.median(ratios)
    # Require every paired input to improve, rather than promote a lucky median.
    wall_ratios=[b['wallNanoseconds']/max(1,a['wallNanoseconds']) for a,b in zip(actual,baseline)]
    memory_ok=all(a['peakPythonBytes']<=67108864 for a in actual)
    detected=correct and memory_ok and min(ratios)>1.05 and min(wall_ratios)>1.05
    return {'correct':correct,'gain':max(0.,1-1/min(ratio,statistics.median(wall_ratios))) if detected else 0.,
        'codeReport':{'sourceSha256':hashlib.sha256(source.encode()).hexdigest(),'seed':seed,
            'expectedDigests':expected,'reference':baseline,'candidate':actual,'pairedSpeedups':ratios,
            'medianSpeedup':ratio,'pairedWallSpeedups':wall_ratios,'medianWallSpeedup':statistics.median(wall_ratios),'memoryBudgetPassed':memory_ok,'improvementDetected':detected,
            'scope':'solve CPU and wall time with Python allocation tracing; import, compilation and input copy excluded; trusted local code'}}

def validate(task,proposal,seed,value):
    report=value['codeReport'];source=proposal['source']
    if report['sourceSha256']!=hashlib.sha256(source.encode()).hexdigest() or report['seed']!=seed:raise ValueError('Candidate code identity changed')
    payloads=[]
    for i in range(7):
        shifted=dict(task);shifted['heldoutSeed']=seed+i*104729 if seed==task['heldoutSeed'] else task['heldoutSeed']
        payloads.append(problem(shifted,seed+i*104729))
    scope={};exec(REFERENCE,scope)
    expected=[hashlib.sha256(json.dumps(scope['solve'](p),separators=(',',':')).encode()).hexdigest() for p in payloads]
    if expected!=report['expectedDigests']:raise ValueError('Code workload changed')
    reference=report['reference'];candidate=report['candidate']
    if len(reference)!=7 or len(candidate)!=7:raise ValueError('Incomplete paired measurements')
    if any(type(row['cpuNanoseconds'])is not int or row['cpuNanoseconds']<0 or type(row['cpuQuantumNanoseconds'])is not int or row['cpuQuantumNanoseconds']<=0 or type(row['wallNanoseconds'])is not int or row['wallNanoseconds']<=0 or type(row['peakPythonBytes'])is not int or row['peakPythonBytes']<0 for row in reference+candidate):raise ValueError('Invalid measured cost')
    correct=all(a['digest']==b['digest']==e for a,b,e in zip(candidate,reference,expected))
    ratios=[max(0,b['cpuNanoseconds']-b['cpuQuantumNanoseconds'])/(a['cpuNanoseconds']+a['cpuQuantumNanoseconds']) for a,b in zip(candidate,reference)]
    median=statistics.median(ratios);wall_ratios=[b['wallNanoseconds']/a['wallNanoseconds'] for a,b in zip(candidate,reference)]
    memory_ok=all(a['peakPythonBytes']<=67108864 for a in candidate)
    detected=correct and memory_ok and min(ratios)>1.05 and min(wall_ratios)>1.05
    gain=max(0.,1-1/min(median,statistics.median(wall_ratios))) if detected else 0.
    if report['pairedWallSpeedups']!=wall_ratios or report['medianWallSpeedup']!=statistics.median(wall_ratios) or report['memoryBudgetPassed']!=memory_ok or report['pairedSpeedups']!=ratios or report['medianSpeedup']!=median or report['improvementDetected']!=detected or value['correct']!=correct or value['gain']!=gain:raise ValueError('Code efficiency score changed')
