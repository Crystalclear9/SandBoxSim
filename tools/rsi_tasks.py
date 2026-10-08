"""Frozen, auditable SandBoxSim-derived algorithm workloads for improver probes.

Policies select evaluator-owned algorithms; they cannot supply their own work counters.
These are bounded algorithm-selection probes, not a substitute for full game optimization.
"""
import collections, hashlib, heapq, json, random

CHOICES = {'resource':['scan','by_kind'], 'routing':['bfs','astar'], 'visual_update':['full','incremental']}

def resource(seed, strategy, n, q):
    rng=random.Random(seed)
    items=[(rng.randrange(64),rng.randrange(64),rng.randrange(4)) for _ in range(n)]
    queries=[(rng.randrange(64),rng.randrange(64),rng.randrange(4)) for _ in range(q)]
    groups=collections.defaultdict(list); work=0
    if strategy=='by_kind':
        for i,item in enumerate(items): groups[item[2]].append((i,item)); work+=1
    result=[]
    for x,y,kind in queries:
        best=(100000,-1)
        entries=enumerate(items) if strategy=='scan' else groups[kind]
        for i,(ix,iy,k) in entries:
            work+=1
            if k==kind: best=min(best,(abs(ix-x)+abs(iy-y),i))
        result.append(best[1])
    return result,work

def routing(seed, strategy, n, q):
    rng=random.Random(seed); size=max(8,int(n**.5)); blocked={i for i in range(size*size) if rng.random()<.17}
    result=[]; work=0
    for _ in range(q):
        start,goal=rng.randrange(size*size),rng.randrange(size*size); blocked.discard(start);blocked.discard(goal)
        distance={start:0}; closed=set(); queue=collections.deque([start]); heap=[(0,start)]
        found=-1
        while heap if strategy=='astar' else queue:
            node=heapq.heappop(heap)[1] if strategy=='astar' else queue.popleft();work+=1
            if node in closed: continue
            closed.add(node)
            if node==goal: found=distance[node];break
            x,y=node%size,node//size
            for dx,dy in ((1,0),(-1,0),(0,1),(0,-1)):
                work+=1; nx,ny=x+dx,y+dy; nxt=ny*size+nx
                if not 0<=nx<size or not 0<=ny<size or nxt in blocked:continue
                cost=distance[node]+1
                if cost>=distance.get(nxt,100000):continue
                distance[nxt]=cost;work+=1
                if strategy=='astar':
                    heapq.heappush(heap,(cost+abs(nx-goal%size)+abs(ny-goal//size),nxt))
                else:queue.append(nxt)
        result.append(found)
    return result,work

def visual_update(seed,strategy,n,q):
    rng=random.Random(seed); world=[rng.randrange(1000) for _ in range(n)]
    def geometry(value):return (value*2654435761 ^ (value>>3)) & 0xffffffff
    cached=[geometry(v) for v in world];work=n;observations=[]
    for _ in range(q):
        dirty=[]
        for _ in range(max(1,n//32)):
            i=rng.randrange(n);world[i]=rng.randrange(1000);dirty.append(i)
        for i in (range(n) if strategy=='full' else sorted(set(dirty))):cached[i]=geometry(world[i]);work+=1
        observations.append(hashlib.sha256(json.dumps(cached).encode()).hexdigest())
    return observations,work

def evaluate(task,proposal,seed):
    family=task['family']; strategy=proposal.get('strategy')
    if strategy not in CHOICES[family]:raise ValueError('Unknown strategy for '+family)
    kernel=globals()[family]; baseline=CHOICES[family][0]
    expected,base_work=kernel(seed,baseline,task['n'],task['q'])
    actual,work=kernel(seed,strategy,task['n'],task['q'])
    if actual!=expected:raise ValueError('Candidate changed workload results')
    return {'correct':True,'workUnits':work,'baselineWorkUnits':base_work,
            'gain':max(0.,1-work/base_work),'digest':hashlib.sha256(json.dumps(actual).encode()).hexdigest()}
