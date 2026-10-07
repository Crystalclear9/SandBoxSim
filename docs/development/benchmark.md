# 评测接口与实验运行

SandBoxSim 同时提供自由沙盒客户端和显式启用的本地评测入口。评测模式固定世界种子、人口、预演天数、模拟步数和表现层时钟，输出机器可读记录；正常游戏没有评测任务、强制目标或分数面板。

当前协议为 `schemaVersion: 1`。它支持渲染性能、模型图像识别和界面语义回放，可用于代码优化对照、视觉模型输入样本与交互回归。它是研究实验的基础工具，不是已验证的 RSI 能力基准，也不衡量模型美感或真实感。

## 启动与产物

环境是 Python 3.10+、.NET 8、Godot 4.7.2 .NET。先编译客户端，再运行：

```powershell
./tools/godot.ps1 -Mode build
python tools/evaluate.py run benchmarks/scenarios/visual-characters.json --label baseline
python tools/evaluate.py run benchmarks/scenarios/ui-portrait.json --label ui-review
python tools/evaluate.py run benchmarks/scenarios/render-population.json --label render-review
```

通过 `--engine` 或 `GODOT_EXE` 指定引擎；运行器也会查找 PATH 与安装脚本的本机目录。`--output` 指定独立输出目录，已有目录会被拒绝，保留以前的样本。默认窗口请求为 `1440x900`、关闭 VSync、`--max-fps 0`，实际物理/逻辑尺寸与 VSync 状态写入结果；驱动可能仍限制帧率。`--max-fps 60` 可用于有上限的交互采样，但必须保持对照一致。默认子进程超时为 180 秒，超时终止本次启动的进程树，`--timeout` 可调整。

| 文件 | 用途 |
|---|---|
| `request.json` | 引擎实际消费的规范化配置 |
| `result.json` | 墙钟耗时、帧耗时分位数、环境、最终状态与回放记录 |
| `frame-*.png` | 指定逻辑帧的完整画面，附 SHA-256 |
| `f*-r*.png` | 图册模型区域的图像，按实际像素缩放裁切，不含下方标题 |
| `questions.json` | 视觉模型输入：问题 ID、图像、校验值与候选标签 |
| `answer-key.json` | 可信评测器使用的标签与完整截图区域，不能给被测模型 |
| `engine.log` | 原生引擎输出 |
| `provenance.json` | Git 提交/脏状态、源码与已加载程序集哈希、运行器与引擎哈希、运行参数、退出码、产物哈希 |

输出位于忽略提交的 `runs/evaluation/`。配置、入口、评分器和说明保存在仓库；正式配图仍在 `docs/images/`。引擎可直接使用 `-- --evaluation=/absolute/request.json`，但直接运行不会生成 Python 运行器的溯源文件。图像采样需要原生图形渲染器，不能使用 headless dummy renderer。

## 场景协议

机器可读字段描述见 [request.schema.json](../../benchmarks/request.schema.json)；跨字段条件由引擎验证。预设位于 [benchmarks/scenarios](../../benchmarks/scenarios/visual-characters.json)，包含人物/动物、建筑、面部、握持手部、人物界面和 300 人模拟渲染。字段名严格匹配，未知字段、未知命令、重复截图帧及越界参数会被拒绝。

| 字段 | 含义与范围 |
|---|---|
| `id`, `seed` | 实验名称（1–64 位字母/数字/点/下划线/短横线，首位为字母或数字）与世界种子 |
| `agents`, `previewDays` | 人口 0–2000，初始预演 0–30 天 |
| `panel` | `field`, `person`, `person-hands`, `architecture`, `characters`, `faces`, `equipment`, `actions`, `hands`, `naturemodels` |
| `warmupFrames`, `measuredFrames` | 预热 3–3600 帧，采样 2–36000 帧 |
| `ticksPerFrame` | 每个逻辑帧推进 0–20 个模拟 tick，与本机快慢无关 |
| `captureFrames` | 从 0 开始的逻辑帧索引；至少第 3 帧，最多 32 个，无重复，不能超过运行末帧 |
| `commands` | `{frame, command, value}` 数组，最多 4096 条；同一帧按数组顺序执行 |
| `minimumFps` | 墙钟平均帧率检查线；0 表示仅记录，不设置性能门槛 |
| `outputDirectory` | 引擎输出目录，非空目录拒绝覆盖 |

表现层 `_Process` 使用固定 1/60 秒，包括世界角色、肖像和图册。这不意味着每秒实际渲染 60 帧；状态推进与采样帧独立于渲染速度。字体、驱动、着色器时间及浮点光照可能使像素有差异，不要求 PNG 跨机器逐字节一致。模拟摘要可用于相同配置下的确定性检查。

