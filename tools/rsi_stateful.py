"""Sandbox-derived changing-world probes. Independent Python workloads, not the C# world."""
import hashlib
import math
import random

SUITE = 'sandbox-stream-v1'
FAMILIES = ('resource_ledger', 'dynamic_routes', 'scene_lifecycle')
FORMATS = {
    'resource_ledger': 'items=[x,y,kind,stock]; commands=[query,x,y,kind] or [stock,index,newStock]. Process in order; query returns closest positive-stock item index, Manhattan distance, smallest index on ties, -1 when absent. Return query answers.',
    'dynamic_routes': 'size; costs=row-major positive cell costs; commands=[route,start,goal] or [cost,index,newCost]. Zero cost blocks a cell, including endpoints. Four-neighbour traversal pays destination cell cost; start=goal costs 0 if walkable. Return minimum costs or -1, in query order.',
    'scene_lifecycle': 'entities=[slot,generation,value]; commands=[upsert,slot,generation,value], [delete,slot,generation], or [observe,slots]. Upsert replaces a slot only when its generation is >= current; delete only removes the matching generation. Observe returns rows [slot,generation,geometry] sorted by unique requested slot, skipping absent slots. geometry=(value*2654435761 ^ (value>>3)) & 0xffffffff. Return observations in order.'
}


def cohort(seed, count, phase=0):
    rng = random.Random(seed)
    return [{'id': hashlib.sha256(f'{seed}:{i}'.encode()).hexdigest()[:16],
             'family': FAMILIES[i % 3], 'n': rng.randrange(96, 161), 'q': rng.randrange(48, 73),
             '_suite': SUITE, '_phase': phase,
             'heldoutProfile': ('read_heavy', 'churn')[i // 3 % 2],
             'developmentSeed': rng.randrange(2**31), 'heldoutSeed': rng.randrange(2**31)}
            for i in range(count)]


def problem(task, seed):
    rng = random.Random(seed)
    family, n, q = task['family'], task['n'], task['q']
    profile = task.get('heldoutProfile', 'standard') if seed == task.get('heldoutSeed') else 'standard'
    # Curriculum changes training locality/update rate; held-out extremes remain private.
    phase = task.get('_phase', 0)
    rate = .08 if profile == 'read_heavy' else .75 if profile == 'churn' else (.18, .45, .30, .60, .25)[phase % 5]
    commands = []
    if family == 'resource_ledger':
        items = [[rng.randrange(32), rng.randrange(32), rng.randrange(4), rng.randrange(1, 10)] for _ in range(n)]
        for i in range(q):
            x, y, kind = rng.randrange(32), rng.randrange(32), rng.randrange(4)
            commands.append(['query', x, y, kind])
            if rng.random() < rate:
                index = rng.randrange(n)
                commands.extend([['stock', index, 0], ['query', *items[index][:3]],
                                 ['stock', index, rng.randrange(1, 10)], ['query', *items[index][:3]]])
        # Deterministic depletion/refill at a unique exact-position nearest item.
        items[0] = [100, 100, 0, 1]
        commands[:0] = [['query',100,100,0], ['stock',0,0], ['query',100,100,0], ['stock',0,1], ['query',100,100,0]]
        return {'family': family, 'items': items, 'commands': commands}
    if family == 'dynamic_routes':
        size = max(8, int(math.sqrt(n)))
        costs = [rng.choice((0, 1, 1, 2, 4)) for _ in range(size * size)]
        for i in range(q):
            start, goal = rng.randrange(size*size), rng.randrange(size*size)
            commands.append(['route', start, goal])
            if rng.random() < rate:
                commands.extend([['cost',goal,0], ['route',start,goal], ['cost',goal,1], ['route',start,goal]])
        costs[0] = costs[1] = 1
        commands[:0] = [['route',0,1],['cost',1,0],['route',0,1],['cost',1,4],['route',0,1]]
        return {'family': family, 'size': size, 'costs': costs, 'commands': commands}
    if family != 'scene_lifecycle':
        raise ValueError('Unknown stateful family')
    entities = [[i,0,rng.randrange(1000)] for i in range(n)]
    generations = [0] * n
    for i in range(q):
        slots = [rng.randrange(n) for _ in range(12 if phase % 2 == 0 else 40)]
        commands.append(['observe',slots])
        if rng.random() < rate:
            slot = rng.randrange(n); old = generations[slot]; generations[slot] += 1
            commands.extend([['delete',slot,old], ['observe',[slot]],
                             ['upsert',slot,generations[slot],rng.randrange(1000)],
                             ['delete',slot,old], ['upsert',slot,old,999], ['observe',[slot]]])
    commands[:0] = [['observe',[0,0]], ['delete',0,0], ['observe',[0]],
                    ['upsert',0,1,7], ['delete',0,0], ['upsert',0,0,99], ['observe',[0,0]]]
    return {'family': family, 'entities': entities, 'commands': commands}


# Deliberately direct implementations define semantics independently of candidate caches.
REFERENCE = '''def solve(problem):
    family=problem['family'];answers=[]
    if family=='resource_ledger':
        items=[row[:] for row in problem['items']]
        for command in problem['commands']:
            if command[0]=='stock':items[command[1]][3]=command[2]
            else:
                _,x,y,kind=command
                candidates=[(abs(ix-x)+abs(iy-y),i) for i,(ix,iy,k,stock) in enumerate(items) if k==kind and stock>0]
                answers.append(min(candidates)[1] if candidates else -1)
        return answers
    if family=='dynamic_routes':
        from heapq import heappush,heappop
        costs=problem['costs'][:];size=problem['size']
        for command in problem['commands']:
            if command[0]=='cost':costs[command[1]]=command[2];continue
            _,start,goal=command
            if not costs[start] or not costs[goal]:answers.append(-1);continue
            queue=[(0,start)];distances={start:0};found=-1
            while queue:
                distance,node=heappop(queue)
                if distance!=distances[node]:continue
                if node==goal:found=distance;break
                x,y=node%size,node//size
                for dx,dy in ((1,0),(-1,0),(0,1),(0,-1)):
                    nx,ny=x+dx,y+dy;nxt=ny*size+nx
                    if not 0<=nx<size or not 0<=ny<size or not costs[nxt]:continue
                    candidate=distance+costs[nxt]
                    if candidate<distances.get(nxt,10**20):
                        distances[nxt]=candidate;heappush(queue,(candidate,nxt))
            answers.append(found)
        return answers
    state={slot:(generation,value) for slot,generation,value in problem['entities']}
    for command in problem['commands']:
        if command[0]=='upsert':
            _,slot,generation,value=command
            if slot not in state or generation>=state[slot][0]:state[slot]=(generation,value)
        elif command[0]=='delete':
            _,slot,generation=command
            if slot in state and state[slot][0]==generation:del state[slot]
        else:
            answers.append([[slot,state[slot][0],(state[slot][1]*2654435761 ^ (state[slot][1]>>3)) & 0xffffffff]
                            for slot in sorted(set(command[1])) if slot in state])
    return answers
'''


def development_example(task):
    sample=dict(task,n=min(task['n'],12),q=3)
    payload=problem(sample,task['developmentSeed'])
    namespace={};exec(REFERENCE,namespace)
    return {'problem':payload,'expected':namespace['solve'](payload)}
