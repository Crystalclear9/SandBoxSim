"""Non-LLM stateful-world integration fixture; never evidence of real model RSI."""
import json,sys
STAGE = 0
BASELINE = '''def solve(problem):
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
OPTIMIZED = '''def solve(problem):
    family=problem['family'];answers=[]
    if family=='resource_ledger':
        items=[r[:] for r in problem['items']];groups={};cache={}
        for i,row in enumerate(items):groups.setdefault(row[2],[]).append(i)
        for c in problem['commands']:
            if c[0]=='stock':items[c[1]][3]=c[2];cache.clear();continue
            _,x,y,kind=c;key=(x,y,kind)
            if key not in cache:
                best=(10**20,-1)
                for i in groups.get(kind,[]):
                    ix,iy,k,stock=items[i]
                    if stock>0:best=min(best,(abs(ix-x)+abs(iy-y),i))
                cache[key]=best[1]
            answers.append(cache[key])
        return answers
    if family=='dynamic_routes':
        from heapq import heappush,heappop
        costs=problem['costs'][:];size=problem['size'];cache={}
        for c in problem['commands']:
            if c[0]=='cost':costs[c[1]]=c[2];cache.clear();continue
            _,start,goal=c;key=(start,goal)
            if key in cache:answers.append(cache[key]);continue
            found=-1
            if costs[start] and costs[goal]:
                gx,gy=goal%size,goal//size;queue=[(0,0,start)];distances={start:0}
                while queue:
                    _,distance,node=heappop(queue)
                    if distance!=distances[node]:continue
                    if node==goal:found=distance;break
                    x,y=node%size,node//size
                    for dx,dy in ((1,0),(-1,0),(0,1),(0,-1)):
                        nx,ny=x+dx,y+dy;nxt=ny*size+nx
                        if not 0<=nx<size or not 0<=ny<size or not costs[nxt]:continue
                        candidate=distance+costs[nxt]
                        if candidate<distances.get(nxt,10**20):
                            distances[nxt]=candidate;heappush(queue,(candidate+abs(nx-gx)+abs(ny-gy),candidate,nxt))
            cache[key]=found;answers.append(found)
        return answers
    def row(slot,generation,value):return [slot,generation,(value*2654435761 ^ (value>>3)) & 0xffffffff]
    state={slot:row(slot,generation,value) for slot,generation,value in problem['entities']}
    for c in problem['commands']:
        if c[0]=='upsert':
            _,slot,generation,value=c
            if slot not in state or generation>=state[slot][1]:state[slot]=row(slot,generation,value)
        elif c[0]=='delete':
            _,slot,generation=c
            if slot in state and state[slot][1]==generation:del state[slot]
        else:answers.append([state[slot] for slot in sorted(set(c[1])) if slot in state])
    return answers
'''

def improve(request):
    return {'successorSource':request['source'].replace('STAGE = '+str(STAGE),'STAGE = '+str(STAGE+1),1)}

def propose(request):
    return {'proposal':{'source':BASELINE if STAGE==0 else OPTIMIZED}}

if __name__=='__main__':
    request=json.load(sys.stdin)
    print(json.dumps(improve(request) if request['mode']=='improve' else propose(request)))
