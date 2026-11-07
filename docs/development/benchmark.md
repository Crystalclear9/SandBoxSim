# 评测接口与实验运行

SandBoxSim 同时提供自由沙盒客户端和显式启用的本地评测入口。评测模式固定世界种子、人口、预演天数、模拟步数和表现层时钟，输出机器可读记录；正常游戏没有评测任务、强制目标或分数面板。

当前协议为 `schemaVersion: 1`。能力边界可用 `python tools/evaluate.py describe` 查询，清单见 [interface.json](../../benchmarks/interface.json)。本文描述渲染性能、模型图像识别和界面语义回放；在线逐步控制、HTTP 服务和配对程序效率见 [在线控制与效率实验](online-efficiency.md)；实际代理自修改及三种后端见 [代理改进协议](rsi-improver.md)。实验流程验证不等于通用 RSI 能力已被证明，也不衡量模型美感或真实感。

## 启动与产物

环境是 Python 3.10+、.NET 8、Godot 4.7.2 .NET。先编译客户端，再运行：

```powershell
./tools/godot.ps1 -Mode build
python tools/evaluate.py run benchmarks/scenarios/visual-characters.json --label baseline
python tools/evaluate.py run benchmarks/scenarios/ui-portrait.json --label ui-review
python tools/evaluate.py run benchmarks/scenarios/render-population.json --label render-review
```

通过 `--engine` 或 `GODOT_EXE` 指定引擎，macOS 也可传入 `.app` 目录；明确指定的无效路径直接拒绝，不悄悄换用另一个引擎。启动前核对 4.7.2 .NET 版本。运行器优先查找安装脚本的本机目录，再查找 PATH；支持 Windows 控制台 EXE、Linux 可执行文件与 macOS 包内二进制。`--output` 指定独立输出目录，已有目录会被拒绝，保留以前的样本。默认窗口请求为 `1440x900`、关闭 VSync、`--max-fps 0`，实际物理/逻辑尺寸与 VSync 状态写入结果；驱动可能仍限制帧率。`--max-fps 60` 可用于有上限的交互采样，但必须保持对照一致。`--audio-driver Dummy` 可在无声卡的 CI 中关闭音频设备依赖，执行参数写入溯源并参与比较。默认子进程超时为 180 秒，超时终止本次启动的进程树，`--timeout` 可调整。

地形外观可使用 `benchmarks/scenarios/visual-landscape.json`：固定种子 839102、40 位居民、零模拟步进，第 40 帧记录远景，第 60 帧切换近景，第 80 帧再次截图。该场景不设 FPS 门槛，用于检查扫描表面、岸线和贴地关系，不能当作动态聚落性能或模型审美分数。

构建脚本保存 `build-source.json`，绑定 Core/Godot C# 文件、工程/根构建配置与两个程序集的 SHA-256。运行前与结束后核对该清单；修改 C# 后未重新构建、替换 DLL 或缺少清单会拒绝采样。直接 `dotnet build` 不生成此清单，使用上述项目脚本。历史归档没有该字段时仍可检查原产物完整性，不能补写为具有新的构建校验。

| 文件 | 用途 |
|---|---|
| `request.json` | 引擎实际消费的规范化配置 |
| `result.json` | 墙钟耗时、帧耗时分位数/长帧次数、环境、最终状态、AI 诊断与回放记录 |
| `frame-*.png` | 指定逻辑帧的完整画面，附 SHA-256 |
| `f*-r*.png` | 图册模型区域的图像，按实际像素缩放裁切，不含下方标题 |
| `questions.json` | 视觉模型输入：问题 ID、图像、校验值与候选标签 |
| `answer-key.json` | 可信评测器使用的标签与完整截图区域，不能给被测模型 |
| `engine.log` | 原生引擎输出 |
| `provenance.json` | Git 提交/脏状态、源码与已加载程序集哈希、运行器与引擎哈希、运行参数、退出码、产物哈希 |

输出位于忽略提交的 `runs/evaluation/`。配置、入口、评分器和说明保存在仓库；正式配图仍在 `docs/images/`。引擎可直接使用 `-- --evaluation=/absolute/request.json`，但直接运行不会生成 Python 运行器的溯源文件。图像采样需要原生图形渲染器，不能使用 headless dummy renderer。

## 场景协议

机器可读字段描述见 [request.schema.json](../../benchmarks/request.schema.json)；跨字段条件由引擎验证。预设位于 [benchmarks/scenarios](../../benchmarks/scenarios/visual-characters.json)，包含人物体型、衣装、面部和关节对照、动物、建筑、界面及 300 人模拟渲染。字段名严格匹配，未知字段、未知命令、重复截图帧及越界参数会被拒绝。

| 字段 | 含义与范围 |
|---|---|
| `id`, `seed` | 实验名称（1–64 位字母/数字/点/下划线/短横线，首位为字母或数字）与世界种子 |
| `agents`, `previewDays` | 人口 0–2000，初始预演 0–30 天 |
| `panel` | `field`, `tools`, `trails`, `homes`, `farms`, `person`, `person-hands`, `architecture`, `characters`, `faces`, `equipment`, `actions`, `hands`, `naturemodels`, `model-details`, `ecology-details`, `animal-details`, `animal-motion`, `animal-heads`, `wardrobe`, `female-wardrobe`, `female-builds`, `female-shoulders`, `body-frames`, `body-motion`, `locomotion`, `arm-swing`, `forearm-frames`, `face-frames`, `thorax-frames`, `work-effort`, `motion`, `social`, `social-side`, `shoulder-motion`, `handling-transitions`, `shoulder-elevation`, `captured-stretch`, `arm-articulation`, `upper-arm-flex`, `elbow-joint`, `wrist-motion` |
| `warmupFrames`, `measuredFrames` | 预热 3–3600 帧，采样 2–36000 帧 |
| `ticksPerFrame` | 每个逻辑帧推进 0–20 个模拟 tick，与本机快慢无关 |
| `captureFrames` | 从 0 开始的逻辑帧索引；至少第 3 帧，最多 32 个，无重复，不能超过运行末帧 |
| `commands` | `{frame, command, value}` 数组，最多 4096 条；同一帧按数组顺序执行 |
| `minimumFps` | 墙钟平均帧率检查线；0 表示仅记录，不设置性能门槛 |
| `outputDirectory` | 引擎输出目录，非空目录拒绝覆盖 |

表现层 `_Process` 使用固定 1/60 秒，包括世界角色、肖像和图册。这不意味着每秒实际渲染 60 帧；状态推进与采样帧独立于渲染速度。字体、驱动、着色器时间及浮点光照可能使像素有差异，不要求 PNG 跨机器逐字节一致。模拟摘要可用于相同配置下的确定性检查。状态还记录 `worldVisuals` 的图形刷新欠账和 `ai` 的决策、目标选择失败、动作失败及寻路次数；这些是进程诊断数据，不是通用智能分数。AI 规则改变后，旧版本与新版本的摘要可能不同；比较器会拒绝把这种差异当作纯性能优化。

命令白名单是 `journal.open`, `settings.open`, `escape`, `portrait.resident`, `portrait.face`, `portrait.hands`, `portrait.body`, `portrait.drag`, `portrait.zoom`, `portrait.reset`, `portrait.expand`, `portrait.collapse`, `world.view`。人物命令需要人物面板；拖动固定水平 20 像素，`value` 是垂直位移；缩放正值为向内一步、负值或 0 为向外一步；镜头预设为 `near`, `top`, `oblique`。所有命令都记录执行前后状态与模拟摘要是否保持一致。

`portrait.expand` 和 `portrait.collapse` 使用空 `value` 展开或收起人物观察窗，状态中的 `portraitExpanded` 记录实际开合。Esc 优先关闭观察窗，保留下层设置和手记；校验器兼容缺少该新增字段的旧记录。公开 `ui-portrait-window` 场景在零步进世界中回放构图、拖动和返回，第 90/130/170 帧保留面部、双手与全身画面。它是 UI 和观察纯度检查，不是 RSI 能力结论。

