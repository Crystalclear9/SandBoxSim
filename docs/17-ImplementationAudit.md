# 原计划实现核验 · 2026-10-04

## 结论与范围

项目已超出此前 M3 版本：M0–M4 核心、M5 核心工具已存在；M6、M7、M8 均为部分实现。
目前是 C#/.NET 的 TUI 模拟原型，有 PNG 快照，并非已交付 Godot/Unity 图形客户端。
不能把“有类名、有统计字段、有设计文档”直接算成完成，也不能给长期计划一个可靠的单一完成百分比。

初始核验基线为 `dcf7c06` 及当时工作区已有的两处建造物流修改（ActionSystem、BuildActions）。
其后仓库在 `48b820a`、`c06de65` 接入审计修复，并回退实验性建造物流修改；最终验证以 `c06de65` 加本次取粮边界修复为准。
不扩展后续文明系统。以源码调用链、默认回归、显式开启的验收与实际运行结果为依据。

## 对照任务书

| 任务书模块 | 实际实现及证据 | 结论 / 尚缺内容 |
|---|---|---|
| Step 1–8 设计输出 | docs/01–09、12–15 覆盖愿景、架构、数据、AI、资源、人口、故事、里程碑；README 有工程布局 | 文档已有，部分历史陈旧；设计不是运行验收 |
| 世界、Tile、地形、seed | World、WorldGenerator、Tile、ChunkGrid、Weather；WorldTests | 核心实现 |
| 时钟、分层 Tick、渲染分离 | Simulation、Calendar、GameLoop；Core 不依赖 Console | 核心实现；不是图形引擎/帧率完整验收 |
| 需求、局部搜索、Utility AI、A* | NeedsSystem、AiSystem、ActionRegistry、AStarPathfinder；AgentTests | 实现，含效用分解和失败重选 |
| 资源与物流 | ResourceSystem、GroundStockStore、StorageStore、采集/存取动作 | 实现；本次修复仓库容量和取粮链路，建造物流仍需平衡 |
| House/Farm/Storage | BuildingStore、BuildingSystem、BuildActions、FarmAction | 实现；本次修复农田维护与库存溢出；Mine 不等于完整矿业链 |
| 出生、死亡、住房、儿童 | BirthSystem、NeedsSystem、AgentStore；M4Tests | 核心实现，有床位与食物约束 |
| 玩家干预、火灾、规则 | Simulation.Intervene*、FireSystem、ToolPalette；InterventionTests、M5Tests | 核心实现；自然火规模、长期气候平衡仍有待打磨项 |
| 保存、恢复与随机状态 | SaveFile、SaveLoader、分流 RNG；SaveLoadTests、M6 续跑测试 | 已实现且有回归；不承诺任意版本存档互通 |
| 性格、关系、社会行为 | RelationshipStore、SocialActions；M6Tests | 已有社交、分享、逃跑、攻击、关系衰减；完整个人时间线、家庭称谓等未交付 |
| 聚落与迁移 | SettlementStore 空间聚类、持续性判定、成立/解散；MigrationSystem | 部分实现；领土、迁入归属和解释链未完成，多种子验收单独看结果 |
| 贸易、文明、战争 | TradeSystem 算全图价格；ConflictSystem 算个体冲突压力，接 Attack | 仅部分机制；没有实际聚落交易/路线/外交/文明实体/正式战争与联盟 |
| 观察、统计、历史 | TUI 检查器、叠加层、EventLog、RunReport、每日统计、PNG | 基础观察实现；不是完整个人/聚落历史 UI |
| 性能、发布、终极体验 | 三平台 CI 与测试脚本已存在 | 尚无本次 300 Agent ≥30 FPS、500 天文明场景、30 分钟可玩性与 v1.0 发布验收 |

## 本次复现并修复

1. **仓库完工没有容量**：真实 TickFast 完工后容量仍为 0，存入失败。现按配方启用容量，同步扩容和清理复用槽位。
2. **仓库粮食无法取回**：Take 只查地面堆。现可局部选中有库存的已完工仓库，在目标格取料，转移前后总量守恒。
3. **耕种的农田仍衰败**：产出清除劳动量后才判断维护。现先依据当天劳动判断维护，再结算产出/清空劳动。
4. **满仓丢失农田产出**：忽略 Deposit 返回值。现将仓库未接收部分送到农田地面堆；地面容量仍受配置上限约束。
5. **倒塌仓库残留幽灵库存**：拆除未清 Storage，出生/价格仍可能统计它。现衰减拆除时清理库存和容量。
6. **重建世界沿用旧观测量**：新增系统累计值、小时/天计数、每日采样未归零。现统一重置出生、农业、火灾、价格、冲突等观测值。
7. **NaN 可通过浮点断言**：旧 Near/InRange 会误报成功。现显式拒绝 NaN，Near 同时校验有限值及容差。
8. **未执行验收被记作通过**：环境变量关闭时直接 return。现输出“跳过”并独立计数；M5/M6 正式验收恢复默认执行。
9. **攻击验收场景误删水源**：测试把每格改为草地后居民先脱水死亡。改用已有的保留水源的不平等场景，继续要求自然出现攻击，不预设敌意。
10. **多聚落验收统计错误**：历史累计成立两个不证明同时存在。现逐日记录活跃聚落峰值，要求至少两个同时活跃聚落。
11. **取粮目标失效后隔空扣除另一仓库存粮**：执行阶段重新查最近仓库，会从非目标建筑拿粮。现只允许从当前目标格的已完工仓库取料；目标消失时失败并重新决策。
12. **PNG 解压器空值处理仍解引用原参数**：构造器已将空输入归一为空数组，但分配输出缓冲时仍读取原参数。现统一使用归一后的输入，消除 CS8602 警告。

