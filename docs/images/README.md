# SandBoxSim 实机图册

本页基础配图来自 2026-11-06 的 SandBoxSim 客户端，游戏源码为 `b396be219d860f4997484a092d051009afa97659`。全身姿势图更新到同日的下摆分层版本，女性成衣、全身三视图和抬臂图更新到同日的后领与上背修形版本。各批均使用 Godot 4.7.2 .NET 和 GL Compatibility，静态图默认 1600×1000。地图和人物观察窗使用世界中的实际对象，模型图册使用与世界共用的网格和控制器。帧号为从零开始的表现帧。

## 世界与操作界面

| 内容 | 采样入口 | 配图 |
|---|---|---|
| 世界远景、河岸近景 | `visual-landscape` 第 40／80 帧。第 60 帧切换到近景 | [远景](terrain-modern.png) · [近景](terrain-closeup.png) |
| 现场手记、人物手记 | `visual-landscape` 第 40 帧，清空命令保留手记。人物手记使用 `ui-portrait-window` 的人物面板，第 65 帧选择身份 4294967299，第 70 帧采样 | [现场](free-world-ui.png) · [人物](ui-detail-refined.png) |
| 创造工具库 | `ui-tool-library` 第 90 帧，分别采用 1600×1000 与 1280×800 | [常规窗口](tool-palette.png) · [紧凑窗口](tool-palette-compact.png) |
| 人物观察窗 | `ui-portrait-window` 第 90／130／170 帧，展示面部、双手与全身 | [面部](resident-portrait.png) · [双手](resident-hand-inspector.png) · [全身](resident-body-inspector.png) |
| 女性居民的领口与全身 | `visual-female-portrait` 第 90／130 帧，使用稳定身份 4294967299 | [面部与领口](resident-female-neck.png) · [全身](resident-female-inspector.png) |
| 住房、小径 | `world-living-homes` 第 120 帧，`world-living-paths` 第 90 帧。80 人开局，预演 14 日 | [住房](living-homes.png) · [小径](living-paths.png) |
| 农田、作物与地力 | Windows 独立包的 `--panel=farms --agents=150 --demo-days=14 --capture-delay=1.5` 预览，暂停于第 15 日 | [农田](living-farms.png) |
| 居民与动物在岸边和林地活动 | `world-scenery-contacts` 第 90 帧。40 人开局，预演两日后每帧推进一模拟步 | [场景接触](scene-resource-contacts.png) |
| 自然地貌 | 客户端预览 `--panel=wild`、`--panel=meadow`，8 人开局 | [旧聚落遗迹](free-exploration.png) · [自然花甸](wild-meadow.png) |

界面与常规地形样本使用种子 839102，常规地形为 40 人开局，人物和工具界面样本为 8 人开局，评测期间均冻结模拟。住房、农田和小径的预演会实际推进世界。

## 建筑、动物与环境模型

| 内容 | 采样入口 | 配图 |
|---|---|---|
| 五种住房与仓库 | `visual-architecture` 第 90 帧 | [建筑图册](architecture-detail.png) |
| 成人、儿童、鹿和狼 | `visual-characters` 第 90 帧 | [人物与动物](character-detail.png) |
| 鹿狼眼、耳与足部 | `visual-animal-details`、`visual-animal-heads` 第 90 帧 | [局部细节](animal-detail.png) · [头部](animal-head-detail.png) |
| 鹿的行走与狼的小跑 | `visual-animal-motion` 第 180 帧 | [四足接地](animal-motion.png) |
| 树木、蕨类、芦苇、岩石与灌丛 | `visual-ecology-details` 第 90 帧 | [生态模型](ecology-detail.png) |
| 衣料、靴子、动物和建筑近景 | `visual-model-details` 第 90 帧 | [表面细节](model-surface-detail.png) |
| 树木、田地、矿场和特殊地貌 | 客户端预览 `--panel=naturemodels` | [自然与建筑](nature-detail.png) |

## 人物形体、服装与动作

