# 运行输出

此目录存放本机模拟结果与日志。除说明文件外，内容均忽略提交。

`tools/run.ps1` 的 headless、snapshot 和 batch 模式按运行参数建立结果目录，包含逐日统计、世界事件、模拟记录、状态摘要和地图快照。通过 `-OutDir` 可以指定输出位置。

```powershell
./tools/run.ps1 -Mode snapshot -Seed 839102 -Days 30 -Agents 40 -SnapshotDays 5
./tools/run.ps1 -Mode digest -Seed 839102 -Days 30 -Agents 40
```

建议把人工收集的日志放在 `runs/logs/`，专题运行放在独立子目录。玩家存档由保存对话框选定位置，不需要放在这里。目录可以保留或备份；不影响源码与构建。参数说明见 [构建与运行](../docs/build.md)。
