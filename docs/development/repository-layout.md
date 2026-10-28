# 目录与文件管理

项目按模拟、图形、命令行和测试四个工程分层。文档按使用与开发分组；正式图片、素材和运行输出各有固定位置。

## 目录职责

| 位置 | 内容 | 提交约定 |
|---|---|---|
| `src/SandBoxSim.Core/` | 世界、实体、AI、系统、存档与确定性 | 源码与工程文件 |
| `src/SandBoxSim.Console/` | TUI、CLI、统计、报告、PNG 输出与 `Online/` HTTP 步进服务 | 源码与工程文件 |
| `src/SandBoxSim.Godot/` | 场景、镜头、HUD、展示模型与会话 | 源码、`.uid`、场景与工程设置 |
| `src/SandBoxSim.Tests/` | 自带测试框架与规则回归 | 测试源码与工程文件 |
| `config/` | 模拟默认参数 | 可移植配置，避免本机路径 |
| `benchmarks/` | 渲染/效率场景、Schema 与在线 OpenAPI | 公开配置与接口说明；结果保存在 `runs/evaluation/` |
| `tools/` | 安装、构建、运行、评测、测试与文档检查 | 脚本，使用 UTF-8 和 PowerShell 7 |
| `docs/guides/` | 安装、游玩、排错与更新 | 当前使用文档 |
| `docs/development/` | 架构、扩展、文件管理与维护 | 当前开发文档 |
| `docs/images/` | 当前玩法、界面与模型文档使用的正式实机配图 | 保留并提交 |
| `docs/archive/` | 原始任务书与素材生成来源 | 保留出处；不代替当前说明 |
| `.tools/` | 当前项目的 SDK、Godot、可选 Blender、依赖及原始素材缓存 | 忽略提交；临时试版不混入正式资源 |
| `runs/` | 本机模拟输出、日志、临时截图 | 除 `README.md` 与 `.gitkeep` 外忽略 |
| `runs/archives/` | 用户保留的完整实验归档 | 忽略提交；恢复到原相对路径后检查 |
| `artifacts/`、`bin/`、`obj/`、`.godot/` | 构建与导入缓存 | 忽略提交 |

根目录保留项目入口、解决方案、统一配置、授权、贡献指南与更新记录。SDK、Godot 及开发依赖缓存保留在当前工作目录的 `.tools/`，由 Git 忽略，不提交到远程。Blender 如需下载也放在 `.tools/blender-版本/`。

## 图形资源

成人体型映射集中在 `NatureModels.BodyFrame.cs`，与 `NatureModels.WristContour.cs` 的小臂体积拟合配合；运行时使用 `assets/models/human/torso-cage.json` 和 `torso-feminine-cage.json`。两种体型不另建一套人物控制器，仍使用相同的动作节点和近远景切换。可复现的观察场景位于 `benchmarks/scenarios/visual-body-frames.json`、`visual-forearm-frames.json`，正式配图保存在 `docs/images/`。

- `assets/models/environment/`：CC0 环境衍生网格、原始 PBR 贴图、许可与来源清单，普通运行直接读取。
- `assets/models/human/`：授权头脸、前臂、手部及手部 LOD 衍生数据与署名，普通运行直接读取；原始 `.blend` 和 Blender 工具放在本机维护目录。
- `assets/models/garments/`：项目自有三种缝片上衣、源几何哈希和运行清单，普通运行不需要原始导出或 Blender。
- `assets/models/animals/`：鹿、狼的连续蒙皮网格及许可，普通运行直接读取；临时采样和烘焙输入不放入正式资源目录。
- `assets/materials/scanned/`：可平铺扫描表面、原始贴图、许可与哈希清单；通过 `tools/fetch-surface-materials.py --verify` 离线核对。
- `assets/textures/`：自然材质及早期地形/对象图集；3D 地表使用扫描表面，二维地图仍保留图集入口。

资源 PNG/JPG 和相应 `.import` 描述文件一起提交；Godot 自动生成的 `.godot/` 缓存不提交。模型代码文件与 `.uid` 一起维护，避免移动源码后出现失效脚本引用。来源和导入细节见 [美术资源](assets.md)。

人物表现按职责分在 `ResidentRig.cs`（行为状态）、`ResidentRig.HandPose.cs`（肩点、肘腕和指节）、`NatureModels.HandSkin.cs`（手部蒙皮）、`NatureModels.Forearm.cs`（解剖前臂）、`NatureModels.Proportions.cs`（整体比例与静态配件映射）、`NatureModels.Articulation.cs`（肩部蒙皮）、`NatureModels.TorsoAnatomy.cs`（人体胸肩支撑与衣料拟合）与 `NatureModels.Face.cs`（眼睑与面部表现）、`NatureModels.HumanAsset.cs`（授权网格与变体）。这些文件共用人物节点与网格缓存，地图与肖像读取同一套模型，不维护额外的展示替身。入口见 [模型系统](models.md)。

