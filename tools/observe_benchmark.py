"""Paired complete observation windows: create, mutate, repeated reads and delete."""
import argparse, hashlib, importlib.util, json, secrets, socket, time
from pathlib import Path
from online import Client,launch,ready
from efficiency import paired_summary,write

def window(client,seed,reads):
    start=time.perf_counter();created=client.request('POST','/v1/sessions',{'seed':seed,'width':96,'height':96,'agents':40})
    path='/v1/sessions/'+created['sessionId']
    try:
        expected=client.request('POST',path+'/step',{'requestId':'start','expectedRevision':0,'ticks':20})['observation']
        query_start=time.perf_counter()
        for _ in range(reads):
            if client.request('GET',path)!=expected:raise ValueError('Observation drift')
        read_ms=(time.perf_counter()-query_start)*1000
        map1=client.request('GET',path+'/map');map2=client.request('GET',path+'/map')
        if map1!=map2:raise ValueError('Traversal cache drift')
    finally:client.request('DELETE',path)
    return {'wallMs':(time.perf_counter()-start)*1000,'readMs':read_ms,'digest':expected['digest']}

def run(reference,candidate,output,reads=100,repeats=5,reference_client=None):
    output=Path(output);output.mkdir(parents=True,exist_ok=False);token=secrets.token_hex(24);ports=[];processes=[];logs=[]
    while len(ports)<2:
        with socket.socket() as sock:sock.bind(('127.0.0.1',0));port=sock.getsockname()[1]
        if port not in ports:ports.append(port)
    original=Client
    if reference_client:
        spec=importlib.util.spec_from_file_location('reference_transport',reference_client);module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module);original=module.Client
    clients=[original('http://127.0.0.1:'+str(ports[0]),token),Client('http://127.0.0.1:'+str(ports[1]),token)]
    try:
        identities=[]
        for i,assembly in enumerate((reference,candidate)):
            log=(output/f'engine-{i}.log').open('wb');logs.append(log);process=launch(assembly,ports[i],token,log);processes.append(process);identities.append(ready(clients[i],process))
        window(clients[0],17,10);window(clients[1],17,10)
        samples=[]
        for i in range(repeats):
            order=(0,1) if i%2==0 else (1,0);results={k:window(clients[k],839102+i,reads) for k in order}
            if results[0]['digest']!=results[1]['digest']:raise ValueError('Reference/candidate simulation differs')
            samples.append({'referenceMs':results[0]['wallMs'],'candidateMs':results[1]['wallMs'],'reference':results[0],'candidate':results[1]})
        report={'schemaVersion':1,'kind':'online-observation-window','reads':reads,'samples':samples,'identities':identities,
            'referenceClientSha256':hashlib.sha256(Path(reference_client or Path(__file__).with_name('online.py')).read_bytes()).hexdigest(),
            'candidateClientSha256':hashlib.sha256(Path(__file__).with_name('online.py').read_bytes()).hexdigest(),
            'summary':paired_summary(samples),'scope':'complete create/mutate/read/map/delete window on this host; read-only caching, not simulation tick acceleration'}
        write(output/'result.json',report);return report
    finally:
        for process in processes:
            if process.poll() is None:process.terminate()
        for process in processes:process.wait(timeout=10)
        for log in logs:log.close()

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--reference',required=True);parser.add_argument('--candidate',required=True);parser.add_argument('--output',type=Path,required=True);parser.add_argument('--reads',type=int,default=100);parser.add_argument('--repeats',type=int,default=9);parser.add_argument('--reference-client-file',type=Path);args=parser.parse_args()
    if not 1<=args.reads<=10000 or not 3<=args.repeats<=20:parser.error('Use 1-10000 reads and 3-20 repeats')
    result=run(args.reference,args.candidate,args.output,args.reads,args.repeats,args.reference_client_file);print(json.dumps(result['summary']))
