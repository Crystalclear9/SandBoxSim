# 人物模型与素材转换

当前头脸使用 MakeHuman Community 的 CC0 HM08 拓扑、形体目标和原生 UV 皮肤／头发贴图，已替换此前三套 Blender 头部。前臂和手部仍使用 Julien Kaspar / Blender Studio 的 CC BY 4.0 衍生数据。它们接入同一套居民身份、职业与动作，不增加游玩限制。

## 当前头脸

| 文件 | 配方 | 头颈顶点 / 三角面 | 口部三角面 | 近景头发三角面 | 远景头脸三角面 |
|---|---|---|---|---|---|
| [human-head.mesh.json](../../src/SandBoxSim.Godot/assets/models/human/human-head.mesh.json) | 成年男性 | 20,157 / 31,742 | 2,764 | 3,678 | 2,367 |
| [human-scan.mesh.json](../../src/SandBoxSim.Godot/assets/models/human/human-scan.mesh.json) | 中性男性 | 20,131 / 31,676 | 2,870 | 3,344 | 2,367 |
| [human-feminine.mesh.json](../../src/SandBoxSim.Godot/assets/models/human/human-feminine.mesh.json) | 成年女性 | 19,888 / 31,338 | 3,644 | 5,352 | 2,368 |

`human-scan` 保留历史文件名用于兼容，当前内容是同源拓扑的中性形体变体，不表示它是扫描模型。三套配方共享基础拓扑，各自应用形体目标与贴图；八种身份宽深变化另有儿童比例缓存。儿童使用不含胡茬的皮肤图，眼口、头发和挂点采用一致的年龄变形。

头颈在 Blender 中进行一级曲面细分，裁切到衣领以下，最下方颈环收束到现有领口。反射坐标轴时同时处理面朝向，法线保持朝外。脸、耳、鼻翼和下颌来自连续拓扑；上、下唇从同一网格划分，保留原生 UV，连接边界及 UV 缝两侧权重固定。说话仅轻微移动内部顶点，每人独立保存口部参数。

三张 2048×2048 皮肤颜色图保留原生 UV 和照片表面细节；旧智能投影与三张头部法线图已移除。眼球使用 1024×1024 原始图，正确裁切透明角膜区域，显示下面的虹膜和瞳孔。纹理启用 mipmap；编辑器核对源 PNG 哈希，导出包通过 Godot 的纹理重映射加载。

眨眼使用源眼睑闭合形变，位移经过相同的形体与年龄变换，Y/Z 分量写入第二 UV 通道，网格合并时换算到米制坐标。每个居民拥有独立材质覆盖，避免共用网格导致同步眨眼；不再覆盖一张额外上眼皮。每眼仍保存 49 对实际眼裂边缘供几何检查。视线和眨眼不缩放眼球。

上眼睑每侧有 26 根短睫毛，根部直接采样该身份的上眼裂，长度约 1–2 毫米，儿童略短；共 312 个三角面，几何按身份共享。闭合位移记录在第二 UV，材质参数与实际眼睑同步，每人独立保存；远景头脸隐藏睫毛，暂停冻结其运动。自检覆盖闭合位移、缓存、个体隔离和近远可见性。虹膜与眼白分别调整粗糙度，嘴唇使用较低粗糙度，耳侧保留少量背光；没有启用依赖 Forward+ 的次表面散射。

下眼睑另有读取同一 49 点源眼裂的湿润细边：0.18 毫米半径、768 面，携带最近源三角面重心插值后的实际闭合位移，与眼睑和睫毛同步眨眼；采样按来源族和眼裂位置缓存，不在每帧搜索。近景共享网格、每人独立材质，远景随头部细节隐藏。皮肤和唇部的染色参考也标记为 `source_color`，避免与纹理使用不同色彩基准；原照片与八种身份、儿童变换继续共用原流程。

