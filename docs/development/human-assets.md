# 人物解剖素材与转换

近景头脸使用 Blender Studio 的 **Realistic Human Base** 衍生网格，来源为 Julien Kaspar / Blender Studio，许可为 [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)。原始来源与下载见 [Use of Base Meshes](https://studio.blender.org/training/realistic-human-research/use-of-base-meshes/)。它是人工制作的写实基础网格，当前客户端使用自己的肤色、材质和动作表现。

## 当前数据

[human-head.mesh.json](../../src/SandBoxSim.Godot/assets/models/human/human-head.mesh.json) 包含经过一级多重细分处理的头颈、耳部、独立唇部、左右眼球和头发表面查找表。头颈有 17,689 个导出顶点和 34,585 个三角面，嘴唇有 638 个顶点，每只眼球有 2,786 个顶点。颜色边界处允许分裂顶点以保留唇色与口腔色层。

源码校验原始 `.blend` 的 SHA-256，运行时校验导出数据的 SHA-256。生成脚本是 [export_human_head.py](../../tools/export_human_head.py)，运行时读取、变体和头发贴合位于 [NatureModels.HumanAsset.cs](../../src/SandBoxSim.Godot/NatureModels.HumanAsset.cs)。普通构建、运行和评测直接使用仓库中的导出数据，无须 Blender 或在线下载。

转换保留原作者的面部拓扑，提取头颈和耳部；唇部独立用于原有交谈与进食表现，左右眼球保留体积并由眼睑闭合实现眨眼。颈根经过坐标转换与收束以接入游戏衣身。运行时八种结构调整下颌到上脸的宽度与面部纵深，头发按实际头颅表面查找表贴合；年龄沿用原有缩放。运行时在保留原始唇色的基础上对面颊、鼻部增加浅暖色，发束和分缝仍受头皮包围检查约束。当前没有独立女性基础网格或年龄专用雕刻。

## 解剖手部

[human-hand.mesh.json](../../src/SandBoxSim.Godot/assets/models/human/human-hand.mesh.json) 从同一授权人体提取手掌、指缝与五指。完整手部表面有 7,255 个顶点、14,464 个三角面；甲面从实际指背曲面提取，运行时仅上传甲面引用的顶点。

手部使用平滑位移场接入掌指骨，左右手镜像骨骼位置与展开方向；腕口统一成埋入前臂的截面，避免斜切源网格露出尖角。掌根在相邻指骨间平滑分配权重，手指保留近节、中节与末节。每手 16 骨，与原有握持、取用和收纳动作共用。指甲与皮肤使用同一表面分区、共享权重，随手指一起变形，避免叠面闪烁。

近景、远景、指根和拇指骨骼共用 `ResidentRig.HandMirror` 的左右约定。远景张手与握拳网格按左右独立缓存，镜像时同步反转法线、切线手性和三角面朝向；拇指轮廓位于食指一侧并露出掌缘，避免默认视距把两只手显示成同一侧。`--panel=handedness` 可并排查看同一角色的左右掌心、手背以及远景简模。自检覆盖镜像拓扑、拇指方向、骨骼张合、LOD 切换，以及主手和支撑手的实际蒙皮与工具轴间距。

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

人物手部与动物外皮可一起重新生成：

```powershell
./tools/bake-models.ps1
```

脚本默认查找 `.tools/blender-4.5.14/` 下的 Blender 和 `.tools/sources/realistic_human_base.blend`。可以显式传入 `-BlenderPath`、`-HumanSource` 使用已有文件；下载、缓存与临时数据保持在项目内。输出会打印派生模型哈希，修改生成逻辑后需审查并同步运行时代码中的哈希，再运行客户端自检。普通游玩和构建直接使用已提交的网格，不依赖 Blender。

## 发布与署名

许可记录保存在 [LICENSE.txt](../../src/SandBoxSim.Godot/assets/models/human/LICENSE.txt)，包含来源、作者、原文件校验值及修改内容。该模型数据适用 CC BY 4.0，项目的软件许可不替代素材许可。再分发时保留署名和许可链接，并记录进一步修改。

Windows、Linux、macOS 的 Godot 导出预设包含 `*.json,*.txt`，将网格数据与署名一起打包。图形项目目录包含独立 `SandBoxSim.Godot.sln`，导出配置允许从官方 NuGet 源恢复自包含运行时包；普通内核构建保持原有包源规则。维护检查使用原生 PCK 导出与资源挂载核对，防止编辑器能读取而发行包缺失素材。原始下载、便携 Blender 和试验产物属于本机维护资料，不是客户端的运行依赖。
