# 美术资源与导入

游戏使用程序生成的 3D 网格、现有材质图集及程序着色。人物、动物与房屋由真实网格表现；对象插画图集不替代 3D 模型。模型结构与扩展入口见 [模型系统](models.md)。

## 资源职责

| 路径（相对 `src/SandBoxSim.Godot/assets/`） | 当前用途 |
|---|---|
| `textures/natural-terrain.png` | 4×4 地形图集，按模拟地形混合采样 |
| `textures/natural-materials.png` | 4×4 对象材质图集，结合木纹、石层、布料及肤发表面 |
| `textures/natural-objects.png` | 保留的早期对象图集；当前 3D 人物与建筑使用网格 |
| `illustrations/ecology-projects-v1.png` | 历史生态工程插画，当前客户端不加载 |
| `gameplay/projects.json` | 历史 SDK 工程配方，当前客户端不加载或推进 |

原生成文件标识、完整提示和用途演变保存在 [素材生成来源](../archive/asset-generation.md)。这些提示是来源记录，其中的写实要求不代表现有程序化模型已达到写实品质。项目没有新增外部购买或扫描模型。

## 当前材质与几何

`NatureModels.Surface.cs` 管理木纹、石材、屋面、灰泥和植被表面；`NatureModels.CharacterSurface.cs` 管理肤发，动物材质位于 `NatureModels.Animals.cs`。纹理只提供表面颜色与细节，关节、衣服和房屋轮廓由几何定义。

`WorldView3D` 混合地形纹理、临水色差与压实，河床和水面共享连续岸线采样。`NatureModels.Meadow.cs` 提供近景弯曲草叶，读取共享压实贴图伏低并随距离缩退。CPU 网格缓存和空间批次的维护见 [性能说明](performance.md)。

## 新增与移动资源

资源文件与相应 `.import` 描述一起提交，Godot 自动导入缓存 `.godot/` 不提交。修改图集布局需要同步格子索引、UV、材质和导入设置；文件路径区分大小写，移动后同步 `res://` 引用。模型源码移动时保留对应 `.uid`。

原素材、来源记录和正式文档配图分别保存。调试截图属于 `runs/`，不能当成游戏资源替换材质。正式配图见 [图片索引](../images/README.md)，历史配图见 [归档](../archive/README.md)。

## 检查

通过 `./tools/godot.ps1 -Mode test` 构建、导入并检查网格、法线、UV、缓存与握持，再查看真实窗口中的近景和远景。无头几何检查不能判断美感，图像识别准确率也不能替代材质与解剖质量检查。

---

[文档导航](../README.md) · [项目首页](../../README.md)
