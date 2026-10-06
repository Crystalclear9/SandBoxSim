# 河山 · SandBoxSim

河山是一款以自然生态与居民自主行为为核心的 3D 沙盒模拟游戏。玩家塑造地形、水源和资源，规划土地与聚落；居民依据需求、材料与可达性选择生存、劳动、建造和迁移。人物档案、世界事件与每日曲线帮助观察这些选择的后果。

![自由探索与实际游戏画面](docs/images/free-world-ui.png)

## 安装与开始游玩

源码运行环境为 **PowerShell 7、.NET 8 SDK、Godot 4.7.2 .NET**。从仓库根目录执行：

```powershell
./tools/install-sdk.ps1
./tools/setup-godot.ps1
./tools/godot.ps1 -Mode run
```

Windows 配置完成后可以双击 [Play-3D.cmd](Play-3D.cmd)。Linux/macOS 根据安装脚本输出设置 `GODOT_EXE`，再运行启动脚本。已有环境、工具链路径和常见问题见 [安装与启动](docs/guides/getting-started.md)。

世界没有必须完成的目标，可以自由移动镜头、查看居民或改变土地。**左键拖动平移、右键拖动旋转与俯仰、滚轮缩放**；单击选择或确认落点。底部“生命”“地貌”“气候”直接展开自由工具；没有委托、工程流程、目标或干预额度。完整操作见 [玩家指南](docs/guides/player-guide.md)。

## 当前游戏内容

| 内容 | 可以做什么 |
|---|---|
| 自然与生态 | 改变地形、水土和资源，观察天气、火灾与动物的相互影响 |
| 自主居民 | 观察生存、采集、劳动、建造、迁移及家庭与人生记录 |
| 荒野探索 | 寻找花甸、苇泽、倒木林隙、泉眼、野果地、古树林、遗迹和矿脉，按自己的喜好命名地点并观察变化 |
| 观察与保存 | 使用图层、人物档案、历史和曲线；保存世界与会话进度，记录实验起点 |

人物页可旋转、缩放肖像，并切换面部与全身；握持、拿取和搬运跟随居民实际行为。人物使用连续手部蒙皮、独立指节和贴合面部的眼睑，模型与配件说明见 [3D 模型系统](docs/development/models.md)。

顶部食物、木材与石料表示已采集储备，包括居民背包、地面物资堆和仓库。地表节点仍需要采集；库存总量也不保证每位居民都能到达物资。

## 文档入口

- **开始使用**：[安装与启动](docs/guides/getting-started.md)、[玩家指南](docs/guides/player-guide.md)、[故障排查](docs/guides/troubleshooting.md)。
- **理解玩法**：[自由探索](docs/guides/exploration.md)、[世界系统](docs/guides/world-systems.md)、[聚落营造](docs/guides/settlements.md)、[生态工坊](docs/guides/ecology.md)。
- **继续开发**：[开发指南](docs/development/developer-guide.md)、[项目架构](docs/development/architecture.md)、[扩展指南](docs/development/extending.md)。
- **维护项目**：[目录与文件管理](docs/development/repository-layout.md)、[构建与运行](docs/development/build.md)、[配置](docs/development/configuration.md)、[存档](docs/development/saving.md)。
- **查看更新**：[更新记录](CHANGELOG.md)、[版本更新与兼容](docs/guides/updating.md)。

所有主题见 [文档导航](docs/README.md)。历史设计与阶段记录独立保存在 `docs/archive/`。

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
├─ config/              默认模拟参数
├─ tools/               安装、构建、运行、测试与文档检查脚本
├─ docs/
│  ├─ guides/           安装、游玩、排错和版本更新
│  ├─ development/      架构、扩展、构建、配置、存档与素材
│  ├─ images/           正式文档配图，保留并提交
│  └─ archive/          原始要求、设计与历史记录
├─ .github/workflows/   核心与图形持续集成
├─ artifacts/           本机构建输出，忽略提交
└─ runs/                日志、模拟数据和临时截图，忽略提交
```

游戏纹理、插画和配方分别位于 `src/SandBoxSim.Godot/assets/textures/`、`illustrations/`、`gameplay/`。开发期间的临时截图放在 `runs/screenshots/`；清理临时截图时保留文档配图、游戏素材和存档。详细约定见 [文件管理](docs/development/repository-layout.md)。

贡献约定见 [CONTRIBUTING.md](CONTRIBUTING.md)，代码授权见 [LICENSE](LICENSE)。

房屋木作、人物衣装、四足动物和植被模型的结构说明与近景图见 [3D 模型系统](docs/development/models.md)。
