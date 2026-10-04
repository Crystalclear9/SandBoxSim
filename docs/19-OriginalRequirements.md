# 沙盒模拟游戏完整设计与开发任务书

你是一名同时具备以下能力的资深游戏开发者：

- Game Designer
- Simulation Game Designer
- Gameplay Programmer
- AI Programmer
- Systems Designer
- Technical Designer
- Software Architect

你的任务是帮助我从零设计并实现一个**小型但具有高度涌现性的沙盒模拟游戏（Sandbox Simulation Game）**。

这个项目的重点不是制作大量预先编排好的剧情，也不是一开始就制作一个内容极其庞大的游戏，而是构建一个：

> 由少量基础规则驱动，但不同系统之间能够相互影响，从而自行产生复杂行为、故事、社会变化和文明发展的模拟世界。

游戏设计的最高原则是：

> 玩家创造“条件”，而不是直接创造“结果”。

例如玩家不应该拥有一个简单的：

“创建城市”

按钮。

玩家真正应该做的是：

- 放置居民
- 创造森林
- 创造水源
- 创造矿产
- 改变地形
- 改变气候
- 制造灾害
- 提供资源
- 修改世界规则

之后：

- 居民是否留下
- 是否形成村庄
- 是否人口增长
- 是否产生农业
- 是否形成城市
- 是否发生资源危机
- 是否产生迁移
- 是否发生冲突
- 是否产生贸易
- 是否最终灭亡

都应尽可能由模拟系统自行决定。

整个游戏应围绕：

> Observation → Intervention → Simulation → Emergence → Observation

即：

> 观察世界 → 玩家干预 → 世界自行运行 → 出现新的结果 → 玩家继续观察或再次干预

这个循环展开。

---

# 一、游戏核心定位

希望游戏整体可以理解为：

> WorldBox + RimWorld + 微型 Civilization / Colony Simulation

但不要试图在第一阶段复制这些游戏的完整复杂度。

目标应该是：

> 用尽可能少的系统创造尽可能多的涌现行为。

游戏早期应该可以做到：

即使地图上只有几十个居民，玩家仍然愿意观察他们的生活、发展、迁移、冲突和死亡。

---

# 二、核心设计哲学

整个项目必须始终遵循以下原则。

---

## 2.1 Emergent Gameplay

不要大量硬编码：

- 某个故事
- 某个剧情
- 某个城市必然出现
- 某场战争必然出现
- 某个人一定成为领导者

而应该通过系统之间的相互作用，使这些结果自行出现。

例如：

森林丰富

↓

木材丰富

↓

居民容易建造房屋

↓

住房增加

↓

人口增加

↓

粮食需求增加

↓

居民扩大农田

↓

森林减少

↓

动物减少

↓

食物来源减少

↓

粮食短缺

↓

居民开始迁移

↓

另一地区形成新的聚落

整个过程中：

“建立第二个村庄”

不应该是一条预先编写的剧情任务。

它应该是系统运行的自然结果。

---

# 三、游戏核心循环

核心 Gameplay Loop：

```text
观察世界
    ↓
发现某个现象 / 问题 / 有趣情况
    ↓
玩家选择是否干预
    ↓
修改世界条件
    ↓
模拟世界继续运行
    ↓
NPC / 资源 / 环境 / 社会产生新的状态
    ↓
产生新的故事和问题
    ↓
玩家继续观察
```

玩家的主要乐趣来源应该包括：

### 1. 实验感

例如：

“如果把两个文明之间唯一的河流截断，会发生什么？”

“如果给一个人口很少的村庄大量粮食，会发生什么？”

“如果把所有森林烧掉，会发生什么？”

“如果在两个文明之间放置一个大型铁矿，会发生什么？”

---

### 2. 养成感

例如：

玩家最开始只放下：

3 个居民。

几十分钟后：

这三个居民可能已经形成：

- 家庭
- 村庄
- 农田
- 仓库
- 道路
- 第二代居民
- 第二个聚落

---

### 3. 故事感

玩家可能观察到：

张三出生

↓

张三成为农民

↓

张三与李四建立家庭

↓

张三遭遇饥荒

↓

张三迁移

↓

张三建立新的定居点

↓

定居点发展为村庄

↓

多年以后张三成为村庄领袖

这种故事最好不是剧情系统硬编码生成，而是：

Simulation State + Event Log

自然产生。

---

# 四、整体系统架构

整个游戏至少可以拆分为：

```text
World System
Resource System
Entity System
AI System
Building System
Settlement System
Relationship System
Economy System
Society System
Civilization System
Event System
Player Intervention System
Statistics System
Story / History System
```

但开发阶段必须控制复杂度。

第一阶段只实现：

```text
World
Resource
Entity
AI
Building
Basic Settlement
Player Intervention
Simulation Loop
```

然后逐步扩展。

---

# 五、World System

World System 管理：

- 地图
- Tile
- 地形
- 水域
- 环境
- 时间
- 天气
- 温度
- 湿度
- 土地肥沃度
- 世界资源

第一版推荐：

```text
50 × 50

或

100 × 100
```

Tile Map。

不要第一版就追求超大型世界。

---

# 六、Tile 数据模型

每一个 Tile 可以拥有如下信息：

```cpp
Tile {
    Position position;

    TerrainType terrain;

    float fertility;
    float moisture;
    float temperature;

    ResourceNode resource;

    bool walkable;
    bool buildable;

    Entity occupants[];
    Building building;
}
```

TerrainType 可以包括：

```text
Grass
Forest
Water
Mountain
Sand
Farmland
```

未来再添加：

```text
Snow
Swamp
Desert
Lava
Road
```

---

# 七、地形与规则

