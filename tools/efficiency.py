"""Trusted paired efficiency evaluation and multi-round improvement evidence checks."""
from __future__ import annotations
import argparse, hashlib, heapq, json, math, random, secrets, shutil, statistics, sys
from pathlib import Path
from online import Client

def read(path):
    def invalid(s): raise ValueError('Nonfinite JSON: ' + s)
    return json.loads(Path(path).read_text(encoding='utf-8-sig'), parse_constant=invalid)

def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def write(path, value):
    with Path(path).open('x', encoding='utf-8') as f: json.dump(value, f, ensure_ascii=False, indent=2, allow_nan=False)

def number(v, name, minimum=0):
    if isinstance(v, bool) or not isinstance(v, (int, float)) or not math.isfinite(v) or v < minimum:
        raise ValueError('Invalid ' + name)
    return v

def optimal_cost(grid, query):
    """Independent Dijkstra oracle using the documented traversal rules, never candidate code."""
    w, h, tiles = grid['width'], grid['height'], grid['tiles']
    start, goal = (query['x'], query['y']), (query['goalX'], query['goalY'])
    if not tiles[goal[1]*w+goal[0]]['walkable']: return None
    distances = {start: 0.}; queue = [(0., start)]
    while queue:
        cost, p = heapq.heappop(queue)
        if cost > distances[p]: continue
        if p == goal: return cost
        x, y = p
        for dx, dy in ((1,0),(-1,0),(0,1),(0,-1),(1,1),(1,-1),(-1,1),(-1,-1)):
            nx, ny = x+dx, y+dy
            if not (0 <= nx < w and 0 <= ny < h) or not tiles[ny*w+nx]['walkable']: continue
            if dx and dy and (not tiles[y*w+nx]['walkable'] or not tiles[ny*w+x]['walkable']): continue
            target, source = tiles[ny*w+nx], tiles[y*w+x]
            step = target['moveCost'] * (1.4142 if dx and dy else 1.) + max(0., target['traversalHeight']-source['traversalHeight'])*.5
            new = cost+step; point = (nx, ny)
            if new < distances.get(point, math.inf):
                distances[point] = new; heapq.heappush(queue, (new, point))
    return None

def check_paths(grid, queries, paths):
    if len(queries) != len(paths): raise ValueError('Incomplete path results')
    w, h, tiles = grid['width'], grid['height'], grid['tiles']
    for q, result in zip(queries, paths):
        expected = optimal_cost(grid, q)
        if result['success'] != (expected is not None): raise ValueError('Path reachability is incorrect')
        if expected is None:
            if result['points']: raise ValueError('Failed path contains points')
            continue
        points = result['points']
        if not points or points[0] != [q['x'], q['y']] or points[-1] != [q['goalX'], q['goalY']]: raise ValueError('Path endpoints incorrect')
        cost = 0.
        for before, after in zip(points, points[1:]):
            x,y = before; nx,ny = after; dx,dy = abs(nx-x),abs(ny-y)
            if not 0 <= nx < w or not 0 <= ny < h or max(dx,dy) != 1 or not tiles[ny*w+nx]['walkable']: raise ValueError('Invalid path edge')
            if dx and dy and (not tiles[y*w+nx]['walkable'] or not tiles[ny*w+x]['walkable']): raise ValueError('Path cuts a corner')
            target, source = tiles[ny*w+nx], tiles[y*w+x]
            cost += target['moveCost']*(1.4142 if dx and dy else 1.) + max(0., target['traversalHeight']-source['traversalHeight'])*.5
        if not math.isclose(cost, result['cost'], rel_tol=2e-5, abs_tol=2e-4) or not math.isclose(cost, expected, rel_tol=2e-5, abs_tol=2e-4):
            raise ValueError('Path cost is incorrect or nonoptimal')

def identity(client):
    result = client.request('GET','/v1/capabilities')
    if result['protocol'] != 'sandboxsim-step-v1': raise ValueError('Incompatible service')
    return result

