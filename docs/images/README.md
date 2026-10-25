# SandBoxSim 文档配图

此目录保留项目说明使用的原生 Godot 截图和动作演示，随文档提交。图片反映采样版本，不代表所有驱动、视角或当前帧率，也不作为模型质量、效率或 RSI 的评分。项目正式名称为 SandBoxSim。

最新配图展示身份与可点击缩略肖像并排的居民手记、行式需求与可展开物资、概览／生活／决策人物页、柔光／侧光观察，以及带掌背分区的手部表面。地形图反映按逻辑水陆生成的实际支撑曲面、植被露土斑块和高矮草丛。近景水面使用与长波分开的细波法线；建筑、植被和地形图同时反映当前客户端。动态演示与其他专题保留各自先前采样。截图之外的能力边界见 [模型系统](../development/models.md)。

## 当前配图

| 文件 | 用途与采样入口 |
|---|---|
| `tool-palette.png` | 416 像素工具库、分类和搜索、悬浮控制组；`tools` |
| `free-world-ui.png`、`ui-detail-refined.png` | 自由探索现场手记、人物手记；`field`、`person` |
| `resident-portrait.png` | 当前展开人物观察窗；`person-expanded` |
| `resident-life.png`、`resident-decision.png` | 实际鼠标切换的生活／决策人物页 |
| `resident-raking.png` | 实际鼠标切换侧光后的同一人物模型 |
| `resident-hand-inspector.png`、`resident-body-inspector.png` | 当前展开窗口中的人物双手与全身；公开 `ui-portrait-window` 第 130/170 帧 |
| `character-detail.png`、`resident-face-detail.png` | 人物、动物与面部；`characters`、`faces` |
| `wardrobe-detail.png`、`resident-garment-actions.png` | 三种衣装的正背面与作业动作；`wardrobe`、`actions` |
| `animal-head-detail.png` | 鹿狼正面和侧面的眼位、耳根、额鼻及下颌轮廓；`animal-heads` |
| `animal-detail.png` | 授权鹿狼侧面、独立眼眶近景与源足部表面；`animal-details` |
| `ecology-detail.png` | 扫描树木、蕨类、岩石与程序草本、灌木；`ecology-details` |
| `model-surface-detail.png` | 衣料、靴子、动物与建筑近景；`model-details` |
| `resident-tool-motion.gif` | 当前携带、作业与收纳动作；`actions` 的 30 帧原生采样，覆盖约 7 秒循环 |
| `terrain-modern.png`、`terrain-closeup.png` | 普通世界与 `--view=near` 的地形、植被和水岸 |
| `architecture-detail.png`、`resident-forearm-detail.png` | 建筑和连续前臂；`architecture`、`forearms` |
| `resident-hand-detail.png` | 原网格掌纹、甲床及工具握持近景；`hands` |
| `resident-handedness.png` | 左右手与远近景模型；`handedness` |
| `living-homes.png`、`living-farms.png`、`living-paths.png` | 住房生活、农业与逐渐累积的土地痕迹 |
| `nature-detail.png`、`free-exploration.png` | 较早的自然模型与特殊地点专题 |
| `immersive-world.png` | F10 隐藏界面后的普通世界；保留角落返回按钮 |
| `wild-meadow.png` | 当前通用草花、逐株接地与真实世界花甸；`meadow` |
| 其他 `resident-*.png`、`resident-*.gif` | 较早的人物、配件、手部与生活动作专题 |

## 版本与复现

建筑、人物动物、面部、衣装、生态、网格近景、地形远近景、工具库、现场手记、人物手记、展开观察窗及其手部／全身视图与花甸更新于 2026-10-25，均为原生 Godot 渲染、1600×1000。图册使用实际游戏网格、独立展示环境，模拟暂停，等待 120 帧后采样；普通世界预览等待 2 秒。生活／决策／侧光、物资与工具状态由实际命中检查的鼠标事件回放后采样，回放先用空格暂停世界，并核对工具库实际可见、手记收起；缩略入口另检查 Enter 打开与 Esc 焦点返回。沉浸图使用 F10 后采样。手部与全身分别来自公开 `ui-portrait-window` 场景第 130／170 帧。界面和普通世界使用种子 839102、40 人，无预先天数推进；花甸预览暂停模拟。

字体策略为 `fontProfile=noto-packaged-v1`。对应 `interface=resident-thumbnail-v18`、`architecture=double-lap-tiles-v12`、`animalPresentation=source-eyelids-v9`、`landscapeSurface=coverage-mosaic-v6`、`habitatDetail=stratified-meadow-v10`、`residentSurface=orbital-gaze-v6`、`vegetationSurface=engine-faced-v2`、`waterSurface=logical-bank-v4` 、`handSurface=anatomical-regions-v1`、`portrait.studio=thumbnail-portrait-v6` 和 `garmentMaterial=engine-faced-linen-v8`。动物半闭眼／全闭眼的强制参数图仅用于本地检查，不替代这里的正常运行配图。

地形远景使用普通世界入口；近景加 `--view=near`。两图用于查看实际扫描草叶地面、山坡、岸线和贴地，属于普通客户端采样，不作为固定帧评测成绩。

前臂、左右手专题随当前手部表面重采；动作专题保留先前采样；较早的住房、农业、小径与世界生活图只说明对应玩法。未更新图片中的 UI 不代表当前布局。所有图片均来自实际客户端，没有用概念图替代模型。

复现入口见表格及 [渲染与视觉评测](../development/benchmark.md)。截图用于项目说明，不用于证明模型达到写实品质；效率对照必须使用同一场景、资产、渲染条件与版本标识。

[模型说明](../development/models.md) · [美术资源](../development/assets.md) · [人物素材](../development/human-assets.md) · [环境素材](../development/environment-assets.md) · [目录职责](../development/repository-layout.md)

旧程序动物的毛束和眨眼说明不适用于当前授权动物。