住房回放另提供 `home.focus` 和 `home.resident`，仅接受 `homes` 面板与空 `value`。前者触发实际住房卡的近景按钮，后者触发第一位实际住户的档案链接；没有选中住房或没有住户时运行失败，不植入虚构居民。`world-living-homes` 场景保存交互前后截图和不变的模拟摘要。

这是供实验控制器调用的**语义接口**，直接走游戏处理函数，不等同于鼠标命中、OCR、操作系统输入或自主探索能力测量。受测模型可以生成配置 JSON；此渲染运行器不直接调用模型服务或执行模型输出代码。需要在线控制模拟状态时使用独立 HTTP 服务。

## 性能与成对比较

重复采样方法、CPU 区段的解释与渲染缓存说明见 [性能采样与渲染维护](performance.md)。

帧耗时来自 `Stopwatch` 单调墙钟的帧提交间隔，不使用可能受限的游戏 `delta`。输出平均 FPS、p50/p95/p99/max、绘制调用、图元数及托管堆内存快照。`cpuScopes` 提供选定主线程区段的调用次数与耗时，父子区段重叠，不能相加。没有 GPU fence 时间、显存或峰值内存测量；托管堆数据不能解释为总内存。

截图、写盘和界面回放开销包含在采样里；纯性能预设没有截图。性能候选与基线应各重复至少 3 次，报告中位数与波动，保持供电、后台负载、驱动和机器一致。不能把限帧样本与无限帧样本混合比较。

```powershell
python tools/evaluate.py compare runs/evaluation/baseline runs/evaluation/candidate
python tools/evaluate.py verify runs/evaluation/candidate
```

比较器拒绝配置、引擎、渲染器、设备、分辨率、VSync、`renderQuality` 阴影/MSAA 参数、运行器执行参数或最终模拟摘要不同的结果，返回原因；相同条件才输出 FPS 比值和 p95 耗时变化。它不自动断言优化显著，也不把减少几何造成的视觉损失当成成功。优化实验应同时保留视觉检查与功能自检。

有图册时，`renderQuality.galleries` 还包含各卡片实际阴影模式、范围、偏移和地台投影条件；地图同时记录阴影偏移。可见人物肖像使用 `renderQuality.portrait` 保存实际子视口分辨率、MSAA、背景、环境光及灯光颜色、能量、方向、投影状态及各灯的阴影模式、范围、深度偏移与法线偏移。旧结果缺少这些条件时应重建基线，不能把画质差异归因于程序效率。

模型图册只构建和绘制自己的工作室，避免在六个模型视口背后再绘制完整世界。`renderQuality.worldPresentationEnabled` 在图册中为 `false`，普通地图和人物观察场景为 `true`；原有图册结果需要重新采样。CI 的角色图册烟雾测试检查原生渲染、截图、协议和进程正常结束，不作为全世界渲染性能或 RSI 改进成绩。

CLI 输出使用 UTF-8；`python tools/evaluate.py --json-errors <子命令> ...` 将输入或用法错误作为 JSON 写到 stderr，包含 `schemaVersion`、`error.code` 和 `error.message`。引擎异常的原始退出码保留在溯源中，运行器统一返回接口定义的错误码。验证会核对请求 ID、帧预算、截图排程、状态门槛以及 PNG 结构/CRC，不能把缺少截图的运行视为完成。

退出码：0 为完成且满足设定门槛；1 为低于性能门槛，结果仍保存；2 为协议/产物/对照错误；运行器 124 为超时。`artifactValidation: passed` 只说明产物及回放检查有效，不能替代进程正常退出或性能门槛结果。即使产物完整，真实进程超时也继续返回 124。

截图需要真实的 `frame_post_draw`。截图开始时若评测自己的窗口被最小化且绘制循环开启，客户端将其恢复为窗口模式，等待新绘制帧，并在阶段日志记录 `capture_window_restored`。结果的可选整数 `captureWindowRestores` 记录恢复次数；旧结果缺省为零，验证要求次数不超过截图数。恢复样本可供视觉检查，`compare` 拒绝将其用于耗时对比，需要在稳定窗口条件下重新采样。

客户端在等待新绘制信号超过 15 秒时记录 `capture_wait_timeout`、当前窗口模式与绘制循环状态，并以 2 退出，不保存旧帧充当当前截图。该保护覆盖逻辑仍运行但没有绘制帧的等待；主线程或 GPU 读回整体阻塞仍由运行器的总超时终止进程树。关闭绘制循环不会通过恢复窗口伪造成功。正常游戏的窗口偏好不受此评测逻辑修改。

原生日志在 Windows 上仍被占用时，运行器先保存捕获输出到 `engine.log`，继续写入实际退出码、超时阶段与产物验证；溯源用 `nativeLogRetained`、`nativeLogCleanupError` 记录保留路径及异常类别，避免日志文件操作覆盖真正失败原因。该情况不会把超时或失败判成通过。

运行器将 stdout/stderr 写入项目内临时文件并直接等待引擎 PID，不再依赖日志管道的 EOF；避免 Linux 桌面辅助进程继承管道后，引擎已退出却被误判为超时。结束后合并为 `engine.log`，仍拒绝日志中的引擎错误。CLI 摘要与溯源记录 `engineExitCode`、`timedOut`；溯源另记 `timeoutSeconds`，区分引擎错误退出和实际超时终止。CI 在 Windows/Linux/macOS 上回归继承日志句柄、真实超时及非零退出。

`engine.log` 中的 `EVALUATION_PHASE` 单行 JSON 记录 `world_initialized`、`interface_ready`、`scene_ready`、`warmup_complete`、`capture_started`、`capture_complete`、`quit_requested`。每项包含当前评测帧及从载入配置开始的单调时钟毫秒数，保存到 `provenance.phaseTimings`；失败摘要的 `lastPhase` 帮助区分启动、采样和退出阶段。`quit_requested` 只表示发出了退出请求，进程实际退出码仍单独校验。启动计时不改变已有采样区间 FPS 的计算口径。

## 视觉模型接入与评分

只向被测模型提供 `questions.json` 和它引用的裁切 PNG。机器可读定义见 [predictions.schema.json](../../benchmarks/predictions.schema.json)，结果定义见 [result.schema.json](../../benchmarks/result.schema.json)。预测格式为：

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

本仓库预设公开，答案也可从源码推导，适合开发与回归，不能宣称为无泄漏公开排行榜。正式研究应把留出种子、题目和答案保存在可信评测机，并固定/隔离运行器与评分器；不能允许候选修改自己的判分规则后仍接受分数。SHA-256 与本地溯源帮助发现误改，不是签名、隔离或防作弊证明。多轮效率证据验证器记录代理版本、控制与预算；模型调用和候选构建由外部控制器管理，不自动部署模型输出。

## 维护与检查

```powershell
./tools/godot.ps1 -Mode test
./tools/test-godot-path.ps1
python -m unittest discover -s tools -p test_evaluate.py
./tools/check-docs.ps1
```

Godot 自检包含协议拒绝测试、模型/握持几何和 UI 状态；Python 检查缺失/重复预测、产物改动、不兼容比较及失败的界面回放。修改协议时升版本并同步场景、文档和可信评测器。旧 `--benchmark-seconds` 是交互演示的旧采样方式；研究对照应使用本文的帧预算入口，旧样本与新样本不能直接比较。

## 文档图与实验样本

选定的实机配图可以复制到 `docs/images/`，完整实验样本继续保留在 `runs/evaluation/`；复制出的图片只用于说明，不替代原样本的哈希、状态、成本或答案记录。所有公开样例和协议入口集中在 [benchmarks](../../benchmarks/README.md)。

构建身份记录与 Python 校验器共同使用仓库实际文件名 `NuGet.config`；改变包源配置会使旧构建身份失效，需要重新构建。PowerShell 与 Python 各有规范路径回归，避免 Windows 路径大小写宽容掩盖 Linux 上遗漏配置的差异。

`execution.userDataPolicy` 记录项目内运行目录策略；旧结果缺少该字段时，比较器拒绝直接对照。`.crt` 文件进入源码身份记录，证书哈希、数量、可解析性和包内许可由客户端自检验证。运行器仍拒绝所有 `ERROR:`，包括证书与缓存错误；修复配置不等于过滤错误日志。

