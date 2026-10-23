# SandBoxSim 文档配图

此目录保留项目说明使用的原生 Godot 截图和动作演示，随文档提交。图片反映采样版本，不代表所有驱动、视角或当前帧率，也不作为模型质量、效率或 RSI 的评分。项目正式名称为 SandBoxSim。

## 当前配图

| 文件 | 用途与采样入口 |
|---|---|
| `free-world-ui.png`、`ui-detail-refined.png` | 自由探索现场手记、人物手记；`field`、`person` |
| `resident-portrait.png`、`resident-hand-inspector.png`、`resident-body-inspector.png` | 展开人物观察窗的面部、双手与全身；公开 `ui-portrait-window` 第 90/130/170 帧 |
| `character-detail.png`、`resident-face-detail.png` | 人物、动物与面部；`characters`、`faces` |
| `wardrobe-detail.png`、`resident-garment-actions.png` | 三种衣装的正背面与作业动作；`wardrobe`、`actions` |
| `model-surface-detail.png` | 衣料、靴子、动物与建筑近景；`model-details` |
| `terrain-modern.png`、`terrain-closeup.png` | 地形、植被与水岸；公开 `visual-landscape` 第 40/80 帧 |
| `architecture-detail.png`、`resident-forearm-detail.png` | 建筑和连续前臂；`architecture`、`forearms` |
| `resident-handedness.png` | 左右手与远近景模型；`handedness` |
| `living-homes.png`、`living-farms.png`、`living-paths.png` | 住房生活、农业与逐渐累积的土地痕迹 |
| `nature-detail.png`、`free-exploration.png`、`wild-meadow.png` | 自然模型、特殊地点和生态专题 |
| 其他 `resident-*.png`、`resident-*.gif` | 较早的人物、配件、手部与生活动作专题 |

## 版本与复现

2026-10-23 的九张人物相关配图已更新为新头脸管线：人物观察窗的面部、双手和全身，人物手记、面部与人物动物图册，以及衣装、作业动作和网格近景。当前展示 MakeHuman 原生 UV 皮肤、原脸眼睑闭合、三种发片、儿童比例及扫描亚麻衣料；面部图册给出三种形体配方的正面和侧面。现场手记和建筑图为当天此前采样，其他图片保留各自日期。

2026-10-22 的此前人物、衣装、动物、建筑和人物手记配图展示三套独立头脸、2K 法线细节、细发丝、收束衣装、重烘焙的鹿狼及内凹窗洞。人物观察窗使用种子 839102、40 人、零模拟步进的公开场景；其余使用表中图册入口。十一张现有配图均重新采样为 1600×1000、30 FPS 上限；图册参数为 `--capture-delay=2.4 --quit-after 140`，使用独立工作室与游戏共用模型。面部图册包含三套解剖来源的正面和侧面，部分人物为戴帽状态。

地形两张图来自同日的公开 `visual-landscape` 场景，种子 839102、40 人、零步进、1600×1000。建筑与前臂专题也采自同日，使用表中入口、2.4 秒采样及相同分辨率。图册使用随模型尺度设定范围的正交阴影、0.05 深度偏置和 0.2 法线偏置；地台接收阴影、不投射阴影。`resident-handedness.png` 采于 2026-10-21，1440×900、2.5 秒采样。

其余图保留较早版本的专题表现：住房、农业、小径与世界生活图源自 2026-10-08；自然图册、环境及部分人物专题源自 2026-10-09。旧界面文字和动作 GIF 不代表最新外观。此前本机临时实验归档已清理，这些日期只标识图片来源，不表示仍保留完整运行记录。

所有正式配图均为实际客户端渲染，没有重绘、替换背景或用概念图替代游戏模型。本轮替换人物头脸、皮肤、眼球与近景发片，更新原眼睑闭合；衣装重新烘焙褶皱并使用扫描亚麻材质，动物调整毛束宽度、色调与表面细节。同画面建筑沿用现有模型。地形、植物和其他未替换专题图沿用各自现有来源。需要验证当前版本时，按 [评测接口](../development/benchmark.md) 重新运行公开场景；性能对照必须使用相同场景和渲染条件。

[模型说明](../development/models.md) · [美术资源](../development/assets.md) · [人物素材](../development/human-assets.md) · [环境素材](../development/environment-assets.md) · [目录职责](../development/repository-layout.md)
