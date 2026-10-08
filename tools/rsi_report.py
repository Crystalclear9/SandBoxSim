"""Verified, self-contained research report; no external scripts or private input payloads."""
import html
from pathlib import Path

SIGNALS = (
    ('heldoutCapabilityGain', '未见任务迁移'),
    ('codeInterventionEffect', '代码变化的作用'),
    ('gainAgainstFrozen', '超过冻结代理'),
    ('offspringYieldAdvantage', '产生更好后代'),
    ('improverMechanismEffect', '改进方法的作用'),
)


def report(folder, output):
    from rsi_benchmark import verify, read
    folder, output = Path(folder), Path(output)
    verified = verify(folder)
    result = read(folder/'result.json')
    records = [read(folder/f'stage-{i}.json') for i in range(len(result['stages']))]
    escape = lambda value: html.escape(str(value), quote=True)
    number = lambda value: f'{value:.3f}'
    sections = []
    for stage in records:
        analysis = stage['analysis']; index = stage['stage']
        from rsi_efficiency_study import stage_study
        efficiency = stage_study(stage,result['budget']['taskSeconds'])
        curve_rows = ''.join(f'<tr><td>{r["fraction"]:.0%} / {r["agentSeconds"]:.2f} s</td><td>{number(r["parentGain"])}</td><td>{number(r["childGain"])}</td><td>{number(r["gainEffect"]["stratified95"][0])} … {number(r["gainEffect"]["stratified95"][1])}</td></tr>' for r in efficiency['anytime']['curves'])
        rate_rows = ''.join(f'<tr><td>{label}</td><td>{number(efficiency["secondOrderEfficiency"][key]["meanEffect"])}</td><td>{number(efficiency["secondOrderEfficiency"][key]["hierarchical95"][0])} … {number(efficiency["secondOrderEfficiency"][key]["hierarchical95"][1])}</td></tr>' for key,label in (('parent','子改进器 − 父改进器'),('reverted','子改进器 − 撤销方法')))
        payback_rows = ''.join(f'<tr><td>{escape(r["family"])}</td><td>{r["recordedInvestmentSeconds"]:.3f} s</td><td>{r["optimisticBreakEvenExecutions"] if r["optimisticBreakEvenExecutions"] is not None else "无法确认"}</td><td>{escape(r["reason"] or "七组直接对照通过；仅为乐观回收估计")}</td></tr>' for r in efficiency['deployment']['tasks'])
        efficiency_html = f'''<h3>改进是否划算？</h3><p>成本敏感证据：{"通过" if efficiency["costSensitiveEvidenceSupported"] else "未通过"}。时间 AUC 效应 {number(efficiency['anytime']['timeAucEffect']['meanEffect'])}；父/子尚未达到收益阈值的任务 {efficiency['anytime']['parentCensored']} / {efficiency['anytime']['childCensored']}。阈值是收益 ≥ 0.15，未达到按预算终点计入，不能删除。</p>
<details><summary>实际时间预算下的能力与改进器效率</summary><table><tr><th>预算比例 / 代理墙钟</th><th>同记忆父代理</th><th>子代理</th><th>差异 95% 区间</th></tr>{curve_rows}</table><p class="muted">按原始轨迹截断，完成调用后结果才可用；没有从私有答案中择优。预算感知策略须另行运行，五个预算点仅作探索。</p><table><tr><th>后代增量收益 / 已记录秒</th><th>效应</th><th>任务与分叉重采样区间</th></tr>{rate_rows}</table><p class="muted">成本包含后代生成、搜索调用和记录的 solve 耗时，失败仍有成本且计零收益。缺少训练、导入和完整评估开销，这不是 token、费用或完整系统效率。</p></details>
<details><summary>部署多少次才能回收改进成本？</summary><p>{efficiency['deployment']['confirmedSavingTasks']} 个任务在七组直接父子对照中确认 CPU 与墙钟均改善。</p><table><tr><th>任务族</th><th>已记录投入</th><th>乐观回收次数</th><th>原因</th></tr>{payback_rows}</table><p class="muted">用七组最小观察节省量、子搜索成本及分摊的谱系生成成本估计；遗漏训练与评估开销，回收次数偏乐观。含分配追踪的微基准不等同于真实部署耗时。</p></details>'''
        signals = []
        for key, label in SIGNALS:
            value = analysis[key]; interval = value['stratified95']
            signals.append(f'<tr><td>{label}</td><td>{number(value["meanEffect"])}</td>'
                           f'<td>{number(interval[0])} … {number(interval[1])}</td>'
                           f'<td class="{"pass" if value["supported"] else "fail"}">{"支持" if value["supported"] else "未支持"}</td></tr>')
        reasons = [label for key,label in SIGNALS if not analysis[key]['supported']]
        if not analysis['improverMechanismChanged']: reasons.append('improve 方法 AST 没有改变')
        retention = analysis.get('retention'); retention_html = '<p class="muted">首阶段没有旧课程；静态套件不启用课程保留门槛。</p>'
        if retention:
            rows = ''.join(f'<tr><td>{escape(family)}</td><td>{number(v["meanEffect"])}</td>'
                           f'<td>{number(v["stratified95"][0])} … {number(v["stratified95"][1])}</td></tr>'
                           for family,v in retention['byFamily'].items())
            retention_html = f'<p>{retention["tasks"]} 个新私有实例，按旧课程重新采样。每个任务族的区间下界必须 ≥ −{number(retention["regressionTolerance"])}。</p><p>正确任务：父代理 {retention["parentCorrectTasks"]}，子代理 {retention["childCorrectTasks"]}；正确性退步 {len(retention["correctnessRegressions"])} 项。原本正确的任务不得变错，即使两者速度收益都为零。</p><table><tr><th>任务族</th><th>能力变化</th><th>95% 区间</th></tr>{rows}</table>'
            if not retention['nonRegressionPassed']: reasons.append('旧课程出现任务族回归')
        arms = ''.join(f'<tr><td>{escape(name)}</td><td>{number(v["capabilityAuc"])}</td>'
                       f'<td>{v["successRate"]:.1%}</td><td>{v["agentWallSeconds"]:.2f} s</td>'
                       f'<td>{v["inputBytes"]:,} / {v["outputBytes"]:,}</td></tr>'
                       for name,v in analysis['arms'].items())
        forks = ''.join(f'<tr><td>{escape(name)}</td><td>{v["accepted"]} / {v["attempts"]}</td>'
                        f'<td>{v["wallSeconds"]:.2f} s</td></tr>' for name,v in analysis['successorGeneration'].items())
        failures = []
        for fork in stage['forks']:
            for name in ('parentFork','childFork','mechanismFork'):
                edge = fork[name]
                if not edge['accepted']: failures.append(f'{name} / call {edge["call"]}: {edge["failure"]}')
        # Public development diagnostics only; no answers, seeds, or private workload payloads.
        for name,probes in stage['arms'].items():
            for probe in probes:
                for feedback in probe.get('developmentFeedback',[]):
                    if feedback.get('error'): failures.append(f'{name} / {probe["family"]}: {feedback["error"]}')
        failure_html = '<ul>'+''.join(f'<li>{escape(f)}</li>' for f in failures[:30])+'</ul>' if failures else '<p>本阶段没有记录生成或开发执行错误。</p>'
        sections.append(f'''<section id="stage-{index}">
<div class="section-title"><h2>阶段 {index+1}</h2><span class="badge">{"递归证据支持" if analysis['recursiveEvidenceSupported'] else "递归证据未支持"}</span></div>
<p class="diagnosis">{escape('需要进一步证据：'+'；'.join(reasons) if reasons else '各项操作性证据门槛通过；仍须考虑可信执行、模型身份和夹具边界。')}</p>
<div class="columns"><div><h3>因果证据链</h3><table><tr><th>证据</th><th>效应</th><th>95% 区间</th><th>判定</th></tr>{''.join(signals)}</table><p class="muted">改进方法 AST 改变：{'是' if analysis['improverMechanismChanged'] else '否'}。区间与筛选线来自原实验。</p></div>
<div><h3>旧能力保留</h3>{retention_html}</div></div>
{efficiency_html}
<details><summary>查看各控制组能力与代理成本</summary><table><tr><th>控制组</th><th>搜索 AUC</th><th>成功率</th><th>代理墙钟</th><th>输入 / 输出字节</th></tr>{arms}</table><p class="muted">字节数不是 token；代理墙钟不包含候选测量的全部成本。</p></details>
<details><summary>后代接受率与失败记录</summary><table><tr><th>生成组</th><th>接受 / 尝试</th><th>墙钟</th></tr>{forks}</table>{failure_html}</details></section>''')
    height = 220
    plot = ['<svg viewBox="0 0 720 250" role="img" aria-label="父代理、子代理和冻结代理的搜索能力曲线">']
    for tick in (0,.25,.5,.75,1):
        y=height-170*tick
        plot.append(f'<line x1="50" y1="{y}" x2="690" y2="{y}" stroke="#cbd5df"/><text x="12" y="{y+4}">{tick:g}</text>')
    for name,color,label in (('parent','#697b91','父代理'),('child','#227c73','子代理'),('frozen','#b48752','冻结代理')):
        points=[]
        for i,stage in enumerate(records):
            x=80+i*570/max(1,len(records)-1);y=height-170*stage['analysis']['arms'][name]['capabilityAuc'];points.append(f'{x},{y}')
            plot.append(f'<circle cx="{x}" cy="{y}" r="4" fill="{color}"/>')
        plot.append(f'<polyline points="{" ".join(points)}" fill="none" stroke="{color}" stroke-width="2"/>')
        plot.append(f'<text x="{50+200*(0 if name=="parent" else 1 if name=="child" else 2)}" y="22" fill="{color}">{label}</text>')
    for i in range(len(records)):
        plot.append(f'<text x="{70+i*570/max(1,len(records)-1)}" y="244">阶段 {i+1}</text>')
    plot.append('</svg>')
    fixture = result.get('fixtureOnly',False)
    warning = '这是非 LLM 集成夹具，不能作为真实模型 RSI 结果。' if fixture else '这是本地可信执行实验；模型身份、token 和防作弊未独立认证。'
    document = '''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>SandBoxSim · 代理改进实验</title><style>
*{box-sizing:border-box}body{margin:0;background:#edf1f4;color:#243447;font:15px/1.7 system-ui,sans-serif}main{max-width:1160px;margin:auto;padding:40px 24px}h1{font-size:32px;line-height:1.2}h2{margin:0;font-size:22px}h3{font-size:17px}header{padding:20px 0 30px}.eyebrow{font-size:12px;letter-spacing:.12em;color:#52677d}.muted{color:#63768a;font-size:13px}.warning{border-left:4px solid #b48752;background:#fff8e9;padding:14px 20px}.cards{display:flex;flex-wrap:wrap;gap:12px;margin:24px 0}.card{flex:1;min-width:160px;background:white;padding:18px;border:1px solid #dbe3eb}.card strong{display:block;font-size:26px}.card span{color:#63768a;font-size:13px}section{background:white;border:1px solid #dbe3eb;margin:24px 0;padding:28px}.section-title{display:flex;align-items:center;justify-content:space-between;gap:12px}.badge{padding:3px 12px;background:#e9eef3;border-radius:20px;font-size:12px}.columns{display:grid;grid-template-columns:1.1fr 1fr;gap:32px}table{width:100%;border-collapse:collapse;font-size:13px}td,th{text-align:left;border-bottom:1px solid #e3e9ef;padding:10px 8px}th{font-weight:600;color:#52677d}.pass{color:#227c73}.fail{color:#a66b35}.diagnosis{color:#52677d}details{margin-top:16px;border-top:1px solid #e3e9ef;padding-top:12px}summary{cursor:pointer;font-weight:600}svg{display:block;width:100%;max-height:260px}svg text{font:12px system-ui,sans-serif}nav a{color:#227c73;margin-right:16px}li{overflow-wrap:anywhere}@media(max-width:760px){.columns{grid-template-columns:1fr}main{padding:20px 12px}section{padding:18px}table{font-size:11px}.section-title{align-items:flex-start;flex-direction:column}}
</style><main><header><div class="eyebrow">SANDBOXSIM / IMPROVER LAB</div><h1>代理是否学会了更好地改进？</h1>'''
    document += f'<p>{escape(result.get("taskSuite","algorithm-v1"))} · {escape(result["backend"])} · 协议 v2 · 回放验证通过</p></header><p class="warning">{escape(warning)} 哈希与本地回放不是安全隔离或通用 RSI 认证。</p>'
    document += f'<div class="cards"><div class="card"><strong>{len(records)}</strong><span>完整阶段</span></div><div class="card"><strong>{result["supportedRecursiveStages"]}</strong><span>操作性证据支持阶段</span></div><div class="card"><strong>{result["agentCalls"]}</strong><span>实际代理调用</span></div><div class="card"><strong>{result["failedForks"]}</strong><span>计入零分的失败分叉</span></div></div>'
    document += '<nav>'+''.join(f'<a href="#stage-{i}">阶段 {i+1}</a>' for i in range(len(records)))+'</nav><section><h2>固定预算内的未见任务能力</h2><p class="muted">搜索 AUC 范围 0–1。不同阶段使用新的任务和课程，曲线只展示本次实验，不等同于同一任务的速度比较。</p>'+''.join(plot)+'</section>'+''.join(sections)
    document += '<footer class="muted">报告先验证归档源码、轨迹与控制证据，再绘制原始分析；不重测硬件耗时，不输出留出输入或参考答案。报告包含留出汇总，请勿在实验结束前将它反馈给受测代理。</footer></main></html>'
    output.parent.mkdir(parents=True,exist_ok=True)
    with output.open('x',encoding='utf-8') as handle:handle.write(document)
    return {'report':str(output),'protocolValidated':verified['protocolValidated'],'fixtureOnly':fixture,'supportedRecursiveStages':result['supportedRecursiveStages']}
