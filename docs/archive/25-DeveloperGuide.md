> 归档资料：保留原始要求与当时实现记录。页内版本、命令、任务流程和配图不代表当前功能；使用与开发请从 [现行文档](../README.md) 阅读。

# 开发架构与扩展指南
版本：2026-10-05。使用说明见 [玩家手册](24-PlayerHandbook.md)，系统玩法见 [世界如何运转](26-WorldGuide.md)。

## 分层
SandBoxSim.Core 保持 BCL 内核，不依赖 Godot。Console 和自带测试运行器支持 SDK / csc 双通道。Godot 只负责表现、输入、会话功能与客户端元数据。

    Godot UI → MainGame 会话 → PlayerTools / LandProjects / WorldTrial → Simulation
    Simulation → LocalConditions / WorldAlerts → 现场面板与报告
    Simulation → WorldView3D / WorldMiniMap → 视觉
    Simulation + 客户端元数据 → 存档

MainGame.AdvanceWorld 按日界拆分 tick：先推进 Simulation，再推进工程，最后推进试炼。高倍速仍逐个处理日界，避免遗漏阶段或预警。直接调用 Simulation.Tick 不会推进会话工程或试炼。

## 文件职责
| 文件 | 职责 |
|---|---|
| Core/Systems/LandProjects.cs | 工程队列、三阶段改地、资格判断、冻结报告与持久化 |
| Core/Systems/ProjectCatalog.cs | 配方校验、动作与阈值、默认数据、存档配方快照 |
| Core/Systems/WorldAlerts.cs | 读取真实风险，返回定位和建议 |
| Core/Systems/WorldTrial.cs | 原始居民队列、危机、预算、稳定窗口和结算 |
| Godot/MainGame.Operations.cs | 工程交互、关注、改名、定位与报告 UI |
| Godot/MainGame.LandCards.cs | 施工与长期管理卡片、政策切换、实时条件与冻结对照 |
| Godot/WorldView3D.Atmosphere.cs | 从天气与日间进度读取光照、太阳角度和雾气 |
| Godot/DiscoveryPanel.cs | 默认现场、风险卡、关注人物与事件 |
| Godot/WorldMiniMap.cs | 实际地形缩略图、标记与坐标转换 |
| Godot/WorldView3D.Hazards.cs | 火情、疾病和工程范围的视觉反馈 |
| Tests/LandProjectTests.cs | 工程、只读观察、保护范围、取消和续跑契约 |
| Tests/ProjectManagementTests.cs | 配方扩展、持续管理、预算、生态代价与读档续跑 |

上述路径以 src/SandBoxSim.* 为根。

## 工程契约
内核半径支持 2–8，界面提供 3、5、8。费用由配方决定，默认为 24 + 半径 × 2。最多同时建设四项、管理四片土地，记录最多十二条；只淘汰既未建设也未管理的旧记录。编号递增，取消也不会复用编号。

Queue 仅捕获安排时的局部状态，不立即改地。默认工程为三个日界，可配置配方为 1–8 阶段。每阶段重新判断地块适用性与动作条件。建筑和受保护地形不会被覆盖。取消冻结结束状态并停止后续阶段。完成报告同样冻结，不随未来世界变化改写。

完成后 Policy=0 为自然演替，1 为维护，2 为资源优先。管理的 LastCareDay、CareDays、SkippedDays 和 LastNotice 均存档。每个日界按队列顺序检查土壤条件，使用现有试炼额度付维护费，再执行动作；之后 WorldTrial 才恢复每日额度。条件不足或额度不足会记录会话暂停状态，但不改变核心地块。关闭管理不会追补遗漏天数。

配方来自 Godot 的 assets/Gameplay/projects.json，内核带相同默认数据供独立运行。ProjectCatalog 校验动作、key、阶段、数值和费用。保存时嵌入 Catalog，读取时优先使用存档配方，新资源文件只影响新世界。修改与追加配方见 [生态工坊扩展指南](28-EcologyWorkshop.md)。