## 当前渲染版本

当前渲染元数据为 `animalPresentation=articulated-paws-v12`、`interface=resident-framing-memory-v28`、`architecture=inset-joinery-v13`、`garmentMaterial=quiet-linen-v19`、`landscapeSurface=wet-bank-mosaic-v7`、`habitatDetail=responsive-understory-v12`。另外记录 `residentSurface=linear-skin-v9`、`vegetationSurface=engine-faced-v2`、`wildFlora=arched-bramble-v1`、`waterSurface=shore-activity-v7`、`wristContour=continuous-source-v3`、`hairline=framed-source-root-v2`、`femaleGroom=scalp-guided-plait-v1`、`reedPanicle=connected-axis-v1` 和 `handSurface=anatomical-regions-v2`。人物肖像的 `renderQuality.portrait.studio=continuous-inspector-v9`，并记录实际 `thumbnail` 布尔值。世界方向光 `shadowBias=2.4`，阴影距离和法线偏移仍随相机距离变化，元数据记录实际值。衣装领口与肩袖、人物布光、动物足部、建筑门窗接合及连续观察控件已改变，需重采相同场景的视觉和耗时基线；不能把不同像素覆盖与布局造成的耗时变化算作代码优化收益。`renderQuality.fontProfile=noto-packaged-v1` 记录随包字库策略，字体变化需要新视觉与耗时基线。`renderQuality.interfaceVisible` 标识界面是否显示，公开评测场景默认显示；沉浸观察不会自动成为新的评测指令。正式动物与保留的程序校准动物检查分别运行。`animal-details` 的鹿狼眼部构图已改为更近的眼眶观察，旧图像与新构图应分别采样。

`reachMotion=palm-trajectory-v2` 记录手掌轨迹过渡、朝向插值、错开的社交手势与循环进食表现。动作轨迹改变需要重采相同场景的视觉及耗时基线，不能与旧动作混作效率对照。

`garmentFit=continuous-drape-v37`、`femaleTrouserFit=supported-thigh-v1` 记录女性裤装的髋腿轮廓与据此生成的屈髋、屈膝目标。比较 `female-builds` 时应重新采集同一分辨率的图像，该图册的视野已向下覆盖到膝部。`body-motion` 使用原相机复核静立、步行与拾取。

`bodyFrame=reference-contour-physique-v31`、`faceContour=balanced-adult-v3` 和 `forearmProfile=curvature-flow-v9` 记录完整来源躯干、头脸区域与小臂体积拟合。它们改变轮廓和模型构建成本，视觉及性能对照须使用同一版本、居民身份与近远景条件。

无窗口自检的花株接地检查使用 CPU 保存的实例位置；图形模式额外核对 GPU 实例变换。渲染服务器的占位读回不作为几何证据。快捷操作检查覆盖居民、鹿狼和土地的真实目标位置、失效实体身份拒绝、靠近的实际相机距离、人物观察窗／土地手记及观察纯度。

## 人物观察状态

评测结果 `state.portrait.lighting` 记录 `soft` 或 `raking`，对应隔离人物观察窗的柔光与侧光；它是附加的呈现状态，不是模拟规则或 RSI 指标。现有公开指令仍保留，未加入新的 HTTP 控制动作。改变人物资料页面或观察灯光不应改变模拟摘要；客户端自检与实际鼠标回放均检查页面与选中状态。使用视觉或时间基线时需要固定光线和构图，不能混合两个预设的图像分布。`renderQuality.shoulderMotion=source-clavicle-v11` 标识锁骨弧线、独立肩胛平台及原有肘腕约束；胸肩支撑体、袖窿及姿态求解均已变化，需要重新采样视觉和耗时基线，不能把资产差异算作程序加速收益。

`state.portrait.thumbnail` 是新增布尔呈现字段：人物手记中的缩略状态为 `true`，独立观察窗为 `false`。缩略图与观察窗复用同一视口，展开不重置已有聚焦、旋转、缩放或灯光；关闭后缩略显示固定面部，保存的工作模式仍在 `state.portrait.framing` 等原字段中；重新展开恢复工作视角。`renderQuality.portrait.thumbnailFraming=face` 仅描述实际缩略构图，非缩略状态为 null。关闭后恢复入口焦点。该字段不增加请求指令或修改 `schemaVersion: 1`，更不代表模型审美、输入命中能力或 RSI 得分。镜头仍由原 `portrait.zoom` 指令的正负方向触发滚轮，普通步长现为比例缩放；请求字段和指令名不变。原 `distance` 记录镜头目标距离。窗口布局、发根和芦苇形体已变，应重新采样视觉与耗时基线。语义回放不能替代真实鼠标／键盘检查。

---

[文档导航](../README.md) · [项目首页](../../README.md)

工具及动作元数据增加 `toolGeometry=forged-heads-v2` 与 `handlingMotion=wholebody-motion-v7`；工具近景、动作过渡与人物观察界面改变后，需要重新采样视觉和耗时基线。

搬运网格记录 `cargoGeometry=bound-timber-v2`；两行动作卡与搬运状态切换改变后，应重新采样同场景的视觉和耗时基线。

挂点和社交姿态另记录 `equipmentAttachment=surface-holster-v2`、`socialMotion=paired-conversation-v11`；“查看目的地”是玩家界面入口。

`workEffort=arc-recovery-v4` 标识按工具、个体表现系数、真实疲劳及健康驱动的作业节奏与躯干配合。`visual-work-effort` 对照同一手锤的男女成人外观与状态，`visual-face-frames` 提供正面及三刻面。面部曝光与局部拟合、动作减速和节奏均改变，需要重新采样；这些展示场景不测量真实力量，也不构成 RSI 结果。请求 Schema 的面板枚举与客户端公开白名单一致，工具回归同时核对所有公开场景。

`bodyFrame=reference-contour-physique-v31` 包含胸廓／肩部／骨盆衣料绑定（两种成人均使用二十六骨肩袖支撑）、放松站姿落脚求解、连续来源裤装、五骨臀腿蒙皮及 113×128 的来源躯干截面、胸廓／髋侧／臀部方向性支撑、凸包松量与宽缓胸前衣料包络及缩短的女性上衣低段、按缝片坐标衔接的肩袖、腰带与工具挂点高度、骨盆间距及上大腿轮廓；`faceContour=balanced-adult-v3` 标识眉弓、鼻翼、颊部、下颌与颈部的区域拟合。`toolMotion=constraint-feedback-v4` 标识 IK 前的位置／朝向速度跟踪、弧形蓄力、收纳到位判定及手腕约束后的实际握柄姿态反馈。`work-effort` 镜头为正交尺寸 1.15、目标 (0, 1.05, 0)，用于看清头肩和握柄；不同构图或体型版本不能直接混作视觉或效率基线。`body-frames` 当前为六个正／侧／背全身区域，1600 像素宽屏按同视角男女配对以六列展示，使用无职业装备的标准衣装；旧四格胸腰构图不能混作视觉或效率基线。`ADULT_CONTOURS_PASS` 和 `TRUNK_PROFILE_PASS` 的穿衣宽度／深度检查仅约束本项目的几何与 LOD，不是性别识别、真人相似度或 RSI 得分。

`bodySupport=planted-sagittal-v1` 和 `facialTiming=rested-phrase-blink-v2` 分别记录劳动支撑姿态、身份错开的眨眼及表达节奏；`residentSurface=linear-skin-v9` 包含实测来源 RGB 参考与正确颜色空间校准。动作、头脸像素和成本均改变，需要重新采样。`verify_skin_colour.py` 是需真实显示设备的独立颜色回归，不输出视觉问答分数或 RSI 结论。

`visual-body-motion.json` 使用六格配对画面，上排男性外观、下排女性外观，显示放松站立、行走和弯身后起身。固定 60 Hz 表现步进，默认采样第 90／180／270／330 帧。镜头正交尺寸 2.13、目标 (0, 0.82, 0)，首列相对位置 (1.3, 0.08, 4)，其余为 (2.8, 0.08, 4)。

