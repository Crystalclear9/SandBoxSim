# 原创美术素材来源与导入

资源位于 `src/SandBoxSim.Godot/assets/`，按用途分类：

| 目录 | 内容 |
|---|---|
| `textures/` | 自然地形、建筑材质与对象图集 |
| `illustrations/` | 生态工程卡片插画 `ecology-projects-v1.png` |
| `gameplay/` | 生态工程配方 `projects.json`，格式见 [生态工坊](../guides/ecology.md) |

三张自然图集与工程插画由内置 ImageGen 根据下列提示生成；未采用外部素材站图片。自然图集请求尺寸 1024×1024，实际输出均为 1254×1254，使用 4×4 图集，区域坐标按实际尺寸计算。对象图集保留 RGBA 透明通道。3D 地面与模型材质使用 mipmap 降低远景闪烁；建筑、植被和部分环境细节由 `NatureModels.cs` 构造，干预工具符号由 `ToolGlyphs.cs` 生成；概览、操作与盾形标记由 `HudSymbols.cs` 的原创 SVG 路径生成。

PNG 的 `.import` 文件与资源一起存放，Godot 的生成缓存位于忽略提交的 `.godot/`。修改资源位置时，需要同步源码中的 `res://` 路径和导入设置。

界面皮革底纹复用材质图集，`HudStyle.cs` 的 Canvas 着色器降低纹理对比，`HudBevel.cs` 绘制双层细线、旧铜边角、中央铜饰与微弱高光。`HudButtonDetail.cs` 是不接收鼠标事件的绘制层，只在交互期间渐变，不改变布局或命中范围。面板、按钮与图标沿用项目自己的资源，界面布局参考见 [观察与界面](../guides/gameplay-observation.md)。

## 模型细节

房屋、居民、鹿狼、植被和地貌共用地图中的实际网格，结构及扩展入口见 [3D 模型系统](models.md)。灰泥、砌石、屋面与叶片使用低对比图集细节和程序化表面，人物布料与动物毛发按屏幕采样范围抑制细纹。模型来源仍是项目内构造的几何，没有新增外部商业模型。

## 建筑展示

`BuildingPortrait.cs` 在透明背景的独立 SubViewport 中渲染 `NatureModels.Buildings.cs` 的真实模型。图册卡片与地图共用建筑生成规则，悬停转向与材料显示分别由表现层和建造配方提供。展示模型不进入模拟世界；住房卡片代表一种变体。静止后缓存画面，避免持续绘制每张图册。

## 3D 居民

`NatureModels.Residents.cs` 构造服装截面网格、面部、腰带、鞋靴与职业配件；不以旧对象图集的居民格替代 3D 人物。`ResidentRig.cs` 提供肩肘、髋膝和躯干姿态，`ResidentPortrait.cs` 提供独立展示镜头与交互。布料和肤色材质共享，静态部件按材质合并并缓存，活动关节保留独立节点。

这是项目内的程序化模型系统，服装、关节和职业配件可从源码扩展。模型材质和动画只影响表现，不改变居民行为与模拟数值。

`NatureModels.Sculpture.cs` 提供连续的下颌、颧骨与鼻梁曲面，以及具有前额发线的头发网格。服装采用多层截面与平滑法线，布料着色器生成细微织纹，并按屏幕采样范围减弱远景细纹，以降低闪烁。曲面、网格和材质仍共享缓存。

## natural-objects.png

模式：generate；无参考输入图。

生成文件：`exec-f88ccc9e-a1ff-4d71-bbea-519cb484e4f8.png`，位于生成图片目录 `01a101f9-d25d-7aa2-98a5-ad6b27e23013`。

最终提示：

```text
Use case: stylized-concept. Asset type: a production realistic sprite atlas for a nature and civilization sandbox PC game. Natural realistic art direction, NOT pixel art, NOT cartoons, NOT emoji, NOT isometric game UI. Create a 1024x1024 transparent RGBA atlas, EXACT 4 columns and 4 rows of invisible 256x256 cells. One separate isolated detailed object centered within each cell with large transparent padding, no borders, no text. Consistent aerial three-quarter view, looking down steeply at 65 degrees, realistic photographic matte miniature diorama materials, subtle warm daylight from upper left, realistic natural colors, restrained color saturation, consistent scale appropriate for overhead world-map game objects. Row1: mature broadleaf oak tree with irregular leaf canopy; tall conifer pine; natural weathered grey granite rock cluster; dark metallic iron ore rock cluster with oxidized ochre fissures. Row2: rustic medieval timber-and-stone cottage with brown clay tiled roof; wooden storage barn with barrels/crates; small square cultivated farm wheat patch and earth furrows; wooden mine entrance with railcart and rough stone. Row3: medieval villager in beige linen full-body; villager in muted blue wool full-body; farmer in ochre linen straw hat full-body; guard in muted metal armor spear full-body. Row4: realistic standing deer antlers full body; realistic grey wolf full body; lush berry shrub with red berries; burning small bonfire with realistic orange flames. All objects bounded inside their own cell, especially trees entirely fit. No scenery or floor joining cells. Genuine transparency between and around all sprites. Render ready assets with recognizable detailed silhouettes at 32 to 64 px. Realism and material richness, no thick outlines or cute exaggerated proportions.
```

## natural-terrain.png

模式：generate；无参考输入图。

生成文件：`exec-d78b5a5d-656e-492b-a67a-d375b833cfc1.png`，位于生成图片目录 `01a101f9-d25d-7aa2-98a5-ad6b27e23013`。