不同 Tile 应提供不同规则。

例如：

Grass：

```text
可行走
可建造
可转为农田
```

Forest：

```text
可采集 Wood
可能存在动物
较难建造
可能发生火灾
```

Water：

```text
默认不可行走
不能建造普通建筑
提供饮水
影响附近湿度
未来可支持捕鱼
```

Mountain：

```text
移动成本高
可能存在 Stone
可能存在 Iron
不能种田
```

Farmland：

```text
可种植 Food
产量由 fertility / moisture / weather 决定
```

---

# 八、时间系统

需要一个统一 Simulation Clock。

例如：

```text
1 Tick = 若干游戏秒
```

建议不要让所有逻辑每一帧执行。

可以设计：

```text
Frame Update

↓

Simulation Tick

↓

Entity Tick

↓

World Tick

↓

Daily Tick
```

例如：

```text
AI Decision:
每 0.5 ~ 2 游戏秒一次

Resource Regeneration:
每天一次

Aging:
每天一次

Weather:
若干小时一次

Population Statistics:
每天更新
```

目的是减少计算量。

---

# 九、资源系统

第一版只实现少量资源。

推荐：

```text
Food
Wood
Stone
Iron
```

其中第一阶段甚至可以只实现：

```text
Food
Wood
Stone
```

资源意义：

Food：

```text
维持居民生存
繁殖
人口增长
```

Wood：

```text
房屋
仓库
基础设施
工具
燃料
```

Stone：

```text
高级建筑
道路
强化建筑
```

Iron：

```text
工具
武器
高级建筑
```

---

# 十、资源链

资源系统应该形成最基本的发展链：

```text
采集 Food
    ↓
居民生存
    ↓
采集 Wood
    ↓
建造 House
    ↓
住房增加
    ↓
人口增加
    ↓
需要更多 Food
    ↓
建设 Farm
    ↓
人口进一步增长
    ↓
需要 Stone
    ↓
建设高级设施
    ↓
获取 Iron
    ↓
生产工具 / 武器
```

但不要使用固定时代升级。

应该尽量由资源与需求驱动。

---

# 十一、资源节点

资源最好不要只是简单全局数字。

例如森林 Tile 可以拥有：

```cpp
ResourceNode {
    ResourceType type;
    float amount;
    float maxAmount;
    float regenerationRate;
}
```

例如：

```text
Forest

Wood = 100
Regeneration = 2 / Day
```

如果居民砍伐速度：

```text
5 / Day
```

则森林最终会逐渐减少。

这会自然产生：

```text
过度采集
资源枯竭
迁移
资源竞争
```

---

# 十二、NPC / Entity System

NPC 是整个游戏最关键的部分。

第一版 NPC 不需要特别复杂。

基础属性：

```cpp
Agent {
    EntityID id;

    string name;

    int age;

    float health;
    float hunger;
    float energy;

    Position position;

    Inventory inventory;

    EntityID home;
    EntityID workplace;

    JobType job;

    EntityID settlement;

    Personality personality;

    RelationshipMap relationships;

    AgentState state;
}
```

---

# 十三、NPC 基础需求

至少设计：

```text
Hunger
Energy
Safety
Shelter
Work
Social
```

第一版可以只真正实现：

```text
Hunger
Energy
Shelter
Work
```

这些需求随时间变化。

例如：

\[
Hunger_{t+1}
=
Hunger_t
+
\Delta Hunger
\]

Energy：

\[
Energy_{t+1}
=
Energy_t
-
ActivityCost
+
SleepRecovery
\]

---

# 十四、NPC 行为系统

不建议第一版使用复杂行为树。

推荐使用：

# Utility AI

每个 NPC 在需要决策时：

计算当前可以执行的所有 Action 的 Utility Score。

然后选择收益最高的行为。

---

# 十五、Utility AI

假设行为：

```text
Eat
Sleep
GatherFood
GatherWood
BuildHouse
Farm
Socialize
Explore
Flee
Attack
```

每个行为：

\[
Utility(action)
\]

例如：

吃饭：

\[
U_{eat}
=
w_h
\cdot
Hunger
+
w_f
\cdot
FoodAvailable
\]

睡觉：

\[
U_{sleep}
=
w_e
\cdot
Fatigue
\]

工作：

\[
U_{work}
=
w_r
\cdot
ResourceNeed
\]

逃跑：

\[
U_{flee}
=
w_d
\cdot
Danger
\]

社交：

\[
U_{social}
=
w_s
\cdot
SocialNeed
\]

最后：

\[
Action^*
=
\arg\max_a U(a)
\]

---

# 十六、Utility Score 归一化

建议大部分输入统一到：

\[
[0,1]
\]

范围。

例如 Hunger：

```text
0 = 完全不饿
1 = 极度饥饿
```

Energy：

```text
0 = 精力充足
1 = 极度疲劳
```

这样可以避免不同系统数值尺度混乱。

---

# 十七、Utility Curve

以后不要只使用线性：

\[
U(x)=x
\]

可以增加非线性：

例如饥饿：

\[
U_{eat}(x)
=
x^2
\]

意味着：

不太饿时吃饭欲望很低，

非常饿时吃饭优先级急剧增加。

也可以使用：

\[
U(x)
=
\frac{1}{1+e^{-k(x-x_0)}}
\]

即 Logistic Curve。

用于模拟阈值型行为。

---

# 十八、Action 执行结构

一个 Action 建议拆为：

```text
Condition
Target Selection
Movement
Execution
Result
```

例如：

GatherWood：

```text
检查自己是否需要 Wood

↓

寻找附近 Forest

↓

选择目标 Tile

↓

Pathfinding

↓

移动

↓

采集 Wood

↓

资源进入 Inventory

↓

森林 Wood Amount 减少
```