`animalPresentation=articulated-paws-v12` 包含鹿 18／狼 21 关节、足区权重转接、来源三角面上的趾爪／副蹄、分段狼尾及物种分开的步态轨迹。`visual-animal-motion.json` 使用 `animal-motion` 四格图册，固定 60 Hz、无模拟 tick，采样第 90／180／330 帧；动物区域标签从实际模型物种生成，分别为 `deer`、`wolf`。它检查模型与动作表现，不构成 RSI 能力分数。

`residentLocomotion=captured-boot-rockers-v9` 和 `gaitReference=cmu-contact-curves-v2` 标识摆脚、鞋底俯仰和骨盆重心轨迹。`armSwing=captured-spatial-swing-v8` 加入双侧上臂的外摆采样。`visual-locomotion` 使用同速度成人外观、双手搬运及持锤行走到作业的对照，冻结模拟并推进动画。`BOOT_ROCKER_PASS` 在两种体型的稳定步行中比较实际鞋底承重点与去除补偿后的纵向滑移。

`residentTravel=projected-resident-route-v4` 标识实景的近景路线预判、起步重心转移与换脚转身。`pacedResidentTravel` 记录当前是否启用实景步速。评测配置固定 `ticksPerFrame` 大于零时按该配置推进。`RESIDENT_ROUTE_PROJECTION_PASS` 由模拟动作系统推进实际地形上的多格路线，测量过格时的根节点速度与重复起步，核对转弯支撑、到站拾取、拓扑失效和模拟搜索计数。`RESIDENT_WORLD_MOTION_PASS` 检查只收到已确认格点时的步行与转身。`RESIDENT_DEPARTURE_PASS` 检查起步准备期双足、根节点和骨盆。`WORLD_REDIRECT_PASS` 在实际地形上从向前搬运切换到反向逃离，记录刹停距离、承重脚和根节点漂移，并核对物件支撑与模拟摘要。

`residentContactApproach=supported-contact-v1` 标识加速下的近距离接近、结算前的搬运站位和工具朝向。`WORLD_ACCELERATED_APPROACH_PASS` 测量同一实际堆位的起步准备与峰值加速度，并检查站稳、转向和结算前的物品归属。`WORLD_STOCK_CONTACT_PASS` 同时核对仓库候选与结算后的站位，以及部分入库时的物资类型。

`residentActionHandoff=displayed-palm-v1` 记录手部接续、库存变动后的包裹收回和双人递接的速度过渡。`OBSERVED_CARGO_STOW_PASS` 在两种成年体型的 30／60 Hz 轨迹中检查旧包裹的可见支撑与新包裹的上手顺序。`SHARED_PARCEL_PASS` 同时记录换手后一帧的物件速度变化。

`residentObjectContact=confirmed-support-v1` 记录取物的等待接触、合指后固定物件，以及下放途中失准后的重新接近。`PICKUP_CONTACT_TIMING_PASS` 检查较晚到达的手掌能否握住目标、已握物件是否在接触失效后随手收回，以及不可达目标是否退出。`PLACEMENT_REALIGN_PASS` 和 `WORLD_PLACEMENT_REALIGN_PASS` 检查失准时的双手托举、实景换脚转身及重新到达原接收面。

`residentContactContinuity=bounded-confirmation-v1` 记录场景在渲染帧错过 `Done` 时的近景接触收尾。`WORLD_CONTACT_BRIDGE_PASS` 在真实地图中让模拟完成取物、放货和进食，再于下一帧前指定后续动作。检查抓握、松手和嘴部接触各发生一次，逃跑或离开原格时取消尚未发生的接触，并核对表现前后的模拟摘要。

`pairedInteractions=confirmed-participants-v1` 记录递物和交谈的参与者、准备与收尾规则。`WORLD_HANDOFF_PASS` 检查结算前走近、确认后出现食物、跳过 `Done` 的递接和换手后递出者退出。`WORLD_HANDOFF_RETARGET_PASS` 覆盖接收者忙碌及分享被替换时的回收。`WORLD_CONVERSATION_PASS` 检查空闲状态接续和逃跑中断。三项都核对表现前后的模拟摘要。

`workContactHandoff=shared-stroke-phase-v1` 记录同一接触点的作业衔接和失效接触的取消。`CONTINUOUS_WORK_CONTACT_PASS` 在 60／24 Hz 下测试斧、镐、锤和锄的握持、站姿和挥击相位，以及替换地点、建筑代次或接触丢失后的处理。`WORLD_REPEAT_WORK_PASS` 使用模拟实际采集产生的两次结算，核对同一树干上的连续动作及表现层读取的状态摘要。

`WORLD_OUTSOLE_PASS` 在近景跟随距离测量同一条多格路线的起步加速度、承重中段每帧平面漂移、鞋底对地的最低间隙，以及摆动期离地高度。采样点来自成品鞋底轮廓，地形高度与世界角色共用。相邻帧须同处同一只脚的承重中段才计入漂移。

`ARRIVAL_TURN_PASS` 测量最后两步的转角和骨盆侧向位移，并与到站拾取、额外换脚转向及支撑脚漂移一起检查。`WORK_SUPPORT_PASS` 包含持锤和持锄开工时的左右重心转移。`REST_TRANSFER_PASS` 测量两套成人外观的休息姿态下降、起身与脚底漂移。

`animalLocomotion=ground-distance-v1` 记录鹿狼的距离驱动步态。居民动画时序和画面更新后，相关视觉评测需采集新基线。

`taskReference=cmu-turn-and-dig-v1` 与 `toolContacts=furrow-edge-and-work-stance-v1` 记录捕捉转身、耕作重心和工具姿态。`CULTIVATION_PASS` 使用实际锄刃坐标检查平面与斜面上的拉土、抬刃和胸廓幅度。`WORLD_COMMITTED_WORK_PASS` 在实际农田检查作业前的准备、结算后的锄刃接触和劳动已满时的收势。

`RESIDENT_STATIONS_PASS` 在同一逻辑格放入三位居民，检查显示间距、邻居离开后的站位稳定性与模拟状态摘要。

`bodyFrame=reference-contour-physique-v31` 标识胸廓、短上衣、实际骨盆／臀腿裤装及衣袖排除区的当前造型；`visual-body-frames` 的正／侧／背对照与 `visual-body-motion` 的动作对照使用实际居民网格。髋部截面取上衣及裤装的可见外包络，不能把衣摆宽度当作人体围度，也不是性别分类或 RSI 分数。

`femaleBodyBuild=regional-builds-v2` 标识世界与居民详情使用的三种成年女性身形。`visual-female-builds` 以正交尺寸 0.87、目标 (0, 0.92, 0) 记录同一居民的正面与侧面。对照固定身高、衣装和姿态，核对胸廓宽深、腰髋几何与近远景一致性。

`femaleFlankShape=continuous-flank-v1` 标识共用的下腰、外髋与上腿形体及衣面。`FEMALE_FLANK_SUPPORT_PASS` 检查身体截面的位移、对称性、固定区及局部雅可比，`FEMALE_FLANK_TROUSER_PASS` 核对实际裤装的外髋截面和上腿过渡。服装动态对照使用 `visual-body-motion`，同时检查拾取、作业及步行中上衣与裤装的覆盖。

`femaleRibDrape=coupled-bending-v1` 标识女性前衣片的斜向约束和外侧松量。`FEMALE_RIB_DRAPE_PASS` 在每款成衣的实际前片读取 224 个双侧样本，用 5 毫米间隔测量纵向斜率随横向位置的变化。`CHEST_PANEL_PASS`、`CHEST_LATERAL_PASS` 和 `CHEST_VOLUME_PASS` 分别复核皮肤间隙、外侧宽度及纵向起伏。视觉对照使用 `visual-thorax-frames`。

`femaleLowerLeg=mid-calf-boot-v1` 标识女性中筒靴、延长的裤管和小腿曲面。`FEMALE_MID_CALF_PASS` 读取实际成品截面，核对靴口高度、布料包覆及裤脚的胫骨权重。`FEMALE_BOOT_FIT_PASS` 与 `FEMALE_SHAFT_PASS` 分别校验鞋底接触和九种踝部方向下的接合。实机对照使用 `visual-body-frames` 和 `visual-body-motion`。

