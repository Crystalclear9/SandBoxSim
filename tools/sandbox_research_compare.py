"""Frozen parent/child/method-reverted research on the real C# sandbox. Descriptive, not RSI proof."""
from __future__ import annotations
import argparse
import json
import math
import statistics
from pathlib import Path
from sandbox_research import run, verify, read, save, sha

ARMS=('parent','child','reverted')
CONDITIONS=('editableFiles','attempts','agentSeconds','commandSeconds','repeats','testFilters',
            'developmentSuite','heldoutSuite','envAllowlist')
DEFAULTS={'attempts':2,'agentSeconds':30,'commandSeconds':120,'repeats':3,
          'testFilters':['AgentTests','ResponsivenessTests'],'envAllowlist':[]}


def cost_ledger(folder,result):
    def duration(path):
        value=read(path)['wallSeconds']
        if isinstance(value,bool) or not isinstance(value,(int,float)) or not math.isfinite(value) or value<0:
            raise ValueError('Invalid recorded cost')
        return value
    calls=list((folder/'calls').glob('*/execution.json'))
    commands=list((folder/'reference-logs').glob('*/execution.json'))+list(folder.glob('attempt-*/logs/*/execution.json'))
    pairs=list(folder.glob('attempt-*/cost.json'))+list(folder.glob('heldout/cost.json'))
    costs={'agentSeconds':sum(duration(p) for p in calls),'buildAndTestSeconds':sum(duration(p) for p in commands),
           'pairedWorldSeconds':sum(duration(p) for p in pairs)}
    accounted=sum(costs.values());total=result['wallSeconds']
    if total+.01<accounted:raise ValueError('Controller cost is less than serial recorded subprocess costs')
    costs['otherControllerSeconds']=max(0,total-accounted)
    costs['scope']='Serial agent/build/test/pair costs; remainder includes source copying, baseline profiling and orchestration'
    return costs


def world_gains(folder,result):
    if result['selectedAttempt'] is None or not result['heldoutCorrect']:
        return [0.0]*len(read(folder/'config.json')['heldoutSuite']['cases'])
    report=read(folder/'heldout/report.json');gains=[]
    for i in range(len(report['suite']['cases'])):
        samples=[s for s in report['samples'] if s['case']==i]
        if not samples:raise ValueError('Missing heldout world')
        ratios=[s['reference']['wallMs']/s['candidate']['wallMs'] for s in samples]
        speed=statistics.median(ratios)
        gains.append(1-1/speed)
    return gains


def analyze(folders):
    folders={k:Path(folders[k]).resolve() for k in ARMS}
    if len(set(folders.values()))!=3:raise ValueError('Use three distinct complete research archives')
    for folder in folders.values():verify(folder)
    configs={k:read(p/'config.json') for k,p in folders.items()}
    baseline=read(folders['parent']/'source-manifest.json')
    for arm in ARMS[1:]:
        if read(folders[arm]/'source-manifest.json')!=baseline:raise ValueError('Reference source differs between arms')
        for key in CONDITIONS:
            if configs[arm].get(key,DEFAULTS.get(key))!=configs['parent'].get(key,DEFAULTS.get(key)):
                raise ValueError('Research conditions differ: '+key)
    results={k:read(p/'result.json') for k,p in folders.items()};rows={}
    for arm,folder in folders.items():
        result=results[arm];seconds=result['wallSeconds']
        if isinstance(seconds,bool) or not isinstance(seconds,(int,float)) or not math.isfinite(seconds) or seconds<=0:
            raise ValueError('Invalid full research cost')
        gains=world_gains(folder,result)
        rows[arm]={'archive':str(folder),'resultSha256':sha(folder/'result.json'),'agentSha256':result['agentSha256'],
                   'fixtureOnly':result['fixtureOnly'],'heldoutCorrect':result['heldoutCorrect'],
                   'heldoutImprovementDetected':result['heldoutImprovementDetected'],'worldTimeSavings':gains,
                   'meanWorldTimeSavings':statistics.mean(gains),'controllerWallSeconds':seconds,
                   'savingsPerResearchSecond':statistics.mean(gains)/seconds,
                   'costLedger':cost_ledger(folder,result),
                   'failedAttempts':sum(r['status']!='development-correct' for r in result['attempts']),
                   'attempts':len(result['attempts'])}
    def contrast(other):
        differences=[a-b for a,b in zip(rows['child']['worldTimeSavings'],rows[other]['worldTimeSavings'])]
        return {'pairedWorldSavingsDifference':differences,'meanDifference':statistics.mean(differences),
                'researchEfficiencyDifference':rows['child']['savingsPerResearchSecond']-rows[other]['savingsPerResearchSecond']}
    return {'schemaVersion':1,'kind':'real-sandbox-frozen-research-comparison-v1','analyzerSha256':sha(__file__),
            'arms':rows,'childMinusParent':contrast('parent'),'childMinusReverted':contrast('reverted'),
            'recursiveEvidenceSupported':False,
            'scope':'Frozen supplied research programs on identical C# sources and world suites; descriptive paired worlds only',
            'limitations':['No self-modification, lineage or method-only reversion is authenticated by this comparison.',
                           'Agent profiles are fresh noisy measurements, not identical observations; external model memory is uncontrolled.',
                           'Configured call limits and attempts match; no enforced equal end-to-end wall/compute/token budget.',
                           'Costs include heldout verification; efficiency is retrospective, not deployment cost or online search throughput.',
                           'Single execution per arm, serial machine order and shared world seeds do not establish statistical RSI evidence.']}


def experiment(config,source,output,dotnet):
    if config.get('schemaVersion')!=1:raise ValueError('Unsupported comparison configuration')
    if set(config['agents'])!=set(ARMS):raise ValueError('Supply parent, child and reverted research programs')
    order=config.get('order',list(ARMS))
    if not isinstance(order,list) or len(order)!=3 or set(order)!=set(ARMS):raise ValueError('Order must contain each arm once')
    output=Path(output).resolve();output.mkdir(parents=True,exist_ok=False);save(output/'config.json',config)
    shared=config['research'];folders={arm:output/arm for arm in ARMS}
    for arm in order:
        conditions=dict(shared,agent=config['agents'][arm])
        run(conditions,source,folders[arm],dotnet)
    result=analyze(folders);result['executionOrder']=order;save(output/'comparison.json',result);return result


def main():
    parser=argparse.ArgumentParser(description=__doc__);sub=parser.add_subparsers(dest='command',required=True)
    compare=sub.add_parser('compare')
    for arm in ARMS:compare.add_argument('--'+arm,type=Path,required=True)
    compare.add_argument('--output',type=Path,required=True)
    execute=sub.add_parser('run');execute.add_argument('config',type=Path);execute.add_argument('--output',type=Path,required=True)
    execute.add_argument('--dotnet',required=True)
    args=parser.parse_args()
    try:
        if args.command=='run':result=experiment(read(args.config),args.config.resolve().parent,args.output,args.dotnet)
        else:
            result=analyze({arm:getattr(args,arm) for arm in ARMS});args.output.parent.mkdir(parents=True,exist_ok=True);save(args.output,result)
        print(json.dumps({'output':str(args.output),'recursiveEvidenceSupported':False}));return 0
    except (ValueError,KeyError,TypeError,OSError) as e:parser.exit(2,json.dumps({'error':str(e)})+'\n')

if __name__=='__main__':main()
