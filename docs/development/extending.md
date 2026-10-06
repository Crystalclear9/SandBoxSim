# 功能与内容扩展

扩展前先确定修改的是数据、模拟规则还是表现。现有玩法的可配置数据和正式状态入口优先复用，避免 UI 自行制造资源、人口或完成进度。

## 添加生态工程

只组合现有动作时，编辑 [projects.json](../../src/SandBoxSim.Godot/assets/gameplay/projects.json)，在数组末尾追加配方。保持唯一 key，提供建设阶段、care、harvest、费用与插画编号。字段和完整示例见 [生态工坊](../guides/ecology.md)。

新世界加载外部配方；旧档保留嵌入的配方快照。无效数据会发出警告并回退到默认配方，因此开发时要查看日志，确认新工程实际出现在图册中。

新增动作语义需要同时修改 `Core/Systems/ProjectCatalog.cs` 的解析校验与 `LandProjects.cs` 的动作处理。定义适用地形、燃烧与建筑保护、资源容量、预算和日界行为，补上拒绝条件及存读档续跑检查。不要只增加名称而没有执行逻辑。

## 调整建筑或新增类型

已有造价、体量与材料逻辑由 `Core/World/BuildingStore.cs` 中的 `BuildingKind`、配方和 `BuildingRegistry`，以及 `Core/Systems/BuildingSystem.cs` 管理。玩家落点通过 `ConstructionOrders` 的居民、路径和材料事务校验。

新增类型按以下顺序接入：

1. 定义类型和配方，以及生产、床位、施工和维护行为。
2. 明确地形与可达性要求，失败时不产生工地或重复扣料。
3. 检查存档中的类型编码；保留既有枚举数值，格式变化按照 [存档契约](saving.md) 处理。
4. 为 `NatureModels.Buildings.cs` 接入模型，使地图、施工场景和 `BuildingPortrait` 图册均有正确表现。
5. 补充行为、材料、完工效果和恢复检查，再更新 [聚落营造](../guides/settlements.md)。

只改屋顶、门窗或材质不需要改经济规则。外观使用稳定身份或位置计算变体，不消耗模拟随机流。`BuildingPortrait` 展示代表性样式，实际房屋仍依据自身身份选择变体。

## 修改居民行为

`Core/Systems/AiSystem.cs` 评估选择，`ActionSystem.cs` 执行动作，`NeedsSystem.cs` 推进需求；评分解释位于 `Core/Ai/UtilityBreakdown.cs`。增加偏好之前先定义行动是否可执行、目标是否可到达、材料是否存在，以及失败后的重试条件。

新增长期状态需贯穿实体存储、保存、恢复和确定性摘要。用相同种子与规则比较变化，包含缺粮、缺水、路径受阻、目标失效和存读档继续运行的情形。检查器读取实际状态，避免为了展示新行为而改变决策结果。

## 添加 HUD 操作与观察

主界面入口在 `Godot/MainGame.Interface.cs`，建造与工程操作分别在 `MainGame.Construction.cs`、`MainGame.Operations.cs`。使用 `HudStyle` 的文字、按钮和面板样式，概览与操作符号由 `HudSymbols` 管理，干预工具由 `ToolGlyphs` 管理。

只读信息直接读取真实状态；改变世界的按钮调用正式干预或会话入口。增加资源指标时说明统计口径，区分地表节点、背包、物资堆和仓库。打开面板的动画、肖像旋转和展示模型保持只读。

实际检查至少包含 1280×800、1600×1000 与 1920×1080 的布局，注意鼠标输入是否被面板截获。左键绘制、左键观察平移、右键旋转、滚轮缩放和松开解除手势都需保留。图形世界执行日界计划时使用 `MainGame.AdvanceWorld`。

## 添加模型与素材

建筑使用 `NatureModels.Buildings.cs`，居民使用 `NatureModels.Residents.cs`、`NatureModels.Sculpture.cs` 和 `ResidentRig.cs`。材质与网格尽量复用缓存；活动关节保留独立节点。独立建筑或人物展示使用自己的 SubViewport，不把展示节点加入模拟。

新增纹理放进 `assets/textures/`，插画放进 `assets/illustrations/`；维护 `.import` 和素材来源。更改图集行列、尺寸或编号时，同时更新采样与图册索引。检查近景、远景、旋转视角和暂停状态，避免纹理闪烁或动作继续推进世界。

## 扩展完成后

运行与改动对应的行为与恢复检查；模拟规则变化再执行完整回归，图形变化再做实机检查。将新操作写进玩家指南，将规则、数据或格式写进相应开发文档，并更新 [CHANGELOG](../../CHANGELOG.md)。具体命令见 [开发指南](developer-guide.md)。

本页中的 `Core/`、`Godot/` 分别缩写 `src/SandBoxSim.Core/`、`src/SandBoxSim.Godot/`。

## 添加荒野地貌

`Core/Systems/WildPlaces.cs` 定义种子位置、初始化、中心条件、逐日作用、地点描述与保存。`Godot/NatureModels.WildPlaces.cs` 提供独立外观，`WorldView3D.BuildNature` 根据真实状态放置模型。图形会话在日界调用 `Wild.Advance`，旧档缺失 wild 时保持原地图。

新增类型需明确非再生物资、水土保护和被玩家改造后的行为，并同步枚举、解码范围、初始化、模型和说明。涉及有机资源额外生长时使用 `ResourceSystem.ReplenishLivingNode`，计入再生总量；矿产不能走这一入口。检查改造停止作用、容量、跨日重复执行、只读查询与存档续跑。不要把新地点接成收集进度或完成奖励。