`femaleShoulderLine=clavicular-crest-v2` 记录领口、锁骨到袖山的高度拟合。`CLAVICULAR_YOKE_PASS` 比较三款成衣的实际顶点到局部曲面的高度残差，并约束位移和领缘。`femaleCoatCollar=sewn-facing-v1` 记录随衣身蒙皮的外套低领翻边。`femaleLowerPanel=grain-fitted-waist-v1`、`garmentFit=continuous-drape-v40` 和 `femaleFrontCut=supported-closure-v1` 区分三款衣装的下肋松量、门襟与纽扣。`garmentMaterial=quiet-linen-v19` 记录纱纹和细节法线。视觉对照使用 `visual-female-shoulders`、`visual-female-wardrobe` 与 `visual-female-builds`。冷启动耗时比较须固定款式和身形缓存状态。

`femaleRearShoulder=supported-meridians-v1` 标识后领到肩胛的成衣剖面拟合及高举时的后片修形。`visual-female-shoulders` 以 0.52 米正交尺寸从背面及后侧记录 0°、90°、135° 三种姿态。`FEMALE_BACK_YOKE_PASS` 检查静止后衣片相对领缘的突出量。`FEMALE_OVERHEAD_YOKE_PASS` 在三款衣装上分别测量左右臂举至 135° 时，后肩区域中面夹角超过 55° 的内部边长占比。

`armSwing=elliptic-countermotion-v4` 标识掌面朝内的空手基础姿态、肩肘相位、前臂轴向及独立阻尼目标。`visual-arm-swing` 使用实际网格和控制器，包含正侧面及掌腕近景；人物造型与模拟输入保持独立，图片不是医学步态、视觉真实感或 RSI 分数。

`bodyFrame=reference-contour-physique-v31` 的成人上衣使用缓存凸包松量，短下摆取上髋支撑；`garmentMaterial=quiet-linen-v17` 对应 `faired-panel-v8` 连续袖片、贴合后重算法线及较薄卷袖口。`visual-wardrobe` 以正交尺寸 0.88、目标 (0, 1.13, 0)、相机相对位置 (0.10, 0.035, 3) 展示衣装近景；它与全身 `body-frames` 分别用于表面和整体比例检查。新几何必须重采视觉与构建耗时，不能将旧图片当作当前包络。

`reference-contour-physique-v31` 还包括按衣片最终高度取样的胸腰／上髋贴合、局部胸廓及腰背轮廓、重新导出的连续臀腿表面、同步适配的上臂与卷袖口，以及远景保留的缝片坐标。对照须使用同一身份、衣色、视角和 LOD；穿衣截面反映项目造型，不是医学分类阈值。

`limbCoordination=hinge-carry-v4` 标识肘部屈曲平面、低位携带和按掌柄绑定安排的短柄／长柄朝向；`residentLocomotion=settled-arrival-v3` 增加连续骨盆支撑与局部拾取脚踝求解。`visual-motion`、`visual-social`、`visual-social-side` 的默认采样从第 30 帧开始每 14 帧一次，共 30 帧，冻结模拟而按 60 Hz 推进表现。它们用于观察动作和评测采样链路，不是 RSI 或人体写实程度评分。

`handlingMotion=supported-parcel-v19` 覆盖候选准备、握持、场景接触和中断回收。`INTERACTION_READINESS_PASS` 验证两种成人体型的 12 类不可用交互，以及施工模型迟到后的单次挥击和等待超时。`WORLD_ACCELERATED_CONTACT_PASS` 测量加速观察中转身后的伸手，以及物资清空后的中断。可向引擎传入 `--self-test --interaction-contact-only` 复测世界场景。

`CARGO_TRANSITIONS_PASS` 测量搬运转入进食、饮水、取物、耕作、攻击与休息时的包裹位移和相邻帧步长，并检查下一动作拿起道具时的手部占用。`WORLD_STOCK_CONTACT_PASS` 检查交付木材时余量更多的石料不会替换木捆。`WORLD_RAPID_DELIVERY_PASS` 检查首帧前结算完的铁料仍经历双手承重与一次松手。两个世界测试都可以通过 `--self-test --stock-contact-only` 运行。

`TOOL_PHRASE_PASS` 在 60 Hz 和 24 Hz 下核对五类工具的击打节拍、收势和紧急中断。`TOOL_SURFACE_PASS` 测量斧刃、镐尖和锤面到目标的距离。`WORLD_TOOL_SURFACE_PASS` 在世界居民路径中检查树干接触、目标消失和模拟摘要。可向引擎传入 `--test-tool-phrases`、`--self-test --strike-contact-only` 或 `--self-test --work-contact-only` 分别复测工具动作、实景挥击或实景耕作。

`socialMotion=paired-conversation-v11` 用 `WORLD_CONVERSATION_PASS` 检查实际世界中两人转身后轮流开口、倾听点头和逃跑时中断，可用 `--self-test --conversation-only` 复测。`spearMotion=guarded-thrust-v1` 随 `TOOL_PHRASE_PASS` 检查同一猎物换格时的架势连续性，以及模拟确认后矛尖向前移动并只刺一次。

`shoulderMotion=source-clavicle-v11` 标识分开的抬臂／轴向旋转、上臂驱动的锁骨弧线与肩胛上旋／后倾、接近伸直的稳定参考和左右独立返回；`limbCoordination=hinge-carry-v4` 包含工具肘平面连续性、真实作业支撑与挥锤朝向。`visual-shoulder-motion.json` 以同衣装的两种成人外观、正面、工作臂侧面和背面观察同一完整挥锤循环；默认从第 30 帧每 14 帧采样，共 30 帧。它冻结模拟，在固定 60 Hz 下推进动作，不构成 RSI 或人体动作质量的客观评分。

`shoulderSkin=reference-figure-v25` 标识两种成人衣身共用的二十六骨绑定，包含左右肩胛带、分离摆动与轴向扭转的六区间旋转、袖管位置修正及共享袖窿的表面修整，逐顶点保留四个影响；`bodyFrame=reference-contour-physique-v31` 包含女性来源关节半宽、同步衣袖／逆绑定和身体相对横向目标。`shoulder-motion` 上排为男性、下排为女性，三列依次为正面、实际工作臂侧面和背面；这些几何和镜头变化需要重新采样视觉基线。

`bodyFrame=reference-contour-physique-v31` 包含方向性胸腰衣料、重新导出的骨盆／大腿连续裤装和五档等比身高变化。`ADULT_CONTOURS_PASS` 测量近远景的胸腰与裤装骨盆截面，并检查它们相对来源的偏差；已有动态穿插、臂腿绑定及工具接触检查继续执行。该检查是几何回归门槛，不是人体性别分类、审美或 RSI 的能力分数。

`visual-handling-transitions.json` 使用公开 `handling-transitions` 面板，固定 60 Hz、冻结模拟。左列为到站和近地存放，中列为生活和作业的上身近景，右列每 5.5 秒演示两人走近、递出和接稳。`INTERACTION_SEQUENCE_PASS` 覆盖 25 种居民动作。`SHARED_PARCEL_PASS` 和 `WORLD_HANDOFF_PASS` 分别测量共同接触、物件连续性和实景走近，`CARGO_PLACEMENT_PASS` 核对地面存放的实际松手高度。视觉与耗时基线按版本保存。

客户端的只读 C# 查询 `ActionSystem.TryGetPresentationPartner` 在执行阶段返回当前候选，完成阶段返回仍在范围内的实际对象。`PARTNER_HANDOFF_PASS` 检查对象消失后停止收尾。对象记录绑定动作实例，保存在运行期。

`wholebody-motion-v7`、`elliptic-countermotion-v4` 和 `arc-recovery-v4` 分别标记徒手躯干参与、行走肩肘与胸廓协调、连续劳动相位曲线。改动涉及实际姿态与运行开销，需重新采样视觉及耗时；顶点非平坦和周期连续检查为项目工程约束，不作为真人动作评分。

