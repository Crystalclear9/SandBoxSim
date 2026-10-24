# SandBoxSim

SandBoxSim 是一款以自然生态与居民自主行为为核心的 3D 沙盒模拟游戏。玩家塑造地形、水源和资源，规划土地与聚落；居民依据需求、材料与可达性选择生存、劳动、建造和迁移。人物档案、世界事件与每日曲线帮助观察这些选择的后果。

项目也提供独立的 [在线控制与效率实验接口](docs/development/online-efficiency.md)：HTTP 固定步进、正确性约束下的配对性能比较，以及固定预算/控制代理下的多轮自我改进证据验证。研究模式不改变自由探索玩法。

[真实沙盒源码研究](docs/development/sandbox-research.md) 让代理检查真实工程、提交源码实验，经编译与语义检查后在参考/候选世界中验证；完整过程和失败候选均归档。[冻结研究程序对照](docs/development/sandbox-research-comparison.md) 在相同真实世界中比较父、子与撤销方法组，记录失败和全流程成本。

程序加速与代理改进能力分别评测。[代理改进协议](docs/development/rsi-improver.md) 实际执行代理自修改，并比较父/子产生后代的增益、冻结代理和只撤销自修改方法的对照；多轮程序更快不再直接标记为 RSI。

[重复真实研究对照](docs/development/sandbox-research-series.md) 轮换父/子/撤销组顺序，以完整研究运行作为不确定性单位，并检查每个世界的退步和全部失败成本。

![当前界面与人物观察的实际游戏画面](docs/images/ui-detail-refined.png)

人物观察窗使用独立布光、两列需求卡片和固定跟随/关注操作区，可直接切换上一位、下一位居民。底部工具栏集中提供面部、手部、全身聚焦，正面、三刻、背面视角及缩放；也可鼠标拖动旋转、滚轮缩放、双击复位。观察和界面操作不会给世界添加任务流程。

观察提示卡会避让手记侧栏，创造工具库展开时收起；镜头组显示俯角、距离和可切换的跟随状态。近景住房保留厚茅草檐口、圆弧覆脊与贴合束带，河岸芦苇按实际地面高度和坡度选点。

场景水波的位移与法线保持同相位，浅岸逐渐减弱；植物保留双面光照和根梢层次。芦苇采用分枝花序，野莓具备果粒与萼片，精细窗台具有坡面和滴水槽。人物短睫毛随实际眼睑闭合，鹿狼增加耳内短毛与眼部材质层次；界面使用清楚的时间状态、工具标题和用途层级。来源与实现取舍见[美术资源](docs/development/assets.md)。

花甸采用带曲面花瓣、萼片、花丝与花药的草花，每株独立贴地，并随土地压实更新根部高度。近景木作和石作用实际尺寸控制倒角，植物风摆同步修正表面方向；地表湿度参与干草色分布。人物皮肤区分鼻额、唇周与耳部，鹿狼保留贴合口鼻的短触须及不同鼻镜、蹄部响应。观察卡支持居民、动物与土地，可直接靠近或打开对应档案、手记。

## 安装与开始游玩

Windows 本机已有独立导出包时，双击 [Play-3D.cmd](Play-3D.cmd) 即可游玩，不需要开发工具链。启动器优先使用 `artifacts/game/windows/` 中的完整游戏包；发行文件不提交到源码仓库。包的导出和更新见 [构建与运行](docs/development/build.md)，工具链与临时文件的管理见 [本机存储维护](docs/development/local-storage.md)。

源码运行环境为 **PowerShell 7、.NET 8 SDK、Godot 4.7.2 .NET**。从仓库根目录执行：

```powershell
./tools/install-sdk.ps1
./tools/setup-godot.ps1
./tools/godot.ps1 -Mode run
```

工具默认安装在当前项目 `.tools/` 内，SDK、引擎、NuGet 缓存及可选 Blender 均不提交到 Git；不修改系统安装。

Windows 配置完成后可以双击 [Play-3D.cmd](Play-3D.cmd)。Linux/macOS 根据安装脚本输出设置 `GODOT_EXE`，再运行启动脚本。已有环境、工具链路径和常见问题见 [安装与启动](docs/guides/getting-started.md)。

