# SandBoxSim 文档配图

此目录保留项目说明使用的原生 Godot 截图和动作演示，随文档提交。图片反映采样版本，不代表所有版本、驱动或视角的效果，也不作为模型质量或性能的验证分数。旧截图可能包含早期界面文字；项目正式名称统一为 SandBoxSim。

## 配图用途

| 文件 | 用途 |
|---|---|
| `free-world-ui.png`、`ui-detail-refined.png` | 自由探索界面与人物手记 |
| `terrain-closeup.png`、`terrain-lived.png`、`terrain-modern.png` | 河岸、聚落、地表与环境素材 |
| `ecology-detail.png` | 树木、蕨类、草丛、灌木和岩石图册 |
| `architecture-detail.png` | 五种住房与仓库模型 |
| `resident-face-detail.png`、`character-detail.png` | 面部、居民和动物模型 |
| `resident-forearm-detail.png` | 同源解剖肘部/前臂、左右镜像及实际腕口 |
| `resident-handedness.png` | 左右手掌心、手背与近远景对照 |
| `resident-social.png` | 交谈、分享和进食的实际模型动作 |
| `resident-portrait.png`、`resident-hand-inspector.png`、`resident-body-inspector.png` | 可展开人物观察窗的面部、双手与全身 |
| `resident-garment-actions.png` | 连续肩袖在拿取、作业和存放动作中的表现 |
| `wardrobe-detail.png` | 三种衣装剪裁的正背面 |
| `model-surface-detail.png` | 衣料、靴子、动物与建筑近景 |
| `living-homes.png`、`living-farms.png`、`living-paths.png` | 住房生活、农业与逐渐累积的土地痕迹 |
| 其他 `resident-*.png`、`resident-*.gif` | 人物档案、配件、手部及动作的早期专题记录 |
| `nature-detail.png`、`free-exploration.png`、`wild-meadow.png` | 自然模型、特殊地点和生态专题记录 |

## 来源与版本

`terrain-modern.png`、`terrain-closeup.png` 更新为 2026-10-22 肩袖/动物造型修改后的 1600×1000 原生渲染。它们使用公开 `visual-landscape` 场景的第 40、80 帧，种子 839102、40 人、零步进，第二张切换近景，展示交叉草地采样和斑块岸色。冻结场景用于比较画面，不代表流畅度测试。

`resident-face-detail.png`、`wardrobe-detail.png`、`resident-forearm-detail.png`、`resident-garment-actions.png`、`character-detail.png`、`model-surface-detail.png` 和 `architecture-detail.png` 为同日解剖前臂与鞋楦修改后的最终渲染，分别使用 `faces`、`wardrobe`、`forearms`、`actions`、`characters`、`model-details`、`architecture` 入口。七张图均为 1600×1000 原生渲染，参数为 `--capture-delay=2.4 --max-fps 30 --quit-after 140`，没有重绘。图册使用按模型尺度设置范围的正交阴影、0.05 深度偏置与 0.2 法线偏置；平面地台接收模型阴影但不投射阴影，避免地台自身条纹。

前臂图是世界共用模型的独立切面预览，实际袖口在作业图中查看。头脸、动物、建筑与地形几何没有在本轮重新制作；图中保留之前的共顶点衣片、动物毛束、鹿角和扫描茅草屋面。重新采样这些图是为了让当前人物修改与图册光照设置保持一致，不能据此声称全部资产已经重做。

`resident-face-detail.png`、`character-detail.png`、`model-surface-detail.png` 与 `ui-detail-refined.png` 随后更新为同日的眉毛、侧分发型、动物耳廓和绿灰界面版本，分别使用 `faces`、`characters`、`model-details`、`person` 入口，分辨率仍为 1600×1000，参数仍为 `--capture-delay=2.4 --max-fps 30 --quit-after 140`。人物页缩小标题、加大模型预览并调整补光；地图采用四级级联阴影、深度偏移 1.2，减少坡面条纹。没有重绘截图、替换背景或以概念图代替游戏模型。

`resident-portrait.png`、`resident-hand-inspector.png`、`resident-body-inspector.png` 更新为同日公开 `ui-portrait-window` 场景第 90、130、170 帧的原生截图，种子 839102、40 人、零步进、1600×1000、30 FPS 上限，分别展示展开观察窗的面部、双手和全身。`wardrobe-detail.png`、`resident-garment-actions.png` 与 `ui-detail-refined.png` 同时更新为收紧肩袖、薄圆扣及观察窗入口版本，使用 `wardrobe`、`actions`、`person` 入口与 2.4 秒采样。它们是游戏实际渲染，不经过重绘；原生回放检查不构成正式性能或 RSI 成绩。

`resident-handedness.png`、`free-world-ui.png` 为 2026-10-21 的 1440×900 原生渲染，分别使用 `handedness`、`ecology` 预览入口、2.5 秒采样；展示同源远景手型和早期卡片式工具界面。以下旧来源说明仅适用于未被此次替换的配图。

面部、人物动物、衣装和表面近景分别来自 `proportion-final-{faces,characters,wardrobe,details}-20261009` 的第 90 帧。植物图册、建筑图册和现代地表图来自 `modern-environment-delivery-{ecology,architecture,terrain}-20261009` 的第 90 帧。

界面图来自 `world-life-delivery-ui-20261008`；住房图来自 `detail-series-homes-20261008`；农业图来自 `living-economy-managed-farms-20261008`；小径图来自 `world-life-paths-20261008`。这些配图保留原始像素，没有修图替换模型。动作 GIF 和其他专题图保留各自早期表现，不用于展示最新头脸素材。

上述名称仅记录历史采样来源。本机临时实验目录、日志和旧压缩包已清理，不能据此声称完整实验归档仍可访问。需要重新验证时，按 [评测接口](../development/benchmark.md) 运行公开场景；新结果使用自己的版本与采样身份。

旧版工程、蓝图与任务流程界面的配图已清理。更新正式配图前先检查实际客户端，保留真实渲染结果并同步正文。

[模型说明](../development/models.md) · [美术资源](../development/assets.md) · [人物素材](../development/human-assets.md) · [环境素材](../development/environment-assets.md) · [目录职责](../development/repository-layout.md)
