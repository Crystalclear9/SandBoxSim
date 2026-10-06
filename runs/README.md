# 运行输出

此目录保存本机模拟结果、日志和临时画面。除本说明与 `.gitkeep` 外均忽略提交；被忽略的数据仍可能包含需要保留的世界和实验。

| 子目录 | 用途 |
|---|---|
| `logs/` | 人工保存的故障、构建与性能日志 |
| `screenshots/` | 运行期间查看或调试用的临时截图 |
| 自定义实验目录 | 模拟输出、存档、统计、事件、摘要与地图快照 |

`tools/run.ps1` 的 headless、snapshot 和 batch 模式按参数建立结果目录，通过 `-OutDir` 可指定位置。例如：

```powershell
./tools/run.ps1 -Mode snapshot -Seed 839102 -Days 30 -Agents 40 -SnapshotDays 5 -OutDir runs/experiments/river
./tools/run.ps1 -Mode digest -Seed 839102 -Days 30 -Agents 40
```

清理临时截图时只删除确认用途的文件；不清空整个 `runs/`，也不删除日志、个人存档或实验数据。正式文档配图位于 `docs/images/` 和 `docs/archive/images/`，游戏图片位于客户端 `assets/`，均随项目保留。

命令参数见 [构建与运行](../docs/development/build.md)，目录边界见 [文件管理](../docs/development/repository-layout.md)。