def paired_summary(samples):
    ratios = [number(s['referenceMs'], 'reference time', .000001)/number(s['candidateMs'], 'candidate time', .000001) for s in samples]
    if not ratios: raise ValueError('No measurements')
    rng = random.Random(1729)
    estimates = sorted(statistics.median(rng.choices(ratios,k=len(ratios))) for _ in range(2000))
    return {'medianSpeedup': statistics.median(ratios), 'bootstrap95': [estimates[50],estimates[1949]], 'pairs': len(ratios),
            'improvementDetected': len(ratios) >= 9 and estimates[50] > 1.05}

def episode(client, case):
    initial_calls=client.calls; initial_bytes=client.response_bytes; initial_time=client.wall_seconds
    created = client.request('POST','/v1/sessions',{k:case[k] for k in ('seed','width','height','agents')})
    path = '/v1/sessions/'+created['sessionId']; observation=created['observation']
    try:
        # Identical warmup is charged in the complete episode; step scopes are also reported separately.
        observation = client.request('POST',path+'/step',{'requestId':'warmup','expectedRevision':0,'ticks':5})['observation']
        grid = client.request('GET',path+'/map')
        walkable = [i for i,t in enumerate(grid['tiles']) if t['walkable']]
        rng = random.Random(case['seed']); queries=[]
        for _ in range(case['pathQueries']):
            a,b=rng.choice(walkable),rng.choice(walkable)
            queries.append({'x':a%case['width'],'y':a//case['width'],'goalX':b%case['width'],'goalY':b//case['width']})
        start=client.wall_seconds
        paths = client.request('POST',path+'/path',{'expectedRevision':observation['revision'],'queries':queries})
        path_ms=(client.wall_seconds-start)*1000
        if paths['digest'] != observation['digest']: raise ValueError('Path observation changed state')
        check_paths(grid,queries,paths['paths'])
        trace=[observation['digest']]; times=[]; allocated=[]
        for i in range(case['batches']):
            start=client.wall_seconds
            response=client.request('POST',path+'/step',{'requestId':'measure-'+str(i),'expectedRevision':observation['revision'],'ticks':case['ticksPerBatch']})
            times.append((client.wall_seconds-start)*1000)
            previous=observation; observation=response['observation']
            if observation['faulted'] or observation['tick'] != previous['tick']+case['ticksPerBatch'] or observation['revision'] != previous['revision']+1:
                raise ValueError('Step count, revision or invariants failed')
            trace.append(observation['digest']); allocated.append(number(response['metrics']['allocatedBytes'],'allocation'))
        result={'stepMs':times,'pathMs':path_ms,'digests':trace,
                'allocatedBytesTelemetry':sum(allocated)+paths['metrics']['allocatedBytes'], 'queries':queries,
                'mapSha256':hashlib.sha256(json.dumps(grid['tiles'],sort_keys=True).encode()).hexdigest(),
                'httpCallsBeforeDelete':client.calls-initial_calls,'responseBytesBeforeDelete':client.response_bytes-initial_bytes,
                'measuredTicks':case['batches']*case['ticksPerBatch'],
                'finalObservation':observation}
    finally: client.request('DELETE',path)
    result['wallMs']=(client.wall_seconds-initial_time)*1000
    result['setupAndReleaseMs']=max(0.,result['wallMs']-sum(times)-path_ms)
    result['httpCalls']=client.calls-initial_calls; result['responseBytes']=client.response_bytes-initial_bytes
    return result

def same_binaries(old,new):
    return all(old.get(k)==new.get(k) for k in ('coreSha256','assemblySha256'))

def report_summary(samples,old,new):
    summary=paired_summary(samples)
    if same_binaries(old,new):
        summary['improvementDetected']=False
        summary['binaryUnchanged']=True
    return summary

def evaluate(reference,candidate,suite,repeats):
    if repeats < 3 or repeats > 20: raise ValueError('Use 3-20 paired repeats')
    if suite.get('schemaVersion') != 1 or len(suite['cases']) < 3: raise ValueError('Suite needs at least three cases')
    for c in suite['cases']:
        for key,low,high in [('seed',-2147483648,2147483647),('width',16,64),('height',16,64),('agents',0,100),('pathQueries',1,32),('batches',1,10),('ticksPerBatch',1,1000)]:
            if type(c[key]) is not int or not low <= c[key] <= high: raise ValueError('Invalid suite field '+key)
    old,new=identity(reference),identity(candidate)
    for key in ('machineId','os','architecture','framework','processorCount','protocol'):
        if old[key] != new[key]: raise ValueError('Incompatible service environment: '+key)
    # Retain and charge cold runs so work shifted to setup/warmup cannot become free.
    cold=[]
    for index,case in enumerate(suite['cases']):
        first,second=(reference,candidate) if index%2==0 else (candidate,reference)
        a,b=episode(first,case),episode(second,case)
        base,trial=(a,b) if first is reference else (b,a)
        if base['digests']!=trial['digests'] or base['mapSha256']!=trial['mapSha256']: raise ValueError('Cold-start correctness failed')
        cold.append({'reference':base,'candidate':trial})
    samples=[]; failures=[]
    for repeat in range(repeats):
        for index,case in enumerate(suite['cases']):
            # Alternate order to reduce systematic first-run and thermal bias.
            first,second=(reference,candidate) if (repeat+index)%2==0 else (candidate,reference)
            try:
                a,b=episode(first,case),episode(second,case)
                base,trial=(a,b) if first is reference else (b,a)
                if base['digests'] != trial['digests'] or base['mapSha256'] != trial['mapSha256'] or base['queries'] != trial['queries']:
                    raise ValueError('Candidate changed deterministic simulation behavior')
                samples.append({'case':index,'repeat':repeat,'referenceMs':base['wallMs']+cold[index]['reference']['wallMs']/repeats,
                    'candidateMs':trial['wallMs']+cold[index]['candidate']['wallMs']/repeats,'reference':base,'candidate':trial})
            except (ValueError,OSError,KeyError,TypeError) as e:
                failures.append({'case':index,'repeat':repeat,'error':str(e)})
    correct=not failures and len(samples)==repeats*len(suite['cases'])
    return {'schemaVersion':1,'kind':'paired-efficiency-v1','correct':correct,'suite':suite,
            'suiteSha256':hashlib.sha256(json.dumps(suite,sort_keys=True).encode()).hexdigest(),
            'evaluatorSha256':sha(__file__),'clientSha256':sha(Path(__file__).with_name('online.py')),
            'referenceIdentity':old,'candidateIdentity':new,'repeats':repeats,'coldEpisodes':cold,'samples':samples,'failures':failures,
            'summary':report_summary(samples,old,new) if correct else None,
            'primaryMetric':'complete episode HTTP wall time including setup, warmup, queries, steps and release, plus amortized measured cold run; allocation is untrusted service telemetry',
            'scope':'Controlled code-efficiency experiment; not proof of recursive model capability improvement'}

def validate_report(report):
    if report['kind']!='paired-efficiency-v1' or not report['correct'] or report['failures']:
        raise ValueError('Invalid or failed round report')
    if report['evaluatorSha256']!=sha(__file__) or report['clientSha256']!=sha(Path(__file__).with_name('online.py')):
        raise ValueError('Evaluator changed')
    expected={(i,r) for i in range(len(report['suite']['cases'])) for r in range(report['repeats'])}
    actual=[(s['case'],s['repeat']) for s in report['samples']]
    if set(actual)!=expected or len(actual)!=len(expected) or report['repeats']<3:
        raise ValueError('Incomplete or duplicated paired measurements')
    suite_hash=hashlib.sha256(json.dumps(report['suite'],sort_keys=True).encode()).hexdigest()
    if report['suiteSha256']!=suite_hash: raise ValueError('Suite hash mismatch')
    if report.get('coldEpisodes'):
        if len(report['coldEpisodes'])!=len(report['suite']['cases']): raise ValueError('Incomplete cold measurements')
        for cold in report['coldEpisodes']:
            if cold['reference']['digests']!=cold['candidate']['digests'] or cold['reference']['mapSha256']!=cold['candidate']['mapSha256']:
                raise ValueError('Incorrect cold-start state')
            for value in cold.values():
                total=sum(number(t,'cold step time',.000001) for t in value['stepMs'])+number(value['pathMs'],'cold path time',.000001)+number(value['setupAndReleaseMs'],'cold setup time')
                if not math.isclose(total,value['wallMs'],rel_tol=1e-9): raise ValueError('Cold total mismatch')
    for sample in report['samples']:
        old,new=sample['reference'],sample['candidate']
        if old['digests']!=new['digests'] or old['mapSha256']!=new['mapSha256'] or old['queries']!=new['queries']:
            raise ValueError('Incorrect paired state')
        for key,value in [('referenceMs',old),('candidateMs',new)]:
            expected_ms=sum(number(t,'step time',.000001) for t in value['stepMs'])+number(value['pathMs'],'path time',.000001)+number(value.get('setupAndReleaseMs',0),'setup time')
            if report.get('coldEpisodes'):
                expected_ms+=number(report['coldEpisodes'][sample['case']][key.removesuffix('Ms')]['wallMs'],'cold time',.000001)/report['repeats']
            if not math.isclose(sample[key],expected_ms,rel_tol=1e-9): raise ValueError('Measurement total mismatch')
    if report['summary']!=report_summary(report['samples'],report['referenceIdentity'],report['candidateIdentity']): raise ValueError('Summary was modified')

def rounds(manifest, root):
    """Validate round lineage and budgets against immutable evaluator reports and artifact hashes."""
    if manifest.get('schemaVersion') != 1 or len(manifest['rounds']) < 2: raise ValueError('At least two rounds required')
    budgets=manifest['budget']; number(budgets['tokens'],'token budget',1); number(budgets['wallSeconds'],'time budget',1); number(budgets['attempts'],'attempt budget',1)
    fixed_agent=sha(root/manifest['controlAgentArtifact'])
    if fixed_agent!=manifest['controlAgentSha256']: raise ValueError('Fixed control agent hash mismatch')
    previous=None; previous_report=None; suite_hash=None; reference_identity=None; control_identity=None; improvements=0; recursive_improvements=0; records=[]; seen_reports=set()
    for index,r in enumerate(manifest['rounds']):
        report_path=root/r['report']; report=read(report_path)
        if sha(report_path)!=r['reportSha256']: raise ValueError('Round report hash mismatch')
        if r['reportSha256'] in seen_reports: raise ValueError('Each round requires a fresh evaluation report')
        seen_reports.add(r['reportSha256'])
        validate_report(report)
        if report['suite'].get('split')!='heldout': raise ValueError('RSI evidence requires an operator-held-out suite')
        if reference_identity and report['referenceIdentity']!=reference_identity: raise ValueError('Reference changed between rounds')
        reference_identity=report['referenceIdentity']
        if suite_hash and report['suiteSha256']!=suite_hash: raise ValueError('Rounds must use the same held-out suite')
        suite_hash=report['suiteSha256']
        agent_hash=sha(root/r['agentArtifact'])
        if agent_hash!=r['agentSha256'] or r['parentAgentSha256']!=previous: raise ValueError('Agent lineage/hash mismatch')
        if index and agent_hash==previous: raise ValueError('Self-improvement round must change the agent artifact')
        patch_hash=sha(root/r['patch'])
        if patch_hash!=r['patchSha256']: raise ValueError('Candidate patch hash mismatch')
        for key in ('tokens','wallSeconds','attempts'):
            if number(r['cost'][key],key)>budgets[key]: raise ValueError('Round exceeded fixed budget')
        # Every candidate is paired with a fixed unchanged-agent control under the same suite and environment.
        control_path=root/r['controlReport']; control=read(control_path)
        if r['controlReportSha256']==r['reportSha256']: raise ValueError('Control must be measured separately')
        if sha(control_path)!=r['controlReportSha256'] or control['suiteSha256']!=suite_hash:
            raise ValueError('Invalid fixed-agent control')
        validate_report(control)
        if control['repeats']!=report['repeats']: raise ValueError('Control repeat budget differs')
        if control_identity and control['candidateIdentity']!=control_identity: raise ValueError('Fixed control candidate changed')
        control_identity=control['candidateIdentity']
        if control['referenceIdentity']!=report['referenceIdentity']: raise ValueError('Reference/control environment mismatch')
        for key in ('tokens','wallSeconds','attempts'):
            if number(r['controlCost'][key],key)>budgets[key]: raise ValueError('Control exceeded fixed budget')
        def indexed(value): return {(s['case'],s['repeat']):s for s in value['samples']}
        candidate_samples=indexed(report); control_samples=indexed(control)
        relative_control=paired_summary([{'referenceMs':s['referenceMs']/s['candidateMs'],
            'candidateMs':control_samples[key]['referenceMs']/control_samples[key]['candidateMs']} for key,s in candidate_samples.items()])
        improved=report['summary']['improvementDetected'] and relative_control['improvementDetected']
        parent_gain=None
        if previous_report:
            parent_samples=indexed(previous_report)
            parent_gain=paired_summary([{'referenceMs':parent_samples[key]['candidateMs'],'candidateMs':s['candidateMs']} for key,s in candidate_samples.items()])
        recursive=bool(improved and parent_gain and parent_gain['improvementDetected'] and not same_binaries(previous_report['candidateIdentity'],report['candidateIdentity']))
        recursive_improvements+=int(recursive)
        improvements+=int(improved); previous=agent_hash
        records.append({'round':index,'agentSha256':agent_hash,'candidateCoreSha256':report['candidateIdentity']['coreSha256'],
                        'cost':r['cost'],'controlCost':r['controlCost'],'speedup':report['summary']['medianSpeedup'],
                        'controlSpeedup':control['summary']['medianSpeedup'],'improvementAgainstControl':improved,
                        'relativeControl':relative_control,'gainOverParent':parent_gain,'improvementOverParent':recursive,
                        'speedupGainPerThousandTokens':max(0.,report['summary']['medianSpeedup']-control['summary']['medianSpeedup'])*1000/r['cost']['tokens'] if r['cost']['tokens'] else None,
                        'speedupGainPerAgentSecond':max(0.,report['summary']['medianSpeedup']-control['summary']['medianSpeedup'])/r['cost']['wallSeconds'] if r['cost']['wallSeconds'] else None})
        previous_report=report
    return {'schemaVersion':1,'kind':'rsi-efficiency-evidence-v1','validated':True,'validationFixture':manifest.get('validationFixture',False),'rounds':records,'improvingRounds':improvements,'recursiveImprovingRounds':recursive_improvements,
            'suiteSha256':suite_hash,'evaluatorSha256':sha(__file__), 'fixedControlAgentSha256':fixed_agent,
            'scope':'Lineage, correctness, budget and paired-efficiency evidence validated; costs and model identity are operator attestations, not independently metered; no general RSI claim'}

def experiment(setup, source, output, client_factory=Client):
    """Execute every round/control against supplied services; snapshot evidence, then verify it."""
    if setup.get('schemaVersion')!=1 or not 2<=len(setup['rounds'])<=20: raise ValueError('Experiment needs 2-20 rounds')
    suite=read(source/setup['suite'])
    if suite.get('split')!='heldout': raise ValueError('Experiment requires a held-out suite')
    repeats=setup.get('repeats',3)
    output.mkdir(parents=True,exist_ok=False)
    manifest={'schemaVersion':1,'validationFixture':setup.get('validationFixture',False),'budget':setup['budget'],
              'controlAgentArtifact':'control-agent.bin','rounds':[]}
    try:
        shutil.copyfile(source/setup['controlAgentArtifact'],output/'control-agent.bin')
        manifest['controlAgentSha256']=sha(output/'control-agent.bin')
        write(output/'suite.json',suite)
        reference=client_factory(setup['reference']); control=client_factory(setup['control'])
        previous=None
        for index,r in enumerate(setup['rounds']):
            for key in ('tokens','wallSeconds','attempts'):
                if number(r['cost'][key],key)>setup['budget'][key] or number(r['controlCost'][key],key)>setup['budget'][key]:
                    raise ValueError('Round/control exceeded budget')
            candidate=client_factory(r['candidate'])
            report=evaluate(reference,candidate,suite,repeats); report_name=f'round-{index}.json'; write(output/report_name,report)
            control_report=evaluate(reference,control,suite,repeats); control_name=f'control-{index}.json'; write(output/control_name,control_report)
            agent=f'agent-{index}.bin'; patch=f'patch-{index}.diff'
            shutil.copyfile(source/r['agentArtifact'],output/agent); shutil.copyfile(source/r['patch'],output/patch)
            agent_hash=sha(output/agent)
            manifest['rounds'].append({'report':report_name,'reportSha256':sha(output/report_name),'controlReport':control_name,'controlReportSha256':sha(output/control_name),
                'agentArtifact':agent,'agentSha256':agent_hash,'parentAgentSha256':previous,'patch':patch,'patchSha256':sha(output/patch),
                'cost':r['cost'],'controlCost':r['controlCost']})
            previous=agent_hash
        write(output/'rounds.json',manifest)
        evidence=rounds(manifest,output); write(output/'evidence.json',evidence)
        return evidence
    except Exception as error:
        write(output/'failure.json',{'schemaVersion':1,'error':str(error),'completedRounds':len(manifest['rounds'])})
        raise

def main():
    parser=argparse.ArgumentParser(description=__doc__); commands=parser.add_subparsers(dest='command',required=True)
    bench=commands.add_parser('run'); bench.add_argument('--reference',required=True); bench.add_argument('--candidate',required=True); bench.add_argument('--suite',type=Path,required=True); bench.add_argument('--repeats',type=int,default=3); bench.add_argument('--output',type=Path,required=True)
    rsi=commands.add_parser('rounds'); rsi.add_argument('manifest',type=Path); rsi.add_argument('--output',type=Path,required=True)
    generator=commands.add_parser('generate-suite'); generator.add_argument('--cases',type=int,default=5); generator.add_argument('--output',type=Path,required=True)
    multi=commands.add_parser('experiment'); multi.add_argument('setup',type=Path); multi.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    try:
        if args.output.exists(): raise ValueError('Output already exists')
        if args.command=='experiment':
            result=experiment(read(args.setup),args.setup.resolve().parent,args.output)
            print(json.dumps({'output':str(args.output),'validated':result['validated'],'improvingRounds':result['improvingRounds']})); return 0
        if args.command=='generate-suite':
            if not 3<=args.cases<=20: raise ValueError('Use 3-20 held-out cases')
            result={'schemaVersion':1,'split':'heldout','cases':[{'seed':secrets.randbelow(2147483647),'width':48 if i%2 else 64,'height':48,'agents':24+i%4*16,'pathQueries':24,'batches':3,'ticksPerBatch':100} for i in range(args.cases)]}
        elif args.command=='run': result=evaluate(Client(args.reference),Client(args.candidate),read(args.suite),args.repeats)
        else: result=rounds(read(args.manifest),args.manifest.resolve().parent)
        write(args.output,result); print(json.dumps({'output':str(args.output),'correct':result.get('correct'),'validated':result.get('validated'),'summary':result.get('summary')}))
        return 0 if args.command=='generate-suite' or result.get('correct',result.get('validated',False)) else 1
    except (ValueError,OSError,KeyError,TypeError) as e:
        print(json.dumps({'schemaVersion':1,'error':str(e)}),file=sys.stderr); return 2

if __name__=='__main__': sys.exit(main())
