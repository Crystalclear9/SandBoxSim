# 15 · 配置参数总表

> **这一份文档的存在理由**：`config/sim.default.json` 里的每个数字都是**一个设计决定**。
> 改了它，涌现行为就会变 —— 而"为什么是这个值"如果只存在于某个人的脑子里，
> 下一个人就只能在黑暗里试参数。
>
> 因此本文按"改这个值会发生什么"来组织，而不是按字段字母序。

---

## 0. 配置文件如何被加载与校验

| 事项 | 行为 |
|---|---|
| 默认路径 | `config/sim.default.json`（可用 `--config <path>` 覆盖） |
| 与代码默认值的关系 | `SimConfig` 的字段默认值**必须**与这份 JSON 完全一致，由 `ConfigTests.DefaultsMatchJson` 强制 |
| 未知键 | 加载时产生**提示**（`ConfigLoadResult.Warnings`），不静默忽略 |
| 缺失键 | 用代码默认值补齐，并产生提示 |
| 类型错误 | 产生提示并保留默认值（不让一个手滑的字符串毁掉整次长跑） |
| 存档里的配置 | 存档**自带**一份生效配置（`config` 段），用于回答"这个存档是在什么规则下产生的" |

> **为什么"默认值必须与 JSON 一致"值得一条测试**：两边不一致时，
> "不传 `--config` 跑出来的世界"与"传了 `--config` 跑出来的世界"会是两个世界，
> 而确定性验收只会测其中一条路径 —— 于是另一条路径上的偏差永远没人发现。

---

## 1. `clock` — 时间

| 键 | 默认 | 含义与影响 |
|---|---|---|
| `minutesPerTick` | 1 | 1 tick = 1 游戏分钟。**改动它会同时改变所有"每天"速率**，是全局时间尺度 |
| `ticksPerHour` | 60 | 每小时 tick 数 |
| `hoursPerDay` | 24 | 每天小时数 ⇒ `TicksPerDay = 1440` |
| `ticksPerSecondAt1x` | 10 | 1 倍速下每秒推进多少 tick（**只影响观感**） |
| `speedMultipliers` | `[0,1,2,4,8]` | 速度档位。`0` = 暂停。**倍速只改每秒 tick 数，绝不改 tick 语义**（第 82 条） |
| `targetFramesPerSecond` | 60 | 渲染目标帧率。与模拟解耦：掉帧不影响世界 |
| `maxCatchUpTicksPerFrame` | 40 | 单帧最多补多少 tick，防止卡顿后"追帧雪崩" |

---

## 2. `world` — 世界尺寸与气候基线

| 键 | 默认 | 含义与影响 |
|---|---|---|
| `width` / `height` | 100 / 100 | 地图尺寸。**改变它会改变一切**：资源总量、迁徙距离、chunk 数量 |
| `chunkSize` | 16 | 空间索引分块边长。影响 `ChunkGrid` 聚合精度与 AI 选靶粒度 |
| `ambientTemperature` | 0.55 | 全局温度基线（0..1 归一化），影响蒸发与火险 |
| `ambientMoisture` | 0.5 | 全局湿度基线，影响植被与火险 |

---

## 3. `worldgen` — 地形生成

| 键 | 默认 | 含义与影响 |
|---|---|---|
| `seed` | 839102 | **决定一切**。同 seed + 同配置 + 同指令 ⇒ 逐像素一致 |
| `continentFrequency` | 0.018 | 大陆噪声频率。越小大陆越大越整块 |
| `continentOctaves` | 4 | 大陆 fBm 层数。越多细节越丰富（也越慢） |
| `continentPersistence` | 0.5 | 每层振幅衰减 |
| `moistureFrequency` / `moistureOctaves` / `moisturePersistence` | 0.035 / 3 / 0.55 | 湿度场噪声 |
| `detailFrequency` | 0.12 | 细节噪声频率，用于破碎海岸线 |
| `waterLevel` | 0.28 | **水域目标占比**（不是阈值）。生成按分位数取阈值 ⇒ 小地图也一定有水 |
| `mountainLevel` | 0.16 | **山地目标占比**，同上 |
| `waterShareJitter` | 0.10 | 不同 seed 之间海陆比例的抖动幅度 |
| `mountainShareJitter` | 0.07 | 同上，山地 |
| `sandBand` | 0.035 | 海岸沙带宽度（按高度差） |
| `forestMoistureThreshold` | 0.52 | 湿度超过它才可能长森林 |
| `forestDensityBonus` | 0.18 | 森林概率的额外加成 |
| `fertilityFromHeight` | true | 肥沃度是否沿海拔衰减 |
| `fertilityBase` | 0.55 | 肥沃度基线 |
| `fertilityNoiseFrequency` | 0.06 | 肥沃度噪声频率 ⇒ 地力呈"成片"分布 |

