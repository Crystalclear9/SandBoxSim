# 人物模型与素材转换

当前头脸使用 MakeHuman Community 的 CC0 HM08 拓扑、形体目标和原生 UV 皮肤／头发贴图，已替换此前三套 Blender 头部。前臂和手部仍使用 Julien Kaspar / Blender Studio 的 CC BY 4.0 衍生数据。它们接入同一套居民身份、职业与动作，不增加游玩限制。

成人身体采用 Blender 官方 CC0 写实基础身体的形体转移，躯干、肩臂、前臂支撑及裤装共用来源配方。颈口尺寸、肩关节间距与袖口围度分别拟合，锁骨内端使用来源关节标记。身体参考的下载、复现和许可见下文。

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

近景头发的来源是 `short01`、`short02`、`ponytail01` 发片。源 MHCLO 的重心权重和偏移用于贴合头部，材质保留发丝明暗并按居民发色染色。`NatureModels.Hairline.cs` 按来源族缓存帽体物理根边和米制距离／弧长，仅在约 1.5 毫米根带调整短束覆盖。成年女性以 `ponytail01` 的头皮和短碎发为基础，按连通片分离原尾部，再在实际后头皮上拟合项目生成的编发和盘发。源 Alpha 文件、顶点、UV 和索引用于复现与校验。`MeshBatching` 保留发根度量的原单位。女性远景使用同一拟合头皮的主体和同一束发导线，以较低的发束采样密度与简化头脸合并成一个材质。

来源、文件大小与 SHA-256 位于 [head-sources.json](../../src/SandBoxSim.Godot/assets/models/human/head-sources.json)，每套模型也记录实际输入和纹理哈希。普通构建及 CI 不运行 Blender、不联网。

## 成人身体配方

`reference-contour-physique-v31` 使用 Blender 官方 Human Base Meshes v1.4.1 中 Dan Ulrich 的两套 CC0 写实身体作为形体来源，保留 HM08 的拓扑和语义。来源包身份、两套转移位移及其成品哈希由 [reference-shape-sources.json](../../src/SandBoxSim.Godot/assets/models/human/reference-shape-sources.json) 固定；[body-profiles.json](../../src/SandBoxSim.Godot/assets/models/human/body-profiles.json) 分别选择 `reference-shape-male.json`、`reference-shape-female.json`，两种成人均使用各自参考形体的 1.0 权重，保留 HM08 拓扑与语义。头脸和手部不随该形体重做而替换。

[export_resident_reference.py](../../tools/export_resident_reference.py) 在项目内 Blender 中生成转移数据：41 个关节与表面参考、肩骨盆中性轴、受约束的表面位移、辅助衣片对应及关节中心恢复。输出保存原始输入哈希、参考点、投射和位移梯度边界。重建过程使用 Blender 自带 NumPy，普通游戏运行没有该依赖；具体几何与局限见[模型系统](models.md)。

女性 `balanced-silhouette-v6` 通过 [resident_shape.py](../../tools/resident_shape.py) 连续协调胸廓、腰侧、下腹、后腰、臀部与大腿，避免分别附加独立体块。来源关节保持固定，单点塑形最多 18 毫米；后侧深度渐入覆盖 8–110 毫米。躯干、上臂、前臂、肩面及裤装共十份衍生支撑从同一配方导出并固定成品哈希；上游 CC0 参考文件和许可独立保留。

[resident_body.py](../../tools/resident_body.py) 校验并应用位移。躯干、上臂、肩部原始 A 姿势／绑定三角面、连续裤装与前臂拟合各自记录同一 `shapeRecipe`：配方标记、配方文件 SHA-256 与实际形体权重。五组输出的来源测试、成品哈希和截面检查共同约束一致性。原胸肩衣片、头脸、手部和 CMU 捕捉数据仍独立保留。

身体参考来源下载和核对：

```powershell
python -B tools/fetch-body-reference.py
python -B tools/fetch-body-reference.py --verify
```

下载器只读取官方 ZIP 中指定的 `.blend` 成员，校验长度、CRC 与 SHA-256，来源放在 `.tools/sources/blender-assets/human-base-meshes-v1.4.1.blend`；`--verify` 仅验证本地文件。重建转移时，将 `TEMP`／`TMP` 指向项目 `.tools/tmp/<主题>/`，使用项目内 Blender 后台执行以下参数，再按下文重建十份身体支撑：

```text
--background --factory-startup --disable-autoexec --python-exit-code 1
--python tools/export_resident_reference.py --
--source .tools/sources/makehuman
--bundle .tools/sources/blender-assets/human-base-meshes-v1.4.1.blend
--output src/SandBoxSim.Godot/assets/models/human
```

## 肘部与前臂

