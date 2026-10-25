# 美术资源与导入

游戏使用授权的人物头脸、前臂、手部、鹿狼与 CC0 环境衍生网格、程序生成的衣身及建筑几何、现有材质图集及程序着色。人物、动物与房屋由真实网格表现；对象插画图集不替代 3D 模型。模型结构与扩展入口见 [模型系统](models.md)。

## 资源职责

| 路径（相对 `src/SandBoxSim.Godot/assets/`） | 当前用途 |
|---|---|
| `models/environment/` | Poly Haven CC0 环境网格、PBR 贴图、清单与来源记录 |
| `models/human/` | 三套头脸与原生 2K 皮肤、发片图集、前臂、手部和手部 LOD；CC BY 4.0 / CC0 许可分别列明 |
| `materials/scanned/` | 八种扫描表面、22 张 1K 原始贴图、哈希清单与许可 |
| `models/animals/cdmir-deer/`、`models/animals/0ad-wolf/` | 正式鹿狼蒙皮、源 UV 贴图、成品/源哈希与 CC0 / CC BY-SA 3.0 署名 |
| `runtime/` | Mozilla CA 证书包、固定源文件链接与哈希、MPL 2.0 许可；供游戏与项目内便携编辑器的 TLS 使用 |
| `models/garments/` | 三种项目自有连续上衣蒙皮、源几何哈希和成品清单 |
| `textures/natural-terrain.png` | 早期地形图集，保留二维地图入口；当前 3D 地表不使用 |
| `textures/natural-materials.png` | 4×4 对象材质图集，结合木纹、石层、布料及肤发表面 |
| `textures/natural-objects.png` | 早期对象图集，保留二维地图入口；当前 3D 人物与建筑使用网格 |

原生成文件标识、完整提示和用途演变保存在 [素材生成来源](../archive/asset-generation.md)。这些提示是来源记录，其中的写实要求不代表现有程序化模型已达到写实品质。人物头脸使用 MakeHuman CC0 拓扑、形体目标及皮肤／发片图集；前臂和手部沿用 Blender Studio 的 CC BY 4.0 数据，转换、来源与再分发规则见 [人物头脸素材](human-assets.md)。软件许可不替代该素材许可。

## 来源选择与调研

选择按当前公开源码项目的再分发、真实轮廓、贴图、骨架和运行预算核对，不以“免费下载”代替许可确认。以下列出截至 2026-10-25 已核对的主要渠道，不声称穷尽全部素材网站。

