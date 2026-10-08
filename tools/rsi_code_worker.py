"""Trusted local candidate execution; process limits are imposed by the parent."""
import copy, hashlib, json, os, sys, time, tracemalloc

request=json.load(sys.stdin)
namespace={}
exec(compile(request['source'],'candidate.py','exec'),namespace)
solve=namespace.get('solve')
if not callable(solve):raise ValueError('Candidate must define solve(problem)')
rows=[]
for problem in request['problems']:
    value=copy.deepcopy(problem)
    tracemalloc.start()
    wall=time.perf_counter_ns();start=time.process_time_ns()
    output=solve(value)
    elapsed=time.process_time_ns()-start;wall=time.perf_counter_ns()-wall
    _,peak=tracemalloc.get_traced_memory();tracemalloc.stop()
    encoded=json.dumps(output,separators=(',',':'),allow_nan=False).encode()
    rows.append({'digest':hashlib.sha256(encoded).hexdigest(),'cpuNanoseconds':elapsed,'cpuQuantumNanoseconds':15625000 if os.name=='nt' else max(1,int(time.get_clock_info('process_time').resolution*1e9)),'wallNanoseconds':wall,'peakPythonBytes':peak})
print(json.dumps({'rows':rows}))