建筑表现同步分在 `WorldView3D.Buildings.cs`，CPU 网格合并分在 `NatureModels.MeshBatching.cs`，评测区段采样分在 `RenderProfile.cs`。性能操作与缓存边界见 [性能采样与渲染维护](performance.md)。

地表与水面材质在 `WorldView3D.Surface.cs`，建筑扫描表面在 `NatureModels.ScannedCraft.cs`，原始纹理读取与法线/粗糙度打包共用 `ScannedSurfaces.cs`。素材下载只写入 `.tools/sources/surface-materials/` 与验证后的正式资源目录，普通运行不读取原始下载缓存。

上衣采样源与成品读取在 `NatureModels.GarmentSkin.cs`，连续草束屋面在 `NatureModels.Thatch.cs`。`tools/bake-garments.ps1` 把维护用输入放在 `.tools/garment-sources/`，只将正式蒙皮与清单写入 `assets/models/garments/`；预览和失败试版不进入正式目录。

正式动物由 `tools/export_authored_animals.py` 转换，`NatureModels.AuthoredAnimals.cs` 校验并读取鹿 14 / 狼 15 关节的作者网格与贴图；`AnimalRig.cs` 驱动步态、坡面接地和下颌。程序动物构造器作为独立校准实现保留。动物图册使用 `animal-details`、`animal-heads` 场景，正式配图位于 `docs/images/`。`addons/texture_import_guard/` 为编辑器提供实际 mip 链维护，普通游戏不运行该插件。

## 运行资料与截图

| 资料 | 位置 | 用途 |
|---|---|---|
| 开发采样 | `.tools/tmp/<主题>/` | 当前开发过程的工作目录，不进入源码提交 |
| 故障与性能日志 | `runs/logs/` | 定位运行问题与性能区段 |
| 独立模拟实验 | `runs/experiments/<实验名>/` | 存档、统计和对照数据 |
| 完整评测与源码研究 | `runs/evaluation/<实验名>/` | 请求、候选、源码、日志和判分的完整实验 |
| 独立 HTML / JSON 报告 | `runs/reports/<实验名>/` | 完整实验的可读结果 |
| 玩家存档 | 文件对话框所选位置 | 世界、会话与进度 |
| 正式文档配图 | `docs/images/` | 当前项目和专题说明 |
| 游戏纹理与插画 | `src/SandBoxSim.Godot/assets/` | 实际运行资源与来源信息 |

运行资料与源码目录有不同职责，实验请求、源码身份、判分和对照数据应在同一个实验目录内关联。来源清单和可复用转换工具分别位于正式资源目录与 `tools/`。

## 移动和新增文件

移动前确认所有相关引用：Markdown 的相对链接、源码的 `res://` 路径、场景脚本、导入文件及脚本注释中的文档地址。源码搬迁还要检查工程的编译规则和 Godot `.uid`。

文档移动后运行：

```powershell
./tools/check-docs.ps1
git diff --check
```

检查器核对当前 Git 文件与未忽略的新 Markdown 文件中的本地链接，包含 Windows 上容易漏掉的大小写错误。资源移动后通过 `tools/godot.ps1 -Mode test` 重新构建、导入并运行客户端自检。

## 文档维护

`README.md` 是项目概览，`docs/README.md` 是阅读导航，`CHANGELOG.md` 是用户和维护者的更新记录。功能行为写入对应指南；配置和存档变化写入开发文档。正式配图位于 `docs/images/`，`docs/archive/` 保存原始任务书与素材来源。

## 项目入口

[src](../../src/README.md)、[tools](../../tools/README.md)、[config](../../config/README.md)、[benchmarks](../../benchmarks/README.md) 各自提供职责与入口；[文档图](../images/README.md) 区分现行界面和专题记录。完整素材生成提示独立放在 `docs/archive/asset-generation.md`，当前美术说明专注实际资源、导入与扩展。

当前接口以 `benchmarks/interface.json` 与对应文档为准。世界存档通常集中于 `saves/`，实验输出由运行工具按需创建。独立包、工具链和缓存的职责见[本地环境与运行目录](local-storage.md)。工程、资源和公共脚本路径同时用于编译、`res://`、CI 和外部调用。

---

[文档导航](../README.md) · [项目首页](../../README.md)