命令白名单是 `journal.open`, `settings.open`, `escape`, `portrait.face`, `portrait.hands`, `portrait.body`, `portrait.drag`, `portrait.zoom`, `portrait.reset`, `world.view`。人物命令需要人物面板；拖动固定水平 20 像素，`value` 是垂直位移；缩放正值为向内一步、负值或 0 为向外一步；镜头预设为 `near`, `top`, `oblique`。所有命令都记录执行前后状态与模拟摘要是否保持一致。

这是供实验控制器调用的**语义接口**，直接走游戏处理函数，不等同于鼠标命中、OCR、操作系统输入或自主探索能力测量。受测模型可以生成配置 JSON；当前运行器不直接调用模型服务、执行模型输出代码或提供在线 RL 步进服务。

## 性能与成对比较

重复采样方法、CPU 区段的解释与渲染缓存说明见 [性能采样与渲染维护](performance.md)。

帧耗时来自 `Stopwatch` 单调墙钟的帧提交间隔，不使用可能受限的游戏 `delta`。输出平均 FPS、p50/p95/p99/max、绘制调用、图元数及托管堆内存快照。`cpuScopes` 提供选定主线程区段的调用次数与耗时，父子区段重叠，不能相加。没有 GPU fence 时间、显存或峰值内存测量；托管堆数据不能解释为总内存。

截图、写盘和界面回放开销包含在采样里；纯性能预设没有截图。性能候选与基线应各重复至少 3 次，报告中位数与波动，保持供电、后台负载、驱动和机器一致。不能把限帧样本与无限帧样本混合比较。

```powershell
python tools/evaluate.py compare runs/evaluation/baseline runs/evaluation/candidate
python tools/evaluate.py verify runs/evaluation/candidate
```

比较器拒绝配置、引擎、渲染器、设备、分辨率、VSync、运行器执行参数或最终模拟摘要不同的结果，返回原因；相同条件才输出 FPS 比值和 p95 耗时变化。它不自动断言优化显著，也不把减少几何造成的视觉损失当成成功。优化实验应同时保留视觉检查与功能自检。

退出码：0 为完成且满足设定门槛；1 为低于性能门槛，结果仍保存；2 为协议/产物/对照错误；运行器 124 为超时。`artifactValidation: passed` 只说明产物及回放检查有效，不能替代性能门槛结果。

## 视觉模型接入与评分

只向被测模型提供 `questions.json` 和它引用的裁切 PNG。预测格式为：

```json
[{"id": "f00090-r0", "label": "resident"}]
```

```powershell
python tools/evaluate.py score runs/evaluation/visual-run predictions.json
```

评分输出总题数、回答数、正确数、准确率与缺失 ID。缺失计错，重复 ID、未知 ID、候选集外标签和校验值不符拒绝评分；没有视觉题目的运行准确率为 `null`。当前任务是十类对象识别，尚不衡量握柄接触质量、解剖正确性、空间推理或 UI 使用能力；手部与面部样本可用于人工缺陷判断，不能拿简单对象识别准确率代表建模质量。模型身份、推理参数、提示词、token/时间预算应由外部实验管理器记录。

## 自我改进实验的使用边界

可将版本 A 的性能与视觉样本交给编码模型，允许它修改游戏实现，随后用固定评测器运行版本 B，保留 Git 补丁、成本与全部成功/失败结果。功能自检、状态摘要、画面检查和性能是不同门槛，不能用一个数值掩盖退化。

[DGM 原论文](https://arxiv.org/abs/2505.22954) 用编码基准实证检验自修改候选；本项目提供相似实验所需的可重放环境与证据产物，但一次游戏优化只能说明特定修改有效，不能证明模型递归提升了自身改进能力。要研究后者，外部实验还须记录多轮代理版本、未见任务上的迁移、固定预算和无自修改对照。

本仓库预设公开，答案也可从源码推导，适合开发与回归，不能宣称为无泄漏公开排行榜。正式研究应把留出种子、题目和答案保存在可信评测机，并固定/隔离运行器与评分器；不能允许候选修改自己的判分规则后仍接受分数。SHA-256 与本地溯源帮助发现误改，不是签名、隔离或防作弊证明。当前入口不会自动自修改、部署代码或执行 RSI 循环。

## 维护与检查

```powershell
./tools/godot.ps1 -Mode test
python -m unittest discover -s tools -p test_evaluate.py
./tools/check-docs.ps1
```

Godot 自检包含协议拒绝测试、模型/握持几何和 UI 状态；Python 检查缺失/重复预测、产物改动、不兼容比较及失败的界面回放。修改协议时升版本并同步场景、文档和可信评测器。旧 `--benchmark-seconds` 是交互演示的旧采样方式；研究对照应使用本文的帧预算入口，旧样本与新样本不能直接比较。