`visual-shoulder-elevation.json` / `panel=shoulder-elevation` 是十二秒肩袖范围观察：上排男性外观、下排女性外观，三列为正面、固定侧面和背面。固定 60 Hz、零模拟步进，默认从第 30 帧每 24 帧采样，共 30 帧；左右臂各占六秒：1.5 秒抬起至 135°、一秒保持、1.5 秒放下、两秒休息。它使用实际游戏网格、肩肘腕求解和蒙皮，可用于同版本下的视觉比较，不是 RSI 成绩、帧率测试或医学动作评分。正常作业循环仍由 `visual-shoulder-motion.json` 覆盖。两者必须记录同一 `shoulderSkin`、`shoulderMotion`、构图与灯光后再作对照。

`reference-figure-v25` 含一百二十个平面／高度／轴向扭转驱动的位置／法线姿态修正，`source-clavicle-v11` 以锁骨弧线移动肩点、以旋转肩胛平台带动肩上衣片，并以实际肩肘腕平面确定肱骨朝向、运输肘平面及约束收回姿态，`reference-contour-physique-v31` 加入来源上臂截面及受短边约束的姿态修整。这些变化涉及网格、启动时缓存及 GPU 形态混合开销，应重新采样相同配置的视觉与耗时基线，不能把更换表示方式算作 RSI 或纯程序加速。表面法线角误差只针对指定姿态的实际三角面，不代表整体人体自然度或任意动作的质量。

### 来源动作观察

`visual-captured-stretch.json` 使用 `captured-stretch` 图册，固定 60 Hz、零模拟推进，观察来源 120 Hz 的 CMU 143_30 如何映射到实际游戏的两种成人外观。暖场 30 帧、测量 609 帧，默认每 20 帧取样；完整动图另每三帧采样。`renderQuality.motionReference=cmu-143-30-v1`、`shoulderSurface=authored-bind-surface-v3` 和当前肩袖标签共同标识数据与呈现版本。

来源表面的法向贴合残差用于检查衣料是否更贴近已变形的人体支撑，来源方向／手腕误差用于检查重定向是否保留输入。它们不构成审美、肌肉仿真或 RSI 分数。新增形态与缓存改变了启动和 GPU 开销，效率比较须冻结同一来源、配置、资产与版本。

`visual-arm-articulation.json` / `panel=arm-articulation` 提供实际上肢近景：暖场 30 帧、测量 480 帧，固定 60 Hz、零模拟推进，默认每 16 帧取样。八秒循环依次抬臂／屈肘、翻掌、放下；三列为正侧背面，两排为两种成人外观。完整动图每三帧采样，原生 1600×1000、160 个采样和八秒，分五次回放。

`renderQuality.forearmSkin=continuous-elbow-v9` 标记每手 56 骨的分段旋转表面与姿态法线，`shoulderSurface=authored-bind-surface-v3` 标记来源 A 姿势直接求解、绑定变换组合及固定三角面对应的双四元数人体支撑。二者改变了缓存生成和近景骨骼开销，须冻结同一版本再比较效率；动作端点、边长和表面距离只用于几何回归，不能作为写实度或 RSI 能力分数。

人体支撑 v2 的贴合残差以固定三角面对应和衣片参与规则为参考，与 v1 的逐姿态最近面查询不同；两者不能直接作为同一质量指标比较。完整 sourceFiles、骨骼／网格版本、构图和照明须一并冻结。

`workPose=raised-hammer-v1` 与 `sleeveFairing=rest-detail-v2` 分别标记较高的锤击准备路径、工具角速度约束、前臂参考系运输及肩袖凸脊修整。`visual-shoulder-motion.json` 使用当前实现；这会改变视觉和缓存开销，效率比较应冻结这些标签、源文件和相同场景配置。

`reference-figure-v25` 的袖管中心线补偿使用实际轴向站位，共享袖窿保留 4 厘米缝合过渡；`authored-bind-surface-v3` 使用重新导出的 schema 3 来源数据。`shoulder-elevation` 的正交尺寸为 1.38、目标为 (0, 1.24, 0)，覆盖肩部与举高手掌。几何表示和构图均已变化，比较效率或视觉代理前应重新冻结相同配置与版本。姿态几何导出用于定位变形，不能直接当作肌肉真实性或 RSI 得分。

`source-clavicle-v11` 使用共享中性上臂参考产生程序肘向，`reference-figure-v25` 以同一参考居中一百二十个肩袖修正。捕捉源文件与 `cmu-143-30-v1` 标识保持一致，姿态几何导出增加三段上肢的坐标轴列向量。姿态烘焙按左右臂预先整理有效约束边和边长，每个姿态的混合矩阵逆只计算一次，保持原有约束顺序与限值。模型基线需要重新冻结这些求解与形态版本，不能将参考系变化直接计为程序加速或 RSI 增益。

`reference-figure-v25`／`sleeveFairing=rest-detail-v2` 加入静止表面细节运输，姿态总数仍为 120；`source-clavicle-v11` 加入肘平面角速度与加速度约束。两者改变形态和动作时间响应；视觉／效率对照应同时固定这些版本、资产、输入序列与源码哈希。松弛在姿态缓存创建时执行，当前没有把模型质量变化计为 RSI 或程序加速收益。

`reference-contour-physique-v31` 的身体输入由 `body-profiles.json`、统一三轴比例、十份来源支撑资产及其固定哈希共同定义。躯干、上臂、肩部 A 姿势／绑定表面、连续裤装和前臂截面共享 `shapeRecipe`。官方 CC0 写实身体通过 41 个参考点转移到 HM08，形成两套成人参考；躯干取样按完整骨骼后代排除手臂与手掌，衣袖与逆绑定同步适配。前臂拟合保留原雕刻拓扑、手指和握柄位置，比较时必须同时冻结两套参考形体位移、来源清单、截面文件与拟合代码。

当前衣装按体型和款式缓存六组法线松量包络，腰带收褶与胸腹垂落使用衣片拟合后的腰带高度；中央锁骨衣片与三维肩面各自保留来源支撑。衣装使用实际缝合衣片截面、胸腹垂落、按款式组织的下摆、来源骨盆与上腿支撑、受支撑褶皱及裤裆局部松弛；裤装保持五骨绑定与靴口连接。`garmentMaterial=quiet-linen-v17` 标识静止表面坐标中的缝线，`residentLocomotion=ankle-offset-v4` 在踝关节求解后应用共同鞋楦的中心偏移。共同中性站姿、整体等比身高变化和近远景状态也属于比较条件。

这些变化影响轮廓、蒙皮和缓存构建成本。效率或视觉能力对照应固定相同源码、完整资产、居民身份、衣色、相机、灯光、姿态和 LOD；旧体型与旧图片不能只改标签混作同一输入。身体截面、工具接触与变形边界检查是项目回归约束，不是人体真实度、性别识别、医学或 RSI 评分，也不能把更换体型计为程序加速收益。

成人锁骨内端取自转移后体型的 `joint-l-clavicle`，再镜像到另一侧；`source-clavicle-v11` 将这一来源支点用于肩带弧线，避免较窄肩点继续绕固定旧支点旋转。肩胛旋转、腕部接触和衣袖边长约束继续独立验证。

`wristContour=continuous-source-v3`、`forearmProfile=curvature-flow-v9` 与 `forearmSkin=continuous-elbow-v9` 分别标记连体腕臂曲面、来源中心线拟合与腕部渐进蒙皮。`visual-wrist-motion.json` 在冻结模拟、固定 60 Hz 下，从手背、掌侧和侧面观察屈伸、侧偏、前臂旋转和握拳。比较接缝光照时使用同一视角、同一光照及帧号。冷启动比较需固定近手网格缓存状态。

`garmentFit=continuous-drape-v35`、`forearmProfile=curvature-flow-v9`、`faceContour=balanced-adult-v3`、`hairline=framed-source-root-v2` 与 `facialTiming=rested-phrase-blink-v2` 区分衣片拟合和姿态形态、女性前臂表面、成人脸部比例、额前发根和静止眼睑。视觉与效率比较须同时冻结这些标识、输入和源码。身体配方 `reference-contour-physique-v31` 的两种成人参考转移权重均为 1.0，五类身体支撑共享同一配方哈希。固定点、体积、截面与三角形检查用于几何回归。