> **`waterLevel` / `mountainLevel` 是目标占比而不是阈值**，这是刻意的：
> 用固定阈值时，不同 seed 的小地图可能完全没有水，玩家会以为是 bug。

---

## 4. `resources` — 资源与再生

模型：`R_{t+1} = clamp(R_t + Regen − Harvest, 0, K)`，再生用**离散 Logistic**
（存量越低再生越慢 ⇒ 采秃了不会自动恢复）。详见 [06-ResourceModel](06-ResourceModel.md)。

| 键 | 默认 | 含义与影响 |
|---|---|---|
| `woodCapacityPerForestTile` | 100 | 森林格木材容量 K |
| `woodInitialFraction` | 0.85 | 初始存量占 K 的比例 |
| `woodGrowthRate` | 0.02 | 每（小时/天，见 `regenerateHourly`）的 Logistic 增长率 |
| `woodHarvestPerAction` | 6.0 | 一次采集动作取走多少 |
| `stoneCapacityPerMountainTile` | 80 | 山地石料容量 |
| `stoneInitialFraction` / `stoneHarvestPerAction` | 0.9 / 4.0 | 同上 |
| `ironCapacityPerMountainTile` | 30 | 山地铁矿容量（M3 起建造需要） |
| `ironInitialFraction` / `ironHarvestPerAction` | 0.6 / 2.0 | 同上 |
| `foodCapacityPerGrassTile` | 60 | 草/林地食物容量 |
| `foodInitialFraction` | 0.6 | 初始食物比例 |
| `foodGrowthRate` | 0.03 | 食物再生率（比木材快，是刻意的：食物是生存瓶颈） |
| `foodHarvestPerAction` | 24.0 | 一次采集的食物量 |
| `regenerateHourly` | true | `true` = 每小时再生一次（而非每 tick），大幅省算力 |
| `depletionWarnFraction` | 0.2 | 低于此比例触发枯竭告警（事件 + 统计） |

---

## 5. `needs` — 需求与生命历程

速率单位是**"每天增加多少"**（0..1 归一化）。`1.0` = 不吃不喝一整天才会从满到极端。

| 键 | 默认 | 含义与影响 |
|---|---|---|
| `hungerPerDay` | 1.0 | 饥饿增速 |
| `fatiguePerDay` | 1.15 | 疲劳增速 |
| `thirstPerDay` | 1.5 | 干渴增速（**最快** ⇒ 水是硬约束，这是刻意的） |
| `socialPerDay` | 0.5 | 社交需求增速 |
| `sleepRecoveryPerDay` | 1.45 | 睡眠恢复疲劳的速率 |
| `starvationDamageThreshold` | 0.85 | 饥饿超过它开始掉血 |
| `starvationDamagePerDay` | 0.25 | 掉血速率 |
| `dehydrationDamageThreshold` | 0.9 | 干渴致伤阈值 |
| `dehydrationDamagePerDay` | 0.45 | 脱水掉血速率（比饿死快 1.8 倍） |
| `healthRecoveryPerDay` | 0.1 | 健康自然恢复 |
| `adulthoodDays` | 16 | 成年年龄 |
| `elderDays` | 55 | 进入老年的年龄 |
| `elderMortalityPerDay` | 0.012 | 老年每日自然死亡概率 |
| `maxLifespanDays` | 90 | 硬寿命上限 |

