# SandBoxSim 文档配图

此目录保留项目说明使用的原生 Godot 截图和动作演示，随文档提交。图片反映采样版本，不代表所有驱动、视角或当前帧率，也不作为模型质量、效率或 RSI 的评分。项目正式名称为 SandBoxSim。

最新配图展示带居民快捷操作的提示卡、门窗板材、衣料与动物表面，以及逐株贴地的花甸。其余专题保留各自采样。工具动态演示和其他专题保留各自先前采样。截图之外的能力边界见 [模型系统](../development/models.md)。

## 当前配图

| 文件 | 用途与采样入口 |
|---|---|
| `tool-palette.png` | 416 像素工具库、分类和搜索、悬浮控制组；`tools` |
| `free-world-ui.png`、`ui-detail-refined.png` | 自由探索现场手记、人物手记；`field`、`person` |
| `resident-portrait.png` | 当前展开人物观察窗；`person-expanded` |
| `resident-hand-inspector.png`、`resident-body-inspector.png` | 较早的人物双手与全身；公开 `ui-portrait-window` 第 130/170 帧 |
| `character-detail.png`、`resident-face-detail.png` | 人物、动物与面部；`characters`、`faces` |
| `wardrobe-detail.png`、`resident-garment-actions.png` | 三种衣装的正背面与作业动作；`wardrobe`、`actions` |
| `animal-head-detail.png` | 鹿狼正面和侧面的眼位、耳根、额鼻及下颌轮廓；`animal-heads` |
| `animal-detail.png` | 授权鹿狼侧面、独立眼眶近景与源足部表面；`animal-details` |
| `ecology-detail.png` | 扫描树木、蕨类、岩石与程序草本、灌木；`ecology-details` |
| `model-surface-detail.png` | 衣料、靴子、动物与建筑近景；`model-details` |
| `resident-tool-motion.gif` | 当前携带、作业与收纳动作；`actions` 的 30 帧原生采样，覆盖约 7 秒循环 |
| `terrain-modern.png`、`terrain-closeup.png` | 普通世界与 `--view=near` 的地形、植被和水岸 |
| `architecture-detail.png`、`resident-forearm-detail.png` | 建筑和连续前臂；`architecture`、`forearms` |
| `resident-handedness.png` | 左右手与远近景模型；`handedness` |
| `living-homes.png`、`living-farms.png`、`living-paths.png` | 住房生活、农业与逐渐累积的土地痕迹 |
| `nature-detail.png`、`free-exploration.png` | 较早的自然模型与特殊地点专题 |
| `wild-meadow.png` | 当前通用草花、逐株接地与真实世界花甸；`meadow` |
| 其他 `resident-*.png`、`resident-*.gif` | 较早的人物、配件、手部与生活动作专题 |

## 版本与复现

建筑、人物动物、面部、衣装、生态、网格近景、地形远近景、工具库、现场手记、人物手记、展开观察窗与花甸更新于 2026-10-24，均为原生 Godot 渲染、1600×1000、2 秒采样延迟。对应 `interface=selection-workspace-v11`、`architecture=bevelled-joinery-v9`、`animalPresentation=bound-surfaces-v4` 和 `landscapeSurface=oriented-meadow-v4`；植物摆放标识为 `habitatDetail=rooted-flowers-v7`。人物睫毛与水波分别记录 `residentSurface=anatomical-lashes-v1`、`waterSurface=shore-ripples-v1`。模型图册暂停模拟；世界界面使用种子 839102、40 人，不作预先天数推进，但普通界面截图等待期间按正常 4× 倍率运行；花甸预览暂停模拟。

地形远景使用普通世界入口；近景加 `--view=near`。两图用于查看实际扫描草叶地面、山坡、岸线和贴地，属于普通客户端采样，不作为固定帧评测成绩。

前臂、左右手和动作专题保留各自先前采样；较早的住房、农业、小径与世界生活图只说明对应玩法。未更新图片中的 UI 不代表当前布局。所有图片均来自实际客户端，没有用概念图替代模型。

复现入口见表格及 [渲染与视觉评测](../development/benchmark.md)。截图用于项目说明，不用于证明模型达到写实品质；效率对照必须使用同一场景、资产、渲染条件与版本标识。

[模型说明](../development/models.md) · [美术资源](../development/assets.md) · [人物素材](../development/human-assets.md) · [环境素材](../development/environment-assets.md) · [目录职责](../development/repository-layout.md)

旧程序动物的毛束和眨眼说明不适用于当前授权动物。