| 来源 | 核对结果与本项目取舍 |
|---|---|
| [OpenGameArt / CDmir](https://opengameart.org/content/old-deer-male) | 原作标注 CC0，含鹿体态、复杂鹿角、眼部、UV 贴图和骨架；已采用并转成运行蒙皮 |
| [0 A.D. 美术许可](https://github.com/0ad/0ad/blob/61a3b9507d974084e6badb88a0826bd89a6d5b8b/binaries/data/mods/public/art/LICENSE.txt) | 美术按 CC BY-SA 3.0 提供；已采用狼网格和灰狼贴图，保留来源提交、署名及衍生资产同许可 |
| [Poly Haven](https://polyhaven.com/license) | CC0 模型、HDRI 与贴图可再分发；已有六种环境模型与扫描表面继续使用，采用 Leafy Grass 地表，校准岩石反光与地表混合 |
| [ambientCG](https://docs.ambientcg.com/license/) | 资产 CC0，可作为材质来源；当前已有扫描表面覆盖所需材料，本轮未再下载重复贴图 |
| MakeHuman / Blender Studio | 已采用的人体拓扑、皮肤、头发及手臂分别记录在[人物素材](human-assets.md)，继续保留；当前衣领按人体留出开口，调整衣装贴合及皮肤反光 |
| [Sketchfab Download API](https://sketchfab.com/developers/download-api/guidelines) | 有候选写实动物，但下载及逐模型许可需按平台授权流程核对；未绕过账号流程下载 |
| [BlendSwap wolf fur](https://blendswap.com/blend/13997) | 具体候选标注 CC BY-SA，约 21.6 MB，使用 Blender 2.7 粒子毛发；下载有账号流程，粒子表现不能直接代替 Godot 运行网格，未导入 |
| [Open Wolf](https://opengameart.org/content/open-wolf) | 静态网格，标注 LGPL 2.1 / 3.0；缺少直接替换所需的骨架与近景优势，未采用 |
| [Blendkit 许可](https://www.blendkit.com/docs/licenses/) | 区分 CC0 与 Royalty Free；选用时需确认具体资产许可，不能把“可商业使用”当作可公开分发源文件 |
| [Dire Wolf Lite](https://literock-studios.itch.io/direwolf-lite) | 作者列出 35 骨骼、9,991 面和 2K PBR，可用于游戏但禁止重新上传资产本身；不适合放入公开源码资源目录，未采用 |
| [Innerscene Gray wolf](https://www.innerscene.com/tools/library/3d-parts/gray-wolf-c0fab254) | 原站标注 CC0；核对可下载 GLB 后为 32 个网格、没有 skin 或动画，不能直接替换现有运动动物，未采用 |
| [NewDLC 3d wolf](https://opengameart.org/content/3d-wolf) | 原站 CC0，提供带骨架 Blender/FBX；核对原颜色图后为手绘卡通毛色，不符合当前自然写实方向，未采用 |
| [Kenney UI Pack](https://kenney.nl/assets/ui-pack) | CC0 可用，但该套视觉风格与本项目自然写实方向不符；保留项目自己的图标和 Godot 控件 |
| 商业模型商店与生成模型 | 逐项核对原始资产再分发及账号条款；静态生成模型仍需重拓扑、UV、绑定与动作。本轮未购买、未把展示图当成可再分发资源 |

UI 的层级和画布工具组织参考见[观察与界面](../guides/gameplay-observation.md)。来源模型改善了轮廓与材质基础，不等同于已达到当代写实大作的毛发、面部和动画品质；实际图片与边界见[模型系统](models.md)。

## 表现与交互依据

| 参考 | 当前实现 |
|---|---|
| [NVIDIA GPU Gems：复杂地形](https://developer.nvidia.com/gpugems/gpugems3/part-i-geometry/chapter-1-generating-complex-procedural-terrains-using-gpu) | 三向材质的法线在各投影切线方向还原；米制细节与远景采样分开控制。项目仍使用共享高度曲面，没有声称实现该文的 GPU 体素地形 |
| [Boubekeur 与 Alexa：Phong Tessellation](https://perso.telecom-paristech.fr/boubek/papers/PhongTessellation/) | 借鉴顶点切平面投影思想，对选定动物头骨边进行一次性 CPU 细分；不是完整论文实现或硬件细分管线 |
| [Godot：Lights and shadows](https://docs.godotengine.org/en/stable/tutorials/3d/lights_and_shadows.html) | 实机对照阴影自遮挡与接触投影，世界方向光深度偏移采用 2.4，保留级联阴影；具体值是当前场景的实现选择 |
| [Blender：Bevel Modifier](https://docs.blender.org/manual/en/4.4/modeling/modifiers/generate/bevel.html) | 参考绝对尺寸与避免倒角相交的原则，运行时直接按米制尺寸构造木石边缘，不需要在玩家设备中安装 Blender |
| [Godot：Spatial shaders](https://docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/spatial_shader.html) | 分清顶点、法线、切线和片元坐标空间；扫描植物的风摆同时更新表面框架。兼容渲染器使用有限薄层背光，不声明完整皮下散射 |
| [RHS：Parts of Flowering Plants](https://www.rhs.org.uk/education-learning/school-gardening/resources/curriculum-linked/flower-structure-diagram) | 区分萼片、花瓣、花丝和花药，在通用草花中分开建模；数量、尺度和简化属于项目取舍 |
| [WDFW：Wolf identification](https://wdfw.wa.gov/species-habitats/at-risk/species-recovery/gray-wolf/identification) | 对照真实狼的口鼻、耳部与轮廓参考；保留授权源动物轮廓，补充短触须，没有声称复原完整毛发系统 |
| [Godot：Standard Material 3D](https://docs.godotengine.org/en/stable/tutorials/3d/standard_material_3d.html) | 依据透明裁切、法线和粗糙度的实际用途处理叶片／发片、皮革、木石；检查真实纹理缓存，避免仅有配置而没有 mip 链 |
| [NN/g：渐进呈现](https://www.nngroup.com/articles/progressive-disclosure/) | 世界画面保留主要控件，工具与资料按需展开；页签共享间距、字重、选中与悬停反馈，原有鼠标和键盘操作保持可见 |
| [Historic England：茅草屋面](https://historicengland.org.uk/advice/technical-advice/buildings/thatching-advice) | 观察屋脊、檐口和山墙边缘的独立结构，采用厚坡面与圆弧覆脊；项目尺寸是视觉取舍，不作为传统工艺复原 |
| [Missouri Department of Conservation：溪流岸缘](https://mdc.mo.gov/discover-nature/habitats/stream-edges) | 区分临水低岸与较高岸缘，普通河岸芦苇按实际高度和坡度选点；具体数值是项目表现参数 |
| [NN/g：识别与回忆](https://www.nngroup.com/articles/recognition-and-recall/) | 操作提示靠近当前工作区，镜头角度、距离和跟随状态可直接查看；未选择居民时禁用跟随并说明原因 |

这些资料用于明确实现方法；素材页、许可与作者记录用于实际资源来源。界面没有引入网页运行时；字体作为带许可的正式资源随包提供。

衣料法线投影继续参考 [NVIDIA 三向法线说明](https://docs.omniverse.nvidia.com/extensions/latest/ext_material/ext_material-graph/nodes/Texturing_High_Level/triplannar_normalmap_texture.html)，按各投影方向和法线变换分别处理；具体数值是本项目参数。草花外观参考 [RHS 草地草花资料](https://www.rhs.org.uk/plants/7914/geranium-pratense/details) 的开放花冠层次，模型为通用五瓣造型，没有赋予该物种标签。

选中居民的快捷操作参考 [NN/g 上下文操作组织](https://www.nngroup.com/articles/contextual-menus/)，把靠近和档案与当前居民放在一起，右键继续用于相机旋转。依据 [Godot RenderingServer 说明](https://docs.godotengine.org/en/stable/classes/class_renderingserver.html)，无窗口模式的部分渲染查询可能返回占位数据，因此实例位置在 CPU 维护，图形模式再校验 GPU 数据。

## 各类对象的表现依据

| 对象 | 参考与当前处理 |
|---|---|
| 场景与水岸 | [Godot 空间着色器](https://docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/spatial_shader.html)：水面位移和光照法线使用同组波相位，临岸根据河床深度衰减；维持现有连续地形和实际落点采样 |
| 建筑 | [Historic England 窗户维护](https://historicengland.org.uk/advice/your-home/maintain-repair/windows/)：保留门窗与墙体的构造层次，近景窗台有外倾顶面、前缘倒角和下缘滴水槽；这是项目几何表现，不模拟排水施工 |
| 人物 | [MakeHuman 身体附件](https://ww.makehumancommunity.org/mpfb/docs/characters/bodyparts.html)：眼球、眼睑与睫毛分别处理。睫毛取根于现有眼裂曲线，和原眼睑共用闭合时序；眼球、嘴唇与皮肤分别控制粗糙度 |
| 动物 | [British Deer Society 鹿类识别](https://bds.org.uk/information-advice/about-deer/deer-species/identify/)与[Wisconsin DNR 灰狼](https://dnr.wisconsin.gov/topic/WildlifeHabitat/wolf)：保留来源模型的分枝鹿角、头颈和短耳轮廓，细节使用来源表面定位；近景增加耳内短毛与角膜／虹膜，未替换成通用球眼 |
| 植物 | [RHS 普通芦苇](https://www.rhs.org.uk/plants/54026/phragmites-australis/details)与[Kew 野莓](https://growwild.kew.org/plants/bramble)：芦苇采用分枝圆锥花序，莓果由多个小果粒组成并保留萼片；当前株高和斑块尺度仍为游戏表现比例，不作为物种识别真值 |
| UI | [NN/g 系统状态可见性](https://www.nngroup.com/articles/visibility-system-status/)：时间组显示暂停或运行倍率；工具标题、用途和选中状态分别表达，提示卡左对齐并保持鼠标操作入口 |

原树木、岩石、头脸与动物的源文件及许可继续沿用上方记录。细节增加并不代表所有资产已重新制作：狼的原始颜色贴图仍为 256×256，细分和程序虹膜无法补回来源贴图缺失的照片细节；当前没有完整动物眼睑闭合或毛发体积模拟。

## 当前材质与几何

人物近景保留同源基础上的三套头颈、耳部与面部形体，独立眼球和唇部接入现有动作，近景发片按源重心关系拟合头部。门襟和系带使用实际衣身求交挂点，腰扣是开口网格。动物使用作者提供的皮毛 UV 与贴图、独立骨架和共享网格；原 PNG 保持原字节，导入开启 mipmap。

`NatureModels.OrganicDetail.cs` 提供共享鞋楦、鞋底和皮革表面；其程序动物耳角保留作校准。衣料受力褶皱位于衣身与截面网格生成器；木材根据局部法线区分纵向纹理与端面年轮。正式动物由 `NatureModels.AuthoredAnimals.cs` 加载，旧程序头骨和毛束不叠加到来源模型。

`NatureModels.Surface.cs` 管理木纹、石材、屋面、灰泥和植被表面；`NatureModels.CharacterSurface.cs` 管理肤发，正式动物材质位于 `NatureModels.AuthoredAnimals.cs`。HUD 使用独立的炭灰平面样式、细边线和阴影，不再从木材/皮革图集提取面板纹理。金属与窗面使用独立材质，木石、布料、肤发及毛皮校准粗糙度和反光。纹理只提供表面颜色与细节，关节、衣服和房屋轮廓由几何定义。

`NatureModels.ScannedEnvironment.cs` 管理专业环境网格、透明叶片遮罩、法线与粗糙度材质，来源和转换见 [环境素材](environment-assets.md)。世界与图册共享这些网格和屏幕 LOD。`NatureModels.Botanical.cs` 保留程序植物辅助结构及几何检查。`WorldView3D.Relief.cs` 为地面提供稳定的小尺度噪声起伏。

`WorldView3D.Surface.cs` 混合扫描地形、临水色差与压实，水色读取实际河床深度，河床和水面共享连续岸线采样。`ScannedSurfaces.cs` 缓存贴图及打包法线/粗糙度，`NatureModels.ScannedCraft.cs` 管理建筑扫描表面。近景使用授权草丛网格，读取共享压实贴图伏低并随距离缩退；`NatureModels.Meadow.cs` 保留程序构造器。CPU 网格缓存和空间批次的维护见 [性能说明](performance.md)。

## 近景表现与界面参考

| 第一方资料 | 本项目的采用方式 |
|---|---|
| [Wisconsin DNR 狼生物学资料](https://dnr.wisconsin.gov/topic/WildlifeHabitat/wolf) | 结合源模型辨认灰褐、黑色混杂的毛色层次及颈肩毛被，调整过强的黑白色阶；形态资料不作为新资产许可 |
| [NPS 鹿类季节与毛被](https://www.nps.gov/grsm/learn/nature/elk.htm) | 区分贴体底毛与较长护毛；当前使用通用鹿表现，不按特定物种复原或新增季节模拟 |
| [Godot `source_color`](https://docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/shading_language.html#using-source-color) | 颜色贴图和肤色参考采用一致的色彩语义，法线与粗糙度保持数据采样 |
| [NVIDIA GPU Gems：水面模型](https://developer.nvidia.com/gpugems/gpugems/part-i-natural-effects/chapter-1-effective-water-simulation-physical-models) | 分开处理较长几何波与细波法线，使用解析梯度；当前是简化的叠加波形，不复刻完整章节系统 |
| [《庄园领主》官方画面](https://store.steampowered.com/app/1363080/Manor_Lords/)与 [NN/g 渐进呈现](https://www.nngroup.com/articles/progressive-disclosure/) | 减少重复的悬浮卡片，全局信息置顶、对象详情按需打开，提供沉浸观察；未复制原游戏素材 |

护毛是从既有授权表面派生的运行网格，沿用对应鹿、狼许可；程序代码、其他模型和界面仍按各自许可管理。客户端继续使用 Godot 原生材质与文字系统，字体资源随包提供，不增加外部渲染运行库。

## 新增与移动资源

资源文件与相应 `.import` 描述一起提交，Godot 自动导入缓存 `.godot/` 不提交。修改图集布局需要同步格子索引、UV、材质和导入设置；文件路径区分大小写，移动后同步 `res://` 引用。模型源码移动时保留对应 `.uid`。非 Godot 资源类型的模型 JSON 和许可 TXT 必须进入导出预设的 `include_filter`，普通运行直接读取导出数据，不需要 Blender。

原素材、来源记录和正式文档配图分别保存。开发采样位于 `.tools/tmp/<主题>/`，正式配图见[图片索引](../images/README.md)，原始要求与素材来源见[来源资料](../archive/README.md)。运行资源由模型和材质入口直接读取。

纹理维护使用 `./tools/godot.ps1 -Mode test -ReimportTextures`，在实际编辑器生命周期中执行 `texture_import_guard`；独立包导出自动执行并要求真实 mip 链核验成功。只修改 `.import` 配置而保留旧导入缓存，可能使游戏仍使用没有 mipmap 的纹理。

## 检查

上衣重建使用 `./tools/bake-garments.ps1`，原始导出在 `.tools/garment-sources/`，正式 JSON 和哈希清单进入 `assets/models/garments/`；普通构建不执行 Blender。改变剪裁采样或 `tools/garment_pattern.py` 缝片构造后必须重新烘焙，不能只修改运行代码并继续使用旧蒙皮。动作、服装配件和近远景均需一同检查，细节见 [模型系统](models.md)。

通过 `./tools/godot.ps1 -Mode test` 构建、导入并检查网格、法线、UV、缓存与握持，再查看真实窗口中的近景和远景。无头几何检查不能判断美感，图像识别准确率也不能替代材质与解剖质量检查。

## 分区与光照参考

[NN/g 视觉层级](https://www.nngroup.com/articles/visual-hierarchy-ux-definition/)用于区分居民概览与生活、决策资料，公共身份和操作保留，避免重复堆叠。人物柔光／侧光使用同一隔离场景的灯，实际切换可用于查看形体和材质，不改变模拟。

湿润表面的第二反光层依据 [Godot 材质说明](https://docs.godotengine.org/en/stable/tutorials/3d/standard_material_3d.html)，实际变量按 [4.7.2 引擎着色器定义](https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/shader_types.cpp) 中的 `CLEARCOAT_ROUGHNESS` 使用；没有采用文档中与当前引擎不一致的旧变量名。原人脸、眼部、动物和亚麻纹理仍保留各自来源和许可，没有把低分辨率纹理描述为新扫描素材。

## 随包界面字体

界面、数字和世界标签使用正式资源中的 Noto Sans SC 可变字体及 Noto Sans Symbols 2，来源为 [Google Fonts 固定提交](https://github.com/google/fonts/tree/e7db6ca603fd4862010383ff17ffd6cc4ffac1c0/ofl/notosanssc)。两个未修改字体分别保留 SIL Open Font License 1.1，来源提交、下载地址与 SHA-256 见[字体清单](../../src/SandBoxSim.Godot/assets/fonts/manifest.json)。字体是运行资源，不需要安装到操作系统。

正文使用常规字重，标题使用 600，数字使用 500 与等宽数字特性。`InterfaceFonts.cs` 统一缓存字体，显式关闭系统回退，并以随包符号字库处理快捷键和箭头。导出包自检检查原始字体与许可哈希、代表性中英文／符号字形、数字列宽及实际文本排版；不依赖主机安装微软、苹果或 Linux 中文字体。

---

[文档导航](../README.md) · [项目首页](../../README.md)
