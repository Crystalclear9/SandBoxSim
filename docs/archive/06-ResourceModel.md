> 归档资料：保留原始要求与当时实现记录。页内版本、命令、任务流程和配图不代表当前功能；使用与开发请从 [现行文档](../README.md) 阅读。

# 06 — 资源模型（M2）

本文件规定资源的**产生、分布、采集、消耗、再生与枯竭**。
它是"世界为什么会有经济"的答案：资源不是分数，而是地图上会被吃光、会长回来、会被搬走的东西。

对应源码：`ResourceSystem.cs`、`World/ResourceKind.cs`、`World/WorldGenerator.cs`、
`Agents/WildlifeStore.cs`、`Systems/WildlifeSystem.cs`、`World/GroundStockStore.cs`。

---

## 1. 资源种类与语义

| 资源 | 可再生 | 载体 | 再生模型 | M2 的用途 |
|---|---|---|---|---|
| `Food` | ✅ | 草地 / 沙地（野果） | Logistic（按格） | 吃（个体生存）、狩猎产出 |
| `Wood` | ✅ | 森林 | Logistic（按格） | 采集练习与储备（M3 建造主料） |
| `Stone` | ✅（极慢） | 山地 | Logistic（按格） | 储备（M3 建造） |
| `Iron` | ❌ | 山地（矿脉） | 无 | M3 之后 |
| `Vegetation` | ✅ | 森林 / 草地 | 由天气与再生驱动 | **野生动物的食物**（生态链枢纽） |

`Vegetation` 与 `Food` 的区别必须说清楚，否则很容易把两者混为一谈：

- `Food` 是**给人采的存量**（一格里能采到多少"野果"）；
- `Vegetation` 是**生态系统的生产力指标**（长草的程度），它决定动物能活多少。

因此"森林被砍光"会同时压 low 两者，但机制不同：`Food` 因为地形变了而消失，
`Vegetation` 因为栖息地少了而减少动物容量。

---

## 2. 世界生成时的资源分布

| 地形 | 初始资源 | 初始量 |
|---|---|---|
| 草地 | `Food` | `FoodCapacityPerGrassTile × FoodInitialFraction × (0.5 + 0.5 × 肥沃度)` |
| 沙地 | `Food` | `FoodCapacityPerGrassTile × FoodInitialFraction × 0.6` |
| 森林 | `Wood` | `WoodCapacityPerForestTile × WoodInitialFraction` |
| 山地 | `Stone` / `Iron` | 按 `MountainOreFraction` 概率决定是石还是铁 |
| 水域 / 道路 | 无 | — |

**肥沃度**来自世界生成（`ComputeFertility`）：由湿度、海拔、到水的距离共同决定。
因此"河边低地的草地"比"山顶附近的草地"更有产出 —— 这是地理影响经济的第一个环节。

`FoodInitialFraction`（默认 0.6）意味着**世界开局不是满的**：
这给"人口增长到超出环境承载"留了空间，否则第一阶段永远是"资源过剩"。

---

## 3. 采集（人的一侧）

采集流程（`Systems/ActionSystem.TickGather`）：

```text
1. 个体站在目标格上（由寻路与移动送到）
2. 检查目标格仍有所需资源（可能被人抢先采走）
3. taken = ResourceSystem.Harvest(x, y, kind, HarvestPerAction)
4. 放进个体背包（AgentStore 的库存数组）
5. 更新采集统计（HarvestedByKind）
```

| 参数 | 默认 | 说明 |
|---|---|---|
| `FoodHarvestPerAction` | 24 | 一次采集的食物量 |
| `WoodHarvestPerAction` | 8 | 一次采集的木材量 |
| `StoneHarvestPerAction` | 6 | 一次采集的石料量 |
| `IronHarvestPerAction` | 2 | 一次采集的铁矿石量 |

### 3.1 一次采集 = 一次"完整采集行为"

口径是**每次动作**而不是每 tick：采集在到达目标格后**一 tick 结算完**。
因此上面这些数字直接对应"跑一趟能拿回多少"，是最容易调的一类参数。

### 3.2 口粮账（为什么 `FoodHarvestPerAction` 从 5 调到 24）

这是 M1 联调时最花时间的一段，值在此处记录，避免以后又"凭感觉调参"：

```text
进食速率        : 每 tick 0.6 食物，0.09 饥饿恢复 ⇒ 一次吃饱（约 0.85 饥饿）消耗约 5.7 食物
饥饿速率        : HungerPerDay = 1.0 ⇒ 一天需要约 1.2 顿 ⇒ 每天约 7 食物/人
40 人            : 每天需要约 270 食物
一次采集 24      : 每人每天约需 0.3 次采集动作 —— 有余量
一次采集 5（旧） : 每人每天需 1.4 次采集，而决策/移动开销让它实际做不到 ⇒ 全体饿死
```

