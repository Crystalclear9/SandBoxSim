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

`--panel=forearms` 是本地维护预览，展示相同模型的左右掌侧/背侧。它不扩展 HTTP 控制或评测请求协议；公开评测面板列表保持原有定义。

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

眼球继续使用同源纹理与拓扑，`NatureModels.EyeOptics.cs` 只在中央加入有界角膜起伏并修正法线；没有替换为通用球体。头颈保留原有形体，衣服前领为喉部留出开口并重新烘焙三套服装，面部解剖与动作挂点仍来自同一来源。虹膜与角膜围绕来源眼心一起作小幅视线转动，替代单独平移眼部贴图；成人与儿童仍共享各自变体的几何缓存。角膜网格由变体缓存复用，详见[模型系统](models.md)。观察窗的柔光与侧光仅切换隔离场景的真实灯光，不消耗模拟状态或替换人物。

## 连续衣装

三套上衣采用 `anatomical-yoke-v6` 缝片构造。胸肩先从 MakeHuman CC0 身体采样，再形成带松量的衣料支撑体；`torso-cage.json` 保留三个输入文件的 SHA-256、肩点、坐标尺度及 65×128 截面。它与头脸共用已有的许可来源，但采用单一青年男性体型作为穿衣支撑，不代表完整的性别、年龄和体型解剖系统。

袖窿收至肩侧，袖根延续衣身切平面，左右三角面方向对应。袖身权重按边长离线平滑并约束空间变化；领口、中线与腰带固定，袖口随肩骨，原手柄和手掌长度不变。当前成品为 7,320／7,320／7,200 三角形。身体支撑体重建后须重烘焙三套衣装；普通构建与运行不需要 Blender。

在项目根目录使用已下载的源数据重建：

```powershell
$torsoTemp = Join-Path (Get-Location) '.tools/tmp/torso-bake'
New-Item -ItemType Directory -Force $torsoTemp | Out-Null
$env:TEMP = $torsoTemp; $env:TMP = $torsoTemp
& './.tools/blender-4.5.14/editor/blender-4.5.14-windows-x64/blender.exe' --background --factory-startup --disable-autoexec --python-exit-code 2 --python tools/export_human_torso.py -- --source .tools/sources/makehuman --output src/SandBoxSim.Godot/assets/models/human/torso-cage.json
./tools/bake-garments.ps1
python -m unittest discover -s tools -p test_garment_pattern.py
./tools/godot.ps1 -Mode test
```

导出器固定写入 UTF-8、LF 换行；运行时先将 CRLF 统一为 LF，再校验 `NatureModels.TorsoAnatomy.cs` 中固定的 SHA-256。Windows、Linux、macOS 的文本换行差异不改变模型身份；几何数据、元数据或其他文本改变仍会被拒绝。客户端自检覆盖两种换行与有效 JSON 中的半径篡改。更改拟合配方后，要同步审查成品哈希；不要直接跳过校验。人物肩点与衣服骨骼同步作有界位移，肘部偏好和握持手腕限制共同参与求解；具体形态、动作边界与研究来源见[模型系统](models.md)。