| 内容 | 采样入口 | 配图 |
|---|---|---|
| 成人体型与女性衣身 | `visual-body-frames`、`visual-thorax-frames` 第 90 帧 | [全身三视图](resident-body-frames.png) · [女性衣身](resident-thorax-detail.png) |
| 成人头脸 | `visual-faces`、`visual-face-frames` 第 90 帧 | [三种头脸](resident-face-detail.png) · [成人外观对照](resident-face-frames.png) |
| 三种衣装的正背面 | `visual-wardrobe`、`visual-female-wardrobe` 第 90 帧 | [男性成衣](wardrobe-detail.png) · [女性成衣](resident-female-wardrobe.png) |
| 手部、前臂和腕部 | `visual-hands`、`visual-forearm-frames` 第 90 帧，`visual-wrist-motion` 第 150 帧 | [握持](resident-hand-detail.png) · [前臂三视图](resident-forearm-frames.png) · [腕部近景](resident-wrist-motion.png) |
| 静立、步行与拾取 | `visual-body-motion` 第 150 帧 | [全身姿势](resident-body-motion.png) |
| 同速步行、搬运和持锤 | `visual-locomotion` 第 180 帧 | [步态对照](resident-style-walk.png) |
| 交谈、递交与进食 | `visual-social` 第 150 帧 | [生活动作](resident-social.png) |
| 同工具与不同疲劳状态 | `visual-work-effort` 第 180 帧 | [作业姿态](resident-work-effort.png) |
| 起手、拾取、换物和交接 | `visual-handling-transitions` 第 222 帧 | [交互衔接](resident-handling-transitions.png) |
| 上肢连动与抬手 | `visual-arm-articulation` 第 270 帧，`visual-shoulder-elevation` 第 108 帧，`visual-shoulder-motion` 第 156 帧 | [肩肘腕](resident-arm-junctions.png) · [抬臂](resident-shoulder-elevation.png) · [作业肩袖](resident-shoulder-motion.png) |
| 空闲伸展、摆臂与屈肘 | `visual-captured-stretch` 第 110 帧，`visual-arm-swing` 第 150 帧，`visual-upper-arm-flex`、`visual-elbow-joint` 第 270 帧 | [伸展](resident-captured-stretch.png) · [摆臂](resident-arm-swing.png) · [上臂](resident-upper-arm-flex.png) · [肘部](resident-elbow-joint.png) |
| 服装配件、职业动作和左右手 | 客户端预览 `--panel=equipment`、`--panel=actions`、`--panel=forearms`、`--panel=handedness` | [配件](resident-equipment.png) · [职业动作](resident-garment-actions.png) · [前臂](resident-forearm-detail.png) · [左右手](resident-handedness.png) |

图册的公开评测使用零模拟人口和零模拟步进，角色由图册构建。动作画面仍按固定 60 Hz 更新。腕部与关节图用于观察局部变形，世界人物的动作和状态可在人口运行场景或正常游戏中查看。

## 动作短片

| 短片 | 原生帧范围 | 内容 |
|---|---|---|
| [步态](resident-locomotion.webp) | `visual-locomotion` 第 100–335 帧，每五帧一张 | 同速度的成人外观步行、转向、搬运与持锤 |
| [腕部](resident-wrist-motion.webp) | `visual-wrist-motion` 第 30–505 帧，每五帧一张 | 两种成人外观的腕部屈伸、侧偏、旋转和握拳 |
| [作业](resident-style-work.webp) | `visual-work-effort` 第 120–355 帧，每五帧一张 | 两种成人外观与两种疲劳状态的提锤和收势 |
| [生活动作](resident-style-social.webp) | `visual-social` 第 60–295 帧，每五帧一张 | 交谈、递交与进食 |

短片以 1280×800 原生帧编码为 WebP，保留原采样顺序，约 12 帧/秒。由于评测每次最多捕获 32 张，长序列按固定配置分段采集。图册的当前版本以本页为准，其他历史配图仍保留原采样内容。

## 复现

在有图形显示的机器上，从仓库根目录执行以下命令。输出目录应为尚未创建的新目录。

```powershell
./tools/godot.ps1 -Mode build
python tools/evaluate.py run benchmarks/scenarios/visual-female-portrait.json --resolution 1600x1000 --audio-driver Dummy --output .tools/tmp/docs-female-portrait
python tools/evaluate.py verify .tools/tmp/docs-female-portrait
```

场景定义见 [公开场景索引](../../benchmarks/README.md)，输出字段见 [评测接口](../development/benchmark.md)。原生输出可从 `result.json` 读取帧号与截图哈希，从 `provenance.json` 核对实际程序集及源码。正式配图用于说明功能；视觉或性能比较需以各自协议重新采样。

[文档导航](../README.md) · [模型说明](../development/models.md) · [人物素材](../development/human-assets.md) · [环境素材](../development/environment-assets.md)
