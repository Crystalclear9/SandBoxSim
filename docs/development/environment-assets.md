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

[Tree Small 02](https://polyhaven.com/a/tree_small_02) 由 Rico Cilliers 制作。[Fir Sapling](https://polyhaven.com/a/fir_sapling)、[Fern 02](https://polyhaven.com/a/fern_02) 和 [Grass Medium 01](https://polyhaven.com/a/grass_medium_01) 的作者和摄影/扫描分工，以及 [Shrub Sorrel 01](https://polyhaven.com/a/shrub_sorrel_01) 的作者均记录在清单中。[Forest Ground 04](https://polyhaven.com/a/forest_ground_04) 为地面提供颜色、法线和粗糙度。全套运行贴图使用 1K，32 个文件保留原始字节，并记录官方 URL 和 SHA-256。

树木高度分别归一化到 5.5 和 5.4 米；针叶树衍生自幼树素材，不代表增加新的成熟树种模拟。蕨类、草丛分别归一化为 0.43 和 0.34 米，灌木为 0.8 米。树、蕨类和草丛以地面根部为原点，灌木以几何中心为原点，以兼容现有灌木和特殊地点的放置方式。

## 转换方式

[export_environment_models.py](../../tools/export_environment_models.py) 在 Blender 后台执行，固定选择源对象和版本。阔叶树先按同位置顶点连接 UV/法线接缝，再按完整叶片单元进行稳定抽样；针叶树保留全部枝叶。木质部分独立简化，叶片连接和法线单独维护；古树林使用同一阔叶树的放大实例与根部结构，避免将草本植物误作高树冠。不会将全部叶片一起塌缩成细小残片。脚本转换坐标、三角面朝向和 UV，保留分材质表面；退化面省略，缺失角点法线从实际三角面重建。

原始 `.blend` 放在本机维护目录，来源 SHA-256 在清单和导出数据内。使用 Blender 4.5.14 LTS，关闭源文件脚本：

```powershell
$environmentBlender = 'C:/path/to/blender.exe'
& $environmentBlender --background --factory-startup --disable-autoexec --python tools/export_environment_models.py -- --source-root runs/maintenance/natural-assets-20261009 --output-root src/SandBoxSim.Godot/assets/models/environment
```

源目录应包含五个素材子目录，各自放置对应的 `<素材名>_1k.blend`。从官方页面下载原文件并核对清单中的来源哈希。贴图保持独立，重新导出模型后更新清单及 `NatureModels.ScannedEnvironment.cs` 的数据哈希；不将原始 Blender 文件或便携工具链提交为运行资源。

## 运行与绘制

`NatureModels.ScannedEnvironment.cs` 读取网格并校验哈希，建立颜色、法线、粗糙度和单独透明遮罩材质。透明叶片使用阈值裁切，避免普通透明排序造成层次错误；双面法线校正，切线由 UV 和几何生成。草叶与地面压实贴图绑定，按相机距离收缩，模型图册保持完整展示。

世界中的树、草、蕨类与图册共用网格，空间批次保留网格自身的多材质表面；不会用单一绿色覆盖树皮和叶片。树木保留导出叶片的原索引，避免自动塌缩 LOD 让树冠丢失面积；透明裁切阈值随纹理覆盖调整。蕨类、草丛和灌木继续使用屏幕 LOD，蕨类与草丛还有近景可见距离。原有稳定植被位置、火灾清理和模拟随机流保持原规则。

阔叶树和针叶树的顶点预算分别为 220,000 和 190,000，其余环境模型为 150,000；纹理/法线接缝会分裂导出顶点。提高树冠密度需要重新采样实际性能，不能仅以几何检查替代帧率记录。

`ENVIRONMENT_SOURCES_PASS` 检查五种素材的数据哈希、材质资源、有限顶点/法线、属性长度、缓存和预算；植物几何与暂停、地面观察纯度由其他自检覆盖。近景回放见 [visual-ecology-details.json](../../benchmarks/scenarios/visual-ecology-details.json)，大规模采样说明见 [性能说明](performance.md)。

## 目录与发布

环境目录包含模型 JSON、清单、许可、原始贴图和相应 `.import` 描述。Godot 的 `.godot/` 导入缓存不提交。三个桌面导出预设包含 JSON 和 TXT；贴图通过资源导入进入发行包。普通运行不读取原始下载目录，模型与许可应随发行包保留。

人物头脸的许可与转换独立记录在 [人物素材](human-assets.md)。动物、服装、建筑和地形几何仍包含项目程序网格；环境资产的更换不等同于引入完整写实人物、动物毛发或动作库。
