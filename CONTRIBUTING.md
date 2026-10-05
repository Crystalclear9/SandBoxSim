# 贡献指南

项目通过少量相互连接的规则呈现生态与社会变化。提交新机制时，说明它读取哪些条件、改变哪些状态、付出什么成本，以及玩家如何观察结果。

## 环境与构建

环境设置见 [安装与启动](docs/getting-started.md)，脚本参数见 [构建与运行](docs/build.md)。

```powershell
./tools/build.ps1 -Mode build -Configuration Release -Channel sdk -ParallelBuild
./tools/godot.ps1 -Mode build -Configuration Release
```

Core、Console 和自带测试框架仅使用 BCL；Godot 客户端使用 Godot 的 .NET SDK。不要将图形引擎依赖引入 Core。

## 实现约定

- 模拟中的随机性使用明确的 `RngStream`，不使用 `System.Random`、系统时间或对象哈希决定结果。
- 改变地块通过 `World` 与干预入口，保持资源和空间索引同步；读取、镜头和视觉动画保持只读。
- 影响未来行为的状态进入存档和摘要；客户端会话状态另验证编码与续跑，不只比较核心摘要。
- 区分行动门槛与偏好加分，避免不可执行的动作持续压过采集与生存动作。
- 热路径遍历存活索引，控制搜索次数；保留明确的失败原因和事务性材料扣除。
- 源码使用四空格缩进，公共接口和复杂规则说明原因；PowerShell 脚本保留 UTF-8 BOM。

系统边界见 [项目架构](docs/architecture.md)，配置约定见 [配置](docs/configuration.md)，状态契约见 [存档与确定性](docs/saving.md)。

## 验证改动

按照改动范围运行相应检查。模拟规则改动需验证相关行为、边界、同种子重放和存读档续跑；提交前运行完整回归。图形改动同时运行客户端自检并检查实际画面。脚本改动运行脚本回归与受影响构建通道。

```powershell
./tools/test.ps1 -Configuration Release -Channel sdk
./tools/godot.ps1 -Mode test
./tools/test-build.ps1
git diff --check
```

PR 描述写清问题、变化后的行为与验证结果。玩法变化同步玩家文档；配置、存档或目录变化同步对应说明和链接。日志、工具链、构建输出和个人存档不提交。
