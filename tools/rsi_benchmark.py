"""Agent-level and second-order improver evaluation. Local trusted-code runner, not OS isolation."""
from __future__ import annotations
import argparse, ast, collections, hashlib, json, os, random, shutil, signal, subprocess, sys, time
from pathlib import Path
from rsi_tasks import CHOICES, evaluate as kernel_evaluate

ROOT=Path(__file__).resolve().parents[1]
def sha(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def save(path,value):
    with Path(path).open('x',encoding='utf-8') as f:json.dump(value,f,ensure_ascii=False,indent=2,allow_nan=False)
def read(path):
    def invalid(v):raise ValueError('Nonfinite JSON '+v)
    return json.loads(Path(path).read_text(encoding='utf-8-sig'),parse_constant=invalid)

def terminate(process):
    if os.name=='nt':subprocess.run(['taskkill','/PID',str(process.pid),'/T','/F'],capture_output=True)
    else:
        try:os.killpg(process.pid,signal.SIGKILL)
        except ProcessLookupError:pass
    process.wait(timeout=10)

class Runner:
    def __init__(self,output,budget,env_allowlist=()):
        self.output=output; self.budget=budget; self.calls=0;self.env_allowlist=env_allowlist
        self.sources={str(ROOT/'tools'/name):sha(ROOT/'tools'/name) for name in ('rsi_benchmark.py','rsi_tasks.py','rsi_stateful.py','rsi_code.py','rsi_code_worker.py','efficiency.py','online.py')}
    def invoke(self,agent,request,seconds=None):
        self.calls+=1; folder=self.output/'calls'/str(self.calls);folder.mkdir(parents=True)
        target=folder/'agent.py';shutil.copyfile(agent,target);source_hash=sha(agent)
        save(folder/'input.json',request)
        encoded=json.dumps(request,allow_nan=False).encode()
        environment={k:v for k,v in os.environ.items() if k in ('PATH','SystemRoot','WINDIR','TEMP','TMP','LANG','HOME') or k in self.env_allowlist}
        environment['PYTHONIOENCODING']='utf-8';environment['PYTHONHASHSEED']='0'
        limit=min(seconds or self.budget['callSeconds'],self.budget['callSeconds']);start=time.perf_counter()
        with (folder/'stdout.txt').open('wb') as stdout,(folder/'stderr.txt').open('wb') as stderr,(folder/'stdin.json').open('wb') as stdin:
            stdin.write(encoded)
        with (folder/'stdout.txt').open('ab') as stdout,(folder/'stderr.txt').open('ab') as stderr,(folder/'stdin.json').open('rb') as stdin:
            process=subprocess.Popen([sys.executable,str(target.resolve())],stdin=stdin,stdout=stdout,stderr=stderr,cwd=folder,
                env=environment,start_new_session=os.name!='nt',creationflags=subprocess.CREATE_NO_WINDOW if os.name=='nt' else 0)
            error=None
            while process.poll() is None:
                if time.perf_counter()-start>limit:error='wall-time budget exceeded';break
                if (folder/'stdout.txt').stat().st_size+(folder/'stderr.txt').stat().st_size>524288:error='output budget exceeded';break
                time.sleep(.005)
            if error:terminate(process)
        elapsed=time.perf_counter()-start
        if (folder/'stdout.txt').stat().st_size+(folder/'stderr.txt').stat().st_size>524288:error='output budget exceeded'
        if process.returncode and not error:error='agent exit '+str(process.returncode)
        if sha(target)!=source_hash or sha(agent)!=source_hash:error='agent source mutated outside successor protocol'
        if any(sha(path)!=expected for path,expected in self.sources.items()):raise ValueError('Trusted evaluator files changed')
        raw=(folder/'stdout.txt').read_bytes()
        result=None
        if not error:
            try:
                result=json.loads(raw)
                if not isinstance(result,dict):raise ValueError('Agent reply must be an object')
            except (ValueError,UnicodeError) as e:error=str(e)
        record={'call':self.calls,'agentSha256':source_hash,'mode':request['mode'],'wallSeconds':elapsed,'inputBytes':len(encoded),
            'outputBytes':len(raw),'exitCode':process.returncode,'error':error}
        save(folder/'execution.json',record)
        return result,record

def cohort(seed,count):
    rng=random.Random(seed); tasks=[]
    for i in range(count):
        family=list(CHOICES)[i%3]
        tasks.append({'id':hashlib.sha256(f'{seed}:{i}'.encode()).hexdigest()[:16],'family':family,
            'n':rng.randrange(128,513),'q':rng.randrange(8,21),
            'heldoutProfile':{'resource':('homogeneous','diverse'),'routing':('dense','open'),'visual_update':('churn','sparse')}[family][(i//3)%2], 'developmentSeed':rng.randrange(2**31),'heldoutSeed':rng.randrange(2**31)})
    return tasks

def tasks_for(seed,count,backend,suite='algorithm-v1',phase=0,scale=1):
    if suite=='sandbox-stream-v1':
        if backend!='code':raise ValueError('Stateful stream suite requires code backend')
        from rsi_stateful import cohort as stream_cohort
        tasks=stream_cohort(seed,count,phase)
        if type(scale)is not int or not 1<=scale<=4:raise ValueError('workloadScale must be an integer from 1 to 4')
        for task in tasks:task['_backend']='code';task['n']*=scale;task['q']*=scale
        return tasks
    if suite!='algorithm-v1':raise ValueError('Unknown task suite')
    tasks=cohort(seed,count)
    if backend=='code':
        for task in tasks:task['_backend']='code';task['n']*=4;task['q']*=2
    return tasks

def public_task(task):
    if task.get('_suite')=='sandbox-stream-v1':
        from rsi_stateful import FORMATS,development_example
        return {k:task[k] for k in ('id','family','n','q')} | {'taskSuite':'sandbox-stream-v1',
            'proposalFormat':{'source':'Python defining solve(problem)'},'problemFormat':FORMATS[task['family']],
            'developmentExample':development_example(task)}
    if task.get('_backend')=='code':
        return {k:task[k] for k in ('id','family','n','q')} | {'proposalFormat':{'source':'Python defining solve(problem)'},
            'problemFormat':{'resource':'items=[x,y,kind]; queries=[x,y,kind]; return nearest item indices (Manhattan distance; smallest index breaks ties; -1 when absent)',
                'routing':'size; blocked=indices; queries=[start,goal]; return shortest 4-neighbour distances or -1; start and goal unblocked per query',
                'visual_update':'world=integers; updates=lists of [index,value]; return full geometry list after each update batch; geometry=(value*2654435761 ^ (value>>3)) & 0xffffffff'}[task['family']]}
    return {k:task[k] for k in ('id','family','n','q')} | {'strategies':CHOICES[task['family']]}

def http_stages(config,source):
    stages=[];seen_ids=set();seen_seeds=set()
    for stage in config['httpStages']:
        loaded={}
        for group in ('training','probes'):
            loaded[group]=[]
            for spec in stage[group]:
                if spec['id'] in seen_ids:raise ValueError('HTTP probe IDs must be fresh across stages')
                seen_ids.add(spec['id']);dev=read(source/spec['developmentSuite']);heldout=read(source/spec['heldoutSuite'])
                seeds={c['seed'] for c in dev['cases']}|{c['seed'] for c in heldout['cases']}
                if seeds&seen_seeds or {c['seed'] for c in dev['cases']} & {c['seed'] for c in heldout['cases']}:
                    raise ValueError('HTTP development/held-out seed reuse')
                seen_seeds.update(seeds)
                loaded[group].append({'id':spec['id'],'family':'http','developmentSeed':0,'heldoutSeed':1,
                    'developmentSuite':dev,'heldoutSuite':heldout,'reference':spec['reference'],
                    'public':{'id':spec['id'],'family':'http','description':spec['description'],
                        'proposalFormat':{'endpoint':'http://127.0.0.1:port'},'workspace':spec.get('workspace')}})
        if len(loaded['probes'])<12:raise ValueError('At least 12 independent HTTP optimization problems per stage')
        stages.append(loaded)
    return stages

def proposal_result(task,proposal,seed,http=False):
    if task.get('_backend')=='code':
        from rsi_code import evaluate
        return evaluate(task,proposal,seed)
    if task['family']!='http':return kernel_evaluate(task,proposal,seed)
    # Actual game-kernel code optimization adapter; evaluator owns both development and held-out suites.
    from efficiency import evaluate
    from online import Client
    suite=task['developmentSuite'] if seed==task['developmentSeed'] else task['heldoutSuite']
    report=evaluate(Client(task['reference']),Client(proposal['endpoint']),suite,3)
    correct=report['correct'];ratio=report['summary']['medianSpeedup'] if correct else 0.
    gain=max(0.,1-1/ratio) if ratio else 0.
    if correct and not report['summary']['improvementDetected']:gain=0.
    return {'correct':correct,'gain':gain,'pairedReport':report}

def probe(runner,agent,tasks,memory):
    results=[]
    for task in tasks:
        feedback=[];checkpoints=[];development=[];heldouts=[]; best=None;best_dev=-1.;cost=0.;calls=[]
        for attempt in range(runner.budget['attempts']):
            if cost>=runner.budget['taskSeconds']:
                checkpoints.append(0.);development.append(None);heldouts.append(None);continue
            request={'schemaVersion':2,'mode':'propose','task':public_task(task) if task['family']!='http' else task['public'],
                'attempt':attempt,'feedback':feedback,'memory':memory,'budget':runner.budget}
            reply,execution=runner.invoke(agent,request,runner.budget['taskSeconds']-cost);cost+=execution['wallSeconds'];calls.append(execution)
            dev=None;error=execution['error']
            if not error:
                try:
                    dev=proposal_result(task,reply['proposal'],task['developmentSeed'])
                    if dev['correct'] and dev['gain']>best_dev:best=reply['proposal'];best_dev=dev['gain']
                except (ValueError,KeyError,TypeError,OSError) as e:error=str(e)
            feedback.append({'proposal':None if not reply else reply.get('proposal'),'developmentGain':None if not dev else dev['gain'],'error':error,
                             'diagnostics':development_diagnostics(dev)})
            # Evaluate checkpoints without giving any held-out measurement to the optimizer.
            heldout=None
            if best is not None:
                try:heldout=proposal_result(task,best,task['heldoutSeed'])
                except (ValueError,KeyError,TypeError,OSError):pass
            checkpoints.append(heldout['gain'] if heldout and heldout['correct'] and cost<=runner.budget['taskSeconds'] else 0.)
            development.append(dev);heldouts.append(heldout)
        if any(sha(path)!=expected for path,expected in runner.sources.items()):raise ValueError('Trusted evaluator files changed')
        results.append({'taskId':task['id'],'family':task['family'],'checkpoints':checkpoints,
            'searchCapability':sum(checkpoints)/len(checkpoints),'finalGain':checkpoints[-1],'success':checkpoints[-1]>=.15,
            'bestProposal':best,'developmentFeedback':feedback,'developmentResults':development,'heldoutResults':heldouts,'agentWallSeconds':cost,'calls':calls})
    return results

def development_diagnostics(value):
    if value is None:return None
    result={'correct':value['correct']}
    if 'codeReport' in value:
        code=value['codeReport']
        result.update(failedReplicates=sum(a['digest']!=e for a,e in zip(code['candidate'],code['expectedDigests'])),
                      cpuSpeedupMedian=code['medianSpeedup'],wallSpeedupMedian=code['medianWallSpeedup'],
                      memoryBudgetPassed=code['memoryBudgetPassed'],improvementDetected=code['improvementDetected'])
    return result


def mean(values):return sum(values)/len(values) if values else 0.
def summary(results):
    return {'capabilityAuc':mean([r['searchCapability'] for r in results]),'successRate':mean([r.get('successProbability',float(r['success'])) for r in results]),
        'firstAttemptGain':mean([r['checkpoints'][0] for r in results]),'agentWallSeconds':sum(r['agentWallSeconds'] for r in results),
        'inputBytes':sum(c['inputBytes'] for r in results for c in r['calls']),'outputBytes':sum(c['outputBytes'] for r in results for c in r['calls'])}

def effect(values,families,margin=.02):
    if len(values)!=len(families) or not values:raise ValueError('Paired observations required')
    groups=collections.defaultdict(list)
    for value,family in zip(values,families):groups[family].append(value)
    rng=random.Random(1729);samples=[]
    for _ in range(1500):
        samples.append(mean([mean(rng.choices(v,k=len(v))) for v in groups.values()]))
    samples.sort();point=mean([mean(v) for v in groups.values()])
    return {'meanEffect':point,'stratified95':[samples[37],samples[1462]],'supported':len(values)>=12 and samples[37]>margin,
            'margin':margin,'tasks':len(values)}

def mechanism(agent):
    tree=ast.parse(Path(agent).read_text(encoding='utf-8-sig'))
    functions=[n for n in tree.body if isinstance(n,(ast.FunctionDef,ast.AsyncFunctionDef)) and n.name=='improve']
    if len(functions)!=1:raise ValueError('Agent ABI requires one top-level improve(request) function')
    return hashlib.sha256(ast.dump(functions[0],include_attributes=False).encode()).hexdigest(),functions[0]

def reverted_source(parent,child):
    _,old=mechanism(parent);tree=ast.parse(child.read_text(encoding='utf-8'))
    tree.body=[old if isinstance(n,(ast.FunctionDef,ast.AsyncFunctionDef)) and n.name=='improve' else n for n in tree.body]
    return ast.unparse(ast.fix_missing_locations(tree))+'\n'

def revert_mechanism(parent,child,path):
    path.write_bytes(reverted_source(parent,child).encode('utf-8'))
    return path

def aggregate_forks(groups):
    result=[]
    for rows in zip(*groups):
        if len({r['taskId'] for r in rows})!=1:raise ValueError('Fork tasks differ')
        checkpoints=[mean([r['checkpoints'][i] for r in rows]) for i in range(len(rows[0]['checkpoints']))]
        result.append({'taskId':rows[0]['taskId'],'family':rows[0]['family'],'checkpoints':checkpoints,
            'searchCapability':mean([r['searchCapability'] for r in rows]),'finalGain':checkpoints[-1],'success':checkpoints[-1]>=.15,
            'successProbability':mean([float(r['success']) for r in rows]),'agentWallSeconds':sum(r['agentWallSeconds'] for r in rows),
            'calls':[c for r in rows for c in r['calls']]})
    return result

def analyze(arms,margin=.02,forks=None,mechanism_changed=True):
    keys=('parent','child','reverted_parent','frozen','parent_offspring','child_offspring','mechanism_reverted_offspring')
    ids=[r['taskId'] for r in arms['parent']]
    if len(ids)!=len(set(ids)) or any([r['taskId'] for r in arms[k]]!=ids for k in keys):raise ValueError('Unmatched probe tasks')
    families=[r['family'] for r in arms['parent']]
    capability={k:[r['searchCapability'] for r in arms[k]] for k in keys}
    paired=lambda a,b:effect([x-y for x,y in zip(capability[a],capability[b])],families,margin)
    code_effect=paired('child','reverted_parent');transfer=paired('child','parent');frozen=paired('child','frozen')
    mechanism_effect=paired('child_offspring','mechanism_reverted_offspring')
    # Difference of improvement yields: does the changed optimizer produce *more capable successors*?
    meta=effect([(co-c)-(po-p) for co,c,po,p in zip(capability['child_offspring'],capability['child'],capability['parent_offspring'],capability['reverted_parent'])],families,margin)
    if forks:
        groups=collections.defaultdict(list)
        for i,family in enumerate(families):groups[family].append(i)
        rng=random.Random(1729);values=[];mechanism_values=[]
        for _ in range(1500):
            selected=rng.choices(forks,k=len(forks));scores=[];mechanism_scores=[]
            for indices in groups.values():
                terms=[];mechanism_terms=[]
                for i in rng.choices(indices,k=len(indices)):
                    child_gain=mean([f['childProbes'][i]['searchCapability'] for f in selected])-capability['child'][i]
                    parent_gain=mean([f['parentProbes'][i]['searchCapability'] for f in selected])-capability['reverted_parent'][i]
                    terms.append(child_gain-parent_gain)
                    mechanism_terms.append(mean([f['childProbes'][i]['searchCapability']-f['mechanismProbes'][i]['searchCapability'] for f in selected]))
                scores.append(mean(terms));mechanism_scores.append(mean(mechanism_terms))
            values.append(mean(scores));mechanism_values.append(mean(mechanism_scores))
        values.sort();meta['stratified95']=[values[37],values[1462]];meta['branchReplicates']=len(forks)
        meta['supported']=len(forks)>=3 and len(ids)>=12 and values[37]>margin
        mechanism_values.sort();mechanism_effect['stratified95']=[mechanism_values[37],mechanism_values[1462]]
        mechanism_effect['supported']=len(forks)>=3 and len(ids)>=12 and mechanism_values[37]>margin
    generation={}
    if forks:
        for key in ('parentFork','childFork','mechanismFork'):
            edges=[f[key] for f in forks]
            generation[key]={'attempts':len(edges),'accepted':sum(e['accepted'] for e in edges),
                'acceptanceRate':mean([float(e['accepted']) for e in edges]),
                'wallSeconds':sum(e['execution']['wallSeconds'] for e in edges),
                'inputBytes':sum(e['execution']['inputBytes'] for e in edges),'outputBytes':sum(e['execution']['outputBytes'] for e in edges)}
    return {'successorGeneration':generation,'arms':{k:summary(arms[k]) for k in keys},'heldoutCapabilityGain':transfer,'codeInterventionEffect':code_effect,
        'gainAgainstFrozen':frozen,'offspringYieldAdvantage':meta,'improverMechanismChanged':mechanism_changed,'improverMechanismEffect':mechanism_effect,
        'recursiveEvidenceSupported':mechanism_changed and transfer['supported'] and code_effect['supported'] and frozen['supported'] and meta['supported'] and mechanism_effect['supported']}


def retention_analysis(parent,child,margin):
    if len(parent)<12 or [(r['taskId'],r['family']) for r in parent]!=[(r['taskId'],r['family']) for r in child]:
        raise ValueError('Retention requires 12 matched unseen problems')
    families=[r['family'] for r in parent]
    values=[c['searchCapability']-p['searchCapability'] for p,c in zip(parent,child)]
    pooled=effect(values,families,margin)
    by_family={name:effect([v for v,f in zip(values,families) if f==name],
                           [name]*families.count(name),margin) for name in sorted(set(families))}
    # No pooled score can conceal a single regressing task family.
    passed=all(v['stratified95'][0]>=-margin for v in by_family.values())
    def correct(row):
        values=row.get('heldoutResults',[])
        return bool(values and values[-1] and values[-1]['correct'])
    losses=[{'taskId':p['taskId'],'family':p['family']} for p,c in zip(parent,child) if correct(p) and not correct(c)]
    return {'meanCapabilityChange':pooled['meanEffect'],'stratified95':pooled['stratified95'],
            'byFamily':by_family,'regressionTolerance':margin,'nonRegressionPassed':passed and not losses,'tasks':len(parent),
            'retainedCorrectnessPassed':not losses,'correctnessRegressions':losses,
            'parentCorrectTasks':sum(correct(r) for r in parent),'childCorrectTasks':sum(correct(r) for r in child)}


def apply_retention(analysis,retention,margin):
    result=dict(analysis)
    if retention is not None:
        result['retention']=retention_analysis(retention['parent'],retention['child'],margin)
        result['recursiveEvidenceSupported']=result['recursiveEvidenceSupported'] and result['retention']['nonRegressionPassed']
    return result

def validate_successor(source,parent):
    if not isinstance(source,str) or len(source.encode())>131072:raise ValueError('successorSource must be Python source <=128 KiB')
    try:ast.parse(source)
    except SyntaxError as e:raise ValueError('Invalid successor syntax: '+str(e)) from e
    if source.replace('\r\n','\n')==parent.read_text(encoding='utf-8-sig'):raise ValueError('Self edit emitted unchanged source')
    mechanism_text=ast.parse(source)
    if ast.dump(mechanism_text,include_attributes=False)==ast.dump(ast.parse(parent.read_text(encoding='utf-8-sig')),include_attributes=False):
        raise ValueError('Self edit changed only formatting or comments')
    if len([n for n in mechanism_text.body if isinstance(n,(ast.FunctionDef,ast.AsyncFunctionDef)) and n.name=='improve'])!=1:
        raise ValueError('Successor requires one top-level improve function')

def failed_probes(tasks,attempts):
    return [{'taskId':t['id'],'family':t['family'],'checkpoints':[0.]*attempts,'searchCapability':0.,
        'finalGain':0.,'success':False,'agentWallSeconds':0.,'calls':[],'generationFailed':True} for t in tasks]

def successor(runner,parent,memory,name,allow_failure=False):
    reply,execution=runner.invoke(parent,{'schemaVersion':2,'mode':'improve','source':parent.read_text(encoding='utf-8'),
        'developmentExperience':memory,'budget':runner.budget})
    try:
        if execution['error']:raise ValueError('Self edit failed: '+execution['error'])
        source=reply.get('successorSource');validate_successor(source,parent)
    except ValueError as e:
        if not allow_failure:raise
        return parent,{'parentSha256':sha(parent),'childSha256':sha(parent),'call':execution['call'],
            'execution':execution,'accepted':False,'failure':str(e)}
    path=runner.output/'agents'/name;path.parent.mkdir(exist_ok=True)
    path.write_bytes(source.encode('utf-8'))
    if sha(path)==sha(parent):raise ValueError('Self edit emitted unchanged source')
    return path,{'parentSha256':sha(parent),'childSha256':sha(path),'call':execution['call'],'execution':execution,'accepted':True}

def run(config,source,output):
    if config.get('schemaVersion')!=2:raise ValueError('Use meta benchmark schemaVersion 2')
    stages=config.get('stages',2);count=config.get('probeTasks',12);fork_count=config.get('forkReplicates',3)
    if type(stages)is not int or not 1<=stages<=5 or type(count)is not int or not 12<=count<=60 or count%3:raise ValueError('Use 1-5 stages, 12-60 tasks in multiples of 3')
    if type(fork_count)is not int or not 3<=fork_count<=5:raise ValueError('Use 3-5 independent paired self-edit forks')
    margin=config.get('effectMargin',.02)
    if isinstance(margin,bool) or not isinstance(margin,(int,float)) or not 0<=margin<=.5:raise ValueError('effectMargin must be 0-0.5')
    budget=config['budget']
    for key,low,high in [('attempts',1,8),('callSeconds',.1,120),('taskSeconds',.1,600)]:
        if isinstance(budget[key],bool) or not isinstance(budget[key],(int,float)) or not low<=budget[key]<=high:raise ValueError('Invalid budget '+key)
    if type(budget['attempts'])is not int:raise ValueError('attempts must be integer')
    suite=config.get('taskSuite','algorithm-v1');backend=config.get('backend','kernel');scale=config.get('workloadScale',1)
    if backend not in ('kernel','code','http'):raise ValueError('Unknown task backend')
    if suite not in ('algorithm-v1','sandbox-stream-v1') or (suite=='sandbox-stream-v1' and backend!='code'):
        raise ValueError('Stateful stream suite requires code backend; unknown suite rejected')
    if type(scale)is not int or not 1<=scale<=4 or (scale!=1 and suite!='sandbox-stream-v1'):raise ValueError('workloadScale 1-4 is supported by the stateful suite only')
    output=Path(output);output.mkdir(parents=True,exist_ok=False)
    (output/'agents').mkdir();frozen=output/'agents/frozen.py'
    # Canonical source bytes keep generated lineage hashes stable across Windows and Unix newlines.
    frozen.write_bytes((source/config['agent']).read_text(encoding='utf-8-sig').encode('utf-8'))
    runner=Runner(output,budget,config.get('envAllowlist',[]));parent=frozen;memory=[];records=[]
    seed=config.get('seed',random.SystemRandom().randrange(2**31))
    loaded=http_stages(config,source) if backend=='http' else None
    if loaded and len(loaded)!=stages:raise ValueError('HTTP stages must match stage count')
    try:
        for stage in range(stages):
            # Disjoint cohort per stage; training and fork experience never includes held-out feedback.
            training=loaded[stage]['training'] if loaded else tasks_for(f'{seed}:train:{stage}',6,backend,suite,stage,scale)
            train=probe(runner,parent,training,memory)
            previous_memory=memory[:]
            memory=memory+[{'family':r['family'],'feedback':r['developmentFeedback']} for r in train]
            child,lineage=successor(runner,parent,memory,f'child-{stage}.py')
            reverted=revert_mechanism(parent,child,output/'agents'/f'mechanism-reverted-{stage}.py')
            tasks=loaded[stage]['probes'] if loaded else tasks_for(f'{seed}:probe:{stage}',count,backend,suite,stage,scale)
            # Never pass arm labels or held-out results to an agent; rotate arm order across stages.
            definitions=[('parent',parent,previous_memory),('child',child,memory),('reverted_parent',parent,memory),('frozen',frozen,memory)]
            definitions=definitions[stage%4:]+definitions[:stage%4]
            arms={name:probe(runner,agent,tasks,experience) for name,agent,experience in definitions}
            forks=[]
            for replicate in range(fork_count):
                parent_offspring,parent_fork=successor(runner,parent,memory,f'parent-fork-{stage}-{replicate}.py',allow_failure=True)
                child_offspring,child_fork=successor(runner,child,memory,f'child-fork-{stage}-{replicate}.py',allow_failure=True)
                mechanism_offspring,mechanism_fork=successor(runner,reverted,memory,f'mechanism-fork-{stage}-{replicate}.py',allow_failure=True)
                pair={'parentFork':parent_fork,'childFork':child_fork,
                    'mechanismFork':mechanism_fork,'parentProbes':probe(runner,parent_offspring,tasks,memory) if parent_fork['accepted'] else failed_probes(tasks,budget['attempts']),
                    'childProbes':probe(runner,child_offspring,tasks,memory) if child_fork['accepted'] else failed_probes(tasks,budget['attempts']),'mechanismProbes':probe(runner,mechanism_offspring,tasks,memory) if mechanism_fork['accepted'] else failed_probes(tasks,budget['attempts'])}
                forks.append(pair)
            arms['parent_offspring']=aggregate_forks([f['parentProbes'] for f in forks]);arms['child_offspring']=aggregate_forks([f['childProbes'] for f in forks])
            arms['mechanism_reverted_offspring']=aggregate_forks([f['mechanismProbes'] for f in forks])
            changed=mechanism(parent)[0]!=mechanism(child)[0]
            result=analyze(arms,config.get('effectMargin',.02),forks,changed)
            retention=None
            if suite=='sandbox-stream-v1' and stage>0:
                retained=[]
                for phase in range(stage):retained+=tasks_for(f'{seed}:retention:{stage}:{phase}',12,backend,suite,phase,scale)
                retention={'cohort':retained,'parent':probe(runner,parent,retained,previous_memory),
                           'child':probe(runner,child,retained,memory)}
                result=apply_retention(result,retention,margin)
            record={'stage':stage,'lineage':lineage,'mechanismRevertedSha256':sha(reverted),'forks':forks,'cohort':tasks,'arms':arms,'analysis':result}
            if retention is not None:record['retention']=retention
            save(output/f'stage-{stage}.json',record);records.append(record);parent=child
        result={'schemaVersion':2,'kind':'agent-improver-benchmark-v2','protocolValidated':True,'fixtureOnly':config.get('fixtureOnly',False),
            'trustedSourceSha256':{Path(k).name:v for k,v in runner.sources.items()},'evaluatorSha256':sha(__file__),'taskBackendSha256':sha(ROOT/'tools/rsi_tasks.py'),'budget':budget,'seed':seed,
            'stages':[r['analysis'] for r in records],'supportedRecursiveStages':sum(r['analysis']['recursiveEvidenceSupported'] for r in records),
            'agentCalls':runner.calls,'forkReplicates':fork_count,'backend':backend,'measurementScope':'Optimizer-search and successor-production capability on unseen optimization problems; kernel backend is bounded algorithm selection, code backend executes submitted Python with CPU/memory measurements; HTTP backend measures real service code; no general RSI proof',
            'heldoutPolicy':heldout_policy(backend,suite),'taskSuite':suite,'workloadScale':scale,
            'failedForks':sum(not f[k]['accepted'] for r in records for f in r['forks'] for k in ('parentFork','childFork','mechanismFork')),
            'securityScope':'Trusted local processes, not an OS sandbox; model identity/token usage not independently authenticated'}
        save(output/'result.json',result);save(output/'config.json',config);return result
    except Exception as e:
        save(output/'failure.json',{'error':str(e),'completedStages':len(records),'agentCalls':runner.calls,'acceptedRsiOutcome':False});raise

def heldout_policy(backend,suite):
    return 'stateful-curriculum-v1' if suite=='sandbox-stream-v1' else 'distribution-shift-v1' if backend!='http' else 'controller-owned-http-suites'


def verify(folder):
    folder=Path(folder);config=read(folder/'config.json');result=read(folder/'result.json')
    if result['evaluatorSha256']!=sha(__file__) or result['taskBackendSha256']!=sha(ROOT/'tools/rsi_tasks.py'):raise ValueError('Evaluator version changed')
    if any(sha(ROOT/'tools'/path)!=expected for path,expected in result['trustedSourceSha256'].items()):raise ValueError('Trusted backend version changed')
    if result['budget']!=config['budget'] or ('seed' in config and result['seed']!=config['seed']):raise ValueError('Experiment budget or seed changed')
    call_dirs={p.name for p in (folder/'calls').iterdir() if p.is_dir()}
    if call_dirs!={str(i) for i in range(1,result['agentCalls']+1)}:raise ValueError('Agent call total changed')
    previous=sha(folder/'agents/frozen.py');analyses=[];failed_forks=0
    suite=config.get('taskSuite','algorithm-v1');backend=config.get('backend','kernel');scale=config.get('workloadScale',1)
    for i in range(config.get('stages',2)):
        stage=read(folder/f'stage-{i}.json');edge=stage['lineage']
        if edge['parentSha256']!=previous or edge['childSha256']!=sha(folder/f'agents/child-{i}.py'):raise ValueError('Broken agent lineage')
        def check_edge(fork,file,expected_parent):
            if fork['parentSha256']!=expected_parent:raise ValueError('Incorrect second-order fork parent')
            if not fork['accepted']:
                call=folder/'calls'/str(fork['call']);execution=read(call/'execution.json');request=read(call/'input.json')
                if execution!=fork['execution'] or sha(call/'agent.py')!=expected_parent or request['mode']!='improve' or hashlib.sha256(request['source'].encode()).hexdigest()!=expected_parent:raise ValueError('Failed fork identity changed')
                try:
                    if execution['error']:raise ValueError('Self edit failed: '+execution['error'])
                    validate_successor(read(call/'stdout.txt').get('successorSource'),call/'agent.py')
                except ValueError as e:
                    if str(e)!=fork['failure'] or fork['childSha256']!=expected_parent:raise ValueError('Failed fork reason changed')
                    return
                raise ValueError('Successful fork marked failed')
            if fork['childSha256']!=sha(folder/'agents'/file):raise ValueError('Fork hash mismatch')
            call=folder/'calls'/str(fork['call'])
            if read(call/'execution.json')!=fork['execution']:raise ValueError('Self-edit execution record changed')
            if read(call/'execution.json')['agentSha256']!=fork['parentSha256'] or hashlib.sha256(read(call/'stdout.txt')['successorSource'].encode()).hexdigest()!=fork['childSha256']:
                raise ValueError('Fork was not generated by recorded parent')
            request=read(call/'input.json')
            if request['mode']!='improve' or hashlib.sha256(request['source'].encode()).hexdigest()!=expected_parent:raise ValueError('Incorrect self-edit input source')
        check_edge(edge,f'child-{i}.py',previous)
        task_index={t['id']:t for t in stage['cohort']}
        if backend!='http' and stage['cohort']!=tasks_for(f"{result['seed']}:probe:{i}",config.get('probeTasks',12),backend,suite,i,scale):
            raise ValueError('Probe cohort changed')
        def check_probes(records,expected_identity):
            def check_http(value,suite):
                from efficiency import validate_report
                report=value['pairedReport']
                if report['suite']!=suite:raise ValueError('HTTP probe suite changed')
                if report['correct']:validate_report(report)
                ratio=report['summary']['medianSpeedup'] if report['correct'] else 0.
                gain=max(0.,1-1/ratio) if ratio and report['summary']['improvementDetected'] else 0.
                if value['correct']!=report['correct'] or value['gain']!=gain:raise ValueError('HTTP capability score changed')
            for record in records:
                task=task_index[record['taskId']];best=None;best_dev=-1.;recomputed=[];wall=0.
                if record['family']!=task['family']:raise ValueError('Probe family changed')
                for attempt,call in enumerate(record['calls']):
                    directory=folder/'calls'/str(call['call']);actual=read(directory/'execution.json');request=read(directory/'input.json')
                    if actual!=call or call['agentSha256']!=expected_identity or sha(directory/'agent.py')!=expected_identity:raise ValueError('Probe agent identity mismatch')
                    if request['mode']!='propose' or request['task']!=(public_task(task) if task['family']!='http' else task['public']) or request['attempt']!=attempt or request['budget']!=config['budget']:raise ValueError('Probe trace mismatch')
                    if any(k in request['task'] for k in ('heldoutSeed','heldoutSuite','heldoutResults','heldoutProfile')):raise ValueError('Held-out data was exposed')
                    wall+=call['wallSeconds'];dev=record['developmentResults'][attempt];held=record['heldoutResults'][attempt]
                    feedback=record['developmentFeedback'][attempt]
                    if feedback.get('diagnostics')!=development_diagnostics(dev):raise ValueError('Development diagnostics changed')
                    if request['feedback']!=record['developmentFeedback'][:attempt]:raise ValueError('Development feedback history changed')
                    if dev is not None:
                        reply=read(directory/'stdout.txt');proposal=reply['proposal']
                        if task['family']!='http' and task.get('_backend')!='code' and proposal_result(task,proposal,task['developmentSeed'])!=dev:raise ValueError('Development score changed')
                        if task.get('_backend')=='code':
                            from rsi_code import validate
                            validate(task,proposal,task['developmentSeed'],dev)
                        if task['family']=='http':check_http(dev,task['developmentSuite'])
                        if dev['correct'] and dev['gain']>best_dev:best=proposal;best_dev=dev['gain']
                    if held is not None and task['family']!='http' and task.get('_backend')!='code' and proposal_result(task,best,task['heldoutSeed'])!=held:raise ValueError('Held-out score changed')
                    if held is not None and task.get('_backend')=='code':
                        from rsi_code import validate
                        validate(task,best,task['heldoutSeed'],held)
                    if held is not None and task['family']=='http':check_http(held,task['heldoutSuite'])
                    recomputed.append(held['gain'] if held and held['correct'] and wall<=config['budget']['taskSeconds'] else 0.)
                recomputed += [0.]*(config['budget']['attempts']-len(recomputed))
                if recomputed!=record['checkpoints'] or mean(recomputed)!=record['searchCapability'] or recomputed[-1]!=record['finalGain'] or (recomputed[-1]>=.15)!=record['success']:
                    raise ValueError('Capability trajectory changed')
        identities={'parent':previous,'reverted_parent':previous,'child':edge['childSha256'],'frozen':sha(folder/'agents/frozen.py')}
        parent_path=folder/'agents'/('frozen.py' if i==0 else f'child-{i-1}.py');child_path=folder/f'agents/child-{i}.py';reverted_path=folder/f'agents/mechanism-reverted-{i}.py'
        if reverted_path.read_text(encoding='utf-8')!=reverted_source(parent_path,child_path) or sha(reverted_path)!=stage['mechanismRevertedSha256']:
            raise ValueError('Improver component reversion changed')
        for arm,identity in identities.items():check_probes(stage['arms'][arm],identity)
        if len(stage['forks'])!=config.get('forkReplicates',3):raise ValueError('Incomplete fork replicates')
        failed_forks+=sum(not f[k]['accepted'] for f in stage['forks'] for k in ('parentFork','childFork','mechanismFork'))
        for j,fork in enumerate(stage['forks']):
            check_edge(fork['parentFork'],f'parent-fork-{i}-{j}.py',previous)
            check_edge(fork['childFork'],f'child-fork-{i}-{j}.py',edge['childSha256'])
            check_edge(fork['mechanismFork'],f'mechanism-fork-{i}-{j}.py',sha(reverted_path))
            for arm,key in [('parentProbes','parentFork'),('childProbes','childFork'),('mechanismProbes','mechanismFork')]:
                if fork[key]['accepted']:check_probes(fork[arm],fork[key]['childSha256'])
                elif fork[arm]!=failed_probes(stage['cohort'],config['budget']['attempts']):raise ValueError('Failed fork was not scored as zero')
        for arm,key in [('parent_offspring','parentProbes'),('child_offspring','childProbes'),('mechanism_reverted_offspring','mechanismProbes')]:
            if stage['arms'][arm]!=aggregate_forks([f[key] for f in stage['forks']]):raise ValueError('Fork aggregation changed')
        calculated=analyze(stage['arms'],config.get('effectMargin',.02),stage['forks'],mechanism(parent_path)[0]!=mechanism(child_path)[0])
        retention=stage.get('retention')
        if suite=='sandbox-stream-v1' and i>0:
            expected=[]
            for phase in range(i):expected+=tasks_for(f"{result['seed']}:retention:{i}:{phase}",12,backend,suite,phase,scale)
            if retention is None or retention['cohort']!=expected:raise ValueError('Retention cohort changed or missing')
            task_index={t['id']:t for t in expected}
            for name,identity in (('parent',previous),('child',edge['childSha256'])):
                if [r['taskId'] for r in retention[name]]!=[t['id'] for t in expected]:raise ValueError('Retention observations missing')
                check_probes(retention[name],identity)
            calculated=apply_retention(calculated,retention,config.get('effectMargin',.02))
        elif retention is not None:raise ValueError('Unexpected retention cohort')
        if calculated!=stage['analysis']:raise ValueError('Meta score modified')
        analyses.append(calculated);previous=edge['childSha256']
    if result['failedForks']!=failed_forks or result['heldoutPolicy']!=heldout_policy(backend,suite) or result.get('taskSuite','algorithm-v1')!=suite or result.get('workloadScale',1)!=scale:raise ValueError('Protocol summary changed')
    if analyses!=result['stages'] or result['supportedRecursiveStages']!=sum(a['recursiveEvidenceSupported'] for a in analyses):raise ValueError('Result summary mismatch')
    return {'protocolValidated':True,'supportedRecursiveStages':sum(a['recursiveEvidenceSupported'] for a in analyses),'fixtureOnly':result['fixtureOnly']}

def main():
    parser=argparse.ArgumentParser(description=__doc__);commands=parser.add_subparsers(dest='command',required=True)
    launch=commands.add_parser('run');launch.add_argument('config',type=Path);launch.add_argument('--output',type=Path,required=True)
    validation=commands.add_parser('verify');validation.add_argument('folder',type=Path)
    presentation=commands.add_parser('report');presentation.add_argument('folder',type=Path);presentation.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    try:
        if args.command=='report':
            from rsi_report import report
            print(json.dumps(report(args.folder,args.output)));return 0
        result=run(read(args.config),args.config.resolve().parent,args.output) if args.command=='run' else verify(args.folder)
        print(json.dumps({'output':str(args.output) if args.command=='run' else str(args.folder),
            'protocolValidated':result.get('protocolValidated'),'supportedRecursiveStages':result.get('supportedRecursiveStages'),'fixtureOnly':result.get('fixtureOnly')}));return 0
    except (ValueError,KeyError,TypeError,OSError,subprocess.SubprocessError) as e:print(json.dumps({'error':str(e)}),file=sys.stderr);return 2
if __name__=='__main__':sys.exit(main())