> **想快速验证饥荒场景**：把 `hungerPerDay` 调到 `5` 以上即可，
> 不必等真实时间尺度下的几十天。

---

## 6. `ai` — Utility AI 权重

**这是全项目最需要实验的部分**：改这些值就是在改"什么行为在什么时候更值得做"。
曲线与合成公式见 [05-UtilityAI](05-UtilityAI.md)。

| 键 | 默认 | 含义与影响 |
|---|---|---|
| `decisionIntervalTicks` | 600 | 同一个体两次决策的最小间隔（**分批 + 间隔**是性能与自然度的双重来源） |
| `batchCount` | 0 | 决策分批数。`0` = 自动（按 `targetDecisionsPerTick` 与人口算出） |
| `targetDecisionsPerTick` | 12 | 每 tick 目标决策数，用于自动分批 |
| `searchRadius` | 12 | 找目标（食物/木材/水）的搜索半径 |
| `wanderRadius` | 6 | 闲逛半径 |
| `exploreMinDistance` / `exploreMaxDistance` | 15 / 40 | 探索距离区间 |
| `topScoresToKeep` | 6 | 保存 Top-N 候选用于**可解释性**（检查器显示的就是这 6 条） |
| `moveSpeedPerTick` | 0.35 | 每 tick 前进多少格。与 `MoveProgress` 配合 ⇒ 小数步 |
| `wanderWeight` / `exploreWeight` | 0.18 / 0.22 | 基础行为权重 |
| `eatHungerWeight` / `eatFoodAvailableWeight` | 2.0 / 1.2 | 进食 |
| `drinkThirstWeight` | 2.2 | 喝水（略高于进食：脱水更快） |
| `sleepFatigueWeight` / `sleepNightBonus` | 2.0 / 0.6 | 睡眠，夜间加权 |
| `gatherFoodHungerWeight` | **2.5** | **必须显著大于木材/石料的可得性权重** |
| `gatherFoodAvailabilityWeight` | 1.0 | 食物可得性 |
| `gatherWoodNeedWeight` | 1.4 | 木材需求 |
| `gatherOverstockWeight` | 2.2 | 超量囤积惩罚 |
| `gatherForBuildWeight` | 2.6 | 为建造而采集（**把木材需求接到建造意图上**） |
| `inventoryComfort` | 45.0 | 随身超过此值开始算"囤太多" |
| `gatherWoodAvailabilityWeight` / `gatherStoneAvailabilityWeight` | 1.2 / 1.0 | 可得性 |
| `industriousnessWorkBonus` | 0.55 | 勤奋性格的工作加成 |
| `socializeWeight` | 0.5 | 社交 |
| `homeAttachmentWeight` / `homeAttachmentRadius` | 0.35 / 25.0 | 离家惩罚 |
| `blockedUtilityMultiplier` | 0.15 | **"被门挡住"的惩罚系数**：乘性打折而非归零 ⇒ 没有更好选择时仍会做 |
| `actionPatienceTicks` | 900 | 一个动作最多坚持多久（超时判失败并重决策） |
| `huntHungerWeight` / `huntAvailabilityWeight` | 1.9 / 1.1 | 狩猎 |
| `depositSurplusWeight` / `depositPileBonusWeight` | 1.2 / 0.8 | 存入物资堆 |
| `takeNeedWeight` / `takeAvailabilityWeight` | 0.7 / 0.9 | 从堆里取用 |

> **踩过的坑（留在注释与本节里）**：
> `gatherFoodHungerWeight` 必须显著大于木材/石料的可得性权重，
> 否则"没木没石"的人会一直砍树，**快饿死也不去采食物**
> （实测出现过 40 人全部饿死、而食物采集量为 0）。

---

## 7. `wildlife` — 野生动物与生态链

链条：`植被 → 动物 → 猎人产出`。砍伐森林会减少栖息地，从而减少猎物 ——
这是"砍树的长期代价"的来源。

