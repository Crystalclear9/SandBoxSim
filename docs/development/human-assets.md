# 人物解剖素材与转换

居民头脸使用 Dan Ulrich / Blender 提供的三套 CC0 解剖数据，前臂和手部使用 Julien Kaspar / Blender Studio 的 CC BY 4.0 衍生数据。素材复用游戏的骨架、动作和稳定身份，不增加性别、职业或游玩限制。

## 当前头脸数据

| 文件 | 来源 | 头颈顶点 / 三角面 | 远景头脸三角面 |
|---|---|---|---|
| [human-head.mesh.json](../../src/SandBoxSim.Godot/assets/models/human/human-head.mesh.json) | Body Male Realistic 头颈，CC0 | 17,209 / 27,241 | 2,400 |
| [human-scan.mesh.json](../../src/SandBoxSim.Godot/assets/models/human/human-scan.mesh.json) | Head Animation Realistic，CC0 | 14,527 / 22,641 | 2,400 |
| [human-feminine.mesh.json](../../src/SandBoxSim.Godot/assets/models/human/human-feminine.mesh.json) | Body Female Realistic 头颈，CC0 | 17,395 / 27,490 | 2,400 |

默认头部已替换旧长脸来源。每套数据包含独立眼球、唇部、头皮查找表、真实眼裂的上下缘，以及同源减面头脸；保留颈前雕刻区域，裁切边界位于领口以下。八种身份原型另有儿童变形缓存：面中部缩短、面颊略宽、纵深减弱，眼口和头皮使用同一变形，儿童材质不带胡茬。成人躯干按体型收束，头部通过反向宽度补偿保留源解剖比例。

每眼沿实际头部和眼球的可见相交区域测量 49 对上下缘点。运行时以此生成上眼睑，并把闭合坐标写入第二 UV 通道；眨眼沿真实眼球前表面闭合，静息状态保留少量上眼睑下垂，不缩放眼球。旧的通用圆弧眼睑与额外下眼睑贴片已移除。

男性和女性头部同时提取上唇与下唇，连接脸部的边界顶点用颜色 alpha 固定，内部顶点才随说话轻微开合。每个人持有独立口部材质参数，不整体缩放唇网格，也不共享说话状态。

三张 2048×2048 法线图绑定各自 UV。男性和女性头部把高一级雕刻烘焙到运行网格；动画扫描头部保留同级表面。UV 缝分裂顶点，网格合并后生成切线；JSON 保存原图 SHA-256，编辑器核对原图，发行包通过 Godot 的纹理重映射读取导入图。运行时修正偏离实际曲面的异常法线。

远景头脸由同一头颈、眼球与唇部离线减面，最多 2,400 三角面，再与简化的同源头皮发型合并为单材质。远景不再调用独立的程序化脸，也不再额外叠加椭球耳朵和眼睛。普通构建与 CI 直接读取提交数据，不依赖 Blender 或联网。

转换入口为 [export_human_variants.py](../../tools/export_human_variants.py)；[export_human_head.py](../../tools/export_human_head.py) 保留默认男性头部的简短入口。辅助程序为 [bake_human_detail.py](../../tools/bake_human_detail.py)、[human_eyelids.py](../../tools/human_eyelids.py) 与 [human_head_lod.py](../../tools/human_head_lod.py)。运行时由 [NatureModels.HumanAsset.cs](../../src/SandBoxSim.Godot/NatureModels.HumanAsset.cs) 校验、缓存并变形。

近景发型保留 2,400 根细发丝、44 条浅起伏发束及部分低发髻；它们随实际头皮和头部骨骼移动，远景省略细发丝。眉毛是左右各 72 组贴合面部的弧面毛束。

## 肘部与前臂

[human-forearm.mesh.json](../../src/SandBoxSim.Godot/assets/models/human/human-forearm.mesh.json) 来自 Julien Kaspar / Blender Studio 的授权人体的肘部、前臂区域 12/102，共 1,928 顶点、3,687 三角面。它替换了运行时的球形肘部和规则管状前臂，保留原作者的肘尖、肌腹与近腕骨形体；不是肌肉或软组织物理。

[export_human_forearm.py](../../tools/export_human_forearm.py) 校验原始文件哈希后，评估一级多重细分、截取连续表面、平顺原始倾斜中心线，并将长度接入当前骨架。上端埋入卷袖；腕端位于肘部局部 Y=-0.210，与手节点 Y=-0.255 加手网格腕环 Y=0.045 对齐，横截面半径为 0.027/0.024。腕口拟合为平面，避免倾斜裁切或重叠留下台阶。

[NatureModels.Forearm.cs](../../src/SandBoxSim.Godot/NatureModels.Forearm.cs) 校验成品哈希并缓存左右网格，镜像时同时处理顶点、法线和三角面朝向。近景前臂与手掌通过共顶点过渡环连成单张蒙皮，前臂顶点使用现有腕部补偿骨，抵消手腕转动而保持肘部挂接；指骨仍驱动手指。远景使用静态前臂和手部 LOD，腕端保留埋入重叠。自检检查连续性、边界位置、左右镜像，并在实际工具动作中检查变形后的手腕仍与前臂截面一致。

`--panel=forearms` 是本地维护预览，展示相同模型的左右掌侧/背侧。它不扩展 HTTP 控制或评测请求协议；公开评测面板列表保持原有定义。

## 解剖手部

