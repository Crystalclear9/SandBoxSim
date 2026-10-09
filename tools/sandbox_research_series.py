"""Repeated frozen real-source research, with run-cluster uncertainty and regression guards."""
from __future__ import annotations
import argparse
import json
import math
import random
import statistics
from pathlib import Path
import sandbox_research_compare as comparison


def interval(values, seed=731):
    if len(values)<3:return None
    rng=random.Random(seed);n=len(values)
    samples=sorted(statistics.mean(rng.choices(values,k=n)) for _ in range(5000))
    return {'low':samples[125],'high':samples[4874],'method':'paired-run-cluster percentile bootstrap; 5000 resamples','seed':seed}


def aggregate(blocks, regression_tolerance=.05):
    blocks=[Path(p).resolve() for p in blocks]
    if not blocks or len(set(blocks))!=len(blocks):raise ValueError('Provide distinct complete blocks')
    if isinstance(regression_tolerance,bool) or not math.isfinite(regression_tolerance) or not 0<=regression_tolerance<=1:
        raise ValueError('Invalid regression tolerance')
    archives=[(block/arm).resolve() for block in blocks for arm in comparison.ARMS]
    if len(set(archives))!=len(archives):raise ValueError('Research archives reused across blocks')
    rows=[];reference=None;conditions=None;orders=[]
    for block in blocks:
        result=comparison.analyze({arm:block/arm for arm in comparison.ARMS})
        source=comparison.read(block/'parent/source-manifest.json')
        config=comparison.read(block/'parent/config.json')
        comparable={k:config.get(k,comparison.DEFAULTS.get(k)) for k in comparison.CONDITIONS}
        if reference is not None and (source!=reference or comparable!=conditions):raise ValueError('Source or research conditions differ across blocks')
        reference=source;conditions=comparable
        order=comparison.read(block/'comparison.json').get('executionOrder')
        if not isinstance(order,list) or len(order)!=3 or set(order)!=set(comparison.ARMS):raise ValueError('Missing recorded execution order')
        orders.append(order);rows.append(result)
    counts=[[sum(order[position]==arm for order in orders) for arm in comparison.ARMS] for position in range(3)]
    balanced=all(min(values)>0 and max(values)-min(values)<=1 for values in counts)
    contrasts={};regressions=[]
    for other in ('parent','reverted'):
        effects=[r['childMinus'+other.capitalize()]['meanDifference'] for r in rows]
        efficiencies=[r['childMinus'+other.capitalize()]['researchEfficiencyDifference'] for r in rows]
        values=effects+efficiencies+[v for r in rows for v in r['childMinus'+other.capitalize()]['pairedWorldSavingsDifference']]
        if any(isinstance(v,bool) or not isinstance(v,(int,float)) or not math.isfinite(v) for v in values):raise ValueError('Invalid contrast value')
        contrasts[other]={'perRunWorldSavingsDifference':effects,'mean':statistics.mean(effects),'interval':interval(effects),
                          'perRunResearchEfficiencyDifference':efficiencies,'efficiencyInterval':interval(efficiencies)}
        for i,row in enumerate(rows):
            for world,difference in enumerate(row['childMinus'+other.capitalize()]['pairedWorldSavingsDifference']):
                if difference < -regression_tolerance:regressions.append({'block':i,'control':other,'world':world,'savingsDifference':difference})
    correct=all(r['arms']['child']['heldoutCorrect'] for r in rows)
    gains=all(c['interval'] is not None and c['interval']['low']>0 and c['efficiencyInterval']['low']>0 for c in contrasts.values())
    supported=len(rows)>=3 and balanced and correct and not regressions and gains
    return {'schemaVersion':1,'kind':'real-sandbox-frozen-research-series-v1','analyzerSha256':comparison.sha(__file__),
            'blocks':[str(p) for p in blocks],'blockCount':len(rows),'executionOrders':orders,'orderCoverageBalanced':balanced,
            'contrasts':contrasts,'allChildHeldoutCorrect':correct,'regressionTolerance':regression_tolerance,
            'regressions':regressions,'frozenMethodGainSupported':supported,'recursiveEvidenceSupported':False,
            'totalControllerSeconds':sum(r['arms'][a]['controllerWallSeconds'] for r in rows for a in comparison.ARMS),
            'failedAttempts':sum(r['arms'][a]['failedAttempts'] for r in rows for a in comparison.ARMS),
            'runs':rows,'limitations':['Supplied frozen programs: no authenticated self-modification, lineage or method-only reversion.',
              'Confidence units are full paired runs, not correlated worlds or repetitions inside one run.',
              'Fixed heldout worlds estimate repeated execution variability; this does not establish generalization to new task families.',
              'Order coverage is checked from recorded controller order; execution environment and external model state remain uncontrolled.',
              'Bootstrap bounds with few runs are exploratory; positive frozen-method contrasts are not RSI evidence.']}


def experiment(config, source, output, dotnet, blocks):
    if not 1<=blocks<=30:raise ValueError('blocks must be between 1 and 30')
    output=Path(output).resolve();output.mkdir(parents=True,exist_ok=False)
    comparison.save(output/'config.json',{'schemaVersion':1,'blocks':blocks,'comparison':config})
    paths=[];arms=list(comparison.ARMS)
    for i in range(blocks):
        block=output/f'block-{i:03d}';order=arms[i%3:]+arms[:i%3]
        comparison.experiment(dict(config,order=order),source,block,dotnet);paths.append(block)
        print(json.dumps({'block':i,'status':'completed','order':order}),flush=True)
    result=aggregate(paths);comparison.save(output/'series.json',result);return result


def main():
    parser=argparse.ArgumentParser(description=__doc__);sub=parser.add_subparsers(dest='command',required=True)
    run=sub.add_parser('run');run.add_argument('config',type=Path);run.add_argument('--output',type=Path,required=True)
    run.add_argument('--dotnet',required=True);run.add_argument('--blocks',type=int,default=3)
    analyze=sub.add_parser('analyze');analyze.add_argument('blocks',type=Path,nargs='+');analyze.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    try:
        if args.command=='run':result=experiment(comparison.read(args.config),args.config.resolve().parent,args.output,args.dotnet,args.blocks)
        else:result=aggregate(args.blocks);comparison.save(args.output,result)
        print(json.dumps({'output':str(args.output),'frozenMethodGainSupported':result['frozenMethodGainSupported'],'recursiveEvidenceSupported':False}));return 0
    except (ValueError,TypeError,KeyError,OSError) as e:parser.exit(2,json.dumps({'error':str(e)})+'\n')


if __name__=='__main__':main()
