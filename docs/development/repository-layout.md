# 目录与文件管理

项目按模拟、图形、命令行和测试四个工程分层。文档按使用与开发分组；正式图片、素材和运行输出各有固定位置。

## 目录职责

| 位置 | 内容 | 提交约定 |
|---|---|---|
| `src/SandBoxSim.Core/` | 世界、实体、AI、系统、存档与确定性 | 源码与工程文件 |
| `src/SandBoxSim.Console/` | TUI、CLI、统计、报告与 PNG 输出 | 源码与工程文件 |
| `src/SandBoxSim.Godot/` | 场景、镜头、HUD、展示模型与会话 | 源码、`.uid`、场景与工程设置 |
| `src/SandBoxSim.Tests/` | 自带测试框架与规则回归 | 测试源码与工程文件 |
| `config/` | 模拟默认参数 | 可移植配置，避免本机路径 |
| `benchmarks/` | 版本化场景与请求 Schema | 公开配置与接口说明；结果保存在 `runs/evaluation/` |
| `tools/` | 安装、构建、运行、评测、测试与文档检查 | 脚本，PowerShell 使用 UTF-8 BOM |
| `docs/guides/` | 安装、游玩、排错与更新 | 当前使用文档 |
| `docs/development/` | 架构、扩展、文件管理与维护 | 当前开发文档 |
| `docs/images/` | 当前玩法、界面与模型文档使用的正式实机配图 | 保留并提交 |
| `docs/archive/images/` | 历史界面与设计记录的正式配图 | 保留出处并提交 |
| `docs/archive/` | 原始任务、设计、历史记录与配图 | 保留出处；不代替当前说明 |
| `runs/` | 本机模拟输出、日志、临时截图 | 除 `README.md` 与 `.gitkeep` 外忽略 |
| `artifacts/`、`bin/`、`obj/`、`.godot/` | 构建与导入缓存 | 忽略提交 |

根目录保留项目入口、解决方案、统一配置、授权、贡献指南与更新记录。SDK 和 Godot 安装在用户主目录的 `.sandboxsim-tool/`，不放进仓库。

## 图形资源

- `assets/textures/`：地形、对象与自然材质图集。
- `assets/illustrations/`：历史工程插画，当前客户端不使用。
- `assets/gameplay/`：可验证的 JSON 配方。

资源 PNG 和相应 `.import` 描述文件一起提交；Godot 自动生成的 `.godot/` 缓存不提交。模型代码文件与 `.uid` 一起维护，避免移动源码后出现失效脚本引用。来源和导入细节见 [美术资源](assets.md)。

人物表现按职责分在 `ResidentRig.cs`（行为状态）、`ResidentRig.HandPose.cs`（肘腕和指节）、`NatureModels.HandSkin.cs`（手部蒙皮）、`NatureModels.Articulation.cs`（肩部蒙皮）与 `NatureModels.Face.cs`（面部表面）。这些文件共用人物节点与网格缓存，地图与肖像读取同一套模型，不维护额外的展示替身。入口见 [模型系统](models.md)。

建筑表现同步分在 `WorldView3D.Buildings.cs`，CPU 网格合并分在 `NatureModels.MeshBatching.cs`，评测区段采样分在 `RenderProfile.cs`。性能操作与缓存边界见 [性能采样与渲染维护](performance.md)。

## 运行资料与截图

| 资料 | 建议位置 | 清理时的处理 |
|---|---|---|
| 临时查看、调试用的截图 | `runs/screenshots/` | 按明确文件名清理 |
| 故障与性能日志 | `runs/logs/` | 保留需要定位问题的记录 |
| 独立模拟实验 | `runs/<实验名>/` | 先确认是否需要保留存档、统计和对照数据 |
| 玩家存档 | 文件对话框所选位置 | 更新或截图清理时保留 |
| 正式文档配图 | `docs/images/`、`docs/archive/images/` | 随文档保留 |
| 游戏纹理与插画 | `src/SandBoxSim.Godot/assets/` | 随游戏保留 |

目录被 Git 忽略并不代表可以整体删除。`runs/` 中可能包含存档、实验和诊断数据。清理临时截图不会删除文档图或游戏素材，也不使用针对整个仓库的 PNG 通配删除。

## 移动和新增文件

移动前确认所有相关引用：Markdown 的相对链接、源码的 `res://` 路径、场景脚本、导入文件及脚本注释中的文档地址。源码搬迁还要检查工程的编译规则和 Godot `.uid`。

文档移动后运行：

```powershell
./tools/check-docs.ps1
git diff --check
```

检查器核对当前 Git 文件与未忽略的新 Markdown 文件中的本地链接，包含 Windows 上容易漏掉的大小写错误。资源移动后通过 `tools/godot.ps1 -Mode test` 重新构建、导入并运行客户端自检。

## 文档维护

`README.md` 是项目概览，`docs/README.md` 是阅读导航，`CHANGELOG.md` 是用户和维护者的更新记录。功能行为写入对应指南；配置和存档变化写入开发文档。旧界面配图和仅供历史记录引用的图片归入 `docs/archive/images/`，同时更新归档文档链接；保留文件内容，不用旧流程配图介绍当前玩法。历史计划留在 `docs/archive/`，不在当前主页重复放置阶段验收说明。
