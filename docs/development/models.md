# 3D 模型系统

地图、人物档案和建筑图册使用同一套实际网格。模型由 C# 构造，材料图集与程序化表面结合；展示页不会生成模拟实体，世界存档仍保存居民、建筑与资源状态。

## 建筑

住房有原木屋、灰泥屋、石屋、高山墙屋和单坡屋五种轮廓。稳定身份决定宽深、楼高和变体；道路决定地图中的朝向。当前门洞、墙高和图册镜头按人物尺度重新调整，模拟建筑占地与通行规则沿用内核数据。

门由独立木板、门框、过梁、铁带和门闩组成；窗有外框、格栅、侧板和窗台。坡屋面有分层瓦片、檐木和山墙边条，单坡屋有坡顶墙面。石屋与基础使用带细倒角的砌石，侧墙有木架；部分住房有烟囱、檐柱、木料与桶箍。

仓库保留宽门及门前物料；农田采用植株、叶片与通透围栏；矿场具有暗洞口、支架、轨道与矿车。未完工建筑使用基础、立柱、斜撑和材料堆。

![实际建筑模型](../images/architecture-detail.png)

## 人物与动物

人物头部具有连续面部曲面，衣装具有折线、领襟、袖口、缝线和腰带。手部包含掌形、手指与拇指；裤腿、靴筒及鞋底使用连续轮廓。职业配件、年龄比例、衣色和肤色继续由稳定身份和职业决定。肩肘、髋膝动作由表现层关节驱动。

鹿和狼使用独立的胸腹、肩背、颈部和头部截面，包含眼、口鼻、内耳、蹄或脚掌以及尾。鹿保留分枝角；狼采用尖耳和较低肩背。四条腿具有独立关节，移动时形成对角步态，停止时收敛到站姿；暂停停止关节变化。毛发表面按实际屏幕采样范围减弱细纹，避免远景噪点。

![人物与动物模型](../images/character-detail.png)

## 配件与衣装贴合

草帽由双层帽冠、开孔帽檐、檐边、环绕缝带和编织表面构成，帽冠包住额头和头顶；头盔采用开放底部的壳体，并在耳后与颈侧延伸。它们挂在 `Headwear` 节点下，随头部的年龄比例、朝向与呼吸姿态运动。

围裙、护胸、腰带和斜背带从衣服的实际三角面采样，不再用浮在身体前面的矩形或直线替代。曲面层与衣服保留 4–6 毫米的表现间距。行囊通过 `PackMount` 贴合后背，采用圆角皮革轮廓、盖片与扣带。

人物具有独立手腕、四指的两段指节、拇指和脚踝。工具通过 `HandGrip` 横穿合拢的手指，手部目标由肩肘两节逆运动学求解；俯身围绕髋部旋转，脚踝补偿小腿倾斜。工具在腰侧 `BeltToolLoop` 与手掌之间切换父节点，只在手已接触收纳位置时转移；取出和放回期间不会单独漂移。

工具与动作由真实的 `ActionKind` 和 `ActionPhase` 驱动，前往作业地点时可取出工具，到达后才开始作业；停止作业后收回。工具是表现层几何，不新增工具库存或改变作业收益。

| 真实动作 | 外观与动作 |
|---|---|
| 采伐木材 | 斧头、双手支撑与挥击 |
| 采集石料 / 铁矿 | 弯曲镐头、双手握持、蓄力与快速下击 |
| 耕种 | 长柄锄头、双手推拉 |
| 建造住房 / 农田 / 仓库 / 矿场 | 锤头、取出、抬臂、锤击与收回 |
| 采集食物 / 取回物资 | 俯身、屈膝、伸手与手指收拢 |
| 存放 / 分享 / 交易 | 双手递送 |
| 进食 / 饮水 | 手移向口部 |