`renderQuality.femaleGroom=scalp-guided-plait-v1` 标识成年女性的编发、盘发和配套远景几何。评测头部肖像或世界人物时，固定居民身份和观察距离，再比较轮廓与渲染耗时。

`bodyPresentation=continuous-drape-v29` 标识成人女性前领、小腿及鞋部的运行时成品拟合。比较像素和网格构建成本时，须与 `bodyFrame=reference-contour-physique-v31`、`garmentFit=continuous-drape-v35` 及源码哈希一并冻结。

`continuous-drape-v29` 的女性成人衣装还使用独立材质状态、浅色内衫／绿灰外衣分片，以及近远景共用的静止缝片函数。远景顶点颜色保留来源染色数据，另用材质标记选择分片；因此几何截面检查不会把配色变更当成体型变更。新材质、下摆与源码均需冻结后才能比较视觉或效率结果。

`continuous-drape-v31` 与 `continuous-drape-v29` 还记录前胸横向拟合、折痕分布、短门襟和缝线表现；来源身体为 v29。横向拟合只在成衣缓存创建时运行，但会影响首次构建成本与显示像素，效率比较应区分缓存构建和稳定回放。`CHEST_PANEL_PASS` 是最终三角面连续性与近远景一致性的回归检查，不计入 RSI 能力得分。

`continuous-drape-v29`／`continuous-drape-v31` 还覆盖女性肩背来源的运行时形体场与中性支撑重建；虽然上游 CC0 参考保持原状，衍生支撑使用 v29 配方，实际中性网格、姿态修正和缓存构建会改变。视觉及效率对照须冻结这些呈现标识和源码，不能仅按原始资产哈希判断输入相同。

`continuous-drape-v31`／`continuous-drape-v29` 还包括圆领开口、薄折边、下摆／背褶与窄缝线；它们改变成衣几何及像素，来源 v29 不能单独标识相同视觉输入。`NECK_COVERAGE_PASS` 属于成品连接回归约束，不作为形体美观、人体分类或 RSI 分数。

`continuous-drape-v29` 还覆盖女性连续颈部的运行时延展，虽然原始头部文件未变，实际头颈顶点及像素会改变。视觉比较不能只按原始文件哈希或面部区域的旧标识判断输入相同。

`continuous-drape-v29`／`continuous-drape-v31` 区分前胸皮肤采样与衣料凸包，并改变实际衣装曲面及上胸分片。`thorax-frames`／`visual-thorax-frames.json` 提供冻结模拟的成年衣装近景正面、三刻面和侧面；它是视觉输入及连接观察，不能把画面变化计为 RSI 能力或程序加速收益。来源检查同时覆盖 `skinRadii`、保留的 `radii` 与下游支持哈希。

当前前胸远景含局部共享边细分与邻面补齐，再执行表面转移及索引 LOD 重建；这会改变远景顶点／索引与冷缓存创建成本。比较效率时需冻结源码和成品版本，区分冷构建与稳定回放；本次形体变化本身不代表程序加速。

前胸外侧检查使用 `continuous-drape-v29`／`continuous-drape-v31`。运行时从 v31 来源采样构建局部向内重分配的支撑，来源文件哈希仍与原始身体对应；比较视觉输入必须同时固定形体与衣装标记。`BodyFramesOnly` 是原生几何回归入口，测量最终近远景前侧轮廓及来源间隙，不是 RSI 测试或审美评分。支撑求交只在缓存首次创建时执行；不能据此推断稳定帧率提升。

`continuous-drape-v29`／`continuous-drape-v31` 还包含腰腹／侧腰的独立修形、连续衣身分片和纵向衣料弯曲参数。即使上游来源资产未变，视觉及效率比较仍须固定这些运行时标记和源码；几何回归不计为 RSI 得分。

`continuous-drape-v29`／`continuous-drape-v31` 包含连续前胸体积与自适应纵向衣料滤波；它改变运行时支撑和最终显示像素，上游人体来源哈希不能单独标识相同视觉输入。`CHEST_VOLUME_PASS` 测量最终穿衣截面深度差，属于形体回归，不是人体体积积分、审美评分或 RSI 能力指标。

`continuous-drape-v29`／`continuous-drape-v31` 改变胸廓缓存中的前侧截面圆顺和衣料收束函数。`CHEST_CURVE_PASS` 是实际衣面等间距截线的二阶深度差回归，补充体积／宽度检查，不表示审美质量、医学曲率或 RSI 能力。视觉和效率比较仍需固定运行时标记、完整源码、资产及采样条件。

`continuous-elbow-v9`／`curvature-flow-v9`／`continuous-drape-v29` 分别标识肘部过渡、男女前臂表面与隐藏肘口拟合，以及肩袖／腰胯圆顺。近景每手从 25 骨增加到 56 骨，公共变换按每只手复用；远景维持静态代理策略。效率比较必须固定实际 LOD、近景人数、动作和缓存状态，不能将形体更新计为加速收益。`ELBOW_SKIN_PASS` 测量实际顶点连接和混合行列式，不是医学肌肉能力或 RSI 评分。

`continuous-elbow-v9` 在近手缓存 24 组位置／法线形态样本，`curvature-flow-v9` 合并重合位置的法线贡献；`continuous-drape-v31` 与 `continuous-drape-v29` 同时标记女性上臂衣料松量、肩背和腰胯圆顺。缓存按体型／左右手生成，近景增加存储及 GPU 形态混合，远景沿用静态代理。效率实验必须固定近景人数、细节等级、动作、冷／热缓存与源码，重新采样，不把更换形体表示算作程序加速。`FOREARM_NORMAL_PASS` 的角误差只测指定三角面及姿态，既不是医学肌肉验证，也不是 RSI 或审美评分；原始资产哈希未变仍不足以判定视觉输入相同。

`FOREARM_SHAPE_PASS` 测量实际形态混合后曲面的局部变形残差、有向面积及位移；静止细节与固定握持区另有约束。位置修正也会改变像素覆盖和近景混合成本，样本数与变形域改变后，不能沿用旧版效率基线。这些几何数据不是 RSI 能力或肌肉力量指标。

`upperArmFlex=stable-contour-v3`／`shoulderSkin=reference-figure-v25` 标识不含附加体积鼓包的 120 组肩袖基线。`upper-arm-flex` 是直接给定无负载关节角的固定肩位参考，不是握持 IK 或肌肉力学测试；实际游玩动作另由身体动态和工具交互回放验证。相位连续性、相邻顶点步进和游玩待机表面步进属于时序几何回归，不是 RSI 或审美分数。减少形态数量也会改变开销，效率实验须重新冻结源码、缓存及渲染条件。

`continuous-elbow-v9`／`curvature-flow-v9` 标识可见肘域的角度参数蒙皮及 24 组姿态形态，替代窄轴向坡产生的折叠。`elbow-joint`／`visual-elbow-joint.json` 为公开近景。`ELBOW_SURFACE_METRICS` 同时核对真实索引三角面、有限差分 Jacobian、径向误差及开口连接；`GAMEPLAY_ELBOW_PASS` 另在实际 IK 动作过渡中检查肘域三角面朝向；这些是时序几何输入的有效性条件，不是 RSI 或审美分数。近景缓存和混合成本变化，需要重采相同条件的性能基线，不能把表示方式变化算作程序加速。

`continuous-drape-v29`／`continuous-drape-v31`／`reference-figure-v25` 区分法线细节运输、局部圆顺回退、肩袖邻域方向及独立衣片颜色坐标。姿态形态数量未增加，但烘焙、静态缓存及像素输入会改变；效率实验必须重新冻结源码与渲染版本。`REST_NORMAL_FLOW_PASS` 与颜色坐标检查仅是表面表示的回归条件，不作为 RSI 或审美分数。