最终提示：

```text
Use case: stylized-concept. Asset type: realistic terrain textures atlas for top-down ecological sandbox simulation game. Produce a 1024x1024 square texture atlas EXACTLY divided into a regular 4 by 4 grid of 256x256 texture squares, NO borders, NO labels, NO text, NO transparency. Strict directly overhead orthographic camera. Each square is a flat continuous material texture that can tile, no objects with drop shadows, no perspective, no buildings, no paths crossing cell boundaries. Natural realistic photographic painted materials, rich fine surface details yet understated contrast to not distract from sprites. Row1 left to right: lush medium meadow grass; mossy dark forest floor with leaf litter; clear blue green river water ripples; grey weathered mountain stone. Row2: pale natural sand; brown tilled earth agricultural fine furrows; dry compacted earthy footpath gravel; soft white grey snow. Row3: dark wet marsh moss muddy earth; ochre arid desert earth; dark volcanic rock glowing fine magma fissures; dry yellow-green grass. Row4: lush clover meadow; pebble riverbed; charcoal ash burned earth; rich loamy fertile soil. Cohesive natural realistic visual style for aerial 2D game, restrained saturation, realistic textures not pixel art or cartoon, slight warm daylight, absolutely no illustration framing. Details make geography feel alive but textures look good scaled down to tiny tiles.
```

## natural-materials.png

模式：generate；无参考输入图。

生成文件：`exec-e549fd8d-e10c-4477-a077-31dccb24d8ac.png`，位于生成图片目录 `01a101f9-d25d-7aa2-98a5-ad6b27e23013`。

最终提示：

```text
Use case: stylized-concept. Asset type: high quality realistic PBR-like albedo material atlas used on real 3D trees, timber buildings, roofs, humans and wildlife in a natural sandbox game. 1024x1024 square image. Exact regular 4x4 grid of sixteen 256x256 square independent texture swatches, no gaps, no borders, no text. Strictly flat seamless material textures, diffuse color only, no lighting gradients, no cast shadows, no object perspective. Natural photographic materials, fine detail, cohesive restrained earthy palette. Row1: rough brown oak bark vertical grain; weathered brown wood boards; muted red-brown clay roof tiles; cream rough lime plaster. Row2: rich layered natural green leaf surface clusters; dark pine needles dense texture; natural rough grey granite; oxidized dark iron flecked mineral surface. Row3: beige woven linen cloth; muted indigo-blue wool cloth; dark brown worn leather; grey brushed forged steel. Row4: tawny short deer fur; silver grey wolf fur; pale subtle realistic skin texture without features; dry golden wheat stalk texture. Each cell fills exactly its grid square and contains only continuous material. No trees, no people, no animals, no logos. These textures will be applied to meshes with real lighting and 3D geometry, so do not pre-render objects or fake lighting.
```

## ecology-projects-v1.png

用途：生态工坊图册和落点预览的主题插画。使用内置 imagegen 工具生成，没有参考输入图；不使用 CLI。

项目文件：src/SandBoxSim.Godot/assets/illustrations/ecology-projects-v1.png。1536 × 1024，按中心切分四个 768 × 512 区域，由 Godot AtlasTexture 读取；没有修改原图。素材展示主题，地图和结果来自真实模拟。

原生成文件：exec-c77de940-6f1d-41aa-9a9f-52c63deee27b.png，位于生成图片目录 01a101f9-d25d-7aa2-98a5-ad6b27e23013。左上粮地、右上走廊、左下湿地、右下森林。迁徙通道复用走廊插画，林粮镶嵌复用粮地插画。

最终提示：

Create a single production-ready landscape illustration atlas for a nature sandbox simulation game. Image is a seamless 2 by 2 grid of exactly four equal rectangular illustrations, clean straight center boundaries with no gutters, no frames, no labels, NO text, no UI, no logos. Each quadrant must be a separate self-contained landscape composition. Top left: a natural river valley with a small fertile wild meadow, grasses, berry bushes and modest food-growing patches in golden dawn light. Top right: a restrained rural woodland with a narrow cleared firebreak path crossing forest, rocky earth, hazy reddish late afternoon. Bottom left: intricate restored wetlands with reeds, rippling shallow pools, moss, soft mist and reflected silver-green light. Bottom right: a recovering woodland with young saplings, mature pines, ferns and layered undergrowth in deep forest light. Unified refined naturalistic hand-painted environment concept art, sophisticated muted olive greens, charcoal shadows, ochre sunlight, soft realistic atmospheric depth, exceptionally detailed organic vegetation and soils. Quiet, evocative, believable ecosystem scenes without people or magical objects. All four landscapes seen from low oblique environmental viewpoint, NOT overhead maps. The atlas will be sliced at exact center into four illustrated project selection thumbnails in a real Godot client. Make clear identifiable landscape silhouettes even at small thumbnail size. The image must be the art only, not a screenshot or concept UI.

## 荒野地貌模型

`NatureModels.WildPlaces.cs` 构造岩环泉眼、果丛、盘根古树、残墙拱门与矿石露头，复用现有自然材质。静态部件按材质合并并缓存。野果与古树林外观读取生长阶段，遗迹散石读取是否仍有物资，改造或损毁后模型跟随真实条件消失。这里不使用任务标记或领取奖励的图标。

静态部件合并保留顶点、法线与 UV；地貌模型自检核对纹理坐标数量，避免合并后采样固定位置而显示为纯色块。
