"""Real HTTP/service smoke including authentication, concurrency, retry and paired evaluation."""
import http.client
import argparse, concurrent.futures, json, secrets, socket, tempfile, urllib.error
from pathlib import Path
from online import Client, launch, ready, ROOT
from efficiency import evaluate, read, write, experiment

def free_port():
    with socket.socket() as s:
        s.bind(('127.0.0.1',0)); return s.getsockname()[1]

def smoke(assembly, output):
    output=Path(output); output.mkdir(parents=True,exist_ok=False)
    token=secrets.token_hex(24); ports=[]; processes=[]; logs=[]
    while len(ports)<2:
        port=free_port()
        if port not in ports: ports.append(port)
    clients=[Client('http://127.0.0.1:'+str(p),token) for p in ports]
    try:
        for i,p in enumerate(ports):
            log=(output/f'service-{i}.log').open('wb'); logs.append(log)
            process=launch(assembly,p,token,log); processes.append(process); ready(clients[i],process)
        try:
            Client(clients[0].url,'incorrect-token-value').request('GET','/v1/health')
            raise AssertionError('Missing authentication rejection')
        except urllib.error.HTTPError as e: assert e.code==401
        client=clients[0]; creation={'requestId':'create-smoke','seed':17,'width':24,'height':24,'agents':4}
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            created_replies=list(pool.map(lambda _:client.request('POST','/v1/sessions',creation),range(2)))
        assert created_replies[0]==created_replies[1]
        created=created_replies[0]
        path='/v1/sessions/'+created['sessionId']; before=created['observation']
        assert client.request('GET',path)==before
        capabilities=client.request('GET','/v1/capabilities')
        assert capabilities['entityInspection'] and capabilities['creationIdempotency'] and capabilities['sessionListing']
        assert capabilities['creationTombstoneWindow']==64
        listing=client.request('GET','/v1/sessions')['sessions']
        assert len(listing)==1 and listing[0]['sessionId']==created['sessionId'] and listing[0]['requestId']=='create-smoke'
        page=client.request('POST',path+'/inspect',{'expectedRevision':0,'kind':'agents','limit':2})
        assert page['total']==4 and len(page['items'])==2 and page['nextOffset']==2
        assert page['digest']==before['digest'] and client.request('GET',path)==before
        try:
            client.request('GET','/v1/health?ignored=value')
            raise AssertionError('Query parameters were silently ignored')
        except urllib.error.HTTPError as e: assert e.code==400
        connection=http.client.HTTPConnection('127.0.0.1',ports[0],timeout=10)
        try:
            raw=b'{"requestId":"bad\xff","expectedRevision":0,"ticks":0}'
            connection.request('POST',path+'/step',raw,{'Authorization':'Bearer '+token,'Content-Type':'application/json'})
            response=connection.getresponse(); response.read(); assert response.status==400
        finally: connection.close()
        assert client.request('GET',path)==before

        payload={'requestId':'duplicate','expectedRevision':0,'ticks':8}
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            replies=list(pool.map(lambda _:client.request('POST',path+'/step',payload),range(2)))
        assert replies[0]==replies[1] and client.request('GET',path)['tick']==8
        assert client.request('POST','/v1/sessions',dict(reversed(list(creation.items()))))==created
        assert client.request('GET','/v1/sessions')['sessions'][0]['observation']['tick']==8
        try:
            client.request('POST','/v1/sessions',{**creation,'seed':18})
            raise AssertionError('Conflicting creation retry allocated a world')
        except urllib.error.HTTPError as e: assert e.code==409
        try:
            client.request('POST',path+'/reset',payload)
            raise AssertionError('Step reply was replayed through reset')
        except urllib.error.HTTPError as e: assert e.code==409
        assert client.request('GET',path)['tick']==8

        try:
            client.request('POST',path+'/step',{'requestId':'stale','expectedRevision':0,'ticks':1})
            raise AssertionError('Missing stale-revision rejection')
        except urllib.error.HTTPError as e: assert e.code==409
        extra=[]
        for _ in range(capabilities['maxSessions']-1):
            extra.append(client.request('POST','/v1/sessions',{'seed':17,'width':16,'height':16,'agents':0})['sessionId'])
        assert client.request('POST','/v1/sessions',creation)==created
        try:
            client.request('POST','/v1/sessions',{'requestId':'over-capacity','seed':17})
            raise AssertionError('Session capacity was exceeded')
        except urllib.error.HTTPError as e: assert e.code==429
        for session in extra: client.request('DELETE','/v1/sessions/'+session)
        client.request('DELETE',path)
        try:
            client.request('POST','/v1/sessions',creation)
            raise AssertionError('Deleted creation retry resurrected a world')
        except urllib.error.HTTPError as e: assert e.code==410
        assert client.request('GET','/v1/sessions')['sessions']==[]
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
        print(json.dumps({'OPTIMIZATION_HISTORY_PIPELINE_PASS':True,'rounds':len(evidence['rounds']),'fixtureOnly':True,'improvingRounds':0}))
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