[human-forearm.mesh.json](../../src/SandBoxSim.Godot/assets/models/human/human-forearm.mesh.json) 来自 Julien Kaspar / Blender Studio 的授权人体的肘部、前臂区域 12/102，共 1,928 顶点、3,687 三角面。它替换了运行时的球形肘部和规则管状前臂，保留原作者的肘尖、肌腹与近腕骨形体；不是肌肉或软组织物理。

[export_human_forearm.py](../../tools/export_human_forearm.py) 校验原始文件哈希后，评估一级多重细分、截取连续表面、平顺原始倾斜中心线，并将长度接入当前骨架。上端埋入卷袖；腕端位于肘部局部 Y=-0.210，与手节点 Y=-0.255 加手网格腕环 Y=0.045 对齐，横截面半径为 0.027/0.024。腕口拟合为平面，避免倾斜裁切或重叠留下台阶。

[NatureModels.Forearm.cs](../../src/SandBoxSim.Godot/NatureModels.Forearm.cs) 校验成品哈希并缓存左右网格，镜像时同时处理顶点、法线和三角面朝向。近景前臂与手掌通过共顶点过渡环连成单张蒙皮，前臂顶点使用现有腕部补偿骨，抵消手腕转动而保持肘部挂接；指骨仍驱动手指。远景使用静态前臂和手部 LOD，腕端保留埋入重叠。自检检查连续性、边界位置、左右镜像，并在实际工具动作中检查变形后的手腕仍与前臂截面一致。

`--panel=forearms` 是本地维护预览，展示相同模型的左右掌侧/背侧；该旧入口仅用于本地观察。体型与小臂的公开评测入口见下文。

当前小臂另通过 `RefineForearmProfile` 收束肌腹，并以连续曲率过渡保留腕部及隐藏肘缘。两种成人外观拥有独立缓存，近手、远手与前臂保持同源轮廓；法线包含体积形变导数，手指和握柄区不改变。形态依据、项目拟合范围与局限见[成人体型与前臂说明](models.md)。新增公开评测面板 `body-frames` 与 `forearm-frames`，分别复现同衣色体型和小臂正侧面比较；对应 `visual-body-frames.json` 与 `visual-forearm-frames.json`。

