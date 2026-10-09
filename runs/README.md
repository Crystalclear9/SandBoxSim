# 本机运行输出

除本说明与 `.gitkeep` 外均忽略提交。这里保留模拟输出、日志、截图、实验证据和备份；被忽略不意味着可以删除。

| 子目录 | 用途 |
|---|---|
| `logs/` | 故障、构建和性能日志；历史根目录日志在 `logs/legacy/`，维护检查日志按 `logs/maintenance/<日期>/` 分类 |
| `requests/` | 独立评测请求；历史本地请求在 `requests/legacy/` |
| `reports/` | 独立 HTML 与 JSON 报告；历史单文件报告在 `reports/legacy/` |
| `screenshots/` | 运行中查看的临时截图与录制帧 |
| `archives/` | 完整实验与小型维护记录的压缩归档；保留原相对路径 |
| `evaluation/` | 完整实验目录：请求、结果、图片、答案、日志和溯源 |
| `experiments/` 或独立实验目录 | 模拟统计、存档、摘要与地图快照 |
| `backups/`、`git-archive/` | 源码临时备份与 Git bundle，保留恢复用途 |
| `maintenance/<日期>/` | 目录清点、移动清单、研究记录、配图记录与历史操作文本 |

历史模拟目录按原路径保留，完整评测目录不拆分，以免破坏哈希与相对引用。存储维护可将完整实验压缩归档，核对内容后移除原目录；恢复到仓库根目录后仍使用原路径。散落日志、独立 JSON、文本备份和维护记录按职责归档，移动清单记录相对路径和 SHA-256。临时截图与素材下载副本按明确清单清理，玩家存档和恢复备份保留；规则见 [本机存储维护](../docs/development/local-storage.md)。

2026-10-08 的维护归档分为 `inventory/`、`research/`、`visual/`、`tool-output/` 与 `github/`。其中 `github/` 只保留过去的操作记录，整理目录不会创建、更新或关闭 PR。本次 49 个文件的路径变更记录在 `maintenance/2026-10-08/file-moves.json`，文件内容保持原样；旧清单中的路径描述的是生成时的状态，查找现位置使用移动清单。

```powershell
./tools/run.ps1 -Mode snapshot -Seed 839102 -Days 30 -Agents 40 -SnapshotDays 5 -OutDir runs/experiments/river
./tools/run.ps1 -Mode digest -Seed 839102 -Days 30 -Agents 40
```

正式配图位于 `docs/images/` 与 `docs/archive/images/`，游戏素材位于客户端 `assets/`。个人存档由保存对话框决定位置，更新时保留原档。参数见 [构建与运行](../docs/development/build.md)，管理规则见 [文件职责](../docs/development/repository-layout.md)。