**这条账必须能算出来**。如果算不出来，就说明参数之间缺少可解释的关系，
那才是真正的设计问题（而不是"数值没调好"）。

---

## 4. 再生（环境的回应）

再生模型：**Logistic（S 形）**，每格独立计算。

```text
capacity = 该格的资源容量（由地形决定）
growth   = GrowthRate × amount × (1 − amount / capacity) × dt
```

三个性质都很重要：

1. **存量越低，长得越快**（`amount` 因子）⇒ 被采空的地方恢复得最快；
2. **接近容量时增长趋缓**（`1 − amount/capacity`）⇒ 不会无限增长；
3. **存量为 0 时增长为 0**（`amount` 因子）⇒ **采绝 = 永久损失**。

第 3 条是刻意的：它让"过度采集造成不可逆损伤"成为一条真实规则（第 94 条）。
`WoodCapacityPerForestTile` 与 `FoodCapacityPerGrassTile` 就是这条规则的尺度：
容量越高，越难被采绝。

再生频率由 `RegenerateHourly` 决定（默认 true，按小时以 `1/24` 的 dt 推进）。

### 4.1 一个被修掉的性能 bug

`Regenerate` 早期版本在"资源值没有变化"时也会调用 `MarkDirtyAt`，
导致每 tick 全图重算 chunk 统计（10 万格 × 每 tick）。
现在只在 `after != before` 时才标脏。

**教训**：脏标记必须只在"真的变了"时打。否则"惰性重算"会退化成"每 tick 全量重算"，
而且不报错、结果也对，只是慢 —— 这类问题只能靠基准数据发现。

---

## 5. 枯竭与紧张度

### 5.1 单格枯竭

当某格资源低于 `DepletionWarnFraction × capacity` 时算"接近枯竭"。
个体层面看不见这个标志（他们只看到 `Amount`），但聚落评估与 UI 会用它。

### 5.2 局部紧张度（`MigrationSystem.LocalScarcity`）

```text
foodPerCell = chunk.FoodAmount / chunk.CellCount
woodPerCell = chunk.WoodAmount / chunk.CellCount

foodScore = clamp01(foodPerCell / 30)      // 30/格 视为充足
woodScore = clamp01(woodPerCell / 40)
abundance = foodScore × 0.65 + woodScore × 0.35
scarcity  = 1 − abundance
```

为什么用"每格平均"而不是总量：chunk 覆盖的格子数不完全相同（地图边缘的块更小），
用总量会让边缘块显得"资源更少"。

### 5.3 全局稀缺度（`ResourceSystem.GlobalScarcity`）

对每种资源算 `1 − 当前量/初始量`，再按权重合成。
它进日报与曲线，是玩家判断"这个世界过得怎么样"的第一个指标。

---

## 6. 采样口径与"故意留白"

| 数据项 | 采样位置 | 为什么 |
|---|---|---|
| `Food / Wood / Stone / Iron` | 每日快照 | 它们是慢变量，每 tick 采样没有额外信息 |
| `Scarcity` | 每日快照 | 同上 |
| `HarvestedByKind` | 累计（不重置） | 累计量才能反映"这段时间的产出"，日增量可由差分得到 |
| `GroundStocks.TotalOf(kind)` | 即时查询 | 地面物资堆很少，直接求和足够快 |
| `Wildlife.LiveCount` | 即时查询（O(1)） | 维护了存活计数，不需要遍历 |

**每次新增一个统计量，都要问两个问题**：
1. 它每 tick 变化吗？（不变化就不该每 tick 采样）
2. 它能在 O(1) 或 O(存活数) 内拿到吗？（不能就不要进日报）

---

## 7. 生态链：植被 → 动物 → 猎人（M2 新增）

这是 M2 里唯一"跨系统"的经济环节，也是"砍树有长期代价"的机制基础。

```text
植被总量（森林 + 草地的 Vegetation）
    ↓  EnvironmentCapacity = 植被总量 × CapacityPerVegetationTile（上限 2000）
动物环境容量
    ↓  繁殖按 (1 − 种群/容量) 抑制，能量不足不繁殖
动物种群规模
    ↓  HuntRange 内猎杀，每次 FoodPerKill
人的食物来源（另一个来源是采集野果）
```

### 7.1 动物的四条规则

