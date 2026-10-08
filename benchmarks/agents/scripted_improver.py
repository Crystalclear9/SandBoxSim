"""Explicit non-LLM positive-control fixture. Not evidence of real model RSI."""
import json, sys
STAGE = 0
request=json.load(sys.stdin)
def improve(request):
    next_stage = STAGE + 1
    source=request['source'].replace('STAGE = '+str(STAGE),'STAGE = '+str(next_stage),1)
    if STAGE == 1:
        source=source.replace('next_stage = STAGE + 1','next_stage = 4',1)
    return {'successorSource':source,'fixtureOnly':True}
def propose(request):
    family=request['task']['family']
    choice={'routing':'astar' if STAGE>=1 else 'bfs','resource':'by_kind' if STAGE>=2 else 'scan',
            'visual_update':'incremental' if STAGE>=4 else 'full'}[family]
    return {'proposal':{'strategy':choice},'fixtureOnly':True}
print(json.dumps(improve(request) if request['mode']=='improve' else propose(request)))