| 键 | 默认 | 含义与影响 |
|---|---|---|
| `moveSpeedPerTick` | 0.5 | 动物移动速度（比人快，才追得上/逃得掉） |
| `foodPerKill` | 30.0 | 一次猎杀产出多少食物 |
| `reproductionChancePerDay` | 0.30 | 每日繁殖概率 |
| `naturalDeathChancePerDay` | 0.02 | 每日自然死亡概率 |
| `capacityPerVegetationTile` | 0.05 | 环境容纳量：**每单位植被能养多少动物** |
| `initialPopulationCap` | 120 | 开局投放上限 |
| `fleeRadius` | 6 | 察觉到人后的逃跑半径 |
| `huntRange` | 1 | 猎杀判定距离 |
| `huntTicks` | 12 | 一次猎杀需要持续多少 tick |
| `animalSenseRadius` | 8 | 动物感知半径（用于躲避） |

> **调参踩坑**：这三个值必须一起看。曾经因为
> `reproductionChancePerDay` 太低 + `capacityPerVegetationTile` 太低 +
> `foodPerKill` 太高，导致野生动物在第 32 天**被猎到灭绝**。
> 现在 `0.30 / 0.05 / 30` 是让种群能稳定在容纳量附近的一组值。

---

## 8. `migration` — 迁徙

M2 的简化版：只做"个体离开原住地"，**不做"建立新聚落"**（那是 M7）。

| 键 | 默认 | 含义与影响 |
|---|---|---|
| `threshold` | 0.62 | 迁移效用超过它才会真的走 |
| `scarcityGate` | 0.55 | **门（gate）**：本地资源不紧张到一定程度，完全不考虑搬家 |
| `scarcityWeight` / `hungerWeight` | 1.0 / 0.8 | 推力 |
| `housingWeight` / `dangerWeight` | 0.4 / 0.6 | 住房与危险（危险权重 > 住房：安全优先） |
| `populationPressureWeight` | 0.5 | 拥挤推力 |
| `nearbyOpportunityWeight` | 0.9 | 拉力（目的地机会） |
| `homeAttachmentWeight` | 1.1 | **最大的单项权重** ⇒ 搬家是件大事，不是随手决定 |
| `minDistance` / `maxDistance` | 25 / 60 | 迁移距离区间（太近没意义、太远走不到） |
| `homeRegionRadius` | 18 | "这一带算不算我家"的半径 |
| `cooldownDays` | 8.0 | 搬家后的冷却。**它进存档**（漏掉会让读档后全体立刻重新评估搬家） |
| `probeDirections` / `probeDistance` | 8 / 45 | 沿八个方向探出去比较资源密度 ⇒ 机会不是随机的 |

---

## 9. `groundStocks` — 地面物资堆

让"攒物资"在空间上可见，是 M3 共享仓库的前身。

| 键 | 默认 | 含义与影响 |
|---|---|---|
| `capacityPerKind` | 400.0 | 每堆每种资源的容量上限 |
| `surplusThreshold` | 28.0 | 随身超过它才算"富余、值得放下" |
| `dropAmount` | 10.0 | 每次放下多少 |
| `takeNeedThreshold` | 6.0 | 随身低于它才需要取用 |
| `takeAmount` | 15.0 | 每次取用多少 |
| `searchRadius` | 18 | 找堆的搜索半径 |

> **⚠️ 这个值必须显著高于 `ai.inventoryComfort`** —— 两个参数分处两个配置段，
> 但它们是**耦合**的。M4 联调时踩过一次：
> M3 把 `inventoryComfort` 提到 45，越过了当时 `surplusThreshold = 28`，
> 于是 `surplus = clamp01((45 − 28) / 14) = 1.0` **恒为满值**，
> "存放"的效用常年压在 1.2 以上，把进食/饮水/睡眠全部挤掉 ——
> 实测 **75% 的决策都在搬东西**（105,106 / 139,849），
> 人不是没水喝，是**一直在搬东西，没空去喝**，死因以脱水为主，人口 40 → 0。
>
> 现在取 70（高于 45），并由 `M4Tests.DepositThresholdMustExceedInventoryComfort` 锁住。
> **教训：跨配置段的参数耦合是静默失效的高发区** —— 改一个段里的值时，
> 必须回头确认有没有别的段依赖它。