发根表现参考 [MakeHuman 官方材质](https://static.makehumancommunity.org/assets/creatingassets/makeclothes/makeclothes_materials.html)和[Godot 透明材质](https://docs.godotengine.org/en/stable/tutorials/3d/standard_material_3d.html)。当前固定 Compatibility 路径仍使用原 Scissor；没有声称启用 Alpha-to-Coverage，也没有新增扫描或全部毛发几何。

### 与身体配方共用的前臂截面

`forearm-fit-male.json`／`forearm-fit-female.json` 由 `export_upper_arm_support.py --region forearm` 生成。沿当前共同身体配方的肘—腕轴取 49×64 个截面，覆盖掌根以上 0.005–0.245 米；与实际连体手臂比较，记录宽深、横向缩放和中心线偏移。两份数据混合 CC0 身体参考与 Julien Kaspar / Blender Studio 的 CC BY 4.0 衍生测量，许可为 `CC0-1.0 AND CC-BY-4.0`。

当前拟合将来源身体的实际截面传递到连体雕刻，宽深和偏移采用零端曲率的五次过渡，法线包含完整映射导数。成年女性的宽度拟合从掌根开始，使用同一来源截面和两侧实际网格上的接合圆顺，近手、远景前臂及远手复用对应映射。手指、甲面和掌心握柄区保持原坐标。`FOREARM_BODY_SUPPORT_PASS` 比较实际连体蒙皮六个腕臂截面的宽深，容差为 2 毫米。

## 解剖手部

[human-hand.mesh.json](../../src/SandBoxSim.Godot/assets/models/human/human-hand.mesh.json) 从 Julien Kaspar / Blender Studio 的授权人体提取手掌、指缝与五指。近景文件包含连续的前臂和手部，共 9,154 个顶点、18,205 个三角面；其中原始手部区域有 7,255 个顶点、14,464 个三角面，`handVertexCount` 保留该分界，供远景只提取手部。甲面从实际指背曲面提取，运行时仅上传甲面引用的顶点。

手部使用平滑位移场接入掌指骨，左右手镜像骨骼位置与展开方向。`join_human_arm.py` 保留原手掌与指甲索引，以 30 毫米长、七个中间环、每环 96 列的切线约束连接接入前臂；不规则切口的切线先在统一角度上过滤，避免纵向皱棱。重算法线后导出连通表面。来源每手 16 骨，运行时增加三十六个前臂屈肘／扭转支撑和四个腕部支撑，共 56 骨，每顶点仍至多四个影响。四个腕部支撑以四元数渐进分配屈伸及侧偏，减少掌根与前臂直接线性混合造成的收缩。指甲与皮肤共享权重，不增加重叠表面。

`NatureModels.HandSurface.cs` 在原网格上增加分区材质：第一 UV、拓扑、两表面与骨骼不变，第二 UV 保存未摆姿的来源坐标，顶点色红通道保存掌背掩码；甲面使用对应的局部坐标。掌纹、屈曲折痕与指腹只出现在相应手掌区域，指背指节、甲床半月、短游离缘和角质边分别处理，随蒙皮运动，不依赖摆姿后的世界位置。远景跳过细纹计算，仍使用原有低模与缓存。

造型参考 [Leeds 指甲组织说明](https://histology.leeds.ac.uk/home/skin/nails/)及[掌纹分析研究](https://pmc.ncbi.nlm.nih.gov/articles/PMC2998791/)，用于区分一般表面区域，不是医学复原、指纹模板或新扫描。没有下载新的人体照片、替换原手部许可，亦没有增加软组织或肌腱物理；材质不能补回原网格中不存在的几何细节。

`NatureModels.WristContour.cs` 与 `ForearmSupport.cs` 在创建缓存时共用来源截面场，取消固定 54×37 毫米腕环。掌背与桡尺侧结构的区分参考 [AO Foundation 远端前臂解剖](https://surgeryreference.aofoundation.org/orthopedic-trauma/adult-trauma/distal-forearm/approach/anatomy-of-the-distal-forearm)，连续基础表面的制作参考 [Blender Studio](https://studio.blender.org/training/realistic-human-research/use-of-base-meshes/)。这是项目角色的几何与蒙皮近似，没有肌肉或桡尺骨物理。

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

下载器仅通过 HTTP Range 提取固定 ZIP 成员，校验文件长度、CRC 和 SHA-256，现有合格文件直接复用；不会下载整个约 268 MiB 的素材包。33 个来源输入共约 27 MiB，包括许可证。`--verify` 不访问网络。

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

`NatureModels.BodyFrame.cs` 根据来源截面拟合完整躯干并留出衣料松量。当前 `reference-contour-physique-v31` 保留两种来源体型，采用整体等比身高变化、共同中性站姿及独立的衣装拟合；肩袖、卷边、实际关节与逆绑定共同适配。上衣保留原面数，低段随骨盆运动，胸廓与腰部平滑过渡；裤装采用下述连续来源表面。详细参数与局限见[成人身体说明](models.md)。 衣料松量按截面法线分配，收褶和垂落跟随成衣腰带；两种体型与三种款式分别缓存，避免服装额外体积覆盖来源轮廓。

宽松衣身参考 [V&A 的衬衫结构](https://www.vam.ac.uk/articles/sew-your-own-customisable-shirt)，胸腹衔接保留布料松量，减少刻画肌肉的重复褶皱；背部肩部拼接以贴面短针脚表示。头部整体上移 18 毫米以露出源颈部。袖窿收至肩侧，袖根延续衣身切平面，左右三角面方向对应。袖身权重按边长离线平滑并约束空间变化；领口、中线与腰带固定，袖口随肩骨，原手柄和手掌长度不变。当前成品为 7,320／7,320／7,200 三角形。改变原始衣片或其胸肩基础体后须重烘焙三套衣装；完整躯干体型在运行模型创建时拟合，单独重建 trunk 文件不要求重烘焙原衣片。普通构建与运行不需要 Blender。成人运行绑定另以独立肩胛带及六个摆动／轴向扭转分离区间保持肩袖截面，并按拟合网格上的袖窿最短路径距离分配袖管位置修正；两种体型共用二十六骨结构，每顶点至多四个影响，袖窿与袖口之间在实际边长上重新求解过渡，原始烘焙网格与来源哈希不变。肩部范围及实际作业入口见[肩袖绑定与动作说明](models.md)。

在项目根目录使用已校验的源数据重建当前身体配方。顺序为躯干、上臂、肩面、裤装；所有进程完成后再检查成品哈希和运行：

```powershell
$bodyTemp = Join-Path (Get-Location) '.tools/tmp/body-export'
New-Item -ItemType Directory -Force $bodyTemp | Out-Null
$previousTemp, $previousTmp = $env:TEMP, $env:TMP
$bodyBlender = (Resolve-Path './.tools/blender-4.5.14/editor/blender-4.5.14-windows-x64/blender.exe').Path
$bodyOutput = 'src/SandBoxSim.Godot/assets/models/human'
try {
    $env:TEMP = $bodyTemp; $env:TMP = $bodyTemp
    foreach ($frame in 'male','female') {
        $label = if ($frame -eq 'male') { 'masculine' } else { 'feminine' }
        $arguments = @('--background','--factory-startup','--disable-autoexec','--python-exit-code','1',
            '--python','tools/export_human_torso.py','--','--source','.tools/sources/makehuman',
            '--frame',$frame,'--region','trunk','--output',"$bodyOutput/trunk-$label-cage.json")
        $process = Start-Process -FilePath $bodyBlender -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "Trunk export failed: $frame" }
    }
    python -B tools/export_upper_arm_support.py --source .tools/sources/makehuman --output $bodyOutput
    if ($LASTEXITCODE -ne 0) { throw 'Upper-arm export failed' }
    python -B tools/export_upper_arm_support.py --source .tools/sources/makehuman --output $bodyOutput --region forearm
    if ($LASTEXITCODE -ne 0) { throw 'Forearm fit export failed' }
    python -B tools/export_shoulder_surface.py --source .tools/sources/makehuman --output $bodyOutput
    if ($LASTEXITCODE -ne 0) { throw 'Shoulder export failed' }
    $arguments = @('--background','--factory-startup','--disable-autoexec','--python-exit-code','1',
        '--python','tools/export_resident_trousers.py','--','--source','.tools/sources/makehuman','--output',$bodyOutput)
    $process = Start-Process -FilePath $bodyBlender -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw 'Trouser export failed' }
} finally { $env:TEMP = $previousTemp; $env:TMP = $previousTmp }
python -B -m unittest discover -s tools -p test_human_assets.py
python -B -m unittest discover -s tools -p test_resident_trousers.py
python -B -m unittest discover -s tools -p test_upper_arm_support.py
python -B -m unittest discover -s tools -p test_motion_sources.py
python -B -m unittest discover -s tools -p test_forearm_fit.py
python -B -m unittest discover -s tools -p test_body_reference.py
./tools/godot.ps1 -Mode test
```

原始衣片与胸肩构造参考采用独立流程：`export_human_torso.py` 不传 `--region trunk` 时生成 65×128 的胸肩构造体，可用 `--frame female` 生成女性构造参考；改变它们或 `garment_pattern.py` 后执行 `./tools/bake-garments.ps1`。只修改当前身体配方时不需要重烘焙未改变的原衣片。

导出器固定写入 UTF-8、LF 换行；运行时先将 CRLF 统一为 LF，再校验 `NatureModels.TorsoAnatomy.cs`、`NatureModels.BodyFrame.cs` 及各模型加载器中固定的 SHA-256。Windows、Linux、macOS 的文本换行差异不改变模型身份；几何数据、元数据或其他文本改变仍会被拒绝。客户端自检覆盖两种换行与有效 JSON 中的半径篡改。更改拟合配方后，要同步审查成品哈希；不要直接跳过校验。人物肩点与衣服骨骼同步作有界位移，肘部偏好和握持手腕限制共同参与求解；具体形态、动作边界与研究来源见[模型系统](models.md)。

## 连续裤装来源

`trousers-male.mesh.json`／`trousers-female.mesh.json` 从同一 CC0 HM08 的 `helper-tights` 服装辅助几何生成，分别应用共同身体配方；不是旧骨盆和大腿截面体的增面。导出器校验源文件、去掉 A 姿势腿部偏移、裁切并保留单连通衣料，拟合裤腰与靴口，输出五骨权重和原始输入哈希。当前 `reference-contour-physique-v31` 按来源骨盆包络、中性站姿和衣料松量组织腰臀与上腿，输出记录 `physique` 配方标记，运行时仍校验完整成品 SHA-256。运行时用骨盆、实际大腿与膝关节驱动连续表面；面数、约束与测试见[模型系统](models.md)。现有头脸文件和手部授权保持独立。

在上述项目临时环境中，使用同一 Blender 重建裤装：

```powershell
$trouserTemp = Join-Path (Get-Location) '.tools/tmp/trouser-export'
New-Item -ItemType Directory -Path $trouserTemp -Force | Out-Null
$previousTemp, $previousTmp = $env:TEMP, $env:TMP
try {
$env:TEMP = $trouserTemp
$env:TMP = $trouserTemp
$process = Start-Process -FilePath (Resolve-Path './.tools/blender-4.5.14/editor/blender-4.5.14-windows-x64/blender.exe') -WindowStyle Hidden -Wait -PassThru -ArgumentList @('--background', '--factory-startup', '--disable-autoexec', '--python-exit-code', '1', '--python', 'tools/export_resident_trousers.py', '--', '--source', '.tools/sources/makehuman', '--output', 'src/SandBoxSim.Godot/assets/models/human')
if ($process.ExitCode -ne 0) { throw 'Trouser export failed' }
} finally { $env:TEMP = $previousTemp; $env:TMP = $previousTmp }
python -m unittest discover -s tools -p test_resident_trousers.py
```

当前 `reference-contour-physique-v31` 的裤装为男性 2,741 顶点／5,328 三角面、女性 2,591 顶点／5,008 三角面，保持五骨蒙皮、共享材质和原始 CC0 输入来源。导出器、工具测试与运行加载器校验成品 SHA-256、连通性及米制范围；胸腰和骨盆的成衣截面直接与来源比较，具体边界见[模型系统](models.md)。

## 上臂解剖支撑与姿态表面

[upper-arm-male.json](../../src/SandBoxSim.Godot/assets/models/human/upper-arm-male.json) 和 [upper-arm-female.json](../../src/SandBoxSim.Godot/assets/models/human/upper-arm-female.json) 来自本项目现有、哈希校验的 MakeHuman HM08 基础网格、共同身体配方和肩肘关节组，核心素材许可为 [CC0](https://www.makehumancommunity.org/about/license.html)。两个文件分别保存肩肘来源坐标、来源哈希、目标臂长和 25×64 个上臂截面半径；来源区域由完整手臂骨骼后代隔离，截面在来源肩肘轴内计算，保留三角肌区域向肘部收束的轮廓与方向差异，不能当作肌肉运动实测数据。运行时对 LF／CRLF 统一后检查完整文件哈希。

可复用工具 [export_upper_arm_support.py](../../tools/export_upper_arm_support.py) 只依赖 Python 标准库，使用 `.tools/sources/makehuman` 中已校验来源，不重新下载素材。重建命令为 `python tools/export_upper_arm_support.py --source .tools/sources/makehuman --output src/SandBoxSim.Godot/assets/models/human`；对应测试同时检查来源重建一致性、有效截面和方向性体积。修改来源或配方后须同步审查运行时固定哈希。

衣袖在当前体型拟合后加入上臂支撑、布料松量，保留共享袖窿拓扑和袖口连接，姿态修正共同整理肩上衣片及袖窿，再生成一百二十个有限姿态的表面修正。修正覆盖抬臂平面、高度及轴向扭转，包括位置和重建后的法线；静止法线不能直接当作抬臂后皮肤／衣料的真实朝向。当前仍为有限姿态插值、程序动作及衣料蒙皮，没有裸露上臂皮肤、肌肉动力学或布料碰撞；空闲伸展的捕捉数据见下节。算法、边界和运行入口见[模型系统](models.md)。

## 三维肩部来源与捕捉动作

[shoulder-surface-male.json](../../src/SandBoxSim.Godot/assets/models/human/shoulder-surface-male.json) 与[女性表面](../../src/SandBoxSim.Godot/assets/models/human/shoulder-surface-female.json) 使用同一 CC0 人体来源及当前共同身体配方，另采用 MakeHuman 的 `default.mhskel`（SHA-256 `99f179bce0aa850b45d4191a1d0d234c5851f881c057439470ded3bddf729a24`）和 `default_weights.mhw`（`0f3641d651ae3d00ad6b4ccee43142edb109d3bd909d27d9e4139ef1beed8625`）。两份文件的来源元数据均明确为 CC0，作者为 Data Collection AB、Joel Palmius、Jonas Hauquier；[骨架](https://github.com/makehumancommunity/makehuman/blob/master/makehuman/data/rigs/default.mhskel)、[权重](https://github.com/makehumancommunity/makehuman/blob/master/makehuman/data/rigs/default_weights.mhw)保留原定义。原权重存在千分位舍入，导出时先归一化，再与网格一起细分。导出包含静止表面、原始 A 姿势、四组权重、肩点和绑定旋转，保留完整来源重建信息。当前为 schema 3：垂臂绑定以共同肩点的归一化四元数混合生成，保持来源截面；运行时从原始 A 姿势出发，将每根目标变换与相应绑定旋转组合后作双四元数混合。固定三角面对应仍建立在垂臂表面，胸片保留肱骨的静止绑定，袖片参与实际肱骨旋转。导出与运行时的中性姿态有逐顶点一致性检查，详见[模型系统](models.md)。

重建使用 [export_shoulder_surface.py](../../tools/export_shoulder_surface.py)：`python -B tools/export_shoulder_surface.py --source .tools/sources/makehuman --output src/SandBoxSim.Godot/assets/models/human`，原始两份骨架文件存于该来源目录的 `rig/`。工具只依赖 Python 标准库，运行与普通构建不需要 Blender。

### 动作捕捉

[cmu-143-30-stretch.json](../../src/SandBoxSim.Godot/assets/motions/cmu-143-30-stretch.json) 来自 [CMU 143_30 / Stretch And Yawn](http://mocap.cs.cmu.edu/search.php?subjectnumber=143)，原始 `143.asf` 与 `143_30.amc` 的 SHA-256 分别为 `6d7207da3bb9eceeaa32f23fb641b69f5836d11242b00c052e7d64f0cbe20a35`、`ffd7b2b3344cf41eef16a3b9b2910424dc27c24ac1f4d6c3b68b6c719e7b22e3`。数据可用于研究及商业产品，但不得直接转售原始或转换后的数据；它不属于 CC0，必须保留[CMU 来源与许可说明](../../src/SandBoxSim.Godot/assets/motions/LICENSE.txt)。The data used in this project was obtained from mocap.cs.cmu.edu. The database was created with funding from NSF EIA-0196217.

重建命令为 `python -B tools/export_cmu_arm_motion.py --source .tools/sources/mocap/cmu-143 --output src/SandBoxSim.Godot/assets/motions/cmu-143-30-stretch.json`。工具遵循 [ASF/AMC 格式](https://research.cs.wisc.edu/graphics/Courses/cs-838-1999/Jeff/ASF-AMC.html)，排除根位移和未捕捉的指／拇指通道；运行器固定校验导出哈希。导出、来源重建及数据契约由 `tools/test_motion_sources.py` 检查。

[cmu-walk-cycles.json](../../src/SandBoxSim.Godot/assets/motions/cmu-walk-cycles.json) 来自 [CMU 07_01](https://mocap.cs.cmu.edu/search.php?subjectnumber=7) 和 [08_01](https://mocap.cs.cmu.edu/search.php?subjectnumber=8) 的 120 Hz 步行试次，使用相同的来源许可。导出器按前向运动学标注的脚跟触地、脚尖离地和下一次触地选段。48 个循环采样包含胸廓、骨盆俯仰和前后／侧向位移、上臂前后／侧向摆动、屈肘及各脚的鞋底俯仰。每脚另有 21 个摆动采样，记录高度、前进比例和侧向弧线。鞋底俯仰以来源足骨的平足阶段为零位。周期首尾去漂移并排除总体根位移，游戏独立计算行进距离、脚底支撑和握持。

把两组 ASF/AMC 分别放进 `.tools/sources/mocap/cmu-07` 和 `.tools/sources/mocap/cmu-08`。使用 [export_cmu_gait.py](../../tools/export_cmu_gait.py) 重建。

```powershell
python -B tools/export_cmu_gait.py --source .tools/sources/mocap --output src/SandBoxSim.Godot/assets/motions/cmu-walk-cycles.json
```

[cmu-action-phrases.json](../../src/SandBoxSim.Godot/assets/motions/cmu-action-phrases.json) 采用 CMU 的 [02_06 弯身与起身](https://mocap.cs.cmu.edu/search.php?subjectnumber=2)、[18_08 交谈](https://mocap.cs.cmu.edu/search.php?subjectnumber=18) 与 [13_09 饮水](https://mocap.cs.cmu.edu/search.php?subjectnumber=13)，使用上述同一许可。每条片段保留 65 个时间采样及原始 120 Hz 帧段。拾取截取两次下探、取物及起身，交谈截取单手和双手各一段，饮水重定时保留接近、抬头及放下顺序。导出保留各部位时序与相对行程，并对首尾姿态去漂移。文件记录原始哈希，身体位移、握持与目标接触由游戏解算。

[cmu-task-phrases.json](../../src/SandBoxSim.Godot/assets/motions/cmu-task-phrases.json) 使用 CMU 的 47_01 步行折返（120 Hz）和 80_05 挖掘（60 Hz）。转身片段保留头胸相对朝向和双臂在胸廓坐标中的相对位移。两段挖掘分别对齐下探、拉回和提起的关键帧，将胸廓、头部与重心运动重定时到游戏的锄地周期。各源文件的 SHA-256、帧段及原始采样率随成品记录，适用同一 [CMU 许可](../../src/SandBoxSim.Godot/assets/motions/LICENSE.txt)。[47_01](https://mocap.cs.cmu.edu/search.php?subjectnumber=47) · [80_05](https://mocap.cs.cmu.edu/search.php?subjectnumber=80)

```powershell
python -B tools/export_cmu_task_motion.py --source .tools/sources/mocap --output src/SandBoxSim.Godot/assets/motions/cmu-task-phrases.json
```

原始 ASF/AMC 可放在 `.tools/sources/mocap/cmu-02`、`cmu-18` 和 `cmu-13`。用 [export_cmu_actions.py](../../tools/export_cmu_actions.py) 重建。

```powershell
python -B tools/export_cmu_actions.py --source .tools/sources/mocap --output src/SandBoxSim.Godot/assets/motions/cmu-action-phrases.json
```

当前裤装在细分表面加入腰下／膝上压褶和侧缝形体，并对中线裤裆的极窄翻折接缝作局部表面松弛，保留原始索引与面数；不改变裤腰及两只靴口的开放边界。`tools/resident_clothing.py` 提供独立于 Blender 的米制褶皱配方，导出时约束在来源骨盆与靴口之间。运行时上衣的近景拟合取实际缝合网格截面，成衣下摆按三套款式区分；详细参数见[模型系统](models.md)。

女性裤片的内侧圆顺、裤腿静止松量以及左右髋屈与膝屈修形都在构建网格缓存时生成。复现时用相同的已校验源裤装即可，形状及法线由 [NatureModels.Trousers.cs](../../src/SandBoxSim.Godot/NatureModels.Trousers.cs) 生成，并与近远景共享五骨蒙皮。实际动画使用大腿相对骨盆的朝向和各自的膝屈角度更新形状权重。

当前腿部由同一已校验 HM08 身体表面提供径向截面支撑，服装辅助网格仅保留为连续拓扑；导出器在髋—膝中心线上取样、添加一次衣料间隙，并保留骨盆与靴口的渐变区。每套输出的 `anatomicalSupport` 包含 21×64 个来源半径，供最终成衣截面检查；没有添加新的素材许可或运行依赖。具体绑定与鞋楦偏移见[模型系统](models.md)。

腕部专项检查：`./tools/godot.ps1 -Mode test -HandContinuityOnly`。近景公开回放：`python tools/evaluate.py run benchmarks/scenarios/visual-wrist-motion.json --output .tools/tmp/wrist-study`；普通可运行包仍直接使用已导出的模型。

重新烘焙手部时，先生成连体 `human-hand.mesh.json` 与其 LOD，再执行 `export_upper_arm_support.py --region forearm`；后者直接测量当前连体表面，不能与旧版手部截面混用。手掌原索引和指甲分区保留，腕部连接的顶点、法线及运行权重按当前转换流程生成。

当前成人脸部拟合标识为 `balanced-adult-v3`，额前发根为 `framed-source-root-v2`。来源头脸与发片文件、UV 和许可保持各自定义；运行时减少夸张的下脸收缩与颊部修饰，并按来源头皮运输女性额前发片。皮肤、睫毛与泪线共用有限的静止闭合及原眨眼形变。完整参数与衣装、前臂的受限表面松弛见[模型系统](models.md)。女性前臂的 0.8 毫米局部松弛在来源截面拟合后执行，按来源缓存，不修改原始雕刻文件或握柄区域。

女性配方另包含 `surfaceStyle=balanced-silhouette-v6`。重建消费者应使用 `resident_body.body_points`，应用来源目标混合后再执行柔和形体场；该场包含前胸的有界向上重分配与胸下回收，使用三轴米制位移，并固定关节辅助点。只叠加 `targets` 无法复现当前成品。每份支撑的 `shapeRecipe.surfaceStyle` 与配方哈希共同标识这一处理。

当前成衣 `settled-neck-v24` 在缓存构建时增加有界的前胸横向曲面拟合，并同步门襟、纽扣、针脚和分片缝线；它改变运行时成衣网格，来源身体及五类支撑仍使用 `reference-contour-physique-v31`。参数及最终三角面检查见[模型系统](models.md)。

肩背的运行时形体处理标记为 `settled-neck-v22`／`settled-neck-v24`；来源身体与肩面文件均按 v29 配方重建。读取原始肩面不能替代运行时肩背映射及中性绑定重建，详见[模型系统](models.md)。

当前圆领、薄折边及腰胯衣料细节使用 `settled-neck-v24`／`settled-neck-v22`，由已有来源在运行时生成。没有更换原始人体文件；颈部覆盖检查读取实际头部网格与成品衣领，详细参数见[模型系统](models.md)。

女性躯干支持新增 `skinRadii` 与 `anatomicalSampling=source-anterior-skin-rays-v1`，来源为同一 CC0 身体。旧 `radii` 凸包继续保留；重建时应同时输出两层，随后重新导出引用支持文件哈希的裤装，并更新运行时与来源测试的成品哈希。源文件读取与形体配方仍使用已有工具，详见[模型系统](models.md)。

前胸近景回放：`python tools/evaluate.py run benchmarks/scenarios/visual-thorax-frames.json --output .tools/tmp/thorax-study --resolution 1600x1000`。该面板展示同一成年居民的三种观察方向，冻结模拟，用于观察当前衣装形体。

女性成人前胸的运行时支撑由 `FemaleThoraxSupport` 从原始 `skinRadii` 缓存生成：前侧体积局部向内重分配，再按变形截面求径向支撑，供成衣拟合与皮肤间隙检查共用。原始身体、五类来源支撑及裤装没有重导出，既有来源哈希继续有效；当前衣装表现为 `settled-neck-v24`、整体形体表现为 `settled-neck-v22`。外侧衣面在肩胸松弛之后再次受支撑约束，最终前侧轮廓由原生三角面回归验证，参数见[模型系统](models.md)。

当前腰腹与侧腰使用 `BalancedTorsoPoint` 的独立有界场，并与前胸修形共同重采运行时截面；不修改上游人体来源哈希。实际衣身、腰带与挂点共同拟合，成人正侧背与胸廓近景展示当前成品。具体边界和测量入口见[模型系统](models.md)。

成年女性的折领、肩颈过渡、裤腰上方的衣料和靴楦在客户端模型构建时拟合。折领按体型和剪裁分别缓存，靴底接触采样取自同一鞋楦映射。预览使用 `body-frames` 的正、侧、背面，抬臂和步行继续使用实际蒙皮模型。[形体与接触检查](models.md)

当前前胸体积场 `ChestVolumePoint` 与既有内收／腰腹映射共同构建女性成人运行时支撑，最大新增前向位移 5 毫米。来源身体使用 v29，成品采用 `settled-neck-v22`／`settled-neck-v24` 标识；上游参考文件哈希与许可保持原状，当前十份衍生支撑按 v29 重新生成。前胸局部滤波、成衣截面及固定根部检查见[模型系统](models.md)。

当前运行时胸廓支撑在映射后增加有界的前侧纵向圆顺，再用于成衣与接触恢复；前向体积采用较宽的上胸与胸下衰减，最大新增 5 毫米，原始资产哈希继续保留。`settled-neck-v22`／`settled-neck-v24` 与 `CHEST_CURVE_PASS` 参数见[模型系统](models.md)。

当前运行时肘部皮肤使用 `continuous-elbow-v9` 的 56 骨手臂支撑；原始来源仍保留 16 骨和原始索引／许可。男女前臂的截面拟合延续到隐藏肘口，局部圆顺按体型分别缓存；女性裤装的有界圆顺不改变原始来源文件与五骨合同。运行时参数、固定接缝和实际蒙皮检查见[模型系统](models.md)。

`continuous-elbow-v9` 的 24 组前臂位置／法线样本由成品手部网格与实际蒙皮生成。可见肘域使用截面角分配弯曲，随后按弯曲后的轴向坐标分配旋转，袖内开口固定；原始来源仍为 16 骨合同，运行时仍为 56 骨。骨点与矩阵通过不能替代实际三角面朝向检查。来源拓扑、UV、人体及衣装哈希和许可保持原状；局部坐标、缓存与验证条件见[模型系统](models.md)。

`stable-contour-v3` 仅使用来源衣料与 120 组肩袖方向形态，不含额外上臂体积目标。固定肩位参考直接给定关节角并连续跟踪前臂参考相位；实际游玩仍使用原有握持 IK。原始身体、上臂支撑、袖片、UV、许可和哈希不变。回放与逐帧检查见[模型系统](models.md)。

`settled-neck-v22`／`settled-neck-v24` 在运行时保留作者法线细节，肩袖细节以邻域方向运输；女性肩背、腰胯与腿部继续使用来源体型和有界圆顺。成品近景的衣料 UV 作为独立静止颜色参考，UV2 的衣片几何约定保持不变；这属于运行时表示，原始来源 UV、拓扑、资产哈希和许可未变。具体约束见[模型系统](models.md)。

`settled-neck-v22`／`settled-neck-v24` 在来源拟合之后重整实际肩袖表面，原始衣片索引和来源文件保持不变；`settled-neck-v19` 固定绑定会排除退化或过细的支撑面。成人静态远景从同一成品静止网格生成。前臂采用 `curvature-flow-v9` 的连续曲率插值，来源 49 排的值、原始 UV／许可与哈希保持原状。几何范围与验证条件见[模型系统](models.md)。

当前上臂支撑从既有 25×64 来源半径中构建周期三次 B 样条，端部固定接续，保留原有袖口与位移界限。女性颈肩场在原位移范围内使用零端曲率的五次过渡；这些处理只改变运行时成品，不改写原始人体与衣装拓扑、UV、配方或许可。慢速腕／肘更新以分量距离和稳定的小角读取实现，来源骨架及 24 组前臂形态合同保持原状；接口和检查见[模型系统](models.md)。

`balanced-silhouette-v6` 的源形体场由 [resident_shape.py](../../tools/resident_shape.py) 定义，`resident_body.body_points` 在混合参考位移之后统一应用。腰侧、后腰、骨盆后侧及大腿接续与前胸共享一个来源映射；关节辅助点固定，头脸和手掌网格不参与。当前十份支撑的 `shapeRecipe`、配方 SHA-256 和运行时文件哈希一起核对，不能仅更新身体标签或修改其中一份文件。源码重建、全部来源检查与完整客户端自检共同约束当前成品。

`balanced-silhouette-v6` 在原有全身场中以光滑偶坐标连接左右前胸，中线切线随来源测试校验。v29 的十份衍生支撑已共同重建，固定输入哈希与 `shapeRecipe` 同步更新。上胸与袖根的最终圆顺、姿态烘焙末端处理和领口／源颈部接续属于运行时成品流程，见[模型系统](models.md)；不得只读取原始肩面就当作当前显示网格。