近景头发采用 `short01`、`short02`、`ponytail01` 三种带透明发梢的发片，按源 MHCLO 的重心权重和偏移拟合到当前头部，束发采用露出额头和双眼的轮廓。材质保留发丝明暗并按居民发色染色。`NatureModels.Hairline.cs` 按来源族缓存帽体物理根边和米制距离／弧长，仅在约 1.5 毫米根带调整短束覆盖；游离片和马尾使用排除标记，源 Alpha 文件、顶点、UV 与索引不变。`MeshBatching` 对该度量保持原单位，避免复用眼睑位移缩放。眉毛仍为贴合面部的细弧面。远景头脸从同一头颈、眼球和唇部减面到最多 2,400 三角面，结合简化头皮轮廓与单材质；不显示近景皮肤纹理和细发片。

来源、文件大小与 SHA-256 位于 [head-sources.json](../../src/SandBoxSim.Godot/assets/models/human/head-sources.json)，每套模型也记录实际输入和纹理哈希。普通构建及 CI 不运行 Blender、不联网。

## 肘部与前臂

[human-forearm.mesh.json](../../src/SandBoxSim.Godot/assets/models/human/human-forearm.mesh.json) 来自 Julien Kaspar / Blender Studio 的授权人体的肘部、前臂区域 12/102，共 1,928 顶点、3,687 三角面。它替换了运行时的球形肘部和规则管状前臂，保留原作者的肘尖、肌腹与近腕骨形体；不是肌肉或软组织物理。

[export_human_forearm.py](../../tools/export_human_forearm.py) 校验原始文件哈希后，评估一级多重细分、截取连续表面、平顺原始倾斜中心线，并将长度接入当前骨架。上端埋入卷袖；腕端位于肘部局部 Y=-0.210，与手节点 Y=-0.255 加手网格腕环 Y=0.045 对齐，横截面半径为 0.027/0.024。腕口拟合为平面，避免倾斜裁切或重叠留下台阶。

[NatureModels.Forearm.cs](../../src/SandBoxSim.Godot/NatureModels.Forearm.cs) 校验成品哈希并缓存左右网格，镜像时同时处理顶点、法线和三角面朝向。近景前臂与手掌通过共顶点过渡环连成单张蒙皮，前臂顶点使用现有腕部补偿骨，抵消手腕转动而保持肘部挂接；指骨仍驱动手指。远景使用静态前臂和手部 LOD，腕端保留埋入重叠。自检检查连续性、边界位置、左右镜像，并在实际工具动作中检查变形后的手腕仍与前臂截面一致。

`--panel=forearms` 是本地维护预览，展示相同模型的左右掌侧/背侧；该旧入口仅用于本地观察。体型与小臂的公开评测入口见下文。

当前小臂另通过 `RefineForearmProfile` 收束肌腹，并以连续曲率过渡保留腕部及隐藏肘缘。两种成人外观拥有独立缓存，近手、远手与前臂保持同源轮廓；法线包含体积形变导数，手指和握柄区不改变。形态依据、项目拟合范围与局限见[成人体型与前臂说明](models.md)。新增公开评测面板 `body-frames` 与 `forearm-frames`，分别复现同衣色体型和小臂正侧面比较；对应 `visual-body-frames.json` 与 `visual-forearm-frames.json`。