`continuous-drape-v29`／`continuous-drape-v31`／`reference-figure-v25` 标识来源支撑约束的肩袖几何重整、接触净空与固定绑定面质量筛选。`curvature-flow-v9`／`continuous-elbow-v9` 标识前臂剖面的五次接续。成人静态远景从新的近景静止衣面生成，屏幕索引 LOD、构建与绘制输入随之改变。`ARMHOLE_REST_FLOW`、`ARMHOLE_MOTION_FLOW_PASS`、`BODY_LOD_SURFACE_PASS` 与 `FOREARM_PROFILE_FLOW_PASS` 均是表示和几何回归，不是 RSI 分数；不能将更换网格表示直接当作加速收益。

当前 `continuous-drape-v29`／`continuous-drape-v31`／`reference-figure-v25` 增加上臂来源曲面连续取样和女性颈肩的零端曲率接续，`continuous-elbow-v9` 使用小角度敏感的蒙皮更新。姿态数量不变，但近静止时的更新频率可能增加；效率比较需冻结源码、LOD、人数、缓存、动作及采样方式重新测量，不沿用旧版性能数字。`SKIN_MICRO_MOTION_METRICS` 只测特定连接探针与法线角在慢速腕／肘转动中的同步性，`UPPER_ARM_SOURCE_FLOW_PASS` 只测来源范围、接缝与切线接续；它们属于视觉实验的几何有效性约束，不是 RSI 或审美分数。

`reference-contour-physique-v31` 重建十份共同来源支撑，`continuous-drape-v29`／`continuous-drape-v31`／`reference-figure-v25` 标识新的成品形体与衣料跨度、染色和肩袖缓存。肩、手、骨架和 HTTP 协议保持各自合同；视觉输入、冷启动构建及缓存会改变，效率对照必须重新冻结源码、资产、动作、人数、LOD 与采样条件。体型宽深、衣料接续和来源 Jacobian 的回归结果只用于确认输入有效，不作为 RSI 或吸引力评分。

`femaleLowerPanel=supported-fall-v1` 标识三款女性衣装的腰下曲面、后褶和沿布面测得的下摆翻边距离。`visual-female-wardrobe` 以 0.66 米正交尺度从前侧和后侧观察同一居民的三套成衣。图册收起手持工具，以便比较衣片轮廓。镜头、衣料曲面和缝线参数会改变像素及构建成本，跨版本对照需使用相同的标识与参数。

`reference-contour-physique-v31`／`continuous-drape-v29`／`continuous-drape-v31`／`reference-figure-v25` 同时区分腰侧、小腹、后腰／臀腿轮廓、较低的前胸重分配和女性衣摆／裤装染色。来源 Jacobian 与位移预算不变；原生身材、上肢和微动作测试仍分别执行。这些是视觉输入身份及几何约束，不是 RSI 或审美评分；性能实验需要重新冻结当前完整输入。

自然腰线拟合使用相同来源支撑的局部截面，按三种衣装逆解并缓存腰带位置；衣料收褶、工具挂点及视觉输入随之变化。`NATURAL_WAIST_BELT_PASS` 检查成品腰带的中心和宽度，不是人体或效率评分；旧版固定腰带的回放不作为当前基线。

`neckContour=relaxed-neckline-v7` 与 `garmentFit=continuous-drape-v33` 标识女性颈根、按角距圆顺的领缘、锁骨衣面和重新生成的肩袖形态。来源体型为 `reference-contour-physique-v31`。比较视觉输入与构建效率时须固定成衣标识、源码、动作、LOD 和机位。颈部间隙与颈根覆盖是几何回归条件。

`trouserSkin=guided-knee-v1` 标识女性下装的臀腿轮廓、膝部支撑骨与配套姿态修形。源网格哈希仍对应 `reference-contour-physique-v31`。比较此版画面与耗时需要同时固定运行时蒙皮版本、近远景状态和动作。`handling-transitions` 左列镜头随实际骨盆高度收近，图像对照也须固定机位逻辑。

`garmentFit=continuous-drape-v34` 记录三款女性衣片的局部下摆高度、腰带以下的方向性布量和连续翻边。`garmentMaterial=quiet-linen-v18` 记录近远景共用的衣缘距离与缝线过滤。视觉输入或耗时比较须固定剪裁、机位与细节等级。

`garmentFit=continuous-drape-v35` 增加女性成品前片的横纵截面拟合与腰上收褶调整。几何求解发生在首次构建成衣缓存时。冷启动测量需固定款式缓存状态，稳定回放需固定动作、距离和细节等级。`FEMALE_CROSS_DRAPE_PASS` 属于成品几何回归。

衣面烘焙将规范坐标中的候选邻域按剪裁与模式缓存，当前姿态继续计算动态筛选和权重；完整扫描与缓存路径按原索引顺序求和。`BODICE_NEIGHBOR_PASS` 核对实际拟合深度逐位一致，属于程序优化的数值等价检查。缓存影响冷启动成本与内存，效率实验需固定来源、姿态数量、硬件、并行负载与冷／热缓存条件；超时回放保留失败状态，不能视为有效结果。

`portrait.resident` 在 `person`／`person-hands` 中按十进制正整数的稳定身份选择当前世界中的存活居民，取值范围为 Int64 正数；不存在的身份返回运行失败。选择复用名录入口，不修改模拟。结果 `state.portrait.identity`／`child` 记录实际观察对象，校验器核对命令后的身份，拒绝选择未执行或被改写的记录。公开 `visual-female-portrait.json` 使用种子 839102、8 名居民、零模拟步进，观察身份 4294967299，并切换肩颈／全身构图。该入口是视觉回放控制，不增加玩家任务或 HTTP 动作。

`motionStyle=captured-standing-and-stride-v7` 和 `actionReference=cmu-standing-phrases-v1` 标识成人动作与真人片段。`handlingMotion=supported-parcel-v19` 标识拾取和饮水等交互的衔接，`socialMotion=paired-conversation-v11` 标识交谈，`workEffort=overlapping-weight-transfer-v7` 标识蓄力和释放之间的承重重叠。`CAPTURED_PICKUP_PASS` 核对实际髋部回升、空手动作和脚底滑移。视觉或耗时比较须冻结姿态版本、身体、工具、镜头、步进和缓存条件。

`habitatDetail=responsive-understory-v12` 增加四候选的林下层、矮阔叶及最近八角色的草叶接触场，改变顶点着色负载与自然物放置缓存身份。`VEGETATION_CONTACT_PASS` 验证接触选择、上传、清除与模拟观察纯度；放置检查验证实际根部占用及缓存恢复。视觉／效率比较须冻结草丛数量、镜头、角色分布、源码与冷／热缓存条件，不能将植被负载变化解释为内核加速或 RSI 改进。

`sceneryContact=rooted-resource-anchors-v1` 改变树的水平挂点、坡地岩石放置、资源朝向与近景碎片数量，输入源码、像素和自然物冷构建成本均改变。`world-scenery-contacts` 提供两日预运行的实际场景；`TREE_ROOT_ANCHOR_PASS`／`SCENERY_SUPPORT_PASS` 是来源、接地、类型／耗尽回退及观察纯度检查，不是完整物理碰撞或 RSI 得分。比较效率须冻结场景、实例数量与冷／热缓存，不能直接沿用旧画面性能数字。

`sceneryContact=supported-shore-activity-v2`、`landscapeSurface=wet-bank-mosaic-v7`、`waterSurface=shore-activity-v7` 增加实际岸线碎砾、湿润沉积、水岸朝向与最多八个有限寿命的饮水活动脉冲。动物的水平到达判定也改变低头时机，岸边显示站位依实际足部跨度在同格内调整；无法容纳的窄岸另行记录。`SHORE_INTERACTION_PASS`／`ANIMAL_BANK_PASS` 属于显示与模拟观察纯度回归，不是物理接触、审美或 RSI 得分；比较效率需冻结岸线、碎砾数量、饮水状态、镜头、完整源码和冷／热缓存条件。

`sceneryContact=solid-scenery-corridors-v3` 更新树石落点、坡面碎石带、人物和动物的局部显示步以及采集站位。`SCENERY_TRAVERSAL_PASS` 覆盖实测场景的通路净空、地面支撑和逐帧到达。公开 `world-scenery-contacts` 场景先预运行两日，随后每帧推进一个模拟步。比较耗时需要使用这一场景版本，并固定逻辑步进、实例分布和冷／热缓存条件。
