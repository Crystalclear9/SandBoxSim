"""Non-LLM pipeline fixture: failed compile followed by real BinaryHeap source experiment."""
import json
import sys

request=json.load(sys.stdin)
source=request['files'][0];content=source['content']
if request['attempt']==0:
    hypothesis='Deliberately invalid fixture tests whether compilation failure is retained and returned as development feedback.'
    content+='\nthis deliberately does not compile;\n'
else:
    hypothesis='Bubble the cached heap entry through a hole instead of swapping both arrays at each ancestor; preserve priority and node tie ordering.'
    start=content.index('    private void SiftUp(int index)')
    end=content.index('    private void SiftDown(int index)',start)
    content=content[:start]+'''    private void SiftUp(int index)
    {
        int node = _node[index];
        float priority = _priority[index];
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            float parentPriority = _priority[parent];
            bool less = priority < parentPriority || (!(priority > parentPriority) && node < _node[parent]);
            if (!less) { break; }
            _node[index] = _node[parent];
            _priority[index] = parentPriority;
            index = parent;
        }
        _node[index] = node;
        _priority[index] = priority;
    }

'''+content[end:]
print(json.dumps({'hypothesis':hypothesis,'edits':[{'path':source['path'],'baseSha256':source['baseSha256'],'content':content}]}))
