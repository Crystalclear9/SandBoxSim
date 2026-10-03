# runs/ —— 运行产物目录

这个目录存放**模拟产生的数据**，不是源码。默认被 `.gitignore` 忽略（见根目录 `.gitignore`）。

## 为什么保留这个目录

`docs/09-EmergentStories.md` 与 `docs/13-DeterminismAndSave.md` 要求：任何关于
"世界会涌现什么" 的结论都必须**可以被别人重跑复现**。因此每次长跑都会把证据落在这里。

## 目录约定

```text
runs/
├─ .gitkeep                 # 保留空目录（已入库）
├─ README.md                # 本文件（已入库）
└─ <runId>/                 # 每次运行一个目录，runId = <seed>-<days>d-<ticks>t
   ├─ stats.json            # 逐日统计（人口/资源/出生/死亡/迁移/聚落数）
   ├─ events.md             # 重要事件日志（人类可读）
   ├─ report.md             # 单页报告：曲线 + 关键事件 + 验收勾选
   ├─ digest.txt            # 状态摘要（FNV-1a64），用于确定性比对
   └─ World_<seed>_day<NNN>.png   # 世界快照，便于肉眼验证涌现
```

## 使用方式

```powershell
# 100 天 headless 长跑 + 自动产出报告与快照
.\tools\snapshot.ps1 -Seed 839102 -Days 100

# 确定性复现：同一 seed 跑两次，digest 必须完全一致
.\tools\run.ps1 -Mode digest -Seed 839102 -Ticks 144000
```

`runs/` 下的产物**不提交**（体积 + 可复现性），需要留证时用 `git add -f` 显式加入，
并在提交信息里写明复现命令。
