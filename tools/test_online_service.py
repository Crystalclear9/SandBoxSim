"""Real HTTP/service smoke including authentication, concurrency, retry and paired evaluation."""
import argparse, concurrent.futures, json, secrets, socket, tempfile, urllib.error
from pathlib import Path
from online import Client, launch, ready, ROOT
from efficiency import evaluate, read, write, experiment

def free_port():
    with socket.socket() as s:
        s.bind(('127.0.0.1',0)); return s.getsockname()[1]

def smoke(assembly, output):
    output=Path(output); output.mkdir(parents=True,exist_ok=False)
    token=secrets.token_hex(24); ports=[free_port(),free_port()]; processes=[]; logs=[]
    clients=[Client('http://127.0.0.1:'+str(p),token) for p in ports]
    try:
        for i,p in enumerate(ports):
            log=(output/f'service-{i}.log').open('wb'); logs.append(log)
            process=launch(assembly,p,token,log); processes.append(process); ready(clients[i],process)
        try:
            Client(clients[0].url,'incorrect-token-value').request('GET','/v1/health')
            raise AssertionError('Missing authentication rejection')
        except urllib.error.HTTPError as e: assert e.code==401
        client=clients[0]; created=client.request('POST','/v1/sessions',{'seed':17,'width':24,'height':24,'agents':4})
        path='/v1/sessions/'+created['sessionId']; before=created['observation']
        assert client.request('GET',path)==before
        payload={'requestId':'duplicate','expectedRevision':0,'ticks':8}
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            replies=list(pool.map(lambda _:client.request('POST',path+'/step',payload),range(2)))
        assert replies[0]==replies[1] and client.request('GET',path)['tick']==8
        try:
            client.request('POST',path+'/step',{'requestId':'stale','expectedRevision':0,'ticks':1})
            raise AssertionError('Missing stale-revision rejection')
        except urllib.error.HTTPError as e: assert e.code==409
        client.request('DELETE',path)
        report=evaluate(clients[0],clients[1],read(ROOT/'benchmarks/efficiency-smoke.json'),3)
        write(output/'paired.json',report)
        if not report['correct']: raise AssertionError(json.dumps(report['failures']))
        print(json.dumps({'HTTP_SERVICE_PASS':True,'pairs':len(report['samples']),'correct':report['correct'],'summary':report['summary']}))
        # Real paired runs exercise the round-evidence pipeline with explicitly synthetic agent fixtures.
        # This is protocol validation, not an LLM experiment or a claim of improvement.
        suite=read(ROOT/'benchmarks/efficiency-smoke.json'); suite['split']='heldout'
        for case in suite['cases']: case['seed']=secrets.randbelow(2147483647)
        for name,value in [('agent0.json',{'fixture':'unchanged controller'}),('agent1.json',{'fixture':'changed controller'}),('patch0.json',{'fixture':'no-op candidate'}),('patch1.json',{'fixture':'no-op candidate round two'})]:
            write(output/name,value)
        write(output/'heldout-suite.json',suite)
        setup={'schemaVersion':1,'validationFixture':True,'budget':{'tokens':100,'wallSeconds':10,'attempts':1},
            'suite':'heldout-suite.json','repeats':3,'reference':clients[0].url,'control':clients[1].url,
            'controlAgentArtifact':'agent0.json','rounds':[]}
        for index in range(2):
            setup['rounds'].append({'candidate':clients[1].url,'agentArtifact':f'agent{index}.json',
                'patch':f'patch{index}.json','cost':{'tokens':0,'wallSeconds':0,'attempts':1},
                'controlCost':{'tokens':0,'wallSeconds':0,'attempts':1}})
        write(output/'experiment.json',setup)
        evidence=experiment(setup,output,output/'experiment',lambda url:next(c for c in clients if c.url==url))
        assert evidence['validated'] and evidence['improvingRounds']==0
        print(json.dumps({'RSI_EVIDENCE_PIPELINE_PASS':True,'rounds':len(evidence['rounds']),'fixtureOnly':True,'improvingRounds':0}))
    finally:
        for process in processes:
            if process.poll() is None: process.terminate()
        for process in processes:
            try: process.wait(timeout=10)
            except Exception: process.kill(); process.wait()
        for log in logs: log.close()

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__); parser.add_argument('--assembly',type=Path,required=True); parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args(); smoke(args.assembly,args.output)