---

# 十九、NPC 不应该拥有全知信息

NPC 不应知道：

整个世界哪里有什么资源。

可以简单实现：

```text
Search Radius
```

NPC 只搜索自己附近：

例如：

```text
Radius = 10 Tiles
```

否则 AI 会显得非常机械。

未来可以加入：

```text
Memory
Known Locations
Rumor
Settlement Knowledge
```

但第一版不需要。

---

# 二十、Pathfinding

第一版使用：

```text
A*
```

即可。

不同地形拥有不同移动代价：

Grass：

\[
Cost=1
\]

Forest：

\[
Cost=1.5
\]

Mountain：

\[
Cost=3
\]

Water：

```text
不可通过
```

---

# 二十一、建筑系统

第一阶段只需要：

```text
House
Farm
Storage
```

House：

```text
提供居住空间
恢复 Energy
支持人口增长
```

Farm：

```text
生产 Food
受 Fertility / Moisture / Weather 影响
```

Storage：

```text
存储共享资源
```

---

# 二十二、建筑需求

例如：

House：

```text
Wood = 20
Stone = 0
```

Storage：

```text
Wood = 40
Stone = 10
```

Farm：

```text
需要 Grass Tile
需要一定 Fertility
```

之后可以升级。

---

# 二十三、农业系统

农业产量可以简单建模：

\[
FoodProduction
=
BaseYield
\times
Fertility
\times
MoistureFactor
\times
WeatherFactor
\]

其中：

\[
Fertility\in[0,1]
\]

\[
MoistureFactor\in[0,1.5]
\]

\[
WeatherFactor\in[0,1.5]
\]

例如：

干旱：

\[
WeatherFactor=0.4
\]

丰收天气：

\[
WeatherFactor=1.2
\]

---

# 二十四、人口系统

人口不应该无限增长。

必须同时存在：

# 正反馈

和：

# 负反馈

---

# 二十五、人口正反馈

例如：

```text
人口增加
↓
劳动力增加
↓
资源采集增加
↓
粮食增加
↓
建筑增加
↓
人口进一步增长
```

这是一种：

Positive Feedback Loop。

---

# 二十六、人口负反馈

同时需要：

```text
人口增加
↓
食物需求增加
↓
粮食不足
↓
饥饿增加
↓
死亡 / 迁移
↓
人口减少
```

否则世界最终只会无限指数增长。

---

# 二十七、人口出生条件

不要简单随机生人。

例如：

只有：

```text
Food > Minimum
Housing > Population
Population >= 2
```

时才允许一定概率出生。

例如：

\[
P_{birth}
=
P_{base}
\times
FoodFactor
\times
HousingFactor
\]

其中：

FoodFactor：

```text
粮食充足 → 接近 1
粮食不足 → 接近 0
```

---

# 二十八、死亡系统

NPC 可以因为：

```text
Age
Hunger
Combat
Disease
Disaster
Animal Attack
```

死亡。

第一阶段只实现：

```text
Hunger
Age
```

甚至第一版只实现 Hunger Death 也可以。

---

# 二十九、聚落系统

居民数量达到一定程度后：

可以形成 Settlement。

但不要简单规定：

“5 人自动生成村庄”。

更合理的是：

当一群居民：

- 长期生活在相近区域
- 共享 Storage
- 共享建筑
- 拥有稳定住所

则自动形成：

Settlement。

第一版可以适度简化。

---

# 三十、Settlement 数据

```cpp
Settlement {
    SettlementID id;

    string name;

    Position center;

    vector<EntityID> members;

    vector<BuildingID> buildings;

    ResourceStorage sharedResources;

    Territory territory;

    SettlementTraits traits;
}
```

---

# 三十一、聚落发展

聚落自然经历：

```text
Camp
↓
Village
↓
Town
↓
City
```

不要第一版实现全部。

第一阶段：

```text
Village
```

即可。

后续根据：

- Population
- Building Count
- Resource Production

自动判断规模。

---

# 三十二、NPC 性格系统

这是非常重要但可以在第二阶段实现的系统。

建议 Personality Traits：

```text
Brave
Cowardly

Greedy
Generous

Kind
Cruel

Aggressive
Peaceful

Lazy
Hardworking

Social
Introverted

Curious
Conservative
```

每个 Personality Trait 使用：

\[
[-1,1]
\]

或者：

\[
[0,1]
\]

表示。

例如：

```cpp
Personality {
    float aggression;
    float greed;
    float kindness;
    float bravery;
    float industriousness;
    float sociability;
}
```

---

# 三十三、人格影响 Utility AI

人格不能只是 UI 上显示。

必须改变行为。

例如：

Attack：

\[
U_{attack}
=
BaseAttackUtility
+
Aggression
\times
w_a
\]

ShareFood：

\[
U_{share}
=
BaseShareUtility
+
Kindness
\times
w_k
-
Greed
\times
w_g
\]

Work：

\[
U_{work}
=
ResourceNeed
+
Industriousness
\times
w_i
\]

这样不同 NPC 才会真正产生不同人生。

---

# 三十四、关系系统

NPC 之间应该能够形成：

```text
Friend
Enemy
Partner
Parent
Child
Sibling
Leader
Follower
```

可以维护：

\[
Relationship(A,B)
\in
[-100,100]
\]

例如：

```text
-100 = 极端敌对
0 = 中立
100 = 极度亲近
```

---

# 三十五、关系变化

例如：

帮助：

\[
Relationship += 10
\]

分享食物：

\[
Relationship += 5
\]

偷窃：

\[
Relationship -= 20
\]

攻击：

\[
Relationship -= 40
\]

共同战斗：

