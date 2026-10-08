"""Cost-sensitive analysis of verified RSI archives. Does not change original admission."""
from __future__ import annotations
import argparse
import hashlib
import json
import math
import random
import sys
from pathlib import Path

FRACTIONS = (.1, .25, .5, .75, 1.)
HORIZONS = (1, 100, 10000, 1000000)


def mean(values):
    return sum(values)/len(values) if values else 0.


def duration(value):
    if isinstance(value,bool) or not isinstance(value,(int,float)) or not math.isfinite(value) or value<=0:
        raise ValueError('Recorded invocation duration must be finite and positive')
    return value


def trajectory(row, budget, fraction=1., threshold=.15):
    """Replay only completed calls; no hindsight maximum over held-out answers."""
    limit = budget*fraction
    elapsed = 0.; gain = 0.; first = None; area = 0.; previous = 0.
    for i, call in enumerate(row['calls']):
        elapsed += duration(call['wallSeconds'])
        boundary = min(limit, elapsed)
        area += gain*max(0., boundary-previous)
        previous = boundary
        if elapsed > limit: break
        gain = row['checkpoints'][i]
        if first is None and gain >= threshold: first = elapsed
    area += gain*max(0., limit-previous)
    return {'gain':gain, 'timeAuc':area/limit, 'success':gain>=threshold,
            'restrictedTimeToSuccess':limit if first is None else first,
            'censored':first is None}


def paired_effect(values, families, margin=0.):
    from rsi_benchmark import effect
    return effect(values, families, margin)


def anytime(stage, budget):
    parent, child = stage['arms']['reverted_parent'], stage['arms']['child']
    families = [r['family'] for r in parent]
    curves = []
    for fraction in FRACTIONS:
        p = [trajectory(r,budget,fraction) for r in parent]
        c = [trajectory(r,budget,fraction) for r in child]
        curves.append({'fraction':fraction, 'agentSeconds':budget*fraction,
                       'parentGain':mean([r['gain'] for r in p]), 'childGain':mean([r['gain'] for r in c]),
                       'gainEffect':paired_effect([b['gain']-a['gain'] for a,b in zip(p,c)],families)})
    p = [trajectory(r,budget) for r in parent]; c = [trajectory(r,budget) for r in child]
    return {'curves':curves,
            'timeAucEffect':paired_effect([b['timeAuc']-a['timeAuc'] for a,b in zip(p,c)],families),
            'restrictedTimeSaved':paired_effect([a['restrictedTimeToSuccess']-b['restrictedTimeToSuccess'] for a,b in zip(p,c)],families),
            'parentCensored':sum(r['censored'] for r in p), 'childCensored':sum(r['censored'] for r in c),
            'taskSeconds':budget, 'threshold':.15}


def lower_bound_search_cost(row):
    # Invocation time plus recorded solve time; excludes import/compile/copy and evaluator overhead.
    cost = sum(duration(c['wallSeconds']) for c in row['calls'])
    for key in ('developmentResults','heldoutResults'):
        for measurement in row.get(key,[]):
            if measurement and 'codeReport' in measurement:
                for group in ('reference','candidate'):
                    cost += sum(r['wallNanoseconds'] for r in measurement['codeReport'][group])/1e9
    return cost


def second_order(stage):
    """Paired hierarchical resampling, includes failed generation cost and zero yield."""
    forks = stage['forks']; arms = stage['arms']; n = len(arms['child'])
    groups = {}
    for i,row in enumerate(arms['child']):groups.setdefault(row['family'],[]).append(i)
    data = []
    for fork in forks:
        rates = {}
        for label,key,edge,baseline in (
            ('parent','parentProbes','parentFork','reverted_parent'),
            ('child','childProbes','childFork','child'),
            ('reverted','mechanismProbes','mechanismFork','child')):
            generation = duration(fork[edge]['execution']['wallSeconds'])/n
            rates[label] = [(row['searchCapability']-arms[baseline][i]['searchCapability'])/
                            max(1e-12,generation+lower_bound_search_cost(row))
                            for i,row in enumerate(fork[key])]
        data.append(rates)
    def estimate(selected, indices, other):
        return mean([mean([mean([f['child'][i]-f[other][i] for f in selected]) for i in group]) for group in indices])
    rng = random.Random(1729); estimates = {'parent':[], 'reverted':[]}
    for _ in range(1500):
        selected = rng.choices(data,k=len(data))
        indices = [rng.choices(g,k=len(g)) for g in groups.values()]
        for other in estimates:estimates[other].append(estimate(selected,indices,other))
    result = {}
    for other, samples in estimates.items():
        samples.sort()
        result[other] = {'meanEffect':estimate(data,list(groups.values()),other),
                         'hierarchical95':[samples[37],samples[1462]],
                         'supported':n>=12 and len(forks)>=3 and samples[37]>0.,
                         'tasks':n,'branchReplicates':len(forks)}
    result['costScope'] = 'Generation and search invocation plus recorded solve wall time; incomplete total cost lower bound, not tokens or money'
    return result


