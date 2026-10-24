# 美术资源与导入

游戏使用授权的人物头脸、前臂、手部、鹿狼与 CC0 环境衍生网格、程序生成的衣身及建筑几何、现有材质图集及程序着色。人物、动物与房屋由真实网格表现；对象插画图集不替代 3D 模型。模型结构与扩展入口见 [模型系统](models.md)。

## 资源职责

| 路径（相对 `src/SandBoxSim.Godot/assets/`） | 当前用途 |
|---|---|
| `models/environment/` | Poly Haven CC0 环境网格、PBR 贴图、清单与来源记录 |
| `models/human/` | 三套头脸与原生 2K 皮肤、发片图集、前臂、手部和手部 LOD；CC BY 4.0 / CC0 许可分别列明 |
| `materials/scanned/` | 八种扫描表面、22 张 1K 原始贴图、哈希清单与许可 |
| `models/animals/cdmir-deer/`、`models/animals/0ad-wolf/` | 正式鹿狼蒙皮、源 UV 贴图、成品/源哈希与 CC0 / CC BY-SA 3.0 署名 |
| `models/garments/` | 三种项目自有连续上衣蒙皮、源几何哈希和成品清单 |
| `textures/natural-terrain.png` | 早期地形图集，保留二维地图入口；当前 3D 地表不使用 |
| `textures/natural-materials.png` | 4×4 对象材质图集，结合木纹、石层、布料及肤发表面 |
| `textures/natural-objects.png` | 早期对象图集，保留二维地图入口；当前 3D 人物与建筑使用网格 |

原生成文件标识、完整提示和用途演变保存在 [素材生成来源](../archive/asset-generation.md)。这些提示是来源记录，其中的写实要求不代表现有程序化模型已达到写实品质。人物头脸使用 MakeHuman CC0 拓扑、形体目标及皮肤／发片图集；前臂和手部沿用 Blender Studio 的 CC BY 4.0 数据，转换、来源与再分发规则见 [人物头脸素材](human-assets.md)。软件许可不替代该素材许可。

## 来源选择与调研

选择按当前公开源码项目的再分发、真实轮廓、贴图、骨架和运行预算核对，不以“免费下载”代替许可确认。以下为 2026-10-24 本轮覆盖的主要渠道，不声称穷尽全部素材网站。