\[
Relationship += 15
\]

---

# 三十六、家庭系统

未来可以支持：

```text
Marriage
Partner
Parent
Child
Family Household
```

这样玩家更容易关注个体。

例如：

```text
张三
妻子：李四
儿子：张小三
女儿：张小四
```

当张三死亡时：

可能真正让玩家产生情绪。

---

# 三十七、职业系统

不要第一版实现几十种职业。

建议：

```text
Gatherer
Farmer
Builder
Miner
```

第二阶段：

```text
Hunter
Soldier
Trader
Craftsman
Leader
```

---

# 三十八、职业分配

职业最好由需求驱动。

例如：

\[
Need_{farmer}
=
FoodDemand
-
FoodProduction
\]

\[
Need_{builder}
=
HousingDemand
-
HousingSupply
\]

NPC 根据：

```text
Settlement Need
+
Personality
+
Nearby Resources
```

选择职业。

---

# 三十九、最重要原则：系统之间必须连接

任何新系统设计前都必须问：

> 它会影响哪些已有系统？

避免出现：

```text
Weather System
Economy System
Population System
Relationship System
```

但它们彼此完全独立。

正确结构应该类似：

```text
Weather
↓
Farm Production
↓
Food Supply
↓
Food Price
↓
Hunger
↓
Population
↓
Migration
↓
Settlement Growth
```

---

# 四十、典型反馈链

例如：

```text
降雨减少
↓
Moisture 降低
↓
农作物产量下降
↓
Food Supply 下降
↓
粮食价格上升
↓
居民 Hunger 增加
↓
社会稳定下降
↓
犯罪 / 冲突概率上升
↓
居民迁移
```

这里不一定第一版全部实现，

但整个架构应该允许以后逐步加入。

---

# 四十一、生态系统

未来加入：

```text
Plant
Herbivore
Predator
Human
```

形成：

```text
Grass
↓
Rabbit
↓
Wolf
```

以及：

```text
Forest
↓
Animal Population
↓
Human Hunting
```

人类过度砍伐：

```text
Forest ↓
↓
Animal Habitat ↓
↓
Animal Population ↓
↓
Hunting Yield ↓
```

这会形成非常好的涌现效果。

---

# 四十二、天气系统

基础天气：

```text
Sunny
Rain
Storm
Drought
Snow
```

天气影响：

```text
Temperature
Moisture
Farm
Movement
Fire Risk
```

---

# 四十三、火灾系统

例如火灾概率：

\[
P_{fire}
=
BaseFireChance
\times
Dryness
\times
TemperatureFactor
\times
VegetationFactor
\]

其中：

\[
Dryness
=
1-Moisture
\]

森林越干燥，

火灾概率越高。

火灾还应该具有传播：

```text
Burning Tile

↓

Neighbor Tile

↓

根据：

Vegetation
Moisture
Wind

计算传播概率
```

---

# 四十四、事件系统

不要完全依赖随机事件。

事件最好分为：

### Random Event

例如：

```text
Meteor
Rare Blessing
```

### State-driven Event

例如：

```text
Dryness High
+
Forest Exists
→ Fire

Food Low
+
Population High
→ Famine

Disease Exists
+
Population Density High
→ Epidemic
```

这样玩家会觉得：

事件有因果逻辑。

---

# 四十五、玩家上帝工具

玩家不能只是观看。

否则游戏很容易成为：

“屏保模拟器”。

玩家需要拥有主动 Intervention。

推荐工具：

---

## Create

```text
Spawn Human
Spawn Animal
Grow Forest
Create Food
Create Resource
```

---

## Terrain

```text
Raise Land
Lower Land
Create River
Create Mountain
Create Water
Remove Water
```

---

## Resource

```text
Add Food
Add Wood
Add Stone
Add Iron
```

---

## Blessing

```text
Increase Fertility
Increase Birth Rate
Heal
Increase Production
```

---

## Disaster

```text
Fire
Lightning
Flood
Drought
Plague
Meteor
```

---

## Rules

未来可以加入：

```text
Disable War
High Birth Rate
Fast Aging
No Death
Double Resource
Peace Mode
```

---

# 四十六、玩家干预的设计哲学

玩家应尽量：

改变条件。

而不是：

直接指定结果。

例如：

不好：

```text
Create City
Start War
Make Rich
```

更好：

```text
Add Population
Add Resource
Create Scarcity
Change Terrain
Add Weapon Resources
Increase Aggression
```

然后：

城市、战争、财富差距，

自然出现。

---

# 四十七、经济系统

经济系统不要第一版做复杂。

可以从：

Supply / Demand

开始。

例如资源价格：

\[
Price_r
=
BasePrice_r
\times
\left(
\frac{Demand_r+\epsilon}
{Supply_r+\epsilon}
\right)^\alpha
\]

其中：

\[
\epsilon>0
\]

用于避免除零。

\[
\alpha
\]

控制价格弹性。

---

# 四十八、Food Price 示例

如果：

```text
Food Supply = 100
Food Demand = 200
```

则价格上涨。

如果：

```text
Food Supply = 300
Food Demand = 100
```

则价格下降。

---

# 四十九、价格影响职业

例如：

```text
Food Price ↑
↓
Farmer Income ↑
↓
More NPC become Farmer
↓
Food Production ↑
↓
Food Supply ↑
↓
Food Price ↓
```

这形成：

Negative Feedback Loop。

这类反馈环是整个模拟游戏最重要的设计。

---

# 五十、经济第一版要求

第一版本不要做：

```text
银行
股票
贷款
税收
货币政策
国际金融
```

先只做：

```text
Resource Production
Resource Consumption
Supply
Demand
Basic Price
```

---