def final_code(row, budget):
    if sum(c['wallSeconds'] for c in row['calls'])>budget:return None
    measurements = row.get('heldoutResults',[])
    value = measurements[-1] if measurements else None
    if not value or not value['correct'] or not value.get('codeReport',{}).get('memoryBudgetPassed'):return None
    return value['codeReport']


def payback(stage, budget):
    records = []
    for parent,child in zip(stage['arms']['reverted_parent'],stage['arms']['child']):
        p,c = final_code(parent,budget),final_code(child,budget)
        cost = lower_bound_search_cost(child)+duration(stage['lineage']['execution']['wallSeconds'])/len(stage['cohort'])
        saving = None; reason = '没有完整、正确且满足内存限制的配对代码测量'
        if p and c:
            if p['expectedDigests']!=c['expectedDigests']:raise ValueError('Deployment workloads are not paired')
            wall = [(a['wallNanoseconds']-b['wallNanoseconds'])/1e9 for a,b in zip(p['candidate'],c['candidate'])]
            cpu = [(a['cpuNanoseconds']-a['cpuQuantumNanoseconds']-b['cpuNanoseconds']-b['cpuQuantumNanoseconds'])/1e9
                   for a,b in zip(p['candidate'],c['candidate'])]
            if len(wall)==7 and min(wall)>0 and min(cpu)>0:
                saving = min(wall); reason = None
            else:reason = '七组直接父子对照不能全部确认 CPU 与墙钟改善'
        records.append({'taskId':parent['taskId'],'family':parent['family'],
                        'recordedInvestmentSeconds':cost,'minimumObservedSavingSeconds':saving,
                        'optimisticBreakEvenExecutions':math.ceil(cost/saving) if saving else None,
                        'reason':reason,
                        'horizons':[{'executions':h,'optimisticNetSeconds':h*saving-cost if saving else None} for h in HORIZONS]})
    return {'tasks':records, 'confirmedSavingTasks':sum(r['minimumObservedSavingSeconds'] is not None for r in records),
            'costScope':'Child search and amortized lineage generation only; training, failed workers and evaluator overhead omitted. Break-even is optimistic, not an upper bound or deployment guarantee.'}


def stage_study(stage, budget):
    curve = anytime(stage,budget); rates = second_order(stage); recovery = payback(stage,budget)
    gates = {'originalRecursiveEvidence':stage['analysis']['recursiveEvidenceSupported'],
             'timeAucImproved':curve['timeAucEffect']['supported'],
             'costAdjustedOffspringYield':rates['parent']['supported'],
             'costAdjustedMechanismEffect':rates['reverted']['supported']}
    return {'stage':stage['stage'],'anytime':curve,'secondOrderEfficiency':rates,'deployment':recovery,
            'efficiencyGates':gates,'costSensitiveEvidenceSupported':all(gates.values()),
            'unmetGates':[k for k,v in gates.items() if not v]}


def study(folder):
    from rsi_benchmark import verify, read
    folder = Path(folder); verified = verify(folder); result = read(folder/'result.json')
    stages = [stage_study(read(folder/f'stage-{i}.json'),result['budget']['taskSeconds']) for i in range(len(result['stages']))]
    return {'schemaVersion':1,'kind':'rsi-cost-study-v1','protocolValidated':verified['protocolValidated'],
            'sourceResultSha256':hashlib.sha256((folder/'result.json').read_bytes()).hexdigest(),
            'analyzerSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
            'fixtureOnly':result['fixtureOnly'],'backend':result['backend'],'stages':stages,
            'costSensitiveSupportedStages':sum(s['costSensitiveEvidenceSupported'] for s in stages),
            'realModelRsiClaim':False,
            'limitations':['Retrospective budget truncation does not predict a budget-aware policy rerun',
                           'Cost ledger is incomplete; measured allocation tracing affects solve wall time',
                           'Bootstrap intervals are exploratory, not corrected for multiple budget comparisons',
                           'No weight updates, model identity authentication or general RSI certification']}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder',type=Path);parser.add_argument('--output',type=Path,required=True)
    args = parser.parse_args()
    try:
        result = study(args.folder)
        args.output.parent.mkdir(parents=True,exist_ok=True)
        with args.output.open('x',encoding='utf-8') as handle:json.dump(result,handle,ensure_ascii=False,indent=2,allow_nan=False)
        print(json.dumps({'output':str(args.output),'costSensitiveSupportedStages':result['costSensitiveSupportedStages'],'fixtureOnly':result['fixtureOnly']}))
        return 0
    except (ValueError,KeyError,TypeError,OSError) as e:
        print(json.dumps({'error':str(e)}),file=sys.stderr);return 2


if __name__=='__main__':sys.exit(main())