AuditRegressionTests 最初 6 项在修复前 0/6、修复后 6/6；另补仓库取粮全链路和失效目标用例，最终 8/8 通过。
未降低原 M5/M6 的结果断言；攻击测试修正的是失效前提，未靠取消死亡或强制制造仇恨获得通过。

## 验收记录

日志集中在 `runs/audit-*.log`，按仓库约定不提交生成产物。
基线显示 258/258，但其中 11 项未执行，被旧运行器误算为通过。
修复后首轮默认回归：264 项，253 通过、0 失败、11 显式跳过；随后恢复 M5/M6 验收并新增取粮用例。

| 检查 | 结果 | 日志 / 范围 |
|---|---|---|
| 最终 SDK Release 默认回归 | 266 项：260 通过、0 失败、6 跳过 | `audit-final-sdk-release.log`，包含第 11 项修复 |
| 最终 csc Release 默认回归 | 266 项：260 通过、0 失败、6 跳过 | `audit-final-csc-complete.log`，包含全部代码修复 |
| 最终缺陷回归，两条通道 | 各 8/8 通过 | `audit-final-target-release.log`、`audit-final-target-csc.log` |
| 构建脚本回归 | 12/12 通过 | `audit-build-script-final.log` |
| 渲染回归 | 8/8 通过 | `audit-renderer-final.log` |
| GitHub 三平台 CI | Windows、macOS、Ubuntu 全部成功；包含确定性、100 天冒烟与多种子长跑 | 代码提交 `f73c2e7`，[CI #9](https://github.com/Crystalclear9/SandBoxSim/actions/runs/37191723790) |
| 用户报错的 SDK 并行构建命令 | 本地成功，0 错误、0 警告；三平台同命令亦通过 CI | `audit-requested-build.log`，Release、sdk、ParallelBuild |
| 初始快照 M7：20 种子 × 200 天 | 11/20 在 150 天内形成聚落；7/20 曾同时存在至少两个活跃聚落 | `audit-m7-acceptance.log`，对应初始核验快照，形成率低于 15/20 门槛 |
| 最新代码 M7：20 种子 × 200 天 | **验收失败**：形成率 11/20，要求至少 15/20；同时多聚落 7/20，数量达到至少 5/20 | `audit-m7-final-release.log`，1287.35 秒，核心代码对应 `f73c2e7` |

6 项默认跳过是显式门控的批量/诊断测试，不构成这些项已验收通过的证明。
最新批量实测与初始快照一致，累计成立 19 个聚落，但不能将累计成立数量当作同时存在数量。
用例在形成率断言处失败，后续多聚落断言未执行；7/20 来自逐日峰值的完整统计。
因此，默认回归通过和三平台 CI 成功不证明 M7 已完成。当前缺口属于聚落形成可靠性与未交付的里程碑功能，
本次没有通过调整门槛、玩法参数或增开发功能来消除验收失败。

复核命令（PowerShell，仓库根目录）：

```powershell
./tools/test-build.ps1
./tools/build.ps1 -Mode test -Configuration Release -Channel sdk
./tools/build.ps1 -Mode test -Configuration Release -Channel csc
./tools/test.ps1 -Configuration Release -Channel sdk -Filter AuditRegression
$env:SBOX_SIM_BATCH = '1'
./tools/test.ps1 -Configuration Release -Channel sdk -Filter TwentySeedsFormSettlements
Remove-Item Env:SBOX_SIM_BATCH
```

批量验收使用默认 20 个种子、每种子 200 天，世界 100×100、初始 40 人；
`SBOX_SIM_BATCH_SEEDS` / `SBOX_SIM_BATCH_DAYS` 缩小规模仅用于诊断，不能替代完整验收。
请勿同时重建同一通道和配置下正在运行的程序集，Windows 会因文件被运行进程占用而拒绝复制。

## 后续优先级

优先复核聚落形成可靠性、存取与建造因果链、长期气候和火灾平衡。
之后再完成个人时间线、聚落归属与领土；实际贸易/文明系统应作为独立里程碑。
本次修复不代表这些未实现功能已完成。