[human-hand.mesh.json](../../src/SandBoxSim.Godot/assets/models/human/human-hand.mesh.json) 从 Julien Kaspar / Blender Studio 的授权人体提取手掌、指缝与五指。近景文件包含连续的前臂和手部，共 9,154 个顶点、18,205 个三角面；其中原始手部区域有 7,255 个顶点、14,464 个三角面，`handVertexCount` 保留该分界，供远景只提取手部。甲面从实际指背曲面提取，运行时仅上传甲面引用的顶点。

手部使用平滑位移场接入掌指骨，左右手镜像骨骼位置与展开方向；腕环与前臂在运行坐标中对齐；`join_human_arm.py` 用共顶点过渡环接入前臂，并重新计算连续表面法线。掌根在相邻指骨间平滑分配权重，手指保留近节、中节与末节。每手 16 骨，与原有握持、取用和收纳动作共用。指甲与皮肤使用同一表面分区、共享权重，随手指一起变形，避免叠面闪烁。

近景、远景、指根和拇指骨骼共用 `ResidentRig.HandMirror` 的左右约定。远景张手与握拳网格按左右独立缓存，镜像时同步反转法线、切线手性和三角面朝向；拇指轮廓位于食指一侧并露出掌缘，避免默认视距把两只手显示成同一侧。`--panel=handedness` 可并排查看同一角色的左右掌心、手背以及远景简模。自检覆盖镜像拓扑、拇指方向、骨骼张合、LOD 切换，以及主手和支撑手的实际蒙皮与工具轴间距。

远景手部使用 [human-hand-lod.mesh.json](../../src/SandBoxSim.Godot/assets/models/human/human-hand-lod.mesh.json)：从同一文件的手部区域提取、减面得到 708 个顶点、1,400 个三角面，保留指缝、不同指长与拇指掌根。转换按最近源三角面的重心坐标转移 UV、法线和四权重；运行时沿用近景指节角度，缓存张手与握柄两种静态姿势。远景不更新手部骨架，也不再使用独立椭球拼成的手型；远景省略独立甲面材质。左右仍分别缓存并校验镜像朝向，握柄简模也检查实际手柄间距。

LOD 由 [export_hand_lod.py](../../tools/export_hand_lod.py) 生成，读取仓库中的高精度手部，校验源哈希；普通运行直接加载导出 JSON。减面后的皱褶与甲面不具备近景精度，八视图图册用于比较形状连续性。近景与远景分别切换连体蒙皮和静态前臂，避免重复绘制；近景在实际工具动作中检查整段前臂留在肘坐标系内。

手部导出由 [export_human_hands.py](../../tools/export_human_hands.py) 完成，运行时位于 [NatureModels.HandSkin.cs](../../src/SandBoxSim.Godot/NatureModels.HandSkin.cs)。检查覆盖非流形边、腕口边界、权重、握持全过程和变形表面的手柄间距。

## 重新导出

便携 Blender 4.5.14 和原始文件保存在项目 `.tools/` 中，原文件中的脚本保持禁用。三套头部源文件的 SHA-256 为：

| `.tools/sources/blender-assets/` 下的文件 | SHA-256 |
|---|---|
| `body-realistic-male.blend` | `697b55a5595a756f9304d12909c85f9eddb0c0f57ec2b6c8f1edf071e00f9b30` |
| `head-realistic-animation.blend` | `a680ca23a0dee2a16ecc6e3921ddcfa0e039a5a7ef4262ec56214a924d08d3ca` |
| `body-realistic-female.blend` | `74917ec876cdb6aae3b6d291f2f638f5a959276f4115a714cf8c5dcf6a99ce20` |

```powershell
$humanBlender = './.tools/blender-4.5.14/editor/blender-4.5.14-windows-x64/blender.exe'
$humanOutput = 'src/SandBoxSim.Godot/assets/models/human'
& $humanBlender --background --disable-autoexec ./.tools/sources/blender-assets/body-realistic-male.blend --python-exit-code 2 --python tools/export_human_variants.py -- --kind masculine --output $humanOutput
& $humanBlender --background --disable-autoexec ./.tools/sources/blender-assets/head-realistic-animation.blend --python-exit-code 2 --python tools/export_human_variants.py -- --kind scan --output $humanOutput
& $humanBlender --background --disable-autoexec ./.tools/sources/blender-assets/body-realistic-female.blend --python-exit-code 2 --python tools/export_human_variants.py -- --kind feminine --output $humanOutput
```

脚本验证源文件后提取头颈、眼口、眼裂、头皮、远景网格及法线图；不自动下载。生成后同步 `HumanHashes`，运行素材检查、客户端自检与实际画面核对：

```powershell
python -m unittest discover -s tools -p test_human_assets.py
./tools/godot.ps1 -Mode test
```

前臂、手部和动物仍可通过 `./tools/bake-models.ps1` 重建。手臂源文件为 `.tools/sources/realistic_human_base.blend`，SHA-256 为 `1d9d2a6070acd6a3d0d9b676af816001310fb0f6128d07a3ab3e0318c7fdd981`。配置、下载和缓存均留在项目内，普通游玩只需要成品数据。

## 发布与署名

[LICENSE.txt](../../src/SandBoxSim.Godot/assets/models/human/LICENSE.txt) 分别列出三套 CC0 头脸和 CC BY 4.0 手臂的来源、作者、校验值与转换内容。头部当前不再使用旧的 CC BY 长脸数据；手掌、手部 LOD、前臂及连体手臂仍必须保留原作者署名。软件许可不替代素材许可。

Godot 的三平台导出预设包含网格 JSON、许可文本与导入纹理；发行包自检会读取并校验成品。原始 Blender 文件、便携工具和转换中间数据不是运行依赖。