世界没有必须完成的目标，可以自由移动镜头、查看居民或改变土地。**左键拖动平移、右键拖动旋转与俯仰、滚轮缩放**；单击选择或确认落点，观察模式双击靠近；滚轮围绕指针落点缩放，悬停人物和动物显示简短信息。底部“创造”展开左侧可搜索工具库，参数与工具列表集中显示；没有委托、工程流程、目标或干预额度。完整操作见 [玩家指南](docs/guides/player-guide.md)。

## 当前游戏内容

| 内容 | 可以做什么 |
|---|---|
| 自由栽植 | 栽植花甸、野果、苇泽和腐木，组合授粉、涵养、育土与采集，观察受水土与踩踏影响的渐进恢复 |
| 自然与生态 | 改变地形、水土和资源，观察天气、火灾、动物与居民往返逐渐留下的小径和土地压实 |
| 自主居民 | 观察生存、采集、劳动、建造、迁移及家庭与人生记录 |
| 生活经济 | 观察三类作物的周期、耕作消耗与休耕恢复、地面食物腐损及仓储保护，见 [农业与食物流动](docs/guides/living-economy.md) |
| 聚落生活 | 近看真实住户对应的晾晒、长凳和陶器，观察房屋完整度与空置风化；地面物资随库存变化 |
| 荒野探索 | 寻找花甸、苇泽、倒木林隙、泉眼、野果地、古树林、遗迹和矿脉，按自己的喜好命名地点并观察变化 |
| 观察与保存 | 使用图层、人物档案、历史和曲线；保存世界与会话进度，记录实验起点 |

人物页可旋转、俯仰和缩放肖像，并切换面部、手部与全身；握持、拿取、交谈和搬运跟随居民实际行为，世界角色平滑转向并对脚下坡面作小幅补偿。人物使用连续手部蒙皮、独立指节和贴合面部的眼睑，模型与配件说明见 [3D 模型系统](docs/development/models.md)。

人物肘部与前臂使用同源解剖网格，鞋楦与鞋带按实际表面对齐。近景上衣保留三种剪裁与原有动作；头脸已更换为 MakeHuman CC0 拓扑与原生 2K 皮肤图集，三套形体配方接入连续眼睑闭合和独立口部动作，儿童另有比例与无胡茬材质。近景采用透明发片，远景从同一头颈与眼口减面。鹿、狼已换为 CDmir / TinyWorlds 与 0 A.D. 的授权贴图网格，保留源 UV 与皮毛，接入鹿 14 / 狼 15 关节、独立下颌、坡面接地和屏幕 LOD；具体许可随资产提供。建筑同步入口尺度，茅草坡屋面采用带厚度与参差檐口的连续结构。离线重建与当前表现边界见 [模型说明](docs/development/models.md)。

UI 使用与自然场景统一的岩灰、苔色和旧铜色强调，顶部世界、资源和菜单各自悬浮，底部时间、模式与镜头控制分组，保留更多世界画面。左侧工具库支持 Ctrl / ⌘ K 跨分类搜索、固定分类导航、带用途说明与勾选状态的单列工具及带拖动条的固定参数区，图标底座、标题字重和铜色选中边线统一呈现；Alt＋左键平移，Shift＋滚轮调整常规笔刷半径，切换场景先确认。人物肖像可展开为独立观察窗，左侧旋转、俯仰、缩放和切换面部/手部/全身，右侧同步显示真实行动、身体需求与分组物资，并可直接关注居民或返回地图跟随。手记入口与当前观察模式同步显示状态；近景人物眉毛、头发及动物源耳廓保留实际表面，三种衣装使用收紧胸肩的连续蒙皮，裤腿、靴筒按大腿、小腿与踝部重新收束；皮肤使用原生颜色细节，衣料使用扫描亚麻织纹、法线与粗糙度。建筑、人物与动物共享可复用几何和材质，远景使用 LOD；当前表现与操作说明见 [观察与界面](docs/guides/gameplay-observation.md)。

地形使用连续细分曲面，扫描草叶落叶地面、泥土、林地与岩面按坡度、水分和踩踏混合，颜色与法线同步作稳定旋转，陡坡保留有界岩面起伏；河水按实际河床深度变化。树木在格内错位分布，住房与仓库的石基随坡地贴合。来源、许可及离线素材校验见 [环境素材](docs/development/environment-assets.md)，地表状态与显示规则见 [地形与活动痕迹](docs/guides/terrain-and-traces.md)。当前人物衣身、房屋及地形仍包含程序构造，动物源素材也保留年代与分辨率限制，扫描表面不代表全部模型已经写实。

