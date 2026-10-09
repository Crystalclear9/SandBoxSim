# 正式文档配图

此目录保存项目说明使用的实机截图和动作演示，随文档提交。图片只展示采样时的画面，不代表所有版本、驱动或视角的效果；截图也不是模型质量或性能的验证分数。

文档配图保留，本机临时实验与旧压缩包已清理。下文中的历史实验路径仅记录图片来源，不表示当前仍保存完整本机归档；重新验证使用公开评测场景。历史截图来源不会因目录清理而改写为新的实验结果。

| 文件 | 用途 |
|---|---|
| `free-world-ui.png`、`ui-detail-refined.png` | 当前炭灰界面与人物手记 |
| `terrain-closeup.png` | 河岸、水面、草丛和居民近景 |
| `terrain-lived.png` | 固定种子自由运行 14 日后的聚落 |
| `architecture-detail.png` | 五种住房与仓库的实际模型图册 |
| `resident-face-detail.png`、`character-detail.png` | 人物面部、居民和动物模型图册 |
| `wardrobe-detail.png` | 三种实际衣装剪裁的正背面 |
| `model-surface-detail.png` | 世界实际网格的衣料、靴子、动物与建筑近景 |
| `resident-*.png`、`resident-*.gif` | 人物档案、配件、手部及动作的专题记录 |
| `nature-detail.png`、`free-exploration.png`、`wild-meadow.png` | 自然模型、特殊地点和生态内容专题记录 |

当前界面、人物、动物和建筑配图来自原生 Godot 渲染，没有通过修图修改画面。2026-10-08 的界面更新来自 `modern-*-verified-20261008` 完整评测目录；世界界面使用固定种子自由运行 14 日后的状态，模型图册使用中性展示背景。专题记录保留相应功能的早期视图；查看最新菜单与配色使用界面图，查看几何实现使用 [模型说明](../development/models.md)。

旧版工程、蓝图与任务流程界面放在 [archive/images](../archive/images)，不作为当前玩法说明。调试截帧与完整评测样本位于 `runs/screenshots/` 和 `runs/evaluation/`，不批量复制进本目录。

更新配图时先用原生运行确认功能，再替换主题引用；保持图像与正文一致。素材生成来源属于 [美术资源](../development/assets.md)，目录边界见 [文件管理](../development/repository-layout.md)。

2026-10-09，`resident-face-detail.png`、`model-surface-detail.png`、`character-detail.png` 已更新为 `sculpted-character-attached-faces-20261009`、`sculpted-character-attached-details-20261009`、`sculpted-character-attached-characters-20261009` 的原生第 90 帧。它们显示当前头脸、衣装和动物腿部；尖刺/散点毛束原型没有进入正式配图，相关实验仍保留在原运行目录。建筑专题图继续使用 2026-10-08 的原采样。

`model-surface-detail.png`、`character-detail.png`、`architecture-detail.png` 最新来自 `runs/evaluation/organic-models-final-closeup-20261008`、`organic-models-final-characters-20261008`、`organic-models-final-architecture-20261008` 的第 90 帧，均为原生 Godot 截图，已检查源码/程序集绑定。旧实验截图保留在原有运行目录；面部、手部及生活物件专题图仍记录其各自采样版本。

`living-paths.png` 来自 `runs/evaluation/world-life-paths-20261008/frame-00090.png`，为原始 Godot 画面；80 人初始世界自由运行 14 日，土地显示实际压实 19%。后续的小幅人物朝向修正不改变该采样的地表状态。

`free-world-ui.png`、`ui-detail-refined.png` 已更新为 `runs/evaluation/world-life-delivery-ui-20261008/frame-00090.png`，与源码/程序集构建清单一致。前述其他模型专题图仍保留各自的采样来源。

`living-homes.png` 来自 `runs/evaluation/living-world-homes-delivery-20261008/frame-00090.png`，未经修图。80 人初始世界自由运行 14 日，选择实际住户最多的完工住房，显示两位住户对应的晾晒、长凳、陶器和完整度状态卡；镜头读取房屋实际朝向。完整请求、引擎日志、源码/程序集绑定与图像哈希保存在该实验目录。

住房交互补齐后，`living-homes.png` 更新为 `runs/evaluation/living-world-complete-homes-20261008/frame-00090.png`，显示实际住户链接与近景按钮。该完整归档还保存按钮回放、住户姓名链接回放和第 160 帧对应人物档案；历史样本保留原始产物与哈希。

陶器、桶体、窗台细节和土地读数更新后，`living-homes.png` 使用 `runs/evaluation/detail-series-homes-20261008/frame-00090.png` 原始图像。该样本包含实际住房按钮与住户链接回放；历史图片的来源说明保留，当前配图以这一条为准。

`living-farms.png` 来自 `runs/evaluation/living-economy-delivery-farms-20261008/frame-00090.png`，为最终构建绑定的原始 Godot 图像。150 人初始世界自由演化 14 日，选择真实完工农田；作物、住户和现场信息都来自模拟，没有为截图放置农田或居民。

人物可见性与作物远景优化后，当前 `living-farms.png` 更新为 `runs/evaluation/living-economy-managed-farms-20261008/frame-00090.png`，与最终源码/程序集绑定；前述采样独立保留。

2026-10-09 人物素材更新后，当前 `resident-face-detail.png`、`wardrobe-detail.png`、`model-surface-detail.png`、`character-detail.png` 分别采用 `runs/evaluation/human-assets-delivery-faces-20261009`、`human-assets-delivery-wardrobe-20261009`、`human-assets-delivery-details-20261009`、`human-assets-delivery-characters-20261009` 的原生第 90 帧，完整归档核对源码、程序集与图像哈希。上述早期人物截图记录保留为历史来源；当前头脸数据及授权见 [人物头脸素材](../development/human-assets.md)。`resident-face-motion.gif` 和其他动作 GIF 仍是早期专题记录，不用于展示本次头脸素材。

2026-10-09 全场景素材更新后，`ecology-detail.png`、`resident-face-detail.png`、`character-detail.png`、`model-surface-detail.png`、`architecture-detail.png`、`terrain-modern.png` 分别采用 `runs/evaluation/modern-environment-delivery-{ecology,faces,characters,details,architecture,terrain}-20261009` 对应完整归档的第 90 帧。这些为原生 Godot 画面，归档记录构建身份、场景、图片哈希和性能。植物来源见 [环境素材](../development/environment-assets.md)；此前来源记录保留为历史说明，当前配图以本条为准。

2026-10-09 肩颈、袖端与足部校准后，当前 `resident-face-detail.png`、`character-detail.png`、`wardrobe-detail.png`、`model-surface-detail.png` 分别使用 `runs/evaluation/proportion-final-{faces,characters,wardrobe,details}-20261009` 的原生第 90 帧。袖端进入可变形肩部以消除封口台阶；鹿蹄、狼爪使用连续曲面。工具握持另由 `proportion-final-hands-20261009` 第 90/150 帧和关节自检核对。配图来源以本条为准，历史实验保留。
