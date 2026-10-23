# 环境素材与转换

环境使用 Poly Haven 的 CC0 素材衍生网格及原始 PBR 贴图。来源和许可见 [Poly Haven](https://polyhaven.com/license)；记录保存在 [环境素材清单](../../src/SandBoxSim.Godot/assets/models/environment/manifest.json) 与 [LICENSE.txt](../../src/SandBoxSim.Godot/assets/models/environment/LICENSE.txt)。运行时直接读取仓库中的数据，不访问网络，也不需要 Blender。

## 当前模型

| 素材 | 导出三角面 | 导出顶点 |
|---|---:|---:|
| `tree_small_02` | 133,749 | 209,113 |
| `fir_sapling` | 150,876 | 179,580 |
| `fern_02` | 2,384 | 1,590 |
| `grass_medium_01` | 403 | 444 |
| `shrub_sorrel_01` | 990 | 852 |
| `boulder_01` | 6,000 | 4,403 |

[Tree Small 02](https://polyhaven.com/a/tree_small_02) 由 Rico Cilliers 制作。[Fir Sapling](https://polyhaven.com/a/fir_sapling)、[Fern 02](https://polyhaven.com/a/fern_02) 和 [Grass Medium 01](https://polyhaven.com/a/grass_medium_01) 的作者和摄影/扫描分工，以及 [Shrub Sorrel 01](https://polyhaven.com/a/shrub_sorrel_01) 的作者均记录在清单中。[Forest Ground 04](https://polyhaven.com/a/forest_ground_04) 为地面提供颜色、法线和粗糙度。全套运行贴图使用 1K，35 个文件保留原始字节，并记录官方 URL 和 SHA-256。

[Boulder 01](https://polyhaven.com/a/boulder_01) 由 Rico Cilliers 制作，使用扫描几何与原始颜色、OpenGL 法线和粗糙度贴图。运行网格取 LOD2 简化为 6,000 三角面、高度 0.9 米，保留裂隙和矿物色斑；石块材质禁用植物风动。世界中的普通山石与图册共用它，矿石与小碎石仍使用项目程序网格。

树木高度分别归一化到 5.5 和 5.4 米；针叶树衍生自幼树素材，不代表增加新的成熟树种模拟。蕨类、草丛分别归一化为 0.43 和 0.34 米，灌木为 0.8 米。树、蕨类和草丛以地面根部为原点，灌木以几何中心为原点，以兼容现有灌木和特殊地点的放置方式。

## 扫描表面材质

地表、建筑与衣料使用独立的可平铺扫描表面，清单在 [materials/scanned/manifest.json](../../src/SandBoxSim.Godot/assets/materials/scanned/manifest.json)，署名在 [LICENSE.txt](../../src/SandBoxSim.Godot/assets/materials/scanned/LICENSE.txt)。八种素材均来自 Poly Haven、采用 CC0，保留 22 张 1K 原始贴图的官方 URL、尺寸、文件大小及 SHA-256：

| 素材 | 用途 | 原始覆盖宽度 |
|---|---|---:|
| `grass_ground` | 草地颜色、法线、粗糙度 | 2.51 米 |
| `brown_mud_leaves_01` | 土壤、湿岸与踩踏区域 | 1.3 米 |
| `rock_face_03` | 裸坡与石作表面 | 2.7 米 |
| `wood_planks_grey` | 木墙与梁柱 | 1.5 米 |
| `plastered_wall_03` | 灰泥墙 | 4 米 |
| `coast_sand_01` | 沙地颜色 | 15 米 |
| `reed_roof_03` | 连续茅草屋面的颜色、法线、粗糙度 | 2.5 米 |
| `rough_linen` | 衣身、衣袖与领口的亚麻织纹 | 0.27 米 |

`rough_linen` 的 1K 原图为 1024×1026；清单以 `pixelSize` 保留真实尺寸，加载器按此校验。其摄影为 colormass、处理为 Rico Cilliers。

沙地只引入颜色图，沿用通用地表微法线；覆盖宽度是来源记录，显示混合可另行缩放。林地继续使用已有的 `forest_ground_04`。`boulder_01` 的贴图对应岩石自身 UV，不能拿来平铺山坡或墙面。

茅草屋面使用 Rob Tuytel 的 [Reed Roof 03](https://polyhaven.com/a/reed_roof_03) 原始 1K 贴图，裁取草束覆盖区域避开来源顶部的压条；`NatureModels.Thatch.cs` 将它接到连续厚壳、参差檐口和散落草梗。材质不再使用周期正弦条带模拟草束，瓦片屋面仍使用独立几何和材质。

`ScannedSurfaces.cs` 共享加载缓存，并把法线 XY 与粗糙度打包为一张带 mipmap 的运行纹理；原始贴图字节不改动。`WorldView3D.Surface.cs` 的地表材质使用 12 个采样器，按坡度、湿度、岸线和压实混合，陡坡岩面采用三向投影。`NatureModels.ScannedCraft.cs` 为建筑应用木、石、灰泥表面，屋瓦保留独立材质。纹理不会新增真实几何位移或室内结构。

运行不访问网络。重取来源与离线核对分别执行：

```powershell
python tools/fetch-surface-materials.py
python tools/fetch-surface-materials.py --verify
```

下载及元数据缓存仅在 `.tools/sources/surface-materials/`；核对大小及官方 MD5 后才复制到运行目录，清单再记录 SHA-256。CI 只运行 `--verify`，不重新下载。

## 网格转换方式

[export_environment_models.py](../../tools/export_environment_models.py) 在 Blender 后台执行，固定选择源对象和版本。阔叶树先按同位置顶点连接 UV/法线接缝，再按完整叶片单元进行稳定抽样；针叶树保留全部枝叶。木质部分独立简化，叶片连接和法线单独维护；古树林使用同一阔叶树的放大实例与根部结构，避免将草本植物误作高树冠。不会将全部叶片一起塌缩成细小残片。脚本转换坐标、三角面朝向和 UV，保留分材质表面；退化面省略，缺失角点法线从实际三角面重建。

原始 `.blend` 放在本机维护目录，来源 SHA-256 在清单和导出数据内。使用 Blender 4.5.14 LTS，关闭源文件脚本：

```powershell
$environmentBlender = './.tools/blender-4.5.14/editor/blender-4.5.14-windows-x64/blender.exe'
& $environmentBlender --background --factory-startup --disable-autoexec --python tools/export_environment_models.py -- --source-root .tools/sources --output-root src/SandBoxSim.Godot/assets/models/environment
```

源目录应包含六个素材子目录，各自放置对应的 `<素材名>_1k.blend`。从官方页面下载原文件并核对清单中的来源哈希。只更新岩石时追加 `--asset boulder_01`，其他素材保持原数据。贴图保持独立，重新导出模型后更新清单及 `NatureModels.ScannedEnvironment.cs` 的数据哈希；不将原始 Blender 文件或便携工具链提交为运行资源。

## 运行与绘制

`NatureModels.ScannedEnvironment.cs` 读取网格并校验哈希，建立颜色、法线、粗糙度和单独透明遮罩材质。透明叶片使用阈值裁切，避免普通透明排序造成层次错误；双面法线校正，切线由 UV 和几何生成。草叶与地面压实贴图绑定，按相机距离收缩，模型图册保持完整展示。

世界中的树、草、蕨类与图册共用网格，空间批次保留网格自身的多材质表面；不会用单一绿色覆盖树皮和叶片。树木保留导出叶片的原索引，避免自动塌缩 LOD 让树冠丢失面积；透明裁切阈值随纹理覆盖调整。蕨类、草丛和灌木继续使用屏幕 LOD，蕨类与草丛还有近景可见距离。新增河岸芦苇为项目自建网格，使用项目许可，形体和分布见 [模型系统](models.md)。原有稳定植被位置、火灾清理和模拟随机流保持原规则。

阔叶树和针叶树的顶点预算分别为 220,000 和 190,000，其余环境模型为 150,000；纹理/法线接缝会分裂导出顶点。提高树冠密度需要重新采样实际性能，不能仅以几何检查替代帧率记录。

`ENVIRONMENT_SOURCES_PASS` 检查六种素材的数据哈希、材质资源、有限顶点/法线、属性长度、缓存和预算；植物几何与暂停、地面观察纯度由其他自检覆盖。近景回放见 [visual-ecology-details.json](../../benchmarks/scenarios/visual-ecology-details.json)，大规模采样说明见 [性能说明](performance.md)。

## 目录与发布

环境目录包含模型 JSON、清单、许可、原始贴图和相应 `.import` 描述。Godot 的 `.godot/` 导入缓存不提交。三个桌面导出预设包含 JSON 和 TXT；贴图通过资源导入进入发行包。普通运行不读取原始下载目录，模型与许可应随发行包保留。

人物头脸的许可与转换独立记录在 [人物素材](human-assets.md)。动物、服装、建筑和地形几何仍包含项目程序网格；环境资产的更换不等同于引入完整写实人物、动物毛发或动作库。
