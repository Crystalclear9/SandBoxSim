"""Render verified real-world research history, source differences and world observations."""
import argparse
import difflib
import html
import json
from pathlib import Path
from sandbox_research import verify, read


def report(folder, output):
    folder=Path(folder);verify(folder);result=read(folder/'result.json');sections=[]
    esc=lambda value:html.escape(str(value),quote=True)
    profile=read(folder/'development-profile/profile.json')
    profile_rows=''.join(f'<tr><td>开发世界 {r["case"]+1}</td><td>{r["completeEpisodeMs"]:.2f}</td><td>{r["simulationStepMs"]:.2f}</td><td>{r["pathQueryMs"]:.2f}</td><td>{r["setupAndReleaseMs"]:.2f}</td></tr>' for r in profile['cases'])
    for record in result['attempts']:
        i=record['attempt'];summary=record.get('summary');differences=[]
        if not record['call']['error']:
            reply=read(folder/'calls'/str(record['call']['call'])/'stdout.txt')
            for edit in reply.get('edits',[]):
                before=(folder/'reference'/edit['path']).read_text(encoding='utf-8').splitlines(keepends=True)
                difference=''.join(difflib.unified_diff(before,edit['content'].splitlines(keepends=True),fromfile='reference/'+edit['path'],tofile='candidate/'+edit['path']))
                differences.append(f'<details><summary>{esc(edit["path"])}</summary><pre>{esc(difference)}</pre></details>')
        outcome=f'开发集：{summary["medianSpeedup"]:.3f}×，区间 [{summary["bootstrap95"][0]:.3f}, {summary["bootstrap95"][1]:.3f}]' if summary else '未获得有效开发测量'
        failure=f'<details><summary>失败诊断（已反馈给下一次调用）</summary><pre>{esc(record["error"])}</pre></details>' if record.get('error') else ''
        label={'rejected':'候选被拒绝','development-correct':'开发世界语义通过','development-incorrect':'开发世界语义不一致'}.get(record['status'],record['status'])
        sections.append(f'<section><div class="eyebrow">EXPERIMENT {i+1:02d}</div><h2>{esc(label)}</h2><p>{esc(record.get("hypothesis","无有效假设"))}</p><p>{esc(outcome)} · 全流程 {record["wallSeconds"]:.2f} 秒</p>{failure}{"".join(differences)}</section>')
    worlds=[];private_summary='没有可测试的候选'
    if result['selectedAttempt'] is not None:
        held=read(folder/'heldout/report.json');summary=held['summary']
        if summary:private_summary=f'{summary["medianSpeedup"]:.3f}×，95% 区间 [{summary["bootstrap95"][0]:.3f}, {summary["bootstrap95"][1]:.3f}]'
        for case in range(len(held['suite']['cases'])):
            samples=[s for s in held['samples'] if s['case']==case]
            if not samples:continue
            sample=samples[0];obs=sample['candidate']['finalObservation']
            worlds.append(f'<tr><td>世界 {case+1}</td><td>{obs["tick"]}</td><td>{obs["population"]}</td><td>{obs["buildings"]}</td><td>{obs["food"]:.2f} / {obs["wood"]:.2f} / {obs["stone"]:.2f}</td><td>{len(samples)}</td><td>{"一致" if sample["reference"]["digests"]==sample["candidate"]["digests"] else "不一致"}</td></tr>')
    document=f'''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>真实沙盒研究记录</title><style>
*{{box-sizing:border-box}}body{{margin:0;background:#edf1f4;color:#243447;font:15px/1.7 system-ui,sans-serif}}main{{max-width:1120px;margin:auto;padding:40px 24px}}h1{{font-size:32px}}h2{{font-size:21px}}section{{padding:26px;background:white;border:1px solid #dbe3eb;margin:20px 0}}.eyebrow{{font-size:12px;letter-spacing:.12em;color:#52677d}}.muted{{color:#63768a}}.cards{{display:flex;gap:20px;flex-wrap:wrap}}.cards div{{flex:1;min-width:160px}}strong{{display:block;font-size:26px}}table{{width:100%;border-collapse:collapse;font-size:13px}}td,th{{padding:10px;border-bottom:1px solid #dbe3eb;text-align:left}}details{{margin-top:15px}}summary{{cursor:pointer;font-weight:600}}pre{{white-space:pre-wrap;overflow-wrap:anywhere;padding:16px;background:#f3f6f8;font:12px/1.6 monospace}}@media(max-width:720px){{main{{padding:20px 12px}}section{{padding:16px}}.worlds{{overflow-x:auto}}}}
</style><main><header><div class="eyebrow">SANDBOXSIM / REAL CODE RESEARCH</div><h1>从研究假设到真实世界验证</h1><p>源码提案 → 编译 → 语义检查 → 开发世界 → 候选选择 → 私有世界</p><p class="muted">{'公开脚本夹具，没有模型调用。' if result['fixtureOnly'] else '可信本地代理，模型身份未独立认证。'} 此记录验证真实代码研究流程，不能独立证明递归改进能力。</p></header>
<section class="cards"><div><strong>{result['agentCalls']}</strong>实际研究调用</div><div><strong>{sum(r['status']=='rejected' for r in result['attempts'])}</strong>保留的拒绝候选</div><div><strong>{result['wallSeconds']:.2f} s</strong>含构建、测试与服务的全程耗时</div></section>
<section><h2>先调查成本集中在哪里</h2><table><tr><th>样本</th><th>完整 episode ms</th><th>模拟步进 ms</th><th>寻路 ms</th><th>初始化 / 释放 ms</th></tr>{profile_rows}</table><p class="muted">每个公开开发世界一次冷测，已给代理作调查线索；不足以支持稳定的性能结论。</p></section>
{''.join(sections)}<section><h2>独立留出世界</h2><p>被测候选：{result['selectedAttempt']}（从 0 编号，仅按开发结果选择）。{esc(private_summary)}。</p><p>世界语义验证：{'通过' if result['heldoutCorrect'] else '未通过'}；可确认的程序加速：{'是' if result['heldoutImprovementDetected'] else '否'}。</p><div class="worlds"><table><tr><th>样本</th><th>最终 tick</th><th>居民</th><th>建筑</th><th>食物 / 木 / 石</th><th>重复配对</th><th>完整摘要轨迹</th></tr>{''.join(worlds)}</table></div><p class="muted">表格是每个世界第一组重复的观察。判定检查全部重复、冷启动、地图和独立最短路径；这些短场景不能保证所有长期世界行为。留出结果未反馈给代理。</p></section><footer class="muted">报告生成前重新核对源码、候选程序集、调用、编译日志与配对报告。没有覆盖主工作区、改变 Git 分支或自动采用候选。</footer></main></html>'''
    output=Path(output);output.parent.mkdir(parents=True,exist_ok=True)
    with output.open('x',encoding='utf-8') as handle:handle.write(document)
    return {'report':str(output),'protocolValidated':True}


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('folder',type=Path);parser.add_argument('--output',type=Path,required=True);args=parser.parse_args()
    try:print(json.dumps(report(args.folder,args.output)))
    except (ValueError,KeyError,TypeError,OSError) as e:parser.exit(2,json.dumps({'error':str(e)})+'\n')
