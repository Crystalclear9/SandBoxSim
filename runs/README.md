# 本机运行输出

除本说明与 `.gitkeep` 外均忽略提交。日常清理后，本目录只保留需要继续使用的世界存档，放在 `saves/`；不长期堆积文件清点、审计报告、旧日志、临时截图、试验压缩包和重复 Git 备份。

模拟和评测工具会按实际请求创建输出目录。需要用于研究或复现的完整实验由使用者明确选择保留；不再使用的开发试验可整体清理，清理本身不生成新的报告或归档。

```powershell
./tools/run.ps1 -Mode snapshot -Seed 839102 -Days 30 -Agents 40 -SnapshotDays 5 -OutDir runs/experiments/river
./tools/run.ps1 -Mode digest -Seed 839102 -Days 30 -Agents 40
```

正式文档配图位于 `docs/images/` 与 `docs/archive/images/`，游戏素材位于客户端 `assets/`，可运行成品位于 `artifacts/game/`，这些不属于临时记录。个人存档由保存对话框决定位置，清理时保留原档。

[构建与运行](../docs/development/build.md) · [目录职责](../docs/development/repository-layout.md)
