# 在线控制与效率实验

SandBoxSim 的在线入口直接驱动确定性模拟内核。世界没有后台时钟，调用者决定每次推进多少 tick、怎样改变环境以及何时观察。它与正常游戏分开启动，不给玩家添加目标或任务。服务不需要 Godot 或图形设备，适合外部模型、优化代理和 CI。

## 启动与控制

环境为 .NET 8、Python 3.10+；构建仍支持 SDK 与 Roslyn 降级通道。

```powershell
./tools/build.ps1 -Mode build -Configuration Release -Channel sdk
$env:SANDBOXSIM_API_TOKEN = [guid]::NewGuid().ToString('N')
python tools/online.py --port 8765
```

Linux/macOS 使用 `export SANDBOXSIM_API_TOKEN=...` 设置至少 16 字符的随机令牌。第二个终端使用同一令牌。直接启动也可执行 `dotnet artifacts/Release/SandBoxSim.Console.dll --serve --port 8765`。令牌只放环境变量和 Authorization 请求头，不写入评测产物。服务仅绑定 `127.0.0.1`；外部模型的控制器在本机调用，无需把模型本身部署在本机。没有公网绑定、CORS、任意代码执行或文件读写接口。

Python 调用示例（在仓库根目录执行）：

```python
import sys
sys.path.insert(0, 'tools')
from online import Client

client = Client('http://127.0.0.1:8765')
created = client.request('POST', '/v1/sessions',
    {'seed': 17, 'width': 48, 'height': 48, 'agents': 24})
path = '/v1/sessions/' + created['sessionId']
observation = created['observation']
result = client.request('POST', path + '/step', {
    'requestId': 'turn-1', 'expectedRevision': observation['revision'], 'ticks': 60,
    'actions': [{'kind': 'resource', 'x': 24, 'y': 24,
                 'resource': 'Food', 'amount': 30}]
})
print(result['observation'], result['metrics'])
print(client.calls, client.wall_seconds, client.response_bytes)
client.request('DELETE', path)
```

HTTP 协议见 [OpenAPI](../../benchmarks/online.openapi.json)，能力清单见 [interface.json](../../benchmarks/interface.json)。

| 路径 | 方法 | 内容 |
|---|---|---|
| `/v1/health`, `/v1/capabilities` | GET | 协议、程序集/Core 哈希、机器/运行时、限额 |
| `/v1/sessions` | POST | 按种子创建世界；返回 sessionId 与观察 |
| `/v1/sessions/{id}` | GET / DELETE | 观察 / 释放世界 |
| `/v1/sessions/{id}/step` | POST | 先执行环境动作，再推进精确 tick 数 |
| `/v1/sessions/{id}/reset` | POST | 重建同一会话，状态版本继续递增 |
| `/v1/sessions/{id}/map` | GET | 行优先遍历网格、通行性、地形代价与高度代理 |
| `/v1/sessions/{id}/path` | POST | 查询路径、代价与展开节点数，不推进世界 |

`step`/`reset` 必须传 `requestId` 和 `expectedRevision`。版本不匹配返回 409，防止旧观察或并发操作推进错误世界。同一 requestId 和相同 JSON 请求返回缓存的原结果，不重复执行；字段顺序或内容改变会拒绝。保留最近 64 个回复；缓存淘汰后，旧版本请求仍不能再次执行。`path` 也核对版本。

动作支持 `terrain`（枚举名称）、`resource`（Food/Wood/Stone/Iron，amount 0–10000）、`fertility`（radius 0–8，delta −1–1）。每次最多 64 个动作、0–1000 tick；每个 episode 最多一百万 tick。地图 16–128 格、初始人口 0–200、会话最多 8 个，请求体最大 32 KiB。未知/重复字段、越界和非法枚举拒绝；全部动作先验证，输入错误不会造成部分执行。意外内核错误可能发生在执行中，此时会话标为 faulted，必须重置，不能假装事务已回滚。

观察包含版本、tick、摘要、人口、资源、建筑和累计 step 调用/动作/tick 数。读取不改变模拟摘要。Python 客户端另外记录所有调用次数、端到端耗时与响应字节数，包括失败调用的次数与耗时。`step.metrics` 是执行与不变量检查区段的墙钟时间及当前线程托管分配；不是 GPU 时间、峰值内存或模型推理成本。HTTP 当前返回状态，不返回 3D 实时画面；视觉评测继续使用 [渲染接口](benchmark.md)。

## 代码效率 benchmark

效率实验先要求结果正确，再测同一工作量的成本。启动两份分别编译的服务，参考版本使用端口 8765，候选版本使用端口 8766；两个进程设置同一令牌。把完整 Core/Console 程序集和依赖放在各自目录，不能让旧 Console 意外加载新 Core。

```powershell
python tools/online.py --assembly reference/SandBoxSim.Console.dll --port 8765
# 另一个终端
python tools/online.py --assembly candidate/SandBoxSim.Console.dll --port 8766
# 控制器终端
python tools/efficiency.py run --reference http://127.0.0.1:8765 --candidate http://127.0.0.1:8766 --suite benchmarks/efficiency-smoke.json --repeats 3 --output runs/paired.json
```

输出文件必须不存在。公开的 [冒烟套件](../../benchmarks/efficiency-smoke.json) 用于开发验证。每个场景先完整预热，再以交替顺序重复参考/候选实验。协议、机器、OS、架构、运行时与处理器数量不同会拒绝比较。主要指标是客户端单调时钟测量的寻路和 step 请求总耗时，包含传输、服务观察与序列化；创建世界、取地图、独立判题和释放会话不计入这个指标。报告同时保留单次耗时、调用/字节/tick 数、服务分配量遥测、程序集哈希和评测器哈希。

