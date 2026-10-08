# 模拟配置

[sim.default.json](sim.default.json) 是命令行模拟的显式默认参数；字段与代码默认值定义于 [SimConfig.cs](../src/SandBoxSim.Core/Foundation/SimConfig.cs)。

```powershell
./tools/run.ps1 -Mode headless -Agents 40 -Days 30 -ConfigPath config/sim.default.json
```

已有存档保存自己的有效规则，载入后不会自动换成此文件的新默认值。图形新世界可设置客户端起始条件，例如开启自然住房布局；它与命令行默认值分开。

配置中不保存工具链路径、API 令牌或个人设置。Godot 项目设置属于 `project.godot`，界面动态偏好位于本机 `user://interface.cfg`，历史工程 JSON 属于客户端保留资源。

字段语义和覆盖规则见 [配置说明](../docs/development/configuration.md)，有效状态的保存见 [存档说明](../docs/development/saving.md)，环境参数见 [安装与启动](../docs/guides/getting-started.md)。