> **调参踩坑**：`surplusThreshold` 与 `takeNeedThreshold` 之间必须留出**明显间距**。
> 一开始两者太接近，导致个体在"放下"和"取走"之间**高频振荡**
> （实测 201,484 次与 229,855 次决策都是 Deposit）。

---

## 10. `buildings` — 建造

造价与产能写在 `BuildingRegistry.cs` 里（**配方数据**）；
这里只放**行为参数**。

| 键 | 默认 | 含义与影响 |
|---|---|---|
| `workPerFastTick` | 8 | 每个 fast tick 贡献多少施工进度 |
| `siteSearchRadius` | 14 | 找建造点位的搜索半径 |
| `storageSearchRadius` | 24 | 找仓库的搜索半径（比建造点更大：仓库是公共设施） |
| `buildMaterialWeight` | 1.1 | 材料齐备程度对建造意愿的影响 |
| `buildGapWeight` | 1.6 | **缺口的紧迫度**（床位不够时更想盖房） |
| `buildSiteWeight` | 0.7 | 已有工地时继续施工的倾向 |

> **"材料齐备"必须是门而不是加成**：M3 联调时它一度是 `isBonus: true`，
> 结果是"材料不够也去盖"，60 天 0 建筑而人口从 40 掉到 1。
> 改成 `UtilityCurve.Threshold(0.98)` 的门之后才正常。

### 10.1 M4 新增：建筑衰减与农业

| 键 | 默认 | 含义与影响 |
|---|---|---|
| `decayPerDay` | 0.012 | 无人使用的建筑每日掉多少完整度（占 1.0）。**必须大于 0** |
| `decayGraceDays` | 3.0 | 刚建成 / 刚腾空的宽限期 —— 否则"刚盖好就开始烂"，观感很怪 |
| `demolishWhenDecayed` | true | 完整度归零时是否拆除 |
| `farmBaseYieldPerDay` | 16.0 | 农田基础日产量（还要乘地力 / 湿度 / 天气 / 劳动力） |
| `farmLaborBonusMax` | 0.6 | 有人耕种的产量加成上限（即最高 1.6 倍） |
| `farmUnattendedFactor` | 0.25 | **没人种的产量系数** —— 它让"有田"与"有人种田"成为两件事 |
| `farmWorkPerAction` | 1.0 | 一次耕种动作贡献的劳动量 |
| `farmLaborPerDayCap` | 3.0 | 每块田每天最多累积多少劳动量（防止一群人挤在一块田上刷产量） |
| `farmBadWeatherFactor` | 0.45 | 干旱 / 暴雨时的产量系数 |

> **建筑衰减是给"建造"补的负反馈。** 没有它，建筑只会单调增加，
> M3 的饱和点只是掩盖了"没有维护成本"这件事。

---

## 11. `birth` — 出生与人口（M4）

**这是让世界从"必然灭绝"变成"长期稳定"的那一组参数。**
模型：`P = Base × 食物 × 住房(硬门) × 健康 × (1 − 人口压力)`。

四条因子各管**一件不同的事**，缺任何一条都会退化成一种无聊的世界：

| 因子 | 管什么 | 缺了会怎样 |
|---|---|---|
| `食物` | 饿着的时候不该生孩子 | 饥荒里人口照样涨 ⇒ 食物机制形同虚设 |
| `住房`（**门**） | 没床位就生不了 | 人口只受食物限制 ⇒ 建造没有意义 |
| `健康` | 虚弱 / 生病的人不该生孩子 | 生育变成纯抽奖，健康不参与 |
| `(1 − 人口压力)` | 增长会自己慢下来 | 指数爆炸 ⇒ 世界几十天就崩 |

