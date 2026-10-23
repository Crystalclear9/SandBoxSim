# SandBoxSim 文档配图

此目录保留项目说明使用的原生 Godot 截图和动作演示，随文档提交。图片反映采样版本，不代表所有驱动、视角或当前帧率，也不作为模型质量、效率或 RSI 的评分。项目正式名称为 SandBoxSim。

最新一轮更新人物动物、面部、网格近景和生态配图，并增加动物侧面、头部与足部近景。界面沿用自然岩灰与苔色设计。工具动态演示和其他专题保留各自先前采样。截图之外的能力边界见 [模型系统](../development/models.md)。

## 当前配图

| 文件 | 用途与采样入口 |
|---|---|
| `tool-palette.png` | 单列搜索工具库、固定分类与参数、连续顶底控制栏；`brush` |
| `free-world-ui.png`、`ui-detail-refined.png` | 自由探索现场手记、人物手记；`field`、`person` |
| `resident-portrait.png` | 当前展开人物观察窗；`person-expanded` |
| `resident-hand-inspector.png`、`resident-body-inspector.png` | 较早的人物双手与全身；公开 `ui-portrait-window` 第 130/170 帧 |
| `character-detail.png`、`resident-face-detail.png` | 人物、动物与面部；`characters`、`faces` |
| `wardrobe-detail.png`、`resident-garment-actions.png` | 三种衣装的正背面与作业动作；`wardrobe`、`actions` |
| `animal-detail.png` | 鹿狼侧面、耳廓、口鼻、分瓣蹄和四趾；`animal-details` |
| `ecology-detail.png` | 扫描树木、蕨类、岩石与程序草本、灌木；`ecology-details` |
| `model-surface-detail.png` | 衣料、靴子、动物与建筑近景；`model-details` |
| `resident-tool-motion.gif` | 当前携带、作业与收纳动作；`actions` 的 30 帧原生采样，覆盖约 7 秒循环 |
| `terrain-modern.png`、`terrain-closeup.png` | 地形、植被与水岸；公开 `visual-landscape` 第 40/80 帧 |
| `architecture-detail.png`、`resident-forearm-detail.png` | 建筑和连续前臂；`architecture`、`forearms` |
| `resident-handedness.png` | 左右手与远近景模型；`handedness` |
| `living-homes.png`、`living-farms.png`、`living-paths.png` | 住房生活、农业与逐渐累积的土地痕迹 |
| `nature-detail.png`、`free-exploration.png`、`wild-meadow.png` | 自然模型、特殊地点和生态专题 |
| 其他 `resident-*.png`、`resident-*.gif` | 较早的人物、配件、手部与生活动作专题 |

## 版本与复现

当前建筑、人物动物、衣装、网格近景、生态、工具面板、现场与人物手记配图采于 2026-10-23，使用原生 Godot 渲染、2.4 秒采样延迟：最新工具库图为 1280×800，其余上述图片为 1600×1000。地形远近景来自同日公开 `visual-landscape` 的第 40/80 帧，种子 839102、40 人、零模拟步进。最新工具库、现场与人物手记、展开观察窗图使用 `crafted-workspace-v6` 的自然色界面；本轮更新的人物、动物、面部、模型近景与生态图也使用当前界面；其他图片保留各自采样时的布局。

前臂、左右手和动作专题保留各自先前采样；较早的住房、农业、小径与世界生活图只说明对应玩法。未更新图片中的 UI 不代表当前布局。所有图片均来自实际客户端，没有用概念图替代模型。

复现入口见表格及 [渲染与视觉评测](../development/benchmark.md)。截图用于项目说明，不用于证明模型达到写实品质；效率对照必须使用同一场景、资产、渲染条件与版本标识。

[模型说明](../development/models.md) · [美术资源](../development/assets.md) · [人物素材](../development/human-assets.md) · [环境素材](../development/environment-assets.md) · [目录职责](../development/repository-layout.md)
