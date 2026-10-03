# 10 — 当前观察与调试工具

本文对应 M0–M3 已实现功能。完整鼠标检查器、个人时间线和路径可视化仍需后续开发。

## 交互观察

运行 `pwsh -File ./tools/run.ps1 -Agents 30`。方向键/WASD 平移，+/− 缩放，
Space 暂停，0/1/2/4/8 调整速度，O 切换叠加层，H/F1 查看帮助，Enter 检查视野中心。
渲染在 Console 项目，模拟状态在 Core；观察不消耗模拟随机数。

## 用报告解释变化

```powershell
pwsh -File ./tools/run.ps1 -Mode snapshot -Seed 839102 -Days 100 -Agents 40 -SnapshotDays 25 -OutDir runs/experiment
pwsh -File ./tools/test.ps1 -Filter EconomyDiagnostics
pwsh -File ./tools/test.ps1 -Filter BuildingDiagnostics
```

报告、每日统计、事件与 PNG 快照写入指定输出目录。先看人口和资源曲线，再用事件查死因、迁移和建造。
当前无出生机制，人口归零不等于程序崩溃；农田建筑完成也不代表已实现农业生产。

| 症状 | 检查入口 | 可能原因 |
|---|---|---|
| 持续饥饿却不采食物 | Utility 分数、动作选择统计 | 工作效用压过生存效用 |
| 有木材却不建房 | 材料门槛、选址规则、建筑诊断 | 材料不可达或选址失败 |
| 来回存取物资 | 地面库存与携带阈值 | 存放/取回阈值间隔太小 |
| 相同实验结果不同 | digest 模式 | RNG 消耗顺序、配置或调度顺序变化 |
| NaN/负库存 | `Simulation.ValidateInvariants` | 状态写入错误；现有校验主要覆盖 Tile |

## 确定性验证

`pwsh -File ./tools/run.ps1 -Mode digest -Seed 4242 -Ticks 43200 -Agents 30`
会运行两次相同 seed 并比较摘要，再检查不同 seed 的结果。
它验证重复运行，不等价于保存/读取完整模拟状态。存档尚未实现。