| 键 | 默认 | 含义与影响 |
|---|---|---|
| `baseChancePerDay` | 0.20 | 每对合格伴侣的每日基础出生概率 |
| `maxChancePerDay` | 0.30 | 概率硬上限（防爆的最后一道闸） |
| `pairRadius` | 8.0 | "同住"近似半径（格）。M6 会换成真实关系系统 |
| `minDaysBetweenBirths` | 6.0 | 同一对伴侣两次生育的最小间隔 —— 否则会一天生一个 |
| `healthCostPerBirth` | 0.08 | 生育对母体健康的消耗（让连续生育有代价） |
| `foodPerCapitaTarget` | 30.0 | 食物因子的分母：人均可支配食物达到它时因子记 1 |
| `foodFactorFloor` | 0.15 | 食物因子下限（避免"存量略低就完全不生"的悬崖） |
| `popPressureScale` | 0.50 | 人口压力强度：`1 − clamp01(人口 / 床位) × 本值` |
| `mutationScale` | 0.08 | 性格遗传的突变幅度（**对称**突变，否则几代后性格整体漂移） |

> **住房是门（gate），不是加成。** 它要么 1 要么 0 ——
> 验收判据"床位为 0 时出生数为 0"只有在它是乘性零因子时才成立。
> 由 `M4Tests.NoBedsMeansNoBirths` 以**逐日不变式**锁定
> （"只要此刻床位为 0，这一天就不能有新生儿"）。

> **健康是因子，不是资格。** 踩过一次严重的设计错误：
> 一度把 `Health > 0.35` 放进"配对资格"里，于是某人一旦暂时虚弱，
> 伴侣关系就被清理规则**拆掉**，之后还要重新配对 ——
> 实测 40 人世界配对数 5 天内从 16 对崩到 3 对，出生数恒为 0，
> 看起来像"概率太低"，实际是关系被反复拆散。
> **把因子误做成门的典型症状是"某个机制完全失效"，而不是"数值偏小"。**

> **食物口径**：出生读的是"**当下真的能吃到**的食物"
> （共享库存 + 地面物资堆 + 各人随身），**不含**地里还没长出来的存量。
> 调试时要注意：`InterveneAddResource` 写的是**格子存量**，
> 它要先被采集才进入这个口径 —— 这也是为什么验收测试 3 用 `foodPerCapitaTarget`
> 而不是"多撒食物"来制造差异。

> **人口压力用床位而不是地图面积**：这样"盖房子"才成为增长的**前提**而不是装饰。
> 没有床位时压力取一个大于 1 的固定值（**不是 0**）——
> "完全不能生"已经由住房门表达了，这里再叠一个 0 会让两套机制重复、出问题时无法区分。

---

## 12. `fire` — 火灾（M5）

任务书第 68 / 94 条要求的**最小因果证明**（*玩家烧森林 → 人口增速下降*）的起点。

```text
火 → 森林减少 → 木材减少 → 盖房变慢 → 床位不足 → 出生下降
              ↘ 猎物栖息地减少 → 打猎收益下降 → 食物下降 ↗
```

| 键 | 默认 | 含义与影响 |
|---|---|---|
| `enabled` | true | 火灾系统总开关 |
| `baseIgnitionChancePerFastTick` | 0.001 | 自然点燃（雷击）基础概率（每 fast tick 一次判定） |
| `lightningChanceDuringStorm` | 0.0006 | 暴雨期间的额外雷击概率 |
| `drynessWeight` | 1.0 | 点燃判定里干燥度的权重 |
| `temperatureWeight` | 0.6 | 点燃判定里温度的权重 |
| `spreadChancePerFastTick` | 0.04 | 向单个邻居传播的基础概率 |
| `vegetationLossPerFastTick` | 0.04 | 燃烧时每 fast tick 损失多少植被（归零即转焦土） |
| `burntRecoveryPerDay` | 0.006 | 焦土植被恢复速度（**极慢是刻意的**） |
| `burntRecoverThreshold` | 0.25 | 植被恢复到多少时焦土"复原" |
| `windInfluence` | 0.5 | 顺风传播加成（0 = 不看风向） |
| `maxBurningTiles` | 600 | 同时燃烧格数上限（防爆炸的最后一道闸） |
| `burnsWoodResource` | true | 燃烧是否消耗木材存量 |
| `woodLossFractionPerFastTick` | 0.01 | 每 fast tick 额外烧掉多少木材（占容量比例） |

