# 原创美术素材来源与导入

三张 PNG 由内置 ImageGen 根据项目提示生成，再原样复制到 assets；未采用外部素材站图片。请求尺寸 1024×1024，实际输出均为 1254×1254。使用 4×4 图集，区域坐标按实际尺寸计算。对象图集保留 RGBA 透明通道。3D 地面与模型材质启用前三层 mipmap，降低远景闪烁；HUD 符号由 ToolGlyphs.cs 的原创 SVG 路径实时生成。

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

项目文件：src/SandBoxSim.Godot/assets/Art/ecology-projects-v1.png。1536 × 1024，按中心切分四个 768 × 512 区域，由 Godot AtlasTexture 读取；没有修改原图。素材展示主题，地图和结果来自真实模拟。

原生成文件：exec-c77de940-6f1d-41aa-9a9f-52c63deee27b.png，位于生成图片目录 01a101f9-d25d-7aa2-98a5-ad6b27e23013。左上粮地、右上走廊、左下湿地、右下森林。迁徙通道复用走廊插画，林粮镶嵌复用粮地插画。

最终提示：

Create a single production-ready landscape illustration atlas for a nature sandbox simulation game. Image is a seamless 2 by 2 grid of exactly four equal rectangular illustrations, clean straight center boundaries with no gutters, no frames, no labels, NO text, no UI, no logos. Each quadrant must be a separate self-contained landscape composition. Top left: a natural river valley with a small fertile wild meadow, grasses, berry bushes and modest food-growing patches in golden dawn light. Top right: a restrained rural woodland with a narrow cleared firebreak path crossing forest, rocky earth, hazy reddish late afternoon. Bottom left: intricate restored wetlands with reeds, rippling shallow pools, moss, soft mist and reflected silver-green light. Bottom right: a recovering woodland with young saplings, mature pines, ferns and layered undergrowth in deep forest light. Unified refined naturalistic hand-painted environment concept art, sophisticated muted olive greens, charcoal shadows, ochre sunlight, soft realistic atmospheric depth, exceptionally detailed organic vegetation and soils. Quiet, evocative, believable ecosystem scenes without people or magical objects. All four landscapes seen from low oblique environmental viewpoint, NOT overhead maps. The atlas will be sliced at exact center into four illustrated project selection thumbnails in a real Godot client. Make clear identifiable landscape silhouettes even at small thumbnail size. The image must be the art only, not a screenshot or concept UI.