每组比较要求初始网格、查询和每个模拟批次的状态摘要一致。可信 Python 评测器独立运行 Dijkstra，核对可达性、每条路径边、禁止穿角、实际代价与最短代价。参考版本本身失败也会拒绝；不会把错误参考当真值。寻路服务仍使用现有 A* 搜索预算，超出预算的失败可能被判题器识别为不正确。改变居民行为、少跑 tick 或漏算任务都不能取得效率分数。

完整正确的报告才输出配对加速比中位数和 2000 次 bootstrap 的 95% 区间；至少 9 组且区间下界高于 1.05 才标记 `improvementDetected`。它是实验筛选线，不是严格的统计证明；同机负载、热状态、JIT 和短任务噪声仍会影响结果。正式实验增加重复数和批次工作量，并检查各场景，而不是只看合并分数。托管分配量来自候选服务自报，单独展示，不能作为独立防作弊证据。

## 多轮自我改进证据

实现提供的是**RSI efficiency 实验与证据验证器**，用于检查优化代理修改自身后，能否在固定预算下生成更高效且正确的候选代码。一次游戏运行更快不代表代理自我改进成功。设计沿用 [ECCO](https://aclanthology.org/2024.emnlp-main.859/) 的正确性/效率分开检查，以及 [DGM](https://arxiv.org/abs/2505.22954) 的版本修改与实证评测思想；这不意味着已复现其研究结论。

在可信评测机生成留出套件：

```powershell
python tools/efficiency.py generate-suite --cases 5 --output runs/private-suite.json
```

套件使用系统随机源生成种子，标为 `heldout`。优化代理只看训练/开发套件；留出套件、原始参考答案和评分器由实验操作者保管。不要把每轮留出反馈反复交给代理后仍称其为未见测试。

每一轮保存：代理实现或配置文件（agentArtifact）、父代理 SHA-256、代码补丁、配对评测报告，以及 token/墙钟时间/尝试次数。多文件代理可封装为固定归档后哈希。固定不修改的代理在相同预算下单独生成控制候选、进行配对评测；控制代理归档和控制候选版本固定，以检查噪声或原模型本身就能做到的收益。自动模型调用由外部控制器完成，服务不执行模型输出代码。

多轮驱动器可以一次运行各轮候选和控制服务，归档代理、补丁、套件和全部报告，再自动验证父链与预算：

```powershell
python tools/efficiency.py experiment experiment.json --output runs/experiment
```

`experiment.json` 示例（文件路径相对配置所在目录，三个服务使用同一令牌）：

```json
{
  "schemaVersion": 1,
  "suite": "private-suite.json",
  "repeats": 5,
  "reference": "http://127.0.0.1:8765",
  "control": "http://127.0.0.1:8766",
  "controlAgentArtifact": "fixed-agent.zip",
  "budget": {"tokens": 100000, "wallSeconds": 600, "attempts": 5},
  "rounds": [
    {"candidate": "http://127.0.0.1:8767", "agentArtifact": "agent-v0.zip", "patch": "round0.diff",
     "cost": {"tokens": 40000, "wallSeconds": 180, "attempts": 2},
     "controlCost": {"tokens": 40000, "wallSeconds": 180, "attempts": 2}},
    {"candidate": "http://127.0.0.1:8768", "agentArtifact": "agent-v1.zip", "patch": "round1.diff",
     "cost": {"tokens": 50000, "wallSeconds": 200, "attempts": 3},
     "controlCost": {"tokens": 40000, "wallSeconds": 180, "attempts": 2}}
  ]
}
```

这些成本需由真实调用日志填写，不能照抄示例数字。驱动器要求新的输出目录，保存独立控制测量；失败时保存已有产物和 `failure.json`。它评测已经由外部代理生成、编译和启动的候选，不把任意模型输出直接当程序执行。

清单格式见 [rsi-rounds.schema.json](../../benchmarks/rsi-rounds.schema.json)。至少两轮，每轮独立报告，`parentAgentSha256` 指向上一轮代理；首轮为 null。参考二进制/环境、留出套件、评测器和客户端哈希固定。运行：

```powershell
python tools/efficiency.py rounds runs/experiment/rounds.json --output runs/experiment/evidence.json
```

验证器重新计算报告摘要，检查原始样本是否齐全、结果是否一致、文件哈希/父链、固定控制及双方预算。每轮收益需通过配对检测，并比控制加速比再高 5%，才计入 `improvingRounds`。报告同时保留各轮成本，便于比较收益/token、收益/时间；不将不同计价和不同模型混成一个万能分数。

`validated: true` 的含义是上述证据约束通过，**不代表已经证明通用 RSI 能力**。模型身份、真实 token 与控制器耗时当前是操作者记录，不是服务独立计量；本地哈希也不是签名或安全隔离。候选不应修改可信评分器或访问留出数据。需要防作弊的研究应在独立评测进程/机器保管这些文件，保留全部失败尝试，核对候选构建与代理调用日志。

## 验证与维护

```powershell
python -m unittest discover -s tools -p test_efficiency.py
dotnet artifacts/Release/SandBoxSim.Tests.dll --filter OnlineApiTests
python tools/test_online_service.py --assembly artifacts/Release/SandBoxSim.Console.dll --output runs/online-validation
```

最后一条启动两份真实 HTTP 服务，检查鉴权、并发重试、状态版本、9 组配对正确性及两轮证据流程。其代理/补丁是显式标记的测试夹具，正常结果为零个改进轮次，不冒充真实模型实验。三平台 CI 执行同一流程并保留报告；不对不同 CI 机器设置速度门槛。协议变更需同步 OpenAPI、能力清单、评测器和测试。
