# 河山 · SandBoxSim

河山是一款以自然生态与居民自主行为为核心的 3D 沙盒模拟游戏。玩家塑造地形、水源和资源，规划土地与聚落；居民根据需求、材料和可达性选择生存、劳动、建造与迁移。世界通过人物经历、事件历史和每日曲线呈现这些选择的后果。

![聚落营造与实际游戏画面](docs/images/village-construction.png)

## 开始使用

环境要求：PowerShell 7、.NET 8 SDK、Godot 4.7.2 .NET。

```powershell
./tools/install-sdk.ps1
./tools/setup-godot.ps1
./tools/godot.ps1 -Mode run
```

Windows 配置好环境后，可双击根目录的 **Play-3D.cmd**。其他平台通过 `GODOT_EXE` 或 `-GodotPath` 指定引擎路径。安装位置与故障处理见 [安装与启动](docs/getting-started.md)。

观察模式下，**左键拖动平移、右键拖动旋转与俯仰、滚轮缩放**；单击选择对象或确认落点。先暂停世界，查看现场提醒，再选择干预、土地工程或聚落建造。

## 游戏内容

- **自然世界**：地形、水土、资源再生、天气、火灾与野生动物相互影响。
- **自主居民**：需求和行动成本驱动采集、劳动、休息、社交、建造和迁移；人物档案连接家庭、住所与人生事件。
- **生态经营**：六种土地工程支持分阶段建设与自然、维护、资源优先三种管理方式。
- **聚落营造**：指定住房、仓库、农田和矿场落点，使用真实材料施工；房屋有五种体量组合，朝向与布局响应周边条件。
- **经营目标**：河畔农庄、林间驿站、集市小镇蓝图追踪真实发展；旱季、火情、疫病试炼提供有限预算与危机。
- **观察与保存**：地图图层、人物检查、世界历史和曲线帮助理解因果；存档与实验回溯保留世界及会话状态。

## 项目文档

| 需要了解 | 文档 |
|---|---|
| 完整阅读入口 | [文档导航](docs/README.md) |
| 安装、启动与环境设置 | [安装与启动](docs/getting-started.md) |
| 操作、工具、试炼与保存 | [玩家指南](docs/player-guide.md) |
| 资源、居民、生态与文明 | [世界系统](docs/world-systems.md) |
| 建造、房屋、经营目标 | [聚落营造](docs/settlements.md) |
| 持续土地管理与配方扩展 | [生态工坊](docs/ecology.md) |
| 源码分层与系统边界 | [项目架构](docs/architecture.md) |
| 构建、测试与命令行模拟 | [构建与运行](docs/build.md) |
| 参数和存档规则 | [配置](docs/configuration.md)、[存档与确定性](docs/saving.md) |
| 界面和素材 | [观察与界面](docs/gameplay-observation.md)、[美术资源](docs/assets.md) |

## 仓库结构

```text
SandBoxSim/
├─ src/                 模拟内核、命令行客户端、Godot 客户端与测试
├─ config/              默认模拟配置
├─ tools/               安装、构建、运行和测试脚本
├─ docs/                项目说明、玩法和技术文档
│  ├─ images/           文档使用的游戏截图
│  └─ archive/          历史设计、计划和阶段记录
├─ .github/workflows/   跨平台持续集成
├─ artifacts/           本机构建输出，忽略提交
└─ runs/                本机模拟数据与日志，忽略提交
```

Godot 资源按 `assets/textures`、`assets/illustrations`、`assets/gameplay` 分类。SDK 与引擎安装在仓库外；核心内核、命令行客户端和测试可独立于 Godot 构建。贡献约定见 [CONTRIBUTING.md](CONTRIBUTING.md)，授权条款见 [LICENSE](LICENSE)。