# 五十一、文明系统

当多个 Settlement 出现后，

未来可以组合为：

Civilization。

Civilization 可以拥有：

```text
Culture
Military
Trade
Technology
Expansion
Diplomacy
```

但不要第一阶段实现。

---

# 五十二、文明性格

Civilization 可以拥有：

```cpp
CivilizationTraits {
    float militarism;
    float expansionism;
    float tradePreference;
    float technologicalFocus;
    float isolationism;
}
```

例如：

Civilization A：

```text
农业发达
人口多
贸易倾向高
军事弱
```

Civilization B：

```text
人口少
矿产丰富
军事强
粮食不足
```

于是：

A：

拥有 Food。

B：

拥有 Iron。

可能形成：

```text
Trade
```

如果关系恶化：

也可能形成：

```text
War
```

---

# 五十三、战争系统

战争不要第一版实现。

未来应尽量避免：

```text
Randomly Start War()
```

更合理的是：

\[
WarPressure
=
ResourceConflict
+
TerritoryConflict
+
Aggression
+
HistoricalHostility
-
TradeBenefit
-
Relationship
\]

当：

\[
WarPressure>Threshold
\]

时：

战争概率明显提高。

---

# 五十四、迁移系统

Migration 是非常重要的涌现机制。

NPC Migration Utility：

\[
U_{migration}
=
FoodScarcity
+
HousingScarcity
+
Danger
+
PopulationPressure
+
NearbyOpportunity
-
HomeAttachment
\]

当：

\[
U_{migration}>Threshold
\]

居民可能离开。

之后可能：

```text
Join Existing Settlement

或

Create New Settlement
```

---

# 五十五、世界故事系统

需要建立：

Event Log。

每一个重要事件记录：

```cpp
WorldEvent {
    Timestamp time;

    EventType type;

    EntityID actor;
    EntityID target;

    Position location;

    string description;

    Importance importance;
}
```

---

# 五十六、可以记录的事件

例如：

```text
NPC Born
NPC Died
House Built
Settlement Founded
Settlement Destroyed
Marriage
Migration
Combat
Fire
Famine
Leader Changed
War Started
War Ended
```

---

# 五十七、NPC Timeline

点击一个 NPC 时，

应该显示：

```text
Name:
张三

Age:
27

Occupation:
Farmer

Status:
Hungry

Home:
House #23

Settlement:
River Village
```

以及：

```text
Day 3
张三出生

Day 26
张三成为农民

Day 33
张三建造房屋

Day 40
张三与李四建立家庭

Day 62
张三在森林遭到狼攻击

Day 79
张三迁移到北方

Day 103
张三成为新村庄创始人
```

这个系统非常重要，

因为它把：

“一个模拟单位”

变成：

“玩家认识的人”。

---

# 五十八、Settlement History

村庄也应该有历史：

```text
Day 15

River Village 建立

Day 30

人口达到 20

Day 51

发生第一次饥荒

Day 82

15 名居民向北迁移

Day 90

North Village 建立

Day 150

River Village 与 North Village 开始贸易
```

---

# 五十九、数据统计系统

玩家应该能够观察：

```text
Population
Food
Wood
Stone
Birth
Death
Migration
Settlement Count
```

最好拥有历史曲线。

例如：

Population:

\[
P(t)
\]

Food:

\[
F(t)
\]

Wood:

\[
W(t)
\]

---

# 六十、世界稳定性

模拟游戏最大的风险之一是：

系统进入：

```text
Stable Equilibrium
```

然后什么都不发生。

因此必须设计：

```text
Resource Scarcity
Random Variation
Environmental Change
Population Pressure
Personality Difference
```

不断制造新的压力。

但不能让世界永远混乱。

需要：

```text
Stable Period

↓

Pressure Accumulation

↓

Change

↓

New Equilibrium
```

这样的节奏。

---

# 六十一、世界不应该完全平衡

完全平衡的系统通常不好玩。

应该允许：

```text
Boom
Bust
Migration
Collapse
Recovery
Expansion
```

例如：

```text
粮食充足
↓
人口爆发
↓
粮食不足
↓
人口下降
↓
资源恢复
↓
人口重新增长
```

形成周期。

---

# 六十二、重要公式：资源增长

可以使用：

\[
R_{t+1}
=
R_t
+
Regeneration
-
Consumption
\]

资源最大值：

\[
R_{t+1}
=
\min(
R_{max},
R_t+Regeneration-Consumption
)
\]

---

# 六十三、可再生资源

例如森林：

可以使用 Logistic Growth：

\[
\frac{dR}{dt}
=
rR
\left(
1-\frac{R}{K}
\right)
\]

离散形式：

\[
R_{t+1}
=
R_t
+
rR_t
\left(
1-\frac{R_t}{K}
\right)
\]

其中：

\[
r
\]

为生长率。

\[
K
\]

为最大容量。

这比简单固定：

```text
+2 Wood / Day
```

更自然。

---

# 六十四、人口增长

未来也可以参考 Logistic Growth：

\[
\frac{dP}{dt}
=
rP
\left(
1-\frac{P}{K}
\right)
\]

但游戏中不要直接使用纯 Logistic 人口公式。

因为人口上限：

\[
K
\]

最好由：

```text
Food
Housing
Environment
Safety
```

动态决定。

即：

\[
K
=
f(
FoodCapacity,
HousingCapacity,
Environment
)
\]

---

# 六十五、社会压力

未来可以设计：

\[
SocialPressure
=
FoodScarcity
+
HousingScarcity
+
PopulationDensity
+
Danger
+
Inequality
\]

它可以影响：

```text
Migration
Crime
Conflict
Rebellion
```

---

# 六十六、Feedback Loop 设计要求