| 规则 | 实现 | 观察到的现象 |
|---|---|---|
| **逃** | 附近 `FleeRadius` 内有人 ⇒ 朝远离猎人的方向走 | 打猎不是"点一下就拿到肉"，猎物会跑 |
| **吃** | 所在格植被越多，恢复的能量越多 | 动物会聚到植被好的地方 |
| **繁殖** | 能量 ≥ 0.55 且种群未达容量时按概率产仔，产仔消耗能量 | 种群呈 Logistic 增长并在容量附近震荡 |
| **死** | 能量归零饿死；每日按 `NaturalDeathChancePerDay × (1 + 年龄/60)` 老死 | 种群不会无限增长 |

动物**不做效用决策**，只有这四条规则。这是刻意的：
生态的观察价值来自"数量随植被波动"，而不是来自"每只鹿都在深思"。
用最低的复杂度换来一条真实的因果链，性价比最高。

### 7.2 一个刻意留下的"未接线"

动物吃植被**目前不写回 Tile**（`WildlifeSystem.Tick` 里只有注释）。
理由：直接改 Tile 会让"动物吃掉森林"变成一个无法追踪的隐藏耦合。
M5 的生态细化会正式接入（届时会有 `VegetationRegrowth` 与放牧压力）。
在那之前，植被只由天气与资源再生驱动 —— **宁可不接，也不要接一条说不清的线**。

---

## 8. 地面物资堆（M2 新增）

### 8.1 为什么需要它

M1 的个体只把东西塞进自己口袋，玩家看不到任何"经济"。
有了地面物资堆之后，"攒物资"变成**空间上可见的现象**：
某个地方会逐渐堆起食物与木料，玩家据此能看出"这群人主要在哪里活动"。

它是 M3 的 `Storage` 建筑的前身 —— 差别只是"地上一堆" vs "一间仓库"。

### 8.2 数据结构与取舍

稀疏表示：固定容量数组 + 存活索引列表 + 槽位复用。

为什么不"每格一个堆"：那样 10 万格每格都要带一个结构体（哪怕空着），
而实际同时存在的堆只会有几十个。

**性能细节**：选靶时问"附近有没有堆"是高频操作，
如果遍历整个容量数组（几百个槽位）而只有几十个是活的，就是纯粹的浪费。
存活索引列表把这一步压到 O(存活堆数) —— 这与野生动物那边的教训是同一个
（**稀疏数据不要用稠密数组遍历**）。

### 8.3 容量与合并

- 同格的堆会合并（`FindOrCreate`）；
- 每种资源有独立容量 `CapacityPerKind`（默认 400）；
- `Deposit` 返回**实际接受量**，满了就返回 0，绝不覆盖已有内容；
- `Withdraw` 取空后会**释放槽位**，避免"没人用的空堆"长期占位。

---

## 9. 与其它系统的连接

| 方向 | 连接 | 机制 |
|---|---|---|
| 环境 → 资源 | 湿度 / 肥沃度 | Logistic 再生的速率与容量 |
| 资源 → 个体 | 采集动作 | 采集量直接进背包 |
| 个体 → 资源 | 采集扣减 | 地面存量下降，可能造成不可逆损伤 |
| 植被 → 动物 | 环境容量 | 栖息地决定种群上限 |
| 动物 → 个体 | 狩猎 | `FoodPerKill` 进背包 |
| 个体 → 地面 | 存放 / 取回 | 物资在空间上集中，形成可见的"仓库" |
| 资源 → 迁移 | 局部紧张度 | 采光一片地方 ⇒ 有人开始往外走 |
| 资源 → 统计 | 每日采样 | `Scarcity`、各资源存量曲线 |

---

## 10. 测试契约

| 测试 | 覆盖 |
|---|---|
| `DepositMovesResourceToPile` | 存放确实搬运资源 |
| `TakeMovesResourceFromPileToInventory` | 取回确实搬运资源 |
| `EmptyPileIsReleased` | 空堆释放槽位 |
| `PileCapacityIsRespected` | 容量上限且不覆盖 |
| `WildlifeIsSeededOnWorldGeneration` | 世界生成后有动物，且都在可走格上 |
| `HuntingProducesFoodAndReducesPrey` | 猎杀有产出且猎物减少 |
| `RemovingVegetationReducesWildlifeCapacity` | **方向性**：清除植被 ⇒ 环境容量下降 |
| `AnimalsFleeFromHumans` | **方向性**：动物不会主动靠近人 |
| `ReproductionIsSuppressedAtCapacity` | 种群被容量限制 |
| `ResourceSystem` 相关（M0 起） | 再生、采集、注入、稀缺度 |

**"方向性断言"是验证涌现机制的正确姿势**：
它不依赖具体数值（不会因为调参而随机失效），只断言"因果链的方向正确"。