木料束与物资袋根据居民的实际非零库存显示，代表主要携带资源，不按袋子数量计算库存。儿童不使用成人工具。近景独立指节参与握持，远景采用合并的手部和头部网格，近景与肖像保持完整曲面和指节；距离较远的人物降低姿态更新频率，根节点移动仍连续。暂停冻结表现层动作，不推进模拟或随机流。

![取出、握持、使用与收回的实际运行演示](../images/resident-tool-motion.gif)

![帽子、工具与背包的实际模型](../images/resident-equipment.png)

## 植被与地貌

阔叶树冠由带折面的叶簇构成，针叶树使用错层枝叶；树干有渐细与轻微弯曲。地图中的树干、枝条、冠层和岩石仍批量绘制。古树林、野果地和遗迹复用同类叶片、石材与木材；岩石采用较低频轮廓变化，减少尖锐褶皱。表面保留低对比纹理、粗糙度与材质差异。

![地表与植被模型](../images/nature-detail.png)

## 源码入口

| 文件 | 作用 |
|---|---|
| `NatureModels.Buildings.cs` | 建筑轮廓、屋顶与窗结构 |
| `NatureModels.ArchitectureDetail.cs` | 门、屋面搭接、砌石、支架与道具 |
| `NatureModels.Residents.cs`、`NatureModels.Tailoring.cs` | 人物衣装、四肢、手部与配件 |
| `NatureModels.Equipment.cs` | 帽壳、贴合衣装与背包挂点 |
| `NatureModels.HandTools.cs`、`ResidentRig.cs` | 工具、腕部和指节、逆运动学、行为动作及收纳切换 |
| `NatureModels.ResidentLod.cs` | 保留轮廓与肤发色的远景头部网格 |
| `NatureModels.HandlingValidation.cs` | 工具转移连续性、握持、作业、切换、携带与暂停检查 |
| `NatureModels.Sculpture.cs` | 连续头部曲面与布料表面 |
| `NatureModels.Animals.cs`、`AnimalRig.cs` | 动物解剖轮廓、材质与四足步态 |
| `NatureModels.Vegetation.cs` | 树冠、针叶枝层与树干 |
| `NatureModels.Craft.cs` | 平滑截面、倒角石块与连接构件 |
| `NatureModels.Surface.cs` | 灰泥、石材、屋面和植被的表面处理 |
| `NatureModels.Validation.cs` | 曲面朝向、网格完整性、静态合并与关节检查 |

新网格需要采用 Godot 的顺时针正面约定，并保持朝外法线。截面插值保留轮廓；不要仅提高球体细分来替代身体结构。静态部件按材质合并，动物和居民保留活动关节。建筑变体键限制到 140 个稳定组合，图册和地图共用缓存规则。

## 独立预览

先运行 `./tools/godot.ps1 -Mode build -Configuration Debug`，再通过 Godot .NET 可执行文件打开项目：

```powershell
& $env:GODOT_EXE --path src/SandBoxSim.Godot -- --panel=architecture
& $env:GODOT_EXE --path src/SandBoxSim.Godot -- --panel=characters
& $env:GODOT_EXE --path src/SandBoxSim.Godot -- --panel=equipment
& $env:GODOT_EXE --path src/SandBoxSim.Godot -- --panel=actions
& $env:GODOT_EXE --path src/SandBoxSim.Godot -- --panel=motion
& $env:GODOT_EXE --path src/SandBoxSim.Godot -- --panel=naturemodels
```

这些参数仅打开开发用模型预览，使用固定灯光与同一套实际网格；普通游戏不会显示预览面板。原始素材来源见 [美术资源](assets.md)，界面操作见 [观察与界面](../guides/gameplay-observation.md)。

人物头部使用圆润下颌、连续面部截面、鼻梁和鼻尖、眼窝与颈部连接，修正五官悬浮和过大头部的比例。面部近景可使用 `--panel=faces`，独立预览不构成游戏目标或建筑委托入口。

![人物面部近景](../images/resident-face-detail.png)
