# 人物头脸素材与转换

近景头脸使用 Blender Studio 的 **Realistic Human Base** 衍生网格，来源为 Julien Kaspar / Blender Studio，许可为 [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)。原始来源与下载见 [Use of Base Meshes](https://studio.blender.org/training/realistic-human-research/use-of-base-meshes/)。它是人工制作的写实基础网格，当前客户端使用自己的肤色、材质和动作表现。

## 当前数据

[human-head.mesh.json](../../src/SandBoxSim.Godot/assets/models/human/human-head.mesh.json) 包含经过一级多重细分处理的头颈、耳部、独立唇部、左右眼球和头发表面查找表。头颈有 17,689 个导出顶点和 34,585 个三角面，嘴唇有 638 个顶点，每只眼球有 2,786 个顶点。颜色边界处允许分裂顶点以保留唇色与口腔色层。

源码校验原始 `.blend` 的 SHA-256，运行时校验导出数据的 SHA-256。生成脚本是 [export_human_head.py](../../tools/export_human_head.py)，运行时读取、变体和头发贴合位于 [NatureModels.HumanAsset.cs](../../src/SandBoxSim.Godot/NatureModels.HumanAsset.cs)。普通构建、运行和评测直接使用仓库中的导出数据，无须 Blender 或在线下载。

转换保留原作者的面部拓扑，提取头颈和耳部；唇部独立用于原有交谈与进食表现，左右眼球保留体积并由眼睑闭合实现眨眼。颈根经过坐标转换与收束以接入游戏衣身。运行时八种结构调整下颌到上脸的宽度与面部纵深，头发按实际头颅表面查找表贴合；年龄沿用原有缩放。当前没有独立女性基础网格或年龄专用雕刻。

## 重新导出

当前数据由 Blender 4.5.14 LTS 转换。将原始 `.blend` 保存到本机维护目录，SHA-256 必须为：

```text
1d9d2a6070acd6a3d0d9b676af816001310fb0f6128d07a3ab3e0318c7fdd981
```

从仓库根目录执行，路径按本机安装位置填写：

```powershell
$headBlender = 'C:/path/to/blender.exe'
$headSource = 'C:/path/to/realistic_human_base.blend'
& $headBlender --background --disable-autoexec $headSource --python tools/export_human_head.py -- --output src/SandBoxSim.Godot/assets/models/human
```

脚本使用明确的对象名和雕刻区域，评估多重细分、拆分头脸与眼唇、重定位颈部、转换坐标并生成头发表面查找表。原文件中的脚本保持关闭。导出后计算数据 SHA-256，更新运行时代码的 `HumanAssetHash`，运行 `./tools/godot.ps1 -Mode test` 并查看 `visual-faces`、`visual-wardrobe` 和动作场景。

## 发布与署名

许可记录保存在 [LICENSE.txt](../../src/SandBoxSim.Godot/assets/models/human/LICENSE.txt)，包含来源、作者、原文件校验值及修改内容。该模型数据适用 CC BY 4.0，项目的软件许可不替代素材许可。再分发时保留署名和许可链接，并记录进一步修改。

Windows、Linux、macOS 的 Godot 导出预设包含 `*.json,*.txt`，将网格数据与署名一起打包。图形项目目录包含独立 `SandBoxSim.Godot.sln`，导出配置允许从官方 NuGet 源恢复自包含运行时包；普通内核构建保持原有包源规则。维护检查使用原生 PCK 导出与资源挂载核对，防止编辑器能读取而发行包缺失素材。原始下载、便携 Blender 和试验产物属于本机维护资料，不是客户端的运行依赖。