工程调用正式干预入口，因此实际资源变化、空间索引、事件与随机数按内核契约处理。不可用伪造 UI 数字代替物理状态。安排后存读档须保留阶段、起始日、下一个编号和前后报告，测试同时比较后续核心摘要与工程 JSON。

LocalConditions 统计圆形范围内的地表节点资源和当前位置上的居民；不统计居民背包与建筑库存，也不是固定队列。报告需要说明天气、采集和迁移等因素同样影响结果。

## 观察与身份
WorldAlerts 的顺序为火灾、疾病、饥饿、口渴、无住房。该信息提供给玩家观察层；不授予 NPC 全地图信息。风险定位、镜头移动、切页和小地图读取均不能消耗模拟随机数或记录模拟事件。

关注使用稳定人物编号。改名同步 Society.Person.Name 和 AgentStore 的名称覆盖，并记录辅助事件；死亡后的档案仍可以访问。富文本中的名字必须转义，避免用户输入被当作链接或标签。

## 存档
核心采用现有版本化存档和摘要校验。客户端元数据额外保存场景、试炼、工程、关注人物及非递归回溯快照。缺失旧字段应提供默认值，不能让旧档读取失败。

聚落蓝图保存在 client.blueprint：类型、中心、最后观察日、稳定天数与完成状态。SettlementBlueprint.Observe 只读本地真实状态；Advance 在日界积累连续达成，两天才完成，跨越未知日界不补算稳定时间。客户端逐日推进，同时更新工程、试炼和蓝图。命令行加载核心不会执行蓝图追踪。

ConstructionOrders.TryStart 通过附近成年居民和 A* 通行检查后调用原 BuildingSystem 的材料事务；成功工地进入原施工、生产、床位与核心存档路径。图形端在成功事务后扣试炼委托额度，失败不扣额度。蓝图和建筑落点是客户端规划状态；未确认的落点不保存。

BuildingConfig.OrganicHousing 是可保存配置，默认 false 以保留命令行与旧存档的既有选址。新建图形世界开启它，世界设置允许切换。开启后，住房在附近四环候选位置中比较拥挤、道路和坐标/居民差异；找不到位置才继续向外。过程不消耗随机流。NatureModels.Buildings 由位置和种子生成五种住房组合，外观与朝向不进入经济和床位计算。

WorldView3D 的左键观察手势延迟到松开才选择，六像素移动后仅平移；全局松开与窗口失焦解除手势。右键改变 yaw/pitch，滚轮改变距离。键盘方向控制已移除，面板事件不发起地图手势。

CoreDigest 不包含客户端元数据，因此只有摘要相同不足以证明会话续跑相同。工程测试另比较工程编码；Godot 自检验证客户端存读档。Console 读取内核后不会继续执行客户端计划，需要在工具和文档中明确这一边界。

## 渲染和布局
右侧世界手记宽 344 像素，默认收起；布局覆盖 1280、1600、1920 等窗口尺寸。小地图地形约每秒刷新一次，保持真实地图比例。3D 工程圈显示建设与持续管理区域，最多八个，火情粒子有上限。视觉动画与天气光照读取模拟，不写回状态。

矩形地图、中文字体、人物名称、富文本转义、窗口布局和相机都是客户端验收内容。新增热力图、标记或面板时须验证只读摘要不变。

## 扩展工作
新玩法应写清它读取和改变哪些系统、成本和失败条件。所有影响未来的状态必须存档。至少验证真实物理效果、边界保护、取消、失败不扣款和读档后的逐日一致性。最终还需查看实际游戏窗口；无头测试不能替代视觉检查。

原始要求和未完成验收见 [完整交付清单](20-FullDeliveryChecklist.md)。不要因接口存在就标记美术、性能或可玩性要求已完成。