每增加一个机制，

必须至少分析：

### Input

这个系统读取哪些变量？

### Output

它修改哪些变量？

### Positive Feedback

它是否可能自我放大？

### Negative Feedback

有什么机制限制它？

例如：

人口：

Input：

```text
Food
Housing
Safety
```

Output：

```text
Labor
Consumption
Resource Pressure
```

Positive Feedback：

```text
Population
→ Labor
→ Production
→ Population
```

Negative Feedback：

```text
Population
→ Consumption
→ Scarcity
→ Population ↓
```

---

# 六十七、第一版 MVP

第一版必须严格控制规模。

目标：

> 能够看着一群 NPC 自己活下来、工作、采集、建房，并且玩家可以干预。

不要追求文明战争。

MVP 地图：

```text
100 × 100
```

Terrain：

```text
Grass
Forest
Water
Mountain
```

Resource：

```text
Food
Wood
Stone
```

NPC：

```text
Health
Hunger
Energy
Age
Inventory
```

Actions：

```text
Move
Find Food
Eat
Gather Food
Gather Wood
Gather Stone
Sleep
Build House
Build Farm
```

Building：

```text
House
Farm
Storage
```

Player Tools：

```text
Spawn Human
Spawn Food
Grow Forest
Add Wood
Create Fire
Lightning
```

Simulation：

```text
Day / Night
Population
Resource Statistics
```

---

# 六十八、MVP 成功标准

第一阶段完成后，

必须可以发生这样的行为：

玩家创建：

```text
10 Humans
```

附近拥有：

```text
Forest
Food
Water
```

NPC 自行：

```text
寻找 Food
↓
采集 Food
↓
吃饭
↓
采 Wood
↓
建 House
↓
建 Farm
↓
形成共享资源
↓
人口逐渐增长
```

玩家如果：

烧毁 Forest：

```text
Wood Scarcity
↓
House Construction ↓
↓
Population Growth ↓
```

玩家如果：

增加 Food：

```text
Population Growth ↑
```

这证明系统之间已经开始发生联系。

---

# 六十九、第二阶段

第二阶段加入：

```text
NPC Name
Age
Personality
Relationship
Family
Job
Village
Birth
Death
Marriage
Event Log
Timeline
```

目标：

让玩家产生：

“关注某个 NPC”

的感觉。

---

# 七十、第二阶段成功标准

玩家可以点击一个居民：

```text
Alice
Age 34
Farmer
```

看到：

```text
Parents
Partner
Children
Friends
Enemies
Home
Job
Village
History
```

并能够观察：

```text
出生
成长
工作
家庭
迁移
死亡
```

---

# 七十一、第三阶段

加入：

```text
Multiple Settlements
Trade
Territory
Migration
Civilization
Conflict
War
Alliance
```

这个阶段世界才逐渐变成：

文明沙盒。

---

# 七十二、暂时不要做的内容

第一阶段禁止主动扩展到：

```text
完整科技树

几十种资源

几十种职业

复杂装备系统

复杂战斗系统

外交系统

政治系统

宗教系统

完整金融经济

超大地图

多人联机

3D 高质量美术

复杂任务系统

完整剧情系统
```

理由：

这些内容很容易让项目规模失控。

真正需要先验证的是：

> 10～30 个 NPC 在地图上自己生活是否有趣。

如果这个核心没有乐趣，

更多内容不会让游戏变好。

---

# 七十三、性能设计原则

因为未来可能存在大量 NPC，

从一开始就应该注意：

不要：

```text
Every NPC × Every Frame
```

执行复杂 AI。

推荐：

```text
Staggered Update
```

例如：

1000 个 NPC：

每 Tick 更新 100 个。

10 Tick 完成一次完整 AI Refresh。

---

# 七十四、Spatial Query

不要每个 NPC：

扫描整个地图。

使用：

```text
Grid
Spatial Hash
Chunk
Quadtree
```

至少实现一种空间索引。

例如：

```text
World

↓

Chunks

↓

Chunk Contains:
Entities
Resources
Buildings
```

NPC 只搜索附近 Chunk。

---

# 七十五、Entity Component 思路

如果使用 Unity，

可以不一定直接使用 DOTS，

但架构上建议：

数据和行为尽可能解耦。

例如：

```text
HealthComponent
NeedComponent
InventoryComponent
AIComponent
RelationshipComponent
```

不要形成一个：

```text
Human.cs
```

拥有几千行代码。

---

# 七十六、保存系统

Simulation Game 必须考虑 Save / Load。

需要保存：

```text
World Seed
Tiles
Resources
NPC
Buildings
Settlements
Relationships
Events
Simulation Time
Random State
```

如果希望 Save 后结果可重现，

最好保存：

Random Seed / RNG State。

---

# 七十七、随机性设计

不要大量：

```cpp
Random.Range()
```

散落代码。

建议统一：

```text
SimulationRandom
```

管理随机数。

最好支持：

```text
Seeded Random
```

这样相同 Seed：

可以帮助调试。

---

# 七十八、Debug 工具

必须尽早做。

例如 NPC 点击后显示：

```text
Current Action

Utility Scores

Hunger

Energy

Current Target

Path

Inventory

Settlement
```

例如：

```text
Eat       0.92
Sleep     0.31
Gather    0.55
Build     0.22
```

这样可以理解 AI 为什么做出某个决定。

---

# 七十九、Debug Overlay

建议：

```text
Pathfinding View
Resource Heatmap
Food Heatmap
Population Heatmap
Fertility Map
Moisture Map
Settlement Territory
AI State
```

这对于调试 Simulation Game 极其重要。

---

# 八十、游戏 UI

最基础 UI：

顶部：

