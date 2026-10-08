"""Non-LLM integration fixture submitting actual code, never a model RSI result."""
import json, sys
STAGE = 0
BASELINE = '''def solve(problem):
    if problem['family']=='resource':
        result=[]
        for x,y,kind in problem['queries']:
            candidates=[(abs(ix-x)+abs(iy-y),i) for i,(ix,iy,k) in enumerate(problem['items']) if k==kind]
            result.append(min(candidates)[1] if candidates else -1)
        return result
    if problem['family']=='routing':
        from collections import deque
        size=problem['size'];blocked=set(problem['blocked']);result=[]
        for start,goal in problem['queries']:
            excluded=blocked-{start,goal};queue=deque([start]);distance={start:0};found=-1
            while queue:
                node=queue.popleft()
                if node==goal:found=distance[node];break
                x,y=node%size,node//size
                for dx,dy in ((1,0),(-1,0),(0,1),(0,-1)):
                    nx,ny=x+dx,y+dy;nxt=ny*size+nx
                    if 0<=nx<size and 0<=ny<size and nxt not in excluded and nxt not in distance:
                        distance[nxt]=distance[node]+1;queue.append(nxt)
            result.append(found)
        return result
    world=problem['world'][:];result=[]
    for changes in problem['updates']:
        for index,value in changes:world[index]=value
        result.append([(v*2654435761 ^ (v>>3)) & 0xffffffff for v in world])
    return result
'''
OPTIMIZED = '''def solve(problem):
    if problem['family']=='resource':
        groups={};result=[]
        for i,(x,y,k) in enumerate(problem['items']):groups.setdefault(k,[]).append((i,x,y))
        for x,y,kind in problem['queries']:
            best=(10**9,-1)
            for i,ix,iy in groups.get(kind,[]):
                distance=abs(ix-x)+abs(iy-y)
                if distance<best[0]:best=(distance,i)
            result.append(best[1])
        return result
    if problem['family']=='routing':
        from heapq import heappush,heappop
        size=problem['size'];blocked=set(problem['blocked']);result=[]
        for start,goal in problem['queries']:
            excluded=blocked-{start,goal};queue=[(0,start)];distance={start:0};closed=set();found=-1
            gx,gy=goal%size,goal//size
            while queue:
                node=heappop(queue)[1]
                if node in closed:continue
                closed.add(node)
                if node==goal:found=distance[node];break
                x,y=node%size,node//size
                for dx,dy in ((1,0),(-1,0),(0,1),(0,-1)):
                    nx,ny=x+dx,y+dy;nxt=ny*size+nx;cost=distance[node]+1
                    if 0<=nx<size and 0<=ny<size and nxt not in excluded and cost<distance.get(nxt,10**9):
                        distance[nxt]=cost;heappush(queue,(cost+abs(nx-gx)+abs(ny-gy),nxt))
            result.append(found)
        return result
    world=problem['world'][:];cached=[(v*2654435761 ^ (v>>3)) & 0xffffffff for v in world];result=[]
    for changes in problem['updates']:
        dirty={}
        for i,v in changes:world[i]=v;dirty[i]=v
        for i,v in dirty.items():cached[i]=(v*2654435761 ^ (v>>3)) & 0xffffffff
        result.append(cached[:])
    return result
'''
def improve(request):
    return {'successorSource':request['source'].replace('STAGE = '+str(STAGE),'STAGE = '+str(STAGE+1),1)}
def propose(request):
    return {'proposal':{'source':BASELINE if STAGE==0 else OPTIMIZED}}
request=json.load(sys.stdin)
print(json.dumps(improve(request) if request['mode']=='improve' else propose(request)))