| 来源 | 核对结果与本项目取舍 |
|---|---|
| [OpenGameArt / CDmir](https://opengameart.org/content/old-deer-male) | 原作标注 CC0，含鹿体态、复杂鹿角、眼部、UV 贴图和骨架；本轮采用并转成运行蒙皮 |
| [0 A.D. 美术许可](https://github.com/0ad/0ad/blob/61a3b9507d974084e6badb88a0826bd89a6d5b8b/binaries/data/mods/public/art/LICENSE.txt) | 美术按 CC BY-SA 3.0 提供；本轮采用狼网格和灰狼贴图，保留来源提交、署名及衍生资产同许可 |
| [Poly Haven](https://polyhaven.com/license) | CC0 模型、HDRI 与贴图可再分发；已有六种环境模型与扫描表面继续使用，调整岩石偏色、反光和地表混合 |
| [ambientCG](https://docs.ambientcg.com/license/) | 资产 CC0，可作为材质来源；当前已有扫描表面覆盖所需材料，本轮未再下载重复贴图 |
| MakeHuman / Blender Studio | 已采用的人体拓扑、皮肤、头发及手臂分别记录在[人物素材](human-assets.md)，继续保留；本轮修改衣装贴合、肩颈比例及皮肤反光 |
| [Sketchfab Download API](https://sketchfab.com/developers/download-api/guidelines) | 有候选写实动物，但下载及逐模型许可需按平台授权流程核对；未绕过账号流程下载 |
| [BlendSwap](https://blendswap.com/about) | 每项许可可能为 CC0、CC BY、CC BY-SA 或其他条款，下载需账号；本轮未导入未经核对的候选 |
| [Dire Wolf Lite](https://literock-studios.itch.io/direwolf-lite) | 可用于商业游戏，但作者禁止重新上传资产本身；不适合直接放入当前公开源码资源目录，未采用 |
| [Kenney UI Pack](https://kenney.nl/assets/ui-pack) | CC0 可用，但该套视觉风格与本项目自然写实方向不符；保留项目自己的图标和 Godot 控件 |
| 商业模型商店与生成模型 | 逐项核对原始资产再分发及账号条款；静态生成模型仍需重拓扑、UV、绑定与动作。本轮未购买、未把展示图当成可再分发资源 |

UI 的层级和画布工具组织参考见[观察与界面](../guides/gameplay-observation.md)。来源模型改善了轮廓与材质基础，不等同于已达到当代写实大作的毛发、面部和动画品质；实际图片与边界见[模型系统](models.md)。

## 当前材质与几何

人物近景保留同源基础上的三套头颈、耳部与面部形体，独立眼球和唇部接入现有动作，近景发片按源重心关系拟合头部。门襟和系带使用实际衣身求交挂点，腰扣是开口网格。动物使用作者提供的皮毛 UV 与贴图、独立骨架和共享网格；原 PNG 保持原字节，导入开启 mipmap。

`NatureModels.OrganicDetail.cs` 提供共享鞋楦、鞋底和皮革表面；其程序动物耳角保留作校准。衣料受力褶皱位于衣身与截面网格生成器；木材根据局部法线区分纵向纹理与端面年轮。正式动物由 `NatureModels.AuthoredAnimals.cs` 加载，旧程序头骨和毛束不叠加到来源模型。

`NatureModels.Surface.cs` 管理木纹、石材、屋面、灰泥和植被表面；`NatureModels.CharacterSurface.cs` 管理肤发，正式动物材质位于 `NatureModels.AuthoredAnimals.cs`。HUD 使用独立的炭灰平面样式、细边线和阴影，不再从木材/皮革图集提取面板纹理。金属与窗面使用独立材质，木石、布料、肤发及毛皮校准粗糙度和反光。纹理只提供表面颜色与细节，关节、衣服和房屋轮廓由几何定义。

`NatureModels.ScannedEnvironment.cs` 管理专业环境网格、透明叶片遮罩、法线与粗糙度材质，来源和转换见 [环境素材](environment-assets.md)。世界与图册共享这些网格和屏幕 LOD。`NatureModels.Botanical.cs` 保留程序植物辅助结构及几何检查。`WorldView3D.Relief.cs` 为地面提供稳定的小尺度噪声起伏。

`WorldView3D.Surface.cs` 混合扫描地形、临水色差与压实，水色读取实际河床深度，河床和水面共享连续岸线采样。`ScannedSurfaces.cs` 缓存贴图及打包法线/粗糙度，`NatureModels.ScannedCraft.cs` 管理建筑扫描表面。近景使用授权草丛网格，读取共享压实贴图伏低并随距离缩退；`NatureModels.Meadow.cs` 保留程序构造器。CPU 网格缓存和空间批次的维护见 [性能说明](performance.md)。

## 新增与移动资源

资源文件与相应 `.import` 描述一起提交，Godot 自动导入缓存 `.godot/` 不提交。修改图集布局需要同步格子索引、UV、材质和导入设置；文件路径区分大小写，移动后同步 `res://` 引用。模型源码移动时保留对应 `.uid`。非 Godot 资源类型的模型 JSON 和许可 TXT 必须进入导出预设的 `include_filter`，普通运行直接读取导出数据，不需要 Blender。

原素材、来源记录和正式文档配图分别保存。本轮调试截图属于 `.tools/tmp/<主题>/`，不能当成游戏资源替换材质。正式配图见 [图片索引](../images/README.md)，原始要求与素材来源见 [来源资料](../archive/README.md)。不再加载的生态工程插画和旧配方已从运行资源中移除。

## 检查

上衣重建使用 `./tools/bake-garments.ps1`，原始导出在 `.tools/garment-sources/`，正式 JSON 和哈希清单进入 `assets/models/garments/`；普通构建不执行 Blender。改变剪裁采样或 `tools/garment_pattern.py` 缝片构造后必须重新烘焙，不能只修改运行代码并继续使用旧蒙皮。动作、服装配件和近远景均需一同检查，细节见 [模型系统](models.md)。

通过 `./tools/godot.ps1 -Mode test` 构建、导入并检查网格、法线、UV、缓存与握持，再查看真实窗口中的近景和远景。无头几何检查不能判断美感，图像识别准确率也不能替代材质与解剖质量检查。

---

[文档导航](../README.md) · [项目首页](../../README.md)