```text
Day 120
Population 83
Food 340
Wood 120
Stone 85
```

左侧：

```text
Spawn
Terrain
Resource
Disaster
```

右侧：

点击对象 Inspector。

底部：

```text
Pause
1×
2×
4×
8×
```

---

# 八十一、时间倍率

Simulation Game 强烈建议：

```text
Pause
1×
2×
4×
8×
```

甚至：

```text
16×
```

但高速模拟下：

不要简单提高 Update 次数，

应确保 Tick 系统稳定。

---

# 八十二、AI Tick 与 Render 分离

必须区分：

```text
Render FPS

和

Simulation Tick Rate
```

例如：

Rendering：

```text
60 FPS
```

Simulation：

```text
10 TPS
```

高速：

```text
40 TPS
```

而不是：

改变 Time.deltaTime 后让所有系统失控。

---

# 八十三、世界生成

第一版可以：

Random Noise。

例如：

```text
Perlin Noise
```

生成：

```text
Height
Moisture
```

然后：

```text
Height < threshold
→ Water

High Height
→ Mountain

High Moisture
→ Forest
```

这样地图自然一些。

---

# 八十四、世界 Seed

每张地图：

```text
Seed
```

玩家可以：

```text
New World
Seed: 839102
```

保证地图可以复现。

---

# 八十五、未来生物系统

动物也最好使用与人类类似的 Utility AI。

例如 Deer：

```text
Eat
Drink
Sleep
Flee
Reproduce
```

Wolf：

```text
Hunt
Eat
Drink
Sleep
Reproduce
```

于是生态系统：

```text
Grass
↓
Deer
↓
Wolf
```

可以自然波动。

---

# 八十六、未来疾病系统

疾病可以传播：

\[
P_{infection}
=
BaseRate
\times
Contact
\times
Density
\times
DiseaseStrength
\]

人口密度越大，

传播越快。

疾病会进一步影响：

```text
Population
Labor
Food Production
Migration
```

---

# 八十七、未来技术系统

不要使用传统：

```text
Click Technology
```

也可以设计为：

满足条件后：

Innovation Probability 增加。

例如：

人口多

+

铁矿丰富

+

Craftsman 多

↓

Metal Tool Innovation Probability ↑

但是这个属于很后期功能。

---

# 八十八、Agent 最终需要输出的设计内容

请基于以上需求，

首先不要直接开始无规划地写大量代码。

你应该分阶段输出。

---

# 第一部分：Game Design Document

给出：

```text
Game Vision
Core Fantasy
Core Gameplay Loop
Player Motivation
Emergent Gameplay Design
Win / Loss Philosophy
Sandbox Philosophy
```

---

# 第二部分：System Architecture

画出逻辑结构：

```text
World
↓
Resources
↓
Agents
↓
Settlements
↓
Civilization
```

以及各系统的数据依赖。

必须明确：

谁读取谁，

谁修改谁。

---

# 第三部分：Simulation Architecture

设计：

```text
Game Loop
Simulation Tick
AI Tick
World Tick
Daily Tick
```

明确：

每个系统：

多久更新一次。

---

# 第四部分：Data Structure

给出实际可实现的数据结构：

```text
Tile
World
Agent
Resource
Building
Settlement
Action
Utility
Event
Relationship
```

需要说明字段含义。

---

# 第五部分：NPC AI

完整设计：

Utility AI。

至少提供：

```text
Eat
Sleep
GatherFood
GatherWood
GatherStone
BuildHouse
Farm
```

的：

```text
Condition
Utility Formula
Target Selection
Execution
State Change
```

---

# 第六部分：资源模拟

详细说明：

```text
Gather
Consume
Regeneration
Storage
Scarcity
```

及公式。

---

# 第七部分：建筑系统

给出：

```text
House
Farm
Storage
```

的：

```text
Cost
Function
Build Condition
World Effect
```

---

# 第八部分：Population Model

详细说明：

```text
Birth
Death
Housing
Food
Population Pressure
```

及反馈环。

---

# 第九部分：Emergent Systems

至少给出 10 个可能自然出现的故事。

例如：

```text
森林繁荣
↓
村庄扩大
↓
森林枯竭
↓
居民迁移
↓
建立新村庄
```

这些故事不能依赖剧情脚本。

---

# 第十部分：Debug System

必须设计：

```text
Agent Debug Inspector
Utility Score Viewer
World Heatmap
Resource Debug
Event Log
Simulation Statistics
```

---

# 第十一部分：MVP Scope

严格给出：

必须做什么。

可以做什么。

暂时不做什么。

防止 Feature Creep。

---

# 第十二部分：Milestone

请把开发拆分成阶段。

推荐：

Milestone 0：

```text
World
Tile
Camera
Time
```

Milestone 1：

```text
NPC
Movement
Pathfinding
```

Milestone 2：

```text
Needs
Food
Gather
Eat
```

Milestone 3：

```text
Wood
Building
House
```

Milestone 4：

```text
Farm
Storage
Population
```

Milestone 5：

```text
Player Tools
Statistics
Save
```

Milestone 6：

```text
Personality
Relationship
History
```

Milestone 7：

```text
Settlements
Migration
```

Milestone 8：

```text
Trade
Civilization
Conflict
```

但你可以进一步细化。

---

# 八十九、每个阶段必须可玩

不要：

开发三个月后才出现第一个 playable build。

每一个 Milestone：

都应该形成一个可以运行、观察、测试的版本。

例如：

Milestone 1：

可以看：

10 个小人随机移动。

Milestone 2：

可以看：

小人寻找食物并避免饿死。

Milestone 3：

可以看：

小人采木并造房。

Milestone 4：

可以看：

一个简单村庄自然形成。