发根表现参考 [MakeHuman 官方材质](https://static.makehumancommunity.org/assets/creatingassets/makeclothes/makeclothes_materials.html)和[Godot 透明材质](https://docs.godotengine.org/en/stable/tutorials/3d/standard_material_3d.html)。当前固定 Compatibility 路径仍使用原 Scissor；没有声称启用 Alpha-to-Coverage，也没有新增扫描或全部毛发几何。

## 解剖手部

[human-hand.mesh.json](../../src/SandBoxSim.Godot/assets/models/human/human-hand.mesh.json) 从 Julien Kaspar / Blender Studio 的授权人体提取手掌、指缝与五指。近景文件包含连续的前臂和手部，共 9,154 个顶点、18,205 个三角面；其中原始手部区域有 7,255 个顶点、14,464 个三角面，`handVertexCount` 保留该分界，供远景只提取手部。甲面从实际指背曲面提取，运行时仅上传甲面引用的顶点。

手部使用平滑位移场接入掌指骨，左右手镜像骨骼位置与展开方向；腕环与前臂在运行坐标中对齐；`join_human_arm.py` 用共顶点过渡环接入前臂，并重新计算连续表面法线。掌根在相邻指骨间平滑分配权重，手指保留近节、中节与末节。每手 16 骨，与原有握持、取用和收纳动作共用。指甲与皮肤使用同一表面分区、共享权重，随手指一起变形，避免叠面闪烁。

`NatureModels.HandSurface.cs` 在原网格上增加分区材质：第一 UV、拓扑、两表面与骨骼不变，第二 UV 保存未摆姿的来源坐标，顶点色红通道保存掌背掩码；甲面使用对应的局部坐标。掌纹、屈曲折痕与指腹只出现在相应手掌区域，指背指节、甲床半月、短游离缘和角质边分别处理，随蒙皮运动，不依赖摆姿后的世界位置。远景跳过细纹计算，仍使用原有低模与缓存。

造型参考 [Leeds 指甲组织说明](https://histology.leeds.ac.uk/home/skin/nails/)及[掌纹分析研究](https://pmc.ncbi.nlm.nih.gov/articles/PMC2998791/)，用于区分一般表面区域，不是医学复原、指纹模板或新扫描。没有下载新的人体照片、替换原手部许可，亦没有增加软组织或肌腱物理；材质不能补回原网格中不存在的几何细节。

`NatureModels.WristContour.cs` 在加载时对原近圆腕环作局部椭圆拟合：54×48 毫米调整为项目美术选用的 54×37 毫米，并以 C2 过渡延续前臂与掌根。近手、前臂、远手共用映射，法线包含纵向形变导数；源文件和哈希、拓扑、UV、骨权重不变，手指、甲面与握柄区不移动。参考 [AO Foundation 远端前臂解剖](https://surgeryreference.aofoundation.org/orthopedic-trauma/adult-trauma/distal-forearm/approach/anatomy-of-the-distal-forearm) 保留掌背结构差异；37 毫米不是该资料提供的医学尺寸。

近景、远景、指根和拇指骨骼共用 `ResidentRig.HandMirror` 的左右约定。远景张手与握拳网格按左右独立缓存，镜像时同步反转法线、切线手性和三角面朝向；拇指轮廓位于食指一侧并露出掌缘，避免默认视距把两只手显示成同一侧。`--panel=handedness` 可并排查看同一角色的左右掌心、手背以及远景简模。自检覆盖镜像拓扑、拇指方向、骨骼张合、LOD 切换，以及主手和支撑手的实际蒙皮与工具轴间距。

远景手部使用 [human-hand-lod.mesh.json](../../src/SandBoxSim.Godot/assets/models/human/human-hand-lod.mesh.json)：从同一文件的手部区域提取、减面得到 708 个顶点、1,400 个三角面，保留指缝、不同指长与拇指掌根。转换按最近源三角面的重心坐标转移 UV、法线和四权重；运行时沿用近景指节角度，缓存张手与握柄两种静态姿势。远景不更新手部骨架，也不再使用独立椭球拼成的手型；远景省略独立甲面材质。左右仍分别缓存并校验镜像朝向，握柄简模也检查实际手柄间距。

LOD 由 [export_hand_lod.py](../../tools/export_hand_lod.py) 生成，读取仓库中的高精度手部，校验源哈希；普通运行直接加载导出 JSON。减面后的皱褶与甲面不具备近景精度，八视图图册用于比较形状连续性。近景与远景分别切换连体蒙皮和静态前臂，避免重复绘制；近景在实际工具动作中检查整段前臂留在肘坐标系内。

手部导出由 [export_human_hands.py](../../tools/export_human_hands.py) 完成，运行时位于 [NatureModels.HandSkin.cs](../../src/SandBoxSim.Godot/NatureModels.HandSkin.cs)。检查覆盖非流形边、腕口边界、权重、握持全过程和变形表面的手柄间距。

## 重新导出

便携 Blender 和全部源文件均保存在项目 `.tools/`，不会安装到用户目录。先执行：

```powershell
python tools/fetch-human-assets.py
python tools/fetch-human-assets.py --verify
```

下载器仅通过 HTTP Range 提取固定 ZIP 成员，校验文件长度、CRC 和 SHA-256，现有合格文件直接复用；不会下载整个约 268 MiB 的素材包。23 个所需输入共约 25 MiB，包括许可证。`--verify` 不访问网络。

```powershell
$faceBlender = './.tools/blender-4.5.14/editor/blender-4.5.14-windows-x64/blender.exe'
$faceOutput = 'src/SandBoxSim.Godot/assets/models/human'
foreach ($faceKind in 'masculine','neutral','feminine') {
    & $faceBlender --background --factory-startup --disable-autoexec --python-exit-code 2 --python tools/export_resident_faces.py -- --source .tools/sources/makehuman --output $faceOutput --kind $faceKind
    if ($LASTEXITCODE -ne 0) { throw 'Face export failed' }
}
```

转换器只读取 OBJ、目标位移、MHCLO 和图片，不执行 MakeHuman 应用或第三方脚本。`export_human_head.py`、`export_human_variants.py` 作为兼容入口转发到新流程，旧 `scan` 参数映射到 `neutral`。重新导出后同步 `HumanHashes`，运行素材检查和客户端自检，再查看原生近景：

```powershell
python -m unittest discover -s tools -p test_human_assets.py
./tools/godot.ps1 -Mode test
```

前臂、手部和动物通过 `./tools/bake-models.ps1` 重建。手臂源文件为 `.tools/sources/realistic_human_base.blend`，SHA-256 为 `1d9d2a6070acd6a3d0d9b676af816001310fb0f6128d07a3ab3e0318c7fdd981`。下载、工具、配置和中间数据都由 `.gitignore` 排除。

## 许可与发布

[LICENSE.txt](../../src/SandBoxSim.Godot/assets/models/human/LICENSE.txt) 区分 CC0 头脸／贴图与 CC BY 4.0 手臂，并附 [MakeHuman 资产许可](../../src/SandBoxSim.Godot/assets/models/human/MAKEHUMAN-LICENSE.txt)。手掌、前臂和手部 LOD 继续保留 Julien Kaspar 的署名及修改说明。软件许可不替代素材许可。

Godot 三平台导出包含模型 JSON、许可文本与导入纹理；发行包自检实际读取成品。原始工具与素材缓存不是运行依赖。

## 眼部与领口的运行细化

成人头脸在来源网格上叠加有界的眉弓、鼻翼、颊部、下颌和颏部拟合；眼口、发片与挂点共享形变，来源文件及哈希保持固定。头宽不继承身体随机宽度，成人下颌、颈部和下脸段分别采用有界区域拟合，八种成人表面采样检查局部映射不翻折。穿衣体型按肩线、胸廓、腰部和骨盆分区，保留源胸廓比值、独立肩部绑定及原有臂腿长度；腰包挂点适配身体，自身网格不作非线性变形。三个来源皮肤图集另作额部线性亮度曝光校准，由 `tools/measure_head_tones.py` 读取已校验的 JSON 和 PNG 复算，脸与嘴唇使用实测来源参考色及线性运算；头部 LOD 的相对顶点色单独正确编码，不重复套用贴图曝光；儿童保持原材质策略。当前形态、研究依据及正面／三刻面入口见[成人外观说明](models.md)。

眼球继续使用同源纹理与拓扑，`NatureModels.EyeOptics.cs` 只在中央加入有界角膜起伏并修正法线；没有替换为通用球体。头颈保留来源拓扑并应用上述成人局部拟合，衣服前领为喉部留出开口并重新烘焙三套服装，面部解剖与动作挂点仍来自同一来源。虹膜与角膜围绕来源眼心一起作小幅视线转动，替代单独平移眼部贴图；成人与儿童仍共享各自变体的几何缓存。角膜网格由变体缓存复用，详见[模型系统](models.md)。观察窗的柔光与侧光仅切换隔离场景的真实灯光，不消耗模拟状态或替换人物。

## 连续衣装

三套上衣采用 `faired-panel-v8` 连续缝片构造。原始胸肩从 MakeHuman CC0 身体采样，`torso-cage.json` 和 `torso-feminine-cage.json` 保存 65×128 的胸肩支撑与来源哈希。当前运行体型另使用 [trunk-masculine-cage.json](../../src/SandBoxSim.Godot/assets/models/human/trunk-masculine-cage.json) 和 [trunk-feminine-cage.json](../../src/SandBoxSim.Godot/assets/models/human/trunk-feminine-cage.json)，各包含 113×128 个覆盖骨盆、腰背与胸廓的平面截面凸包采样。两套青年宏目标及原始基础体均来自现有已校验的 CC0 来源，不改变头脸、手部来源或许可。

`NatureModels.BodyFrame.cs` 根据三种原衣片的半径参考，拟合完整躯干并留出衣料松量。袖片按原始缝片坐标逐渐接回肩部，法线包含形变导数，网格拒绝局部翻折。当前 `lumbar-drape-v8` 分别调整胸廓宽度、骨盆侧缘和臀部后向体积，女性剪裁另调整胸前包络、腰带和工具挂点高度、骨盆关节间距及大腿上段轮廓，保留现有臂腿长度和动作控制；裤装、近远景及缓存均按体型区分。上衣保留原面数，近景新增骨盆衣料骨与连续腰背权重，肩侧斜度、袖片和实际关节同步；成人裤装改用下述连续来源表面及独立蒙皮。详细参数与局限见[成人身体说明](models.md)。

宽松衣身参考 [V&A 的衬衫结构](https://www.vam.ac.uk/articles/sew-your-own-customisable-shirt)，胸腹衔接保留布料松量，减少刻画肌肉的重复褶皱；背部肩部拼接以贴面短针脚表示。头部整体上移 18 毫米以露出源颈部。袖窿收至肩侧，袖根延续衣身切平面，左右三角面方向对应。袖身权重按边长离线平滑并约束空间变化；领口、中线与腰带固定，袖口随肩骨，原手柄和手掌长度不变。当前成品为 7,320／7,320／7,200 三角形。改变原始衣片或其胸肩基础体后须重烘焙三套衣装；完整躯干体型在运行模型创建时拟合，单独重建 trunk 文件不要求重烘焙原衣片。普通构建与运行不需要 Blender。成人运行绑定另以独立肩胛带及六个摆动／轴向扭转分离区间保持肩袖截面，并按拟合网格上的袖窿最短路径距离分配袖管位置修正；两种体型共用二十六骨结构，每顶点至多四个影响，袖窿与袖口之间在实际边长上重新求解过渡，原始烘焙网格与来源哈希不变。肩部范围及实际作业入口见[肩袖绑定与动作说明](models.md)。

在项目根目录使用已下载的源数据重建：

```powershell
$torsoTemp = Join-Path (Get-Location) '.tools/tmp/torso-bake'
New-Item -ItemType Directory -Force $torsoTemp | Out-Null
$env:TEMP = $torsoTemp; $env:TMP = $torsoTemp
& './.tools/blender-4.5.14/editor/blender-4.5.14-windows-x64/blender.exe' --background --factory-startup --disable-autoexec --python-exit-code 2 --python tools/export_human_torso.py -- --source .tools/sources/makehuman --output src/SandBoxSim.Godot/assets/models/human/torso-cage.json
& './.tools/blender-4.5.14/editor/blender-4.5.14-windows-x64/blender.exe' --background --factory-startup --disable-autoexec --python-exit-code 2 --python tools/export_human_torso.py -- --frame female --source .tools/sources/makehuman --output src/SandBoxSim.Godot/assets/models/human/torso-feminine-cage.json
& './.tools/blender-4.5.14/editor/blender-4.5.14-windows-x64/blender.exe' --background --factory-startup --disable-autoexec --python-exit-code 2 --python tools/export_human_torso.py -- --frame male --region trunk --source .tools/sources/makehuman --output src/SandBoxSim.Godot/assets/models/human/trunk-masculine-cage.json
& './.tools/blender-4.5.14/editor/blender-4.5.14-windows-x64/blender.exe' --background --factory-startup --disable-autoexec --python-exit-code 2 --python tools/export_human_torso.py -- --frame female --region trunk --source .tools/sources/makehuman --output src/SandBoxSim.Godot/assets/models/human/trunk-feminine-cage.json
./tools/bake-garments.ps1
python -m unittest discover -s tools -p test_garment_pattern.py
./tools/godot.ps1 -Mode test
```

导出器固定写入 UTF-8、LF 换行；运行时先将 CRLF 统一为 LF，再校验 `NatureModels.TorsoAnatomy.cs` 中固定的 SHA-256。Windows、Linux、macOS 的文本换行差异不改变模型身份；几何数据、元数据或其他文本改变仍会被拒绝。客户端自检覆盖两种换行与有效 JSON 中的半径篡改。更改拟合配方后，要同步审查成品哈希；不要直接跳过校验。人物肩点与衣服骨骼同步作有界位移，肘部偏好和握持手腕限制共同参与求解；具体形态、动作边界与研究来源见[模型系统](models.md)。

## 连续裤装来源

`trousers-male.mesh.json`／`trousers-female.mesh.json` 从同一 CC0 HM08 的 `helper-tights` 服装辅助几何生成，分别应用青年男女目标；不是旧骨盆和大腿截面体的增面。导出器校验源文件、去掉 A 姿势腿部偏移、裁切并保留单连通衣料，拟合裤腰与靴口，输出五骨权重和原始输入哈希。当前 `thoraco-pelvic-v9` 在原连续表面上处理骨盆宽度、臀部后向体积与上腿收束，输出记录 `physique` 配方标记，运行时仍校验完整成品 SHA-256。运行时用骨盆、实际大腿与膝关节驱动连续表面；面数、约束与测试见[模型系统](models.md)。现有头脸文件和手部授权保持独立。

在上述项目临时环境中，使用同一 Blender 重建裤装：

```powershell
& ./.tools/blender-4.5.14/editor/blender-4.5.14-windows-x64/blender.exe --background --factory-startup --disable-autoexec --python-exit-code 1 --python tools/export_resident_trousers.py -- --source .tools/sources/makehuman --output src/SandBoxSim.Godot/assets/models/human
python -m unittest discover -s tools -p test_resident_trousers.py
```

当前 `adult-proportions-v13` 在同一来源上分别处理胸廓／上背、腰线和骨盆／腿部轮廓。`tools/export_resident_trousers.py` 重新导出五骨连续裤装，男性 2,925 顶点／5,696 三角面，女性 2,731 顶点／5,296 三角面；输入来源及许可保留，运行时与工具测试固定校验成品 SHA-256。上髋松量同时覆盖弯身时的上衣低段，具体表现与边界见[模型系统](models.md)。


## 上臂解剖支撑与姿态表面

[upper-arm-male.json](../../src/SandBoxSim.Godot/assets/models/human/upper-arm-male.json) 和 [upper-arm-female.json](../../src/SandBoxSim.Godot/assets/models/human/upper-arm-female.json) 来自本项目现有、哈希校验的 MakeHuman HM08 基础网格、青年宏目标和肩肘关节组，核心素材许可为 [CC0](https://www.makehumancommunity.org/about/license.html)。两个文件分别保存肩肘来源坐标、来源哈希、目标臂长和 25×64 个上臂截面半径；截面在来源肩肘轴内计算，保留三角肌区域向肘部收束的轮廓与方向差异，不能当作肌肉运动实测数据。运行时对 LF／CRLF 统一后检查完整文件哈希。

可复用工具 [export_upper_arm_support.py](../../tools/export_upper_arm_support.py) 只依赖 Python 标准库，使用 `.tools/sources/makehuman` 中已校验来源，不重新下载素材。重建命令为 `python tools/export_upper_arm_support.py --source .tools/sources/makehuman --output src/SandBoxSim.Godot/assets/models/human`；对应测试同时检查来源重建一致性、有效截面和方向性体积。修改来源或配方后须同步审查运行时固定哈希。

衣袖在当前体型拟合后加入上臂支撑、布料松量，保留共享袖窿拓扑和袖口连接，姿态修正共同整理肩上衣片及袖窿，再生成七十二个有限姿态的表面修正。修正覆盖抬臂平面、高度及轴向扭转，包括位置和重建后的法线；静止法线不能直接当作抬臂后皮肤／衣料的真实朝向。当前仍为有限姿态插值、程序动作及衣料蒙皮，没有裸露上臂皮肤、肌肉动力学或布料碰撞；空闲伸展的捕捉数据见下节。算法、边界和运行入口见[模型系统](models.md)。

## 三维肩部来源与捕捉动作

[shoulder-surface-male.json](../../src/SandBoxSim.Godot/assets/models/human/shoulder-surface-male.json) 与[女性表面](../../src/SandBoxSim.Godot/assets/models/human/shoulder-surface-female.json) 使用同一 CC0 人体来源，另采用 MakeHuman 的 `default.mhskel`（SHA-256 `99f179bce0aa850b45d4191a1d0d234c5851f881c057439470ded3bddf729a24`）和 `default_weights.mhw`（`0f3641d651ae3d00ad6b4ccee43142edb109d3bd909d27d9e4139ef1beed8625`）。两份文件的来源元数据均明确为 CC0，作者为 Data Collection AB、Joel Palmius、Jonas Hauquier；[骨架](https://github.com/makehumancommunity/makehuman/blob/master/makehuman/data/rigs/default.mhskel)、[权重](https://github.com/makehumancommunity/makehuman/blob/master/makehuman/data/rigs/default_weights.mhw)保留原定义。原权重存在千分位舍入，导出时先归一化，再与网格一起细分。导出包含静止表面、原始 A 姿势、四组权重、肩点和绑定旋转，供静止及抬臂后的衣料贴合使用。

重建使用 [export_shoulder_surface.py](../../tools/export_shoulder_surface.py)：`python -B tools/export_shoulder_surface.py --source .tools/sources/makehuman --output src/SandBoxSim.Godot/assets/models/human`，原始两份骨架文件存于该来源目录的 `rig/`。工具只依赖 Python 标准库，运行与普通构建不需要 Blender。

[cmu-143-30-stretch.json](../../src/SandBoxSim.Godot/assets/motions/cmu-143-30-stretch.json) 来自 [CMU 143_30 / Stretch And Yawn](http://mocap.cs.cmu.edu/search.php?subjectnumber=143)，原始 `143.asf` 与 `143_30.amc` 的 SHA-256 分别为 `6d7207da3bb9eceeaa32f23fb641b69f5836d11242b00c052e7d64f0cbe20a35`、`ffd7b2b3344cf41eef16a3b9b2910424dc27c24ac1f4d6c3b68b6c719e7b22e3`。数据可用于研究及商业产品，但不得直接转售原始或转换后的数据；它不属于 CC0，必须保留[CMU 来源与许可说明](../../src/SandBoxSim.Godot/assets/motions/LICENSE.txt)。The data used in this project was obtained from mocap.cs.cmu.edu. The database was created with funding from NSF EIA-0196217.

重建命令为 `python -B tools/export_cmu_arm_motion.py --source .tools/sources/mocap/cmu-143 --output src/SandBoxSim.Godot/assets/motions/cmu-143-30-stretch.json`。工具遵循 [ASF/AMC 格式](https://research.cs.wisc.edu/graphics/Courses/cs-838-1999/Jeff/ASF-AMC.html)，排除根位移和未捕捉的指／拇指通道；运行器固定校验导出哈希。导出、来源重建及数据契约由 `tools/test_motion_sources.py` 检查。