顶部食物、木材与石料表示已采集储备，包括居民背包、地面物资堆和仓库。地表节点仍需要采集；库存总量也不保证每位居民都能到达物资。

## 实验与 benchmark 接口

可通过独立评测模式运行固定场景，采集墙钟帧耗时与状态摘要，生成无标题的模型图像、视觉问题及答案，并回放人物界面操作。正常游戏仍为自由探索。运行方法、JSON 协议、模型预测格式和研究边界见 [评测接口与实验运行](docs/development/benchmark.md)，公开样例见 [评测场景](benchmarks/README.md)。代理实验提供有界策略选择、实际 Python 候选代码和 HTTP 程序优化三种后端，均保留迁移、失败与控制证据。这些接口用于可复现的开发实验，不代表已经验证 RSI 能力。重复采样与渲染缓存说明见 [性能维护](docs/development/performance.md)。[连续世界实验](docs/development/stateful-rsi.md) 进一步检验库存变化、道路变更、实体世代与旧能力保留，并输出可展开的因果证据报告。[改进器效率研究](docs/development/rsi-efficiency.md) 比较实际时间预算、后代增量收益率与部署成本回收，明确拒绝无法确认的收益。

## 文档入口

- **开始使用**：[安装与启动](docs/guides/getting-started.md)、[玩家指南](docs/guides/player-guide.md)、[故障排查](docs/guides/troubleshooting.md)。
- **理解玩法**：[自由探索](docs/guides/exploration.md)、[世界系统](docs/guides/world-systems.md)、[聚落与居民](docs/guides/settlements.md)、[天气与生态](docs/guides/ecology.md)。
- **继续开发**：[开发指南](docs/development/developer-guide.md)、[项目架构](docs/development/architecture.md)、[扩展指南](docs/development/extending.md)。
- **维护项目**：[目录与文件管理](docs/development/repository-layout.md)、[构建与运行](docs/development/build.md)、[配置](docs/development/configuration.md)、[存档](docs/development/saving.md)。
- **查看更新**：[更新记录](CHANGELOG.md)、[版本更新与兼容](docs/guides/updating.md)。

源码入口见 [src](src/README.md)，工具入口见 [tools](tools/README.md)，公开实验与协议见 [benchmarks](benchmarks/README.md)。所有主题见 [文档导航](docs/README.md)。原始任务书与素材来源保存在 `docs/archive/`。

## 开发入口

```powershell
# 内核、命令行与测试工程
./tools/build.ps1 -Mode build -Configuration Release -Channel sdk -ParallelBuild

# 图形客户端：构建或打开编辑器
./tools/godot.ps1 -Mode build -Configuration Release
./tools/godot.ps1 -Mode editor

# 检查文档文件链接与大小写
./tools/check-docs.ps1
```

`SandBoxSim.sln` 包含 Core、Console 和 Tests；Godot 工程通过 `tools/godot.ps1` 单独构建。测试使用项目自带运行器，入口是 `tools/test.ps1`。逐项操作及验证选择见 [开发指南](docs/development/developer-guide.md)。

## 仓库结构

```text
SandBoxSim/
├─ src/                 Core、Console、Godot 与 Tests 四个工程
├─ config/              默认模拟参数与配置说明
├─ benchmarks/          公开场景、代理夹具、Schema 与 OpenAPI
├─ tools/               安装、构建、运行、测试与文档检查脚本
├─ docs/
│  ├─ guides/           安装、游玩、排错和版本更新
│  ├─ development/      架构、扩展、构建、配置、存档与素材
│  ├─ images/           正式文档配图，保留并提交
│  └─ archive/          原始任务书与素材生成来源
├─ .github/workflows/   核心与图形持续集成
├─ .tools/              本机工具链、依赖、原始素材和下载缓存，忽略提交
├─ artifacts/           本机构建输出，忽略提交
└─ runs/                日志、模拟数据和临时截图，忽略提交
```

游戏资源位于 `src/SandBoxSim.Godot/assets/`：`models/` 保存衍生网格与对应贴图，`materials/scanned/` 保存扫描表面及来源清单，`textures/` 保存仍需保留的材质与地图图集。旧插画、配方目录已移除。开发期间的临时截图放在 `runs/screenshots/`；清理时保留正式文档配图、游戏素材和存档。详细约定见 [文件管理](docs/development/repository-layout.md)。

贡献约定见 [CONTRIBUTING.md](CONTRIBUTING.md)，代码授权见 [LICENSE](LICENSE)。
