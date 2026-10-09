# 美术资源与导入

游戏使用授权的人物头脸与 CC0 环境衍生网格、程序生成的衣身及建筑几何、现有材质图集及程序着色。人物、动物与房屋由真实网格表现；对象插画图集不替代 3D 模型。模型结构与扩展入口见 [模型系统](models.md)。

## 资源职责

| 路径（相对 `src/SandBoxSim.Godot/assets/`） | 当前用途 |
|---|---|
| `models/environment/` | Poly Haven CC0 环境网格、PBR 贴图、清单与来源记录 |
| `models/human/human-head.mesh.json`、`models/human/LICENSE.txt` | Blender Studio 头脸衍生数据与 CC BY 4.0 署名 |
| `textures/natural-terrain.png` | 4×4 地形图集，按模拟地形混合采样 |
| `textures/natural-materials.png` | 4×4 对象材质图集，结合木纹、石层、布料及肤发表面 |
| `textures/natural-objects.png` | 保留的早期对象图集；当前 3D 人物与建筑使用网格 |
| `illustrations/ecology-projects-v1.png` | 历史生态工程插画，当前客户端不加载 |
| `gameplay/projects.json` | 历史 SDK 工程配方，当前客户端不加载或推进 |

原生成文件标识、完整提示和用途演变保存在 [素材生成来源](../archive/asset-generation.md)。这些提示是来源记录，其中的写实要求不代表现有程序化模型已达到写实品质。人物头脸新增 Blender Studio 的 CC BY 4.0 基础网格衍生数据，转换、来源与再分发规则见 [人物头脸素材](human-assets.md)。软件许可不替代该素材许可。

## 当前材质与几何

人物近景保留基础网格的头颈、耳部与面部解剖拓扑，独立眼球和唇部接入现有动作，发壳按实际头颅贴合。门襟和系带使用实际衣身求交挂点，腰扣是开口网格。动物毛皮在共享材料中加入微小法线起伏，随屏幕采样减弱；未增加独立装饰模拟实体。

`NatureModels.OrganicDetail.cs` 提供共享鞋楦、鞋底、带内凹耳窝的动物耳朵与渐细鹿角，并管理皮革表面。衣料受力褶皱位于衣身与截面网格生成器，动物胸颈背是连续曲面；木材根据局部法线区分纵向纹理与端面年轮。这些部位继续使用程序网格；外部头脸素材单独记录来源和许可。

`NatureModels.Surface.cs` 管理木纹、石材、屋面、灰泥和植被表面；`NatureModels.CharacterSurface.cs` 管理肤发，动物材质位于 `NatureModels.Animals.cs`。HUD 使用独立的炭灰平面样式、细边线和阴影，不再从木材/皮革图集提取面板纹理。金属与窗面使用独立材质，木石、布料、肤发及毛皮校准粗糙度和反光。纹理只提供表面颜色与细节，关节、衣服和房屋轮廓由几何定义。

`NatureModels.ScannedEnvironment.cs` 管理专业环境网格、透明叶片遮罩、法线与粗糙度材质，来源和转换见 [环境素材](environment-assets.md)。世界与图册共享这些网格和屏幕 LOD。`NatureModels.Botanical.cs` 保留程序植物辅助结构及几何检查。`WorldView3D.Relief.cs` 为地面提供稳定的小尺度噪声起伏。

`WorldView3D` 混合地形纹理、临水色差与压实，河床和水面共享连续岸线采样。`NatureModels.Meadow.cs` 提供近景弯曲草叶，读取共享压实贴图伏低并随距离缩退。CPU 网格缓存和空间批次的维护见 [性能说明](performance.md)。

## 新增与移动资源

资源文件与相应 `.import` 描述一起提交，Godot 自动导入缓存 `.godot/` 不提交。修改图集布局需要同步格子索引、UV、材质和导入设置；文件路径区分大小写，移动后同步 `res://` 引用。模型源码移动时保留对应 `.uid`。非 Godot 资源类型的模型 JSON 和许可 TXT 必须进入导出预设的 `include_filter`，普通运行直接读取导出数据，不需要 Blender。

原素材、来源记录和正式文档配图分别保存。调试截图属于 `runs/`，不能当成游戏资源替换材质。正式配图见 [图片索引](../images/README.md)，历史配图见 [归档](../archive/README.md)。

## 检查

通过 `./tools/godot.ps1 -Mode test` 构建、导入并检查网格、法线、UV、缓存与握持，再查看真实窗口中的近景和远景。无头几何检查不能判断美感，图像识别准确率也不能替代材质与解剖质量检查。

---

[文档导航](../README.md) · [项目首页](../../README.md)
