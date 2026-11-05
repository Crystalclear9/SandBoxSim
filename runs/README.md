# 本机运行输出

除本说明与 `.gitkeep` 外均忽略提交。`runs/saves/` 可保存需要继续使用的世界与会话，`runs/experiments/` 和 `runs/evaluation/` 可保留有复现价值的完整实验。一次性开发截图、脚本和日志放在 `.tools/tmp/<主题>/`。

模拟和评测工具会按实际请求创建输出目录。需要用于研究或复现的完整实验由使用者明确选择保留；不再使用的开发试验可整体清理，清理本身不生成新的报告或归档。

```powershell
./tools/run.ps1 -Mode snapshot -Seed 839102 -Days 30 -Agents 40 -SnapshotDays 5 -OutDir runs/experiments/river
./tools/run.ps1 -Mode digest -Seed 839102 -Days 30 -Agents 40
```

正式文档配图位于 `docs/images/`，游戏素材位于客户端 `assets/`，可运行成品位于 `artifacts/game/`，这些不属于临时记录。个人存档由保存对话框决定位置，清理时保留原档。

[构建与运行](../docs/development/build.md) · [目录职责](../docs/development/repository-layout.md)
