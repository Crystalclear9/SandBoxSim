# 本机运行输出

除本说明与 `.gitkeep` 外均忽略提交。这里保留模拟输出、日志、截图、实验证据和备份；被忽略不意味着可以删除。

| 子目录 | 用途 |
|---|---|
| `logs/` | 新的故障、构建和性能日志；历史根目录日志集中在 `logs/legacy/` |
| `requests/legacy/` | 历史本地评测请求 JSON |
| `reports/legacy/` | 历史本地采样报告和预测 JSON |
| `screenshots/` | 运行中查看的临时截图与录制帧 |
| `evaluation/` | 完整实验目录：请求、结果、图片、答案、日志和溯源 |
| `experiments/` 或独立实验目录 | 模拟统计、存档、摘要与地图快照 |
| `backups/`、`git-archive/` | 源码临时备份与 Git bundle，保留恢复用途 |
| `maintenance/` | 本机目录清点、移动清单和整理检查 |

历史模拟目录按原路径保留，完整评测目录不拆分，以免破坏哈希与相对引用。目录整理只移动根目录的散落日志、独立 JSON 与文本备份，移动清单记录相对路径和 SHA-256；不删除存档、截图、实验或恢复文件。

```powershell
./tools/run.ps1 -Mode snapshot -Seed 839102 -Days 30 -Agents 40 -SnapshotDays 5 -OutDir runs/experiments/river
./tools/run.ps1 -Mode digest -Seed 839102 -Days 30 -Agents 40
```

正式配图位于 `docs/images/` 与 `docs/archive/images/`，游戏素材位于客户端 `assets/`。个人存档由保存对话框决定位置，更新时保留原档。参数见 [构建与运行](../docs/development/build.md)，管理规则见 [文件职责](../docs/development/repository-layout.md)。