> **风向不是状态。** 它由 `(tick, 世界种子)` 确定性推导（每 8 小时转一个方向），
> 因此不需要存档、也不需要进摘要 —— 而这类"辅助状态"正是 M4 期间漏掉八个字段的根源。
> **一个能由 `(tick, seed)` 算出来的东西就不该被存起来。**

> **火灾有自己的随机流（`RngStream.Fire`，第 9 条）。**
> 它绝不能借用 `Events`（天气在用它）：否则"点燃一片森林"会改变接下来的天气序列，
> 对照实验里两组世界连天气都不同，因果无法归因。
> 实测症状极具迷惑性 —— 实验组的床位反而**多于**对照组（24 vs 20），
> 看起来像"火没作用"，实际是信号被天气扰动淹没。

> **调参提示**：这三个值决定火势的"性格"——
> `baseIgnitionChancePerFastTick`（多久来一场）、
> `spreadChancePerFastTick`（一场能烧多大）、
> `vegetationLossPerFastTick`（单格烧多久，它同时决定传播机会的多少）。
> 把后两个一起调大很容易从"几乎没有火"直接跳到"每天十几场火"（实测过 1454 次 / 200 天），
> 因为传播是**指数**过程。**务必用长跑报表里的"累计起火次数"实测，不要凭感觉调。**

---

## 13. `rules` — 规则开关（M5）

这些改的是**世界的规则**，不是世界的数值。玩家翻一个开关就能回答
"如果这个世界不会死，会长成什么样" —— 这是最直接的**对照实验**形式。

| 键 | 默认 | 生效点 |
|---|---|---|
| `noDeath` | false | `NeedsSystem.Kill` —— 唯一的死亡入口，因此一处生效即可覆盖全部死因 |
| `highBirthRate` | false | 出生公式的**基础概率** ×2（不是最终概率，以免绕过 `maxChancePerDay` 防爆闸） |
| `fastAging` | false | 年龄每日推进 ×2（不是改寿命上限，语义更干净） |
| `doubleResource` | false | 资源再生的**经过天数** ×2（Logistic 对时长非线性，乘时长才等价于"长得更快"） |
| `peaceMode` | false | **保留项**：`Attack` 在 M6、战争在 M8 落地后生效 |

> **`StateHash` 不含配置**，这是有意的：摘要回答的是
> "同一套规则下两次运行是否一致"，而不是"两个不同的世界是否相同"。
> 跨规则比较必须靠 `configDigest`（读档时若配置不符会明确提示）。

---
## 14. `debug` — 调试开关

| 键 | 默认 | 含义与影响 |
|---|---|---|
| `logDecisions` | false | 是否把每次决策打到日志（**极慢**，只在定位问题时开） |
| `stateHashEveryTicks` | 1440 | 每多少 tick 记一次状态摘要（用于漂移检测） |
| `assertInvariants` | true | 是否每 64 tick 校验不变量。可用 `--no-invariants` 临时关闭做性能对比 |

---

## 15. 改参数的推荐流程

1. **先记基线**：`.\tools\run.ps1 -Mode digest -Days 30 -Agents 30` 记下摘要；
2. **只改一个键**，跑一次并对比摘要 —— 摘要变了说明**行为确实变了**（这正是你要的）；
3. 用 `-Mode snapshot` 导出 PNG，**用眼睛确认**变化方向符合预期；
4. 跑 `.\tools\run.ps1 -Mode batch -SeedRange 1..6` 看**多 seed 下是否稳健**
   （单 seed 上的"改善"经常只是那一个世界的巧合）；
5. 把结论写进本文件对应的行 —— 否则下一个人还会再踩一次。

> **一句话原则**：参数是**条件**，不是结果。改参数的意义在于"看看世界会怎样回应"，
> 而不在于"把世界调到某个我想要的样子"。