---

# 九十、开发优先级原则

每个新 Feature 加入前判断：

### 是否增加 Emergence？

### 是否增加 Player Agency？

### 是否增加 Observation Value？

如果三者都没有明显提升，

不要优先实现。

---

# 九十一、Observation Value

Simulation Game 中：

让玩家理解发生了什么，

和 Simulation 本身同样重要。

如果 NPC 突然死亡，

玩家必须能够知道：

```text
为什么死亡？
```

例如：

```text
Cause of Death:
Starvation
```

如果村庄崩溃，

玩家应该可以通过：

```text
Population Graph
Food Graph
Event Log
```

理解：

为什么崩溃。

---

# 九十二、Explainable Simulation

所有重大行为最好都有原因。

例如 NPC 决定迁移：

Inspector 显示：

```text
Migration Utility

Food Scarcity:       +0.45

Housing Scarcity:    +0.21

Danger:              +0.10

Nearby Opportunity:  +0.30

Home Attachment:     -0.22

Final Utility:        0.84
```

这既方便玩家，

也极大方便 Debug。

---

# 九十三、核心判断标准

开发过程始终用这一问题检验：

> 如果玩家什么都不做，让世界运行 30 分钟，是否会自然出现值得观察的变化？

如果答案：

No，

说明系统仍然不够有机。

理想状态：

玩家只点击：

```text
Play
```

世界仍然能够经历：

```text
出生
成长
资源增长
资源枯竭
建造
迁移
灾害
恢复
聚落扩张
```

---

# 九十四、第二个判断标准

另一个判断：

> 玩家改变一个条件后，能否通过多个系统传播产生意料之外但合理的后果？

例如：

玩家烧毁森林。

不能只是：

```text
Forest Count ↓
```

而应可能导致：

```text
Forest ↓

↓

Wood ↓

↓

Construction ↓

↓

Housing Shortage ↑

↓

Birth Rate ↓

↓

Population Growth ↓

↓

Migration ↑
```

这才是真正的 Simulation。

---

# 九十五、项目最终愿景

项目最终希望形成这样一种体验：

玩家最开始：

只是创造几个居民。

然后他们：

开始寻找食物。

开始建造住所。

开始形成家庭。

开始组成村庄。

村庄开始扩大。

开始消耗周围资源。

一些居民迁移。

另一个村庄诞生。

两个村庄开始贸易。

资源紧张。

关系恶化。

冲突出现。

灾害发生。

文明衰落。

新的文明从废墟中出现。

而绝大部分：

不是因为游戏设计师提前写好了故事，

而是因为：

大量简单规则相互作用。

这就是整个项目的核心。

---

# 九十六、实现时的重要要求

在后续设计和编码时：

1. 优先简单规则。

2. 避免过度工程。

3. 优先 Data Driven Design。

4. 将 Simulation 与 Rendering 解耦。

5. 将 AI Decision 与 Action Execution 解耦。

6. 将系统参数放入配置文件或 ScriptableObject / Resource。

7. 所有关键数值尽量可实时调整。

8. 设计 Debug Overlay。

9. 设计 deterministic / seeded simulation。

10. 优先验证 Emergent Gameplay，而不是内容数量。

---

# 九十七、如果需要选择游戏引擎

需要分别分析：

```text
Godot
Unity
Unreal Engine
```

从以下方面比较：

```text
2D Sandbox Simulation
Large NPC Count
Pathfinding
UI
Tooling
Data Driven Design
Performance
Development Speed
Learning Cost
Long-term Scalability
```

如果项目定位：

```text
2D

独立开发

几十到几百 NPC

像素 / 简洁风格

Simulation Heavy
```

优先考虑：

Godot 或 Unity。

不要因为画质而默认选择 Unreal。

---

# 九十八、视觉设计原则

第一阶段不要投入大量时间制作美术。

可以使用：

```text
Colored Tile
Simple Sprite
Circle / Square Agent
Simple Icon
```

优先保证：

Simulation 清晰。

例如：

绿色：

Forest

蓝色：

Water

灰色：

Mountain

黄色：

Farm

小人物：

NPC

之后再替换正式美术。

---

# 九十九、代码原则

不要设计巨型类。

例如避免：

```cpp
class Human {

    Move()
    Eat()
    Build()
    Farm()
    Fight()
    Trade()
    Relationship()
    Marriage()
    Politics()
    ...
}
```

应该拆分：

```text
AgentData

NeedSystem

AISystem

MovementSystem

ResourceSystem

BuildingSystem

RelationshipSystem
```

尽量保持：

Data

与

Logic

分离。

---

# 一百、你现在需要首先完成的工作

请不要一次性把整个游戏代码全部生成。

按照以下顺序开始：

## Step 1

给出完整 Game Design Document。

## Step 2

给出系统依赖图。

## Step 3

给出推荐技术栈与引擎选择。

## Step 4

给出工程目录结构。

## Step 5

给出核心数据结构。

## Step 6

给出 Simulation Tick Architecture。

## Step 7

详细实现 Utility AI Design。

## Step 8

给出 MVP Milestones。

## Step 9

从最小可运行版本开始逐模块实现代码。

每完成一个模块，

必须说明：

```text
它解决什么问题？

读取哪些数据？

修改哪些数据？

与其他系统如何连接？

有哪些 Debug 方法？

可能产生什么 Emergent Behavior？
```

禁止未经必要性分析就加入大型新系统。

整个开发过程中始终牢记：

> 少量规则。

> 强系统耦合。

> 高可解释性。

> 高玩家干预自由度。

> 强涌现性。

项目最终目标不是：

“拥有最多功能的游戏”。

而是：

> “拥有最有生命力的世界。”