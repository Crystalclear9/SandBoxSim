# 贡献指南

项目通过相互连接的规则呈现生态与社会变化。提交新机制时，说明它读取什么条件、改变什么状态、付出什么成本，以及玩家怎样观察结果。开发入口见 [开发指南](docs/development/developer-guide.md)，接入方法见 [扩展指南](docs/development/extending.md)。

## 开发约定

- Core 仅依赖 BCL，保持独立于 Godot；Console 与测试支持 SDK/csc 两条构建通道。
- 随机性使用明确的内核随机流，模拟规则不使用系统时间、`System.Random` 或对象哈希。
- 改变地块和资源通过正式状态入口，保持索引、容量与事件一致；观察、镜头和展示动画保持只读。
- 影响未来行为的状态进入存档与摘要；图形会话另检查工程、试炼和蓝图的编码及续跑。
- 区分行动门槛和偏好，控制热路径遍历与搜索次数；材料操作保持事务性，失败原因可观察。
- 源码使用四空格缩进；PowerShell 脚本保留 UTF-8 BOM。移动 Godot 文件时维护 `.uid`、`.import` 和 `res://` 引用。

## 验证与提交

依据改动范围选择 [开发指南中的检查](docs/development/developer-guide.md#按改动选择检查)。模拟规则提交前运行完整回归；图形修改增加实机检查；脚本修改运行脚本回归。所有文档改动运行：

```powershell
./tools/check-docs.ps1
git diff --check
git status --short
```

PR 描述先说明具体问题与变化后的行为，再说明实际运行的检查和限制。使用、配置或存档变化同步对应文档，并在 [CHANGELOG](CHANGELOG.md) 写清更新内容及影响。

文档属于 `docs/guides/` 或 `docs/development/`；历史资料留在 `docs/archive/`。保留正式配图，临时截图放在 `runs/screenshots/`，日志放在 `runs/logs/`。个人存档、SDK、引擎和构建缓存不提交，详细边界见 [文件管理](docs/development/repository-layout.md)。
