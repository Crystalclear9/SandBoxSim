# 人物解剖素材与转换

近景头脸使用 Blender Studio 的 **Realistic Human Base** 衍生网格，来源为 Julien Kaspar / Blender Studio，许可为 [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)。原始来源与下载见 [Use of Base Meshes](https://studio.blender.org/training/realistic-human-research/use-of-base-meshes/)。它是人工制作的写实基础网格，当前客户端使用自己的肤色、材质和动作表现。

## 当前数据

[human-head.mesh.json](../../src/SandBoxSim.Godot/assets/models/human/human-head.mesh.json) 包含经过一级多重细分处理的头颈、耳部、独立唇部、左右眼球和头发表面查找表。头颈有 17,689 个导出顶点和 34,585 个三角面，嘴唇有 638 个顶点，每只眼球有 2,786 个顶点。颜色边界处允许分裂顶点以保留唇色与口腔色层。

源码校验原始 `.blend` 的 SHA-256，运行时校验导出数据的 SHA-256。生成脚本是 [export_human_head.py](../../tools/export_human_head.py)，运行时读取、变体和头发贴合位于 [NatureModels.HumanAsset.cs](../../src/SandBoxSim.Godot/NatureModels.HumanAsset.cs)。普通构建、运行和评测直接使用仓库中的导出数据，无须 Blender 或在线下载。

转换保留原作者的面部拓扑，提取头颈和耳部；唇部独立用于原有交谈与进食表现，左右眼球保留体积并由眼睑闭合实现眨眼。坐标转换在三个方向统一使用 0.90 比例，保留原作者的解剖宽高深关系；颈根仍经过收束以接入游戏衣身。运行时八种结构调整下颌到上脸的宽度与面部纵深，不再分别推移鼻、颊、眉弓、下颌和嘴角；所有面部件和挂点使用同一变形，法线以局部 Jacobian 修正。头发按实际头颅表面查找表贴合，分别控制额前、鬓角、后颈与冠顶轮廓，近景增加渐细弧面发束；年龄沿用原有缩放。运行时保留原始唇色和浅暖色层。当前没有独立女性基础网格或年龄专用雕刻。

## 肘部与前臂

[human-forearm.mesh.json](../../src/SandBoxSim.Godot/assets/models/human/human-forearm.mesh.json) 来自同一授权人体的肘部、前臂区域 12/102，共 1,928 顶点、3,687 三角面。它替换了运行时的球形肘部和规则管状前臂，保留原作者的肘尖、肌腹与近腕骨形体；不是肌肉或软组织物理。

[export_human_forearm.py](../../tools/export_human_forearm.py) 校验原始文件哈希后，评估一级多重细分、截取连续表面、平顺原始倾斜中心线，并将长度接入当前骨架。上端埋入卷袖；腕端位于肘部局部 Y=-0.210，与手节点 Y=-0.255 加手网格腕环 Y=0.045 对齐，横截面半径为 0.027/0.024。腕口拟合为平面，避免倾斜裁切或重叠留下台阶。

[NatureModels.Forearm.cs](../../src/SandBoxSim.Godot/NatureModels.Forearm.cs) 校验成品哈希并缓存左右网格，镜像时同时处理顶点、法线和三角面朝向。近景前臂与手掌通过共顶点过渡环连成单张蒙皮，前臂顶点使用现有腕部补偿骨，抵消手腕转动而保持肘部挂接；指骨仍驱动手指。远景使用静态前臂和手部 LOD，腕端保留埋入重叠。自检检查连续性、边界位置、左右镜像，并在实际工具动作中检查变形后的手腕仍与前臂截面一致。

`--panel=forearms` 是本地维护预览，展示相同模型的左右掌侧/背侧。它不扩展 HTTP 控制或评测请求协议；公开评测面板列表保持原有定义。

## 解剖手部

[human-hand.mesh.json](../../src/SandBoxSim.Godot/assets/models/human/human-hand.mesh.json) 从同一授权人体提取手掌、指缝与五指。近景文件包含连续的前臂和手部，共 9,154 个顶点、18,205 个三角面；其中原始手部区域有 7,255 个顶点、14,464 个三角面，`handVertexCount` 保留该分界，供远景只提取手部。甲面从实际指背曲面提取，运行时仅上传甲面引用的顶点。

手部使用平滑位移场接入掌指骨，左右手镜像骨骼位置与展开方向；腕环与前臂在运行坐标中对齐；`join_human_arm.py` 用共顶点过渡环接入前臂，并重新计算连续表面法线。掌根在相邻指骨间平滑分配权重，手指保留近节、中节与末节。每手 16 骨，与原有握持、取用和收纳动作共用。指甲与皮肤使用同一表面分区、共享权重，随手指一起变形，避免叠面闪烁。

近景、远景、指根和拇指骨骼共用 `ResidentRig.HandMirror` 的左右约定。远景张手与握拳网格按左右独立缓存，镜像时同步反转法线、切线手性和三角面朝向；拇指轮廓位于食指一侧并露出掌缘，避免默认视距把两只手显示成同一侧。`--panel=handedness` 可并排查看同一角色的左右掌心、手背以及远景简模。自检覆盖镜像拓扑、拇指方向、骨骼张合、LOD 切换，以及主手和支撑手的实际蒙皮与工具轴间距。

远景手部使用 [human-hand-lod.mesh.json](../../src/SandBoxSim.Godot/assets/models/human/human-hand-lod.mesh.json)：从同一文件的手部区域提取、减面得到 708 个顶点、1,400 个三角面，保留指缝、不同指长与拇指掌根。转换按最近源三角面的重心坐标转移 UV、法线和四权重；运行时沿用近景指节角度，缓存张手与握柄两种静态姿势。远景不更新手部骨架，也不再使用独立椭球拼成的手型；远景省略独立甲面材质。左右仍分别缓存并校验镜像朝向，握柄简模也检查实际手柄间距。

LOD 由 [export_hand_lod.py](../../tools/export_hand_lod.py) 生成，读取仓库中的高精度手部，校验源哈希；普通运行直接加载导出 JSON。减面后的皱褶与甲面不具备近景精度，八视图图册用于比较形状连续性。近景与远景分别切换连体蒙皮和静态前臂，避免重复绘制；近景在实际工具动作中检查整段前臂留在肘坐标系内。

手部导出由 [export_human_hands.py](../../tools/export_human_hands.py) 完成，运行时位于 [NatureModels.HandSkin.cs](../../src/SandBoxSim.Godot/NatureModels.HandSkin.cs)。检查覆盖非流形边、腕口边界、权重、握持全过程和变形表面的手柄间距。

## 重新导出

当前数据由 Blender 4.5.14 LTS 转换。将便携 Blender 与原始 `.blend` 保存到项目 `.tools/` 下的版本目录和素材来源目录，SHA-256 必须为：

```text
1d9d2a6070acd6a3d0d9b676af816001310fb0f6128d07a3ab3e0318c7fdd981
```

从仓库根目录执行，路径按本机安装位置填写：

```powershell
$headBlender = './.tools/blender-4.5.14/editor/blender-4.5.14-windows-x64/blender.exe'
$headSource = './.tools/sources/realistic_human_base.blend'
& $headBlender --background --disable-autoexec $headSource --python tools/export_human_head.py -- --output src/SandBoxSim.Godot/assets/models/human
```

脚本使用明确的对象名和雕刻区域，评估多重细分、拆分头脸与眼唇、重定位颈部、转换坐标并生成头发表面查找表。原文件中的脚本保持关闭。导出后计算数据 SHA-256，更新运行时代码的 `HumanAssetHash`，运行 `./tools/godot.ps1 -Mode test` 并查看 `visual-faces`、`visual-wardrobe` 和动作场景。

人物前臂、手部与动物外皮可一起重新生成：

```powershell
./tools/bake-models.ps1
```

`bake-models.ps1` 依次生成解剖前臂、高精度手部、手部 LOD 和动物外皮。

脚本默认查找 `.tools/blender-4.5.14/` 下的 Blender 和 `.tools/sources/realistic_human_base.blend`。可以显式传入 `-BlenderPath`、`-HumanSource` 使用已有文件；下载、缓存与临时数据保持在项目内。输出会打印派生模型哈希，修改生成逻辑后需审查并同步运行时代码中的哈希，再运行客户端自检。普通游玩和构建直接使用已提交的网格，不依赖 Blender。

## 发布与署名

许可记录保存在 [LICENSE.txt](../../src/SandBoxSim.Godot/assets/models/human/LICENSE.txt)，包含来源、作者、原文件校验值及修改内容。该模型数据适用 CC BY 4.0，项目的软件许可不替代素材许可。再分发时保留署名和许可链接，并记录进一步修改。

Windows、Linux、macOS 的 Godot 导出预设包含 `*.json,*.txt`，将网格数据与署名一起打包。图形项目目录包含独立 `SandBoxSim.Godot.sln`，导出配置允许从官方 NuGet 源恢复自包含运行时包；普通内核构建保持原有包源规则。维护检查使用原生 PCK 导出与资源挂载核对，防止编辑器能读取而发行包缺失素材。原始下载、便携 Blender 和试验产物属于本机维护资料，不是客户端的运行依赖。
