# 配置

当前项目使用模拟规则与图形客户端设置。历史工程数据保留为开发参考，当前客户端不加载。

## 模拟规则

默认文件是 [`config/sim.default.json`](../../config/sim.default.json)，字段定义位于 [`SimConfig.cs`](../../src/SandBoxSim.Core/Foundation/SimConfig.cs)。命令行通过 `-ConfigPath` 指定文件；配置随存档保存，恢复时使用存档中的有效规则。

| 参数类别 | 主要影响 |
|---|---|
| 时间与世界 | tick 日长、地图生成、天气与环境变化 |
| 居民与行动 | 需求增长、决策、行动成本、移动与生存 |
| 资源与库存 | 生长、容量、采集、消耗与存取 |
| 建筑与人口 | 材料、施工、农业、维护、住房与出生 |
| 火灾与动物 | 可燃条件、蔓延、栖息地、捕食与繁殖 |
| 社会与文明 | 聚落、关系、经济、技术、外交与战争 |
| 规则开关 | 死亡、出生、衰老、资源恢复、和平等辅助规则 |

加载器对未知字段、缺失字段和类型错误返回提示；缺失或无法解析的值保留代码默认值。`ConfigTests.DefaultsMatchJson` 检查显式 JSON 默认值与代码一致。改变配置会改变演化结果，比较策略时使用相同规则和种子。

`buildings.organicHousing` 控制住房的自然选址：考虑邻近建筑、道路和确定性位置差异。代码默认值为 false，新建图形世界设为 true；玩家可在世界设置切换，已有存档保存自己的值。此项在默认 JSON 中缺省，沿用代码默认值。

## 图形客户端

[`project.godot`](../../src/SandBoxSim.Godot/project.godot) 定义主场景、窗口、C# 程序集与 GL Compatibility 渲染器。镜头与界面读取模拟状态；视觉比例、材质和动画不改变经济或人口规则。

引擎位置使用 `GODOT_EXE` 或脚本的 `-GodotPath`；SDK 位置使用 `SANDBOXSIM_DOTNET_ROOT`。安装文件放在仓库外，详见 [安装与启动](../guides/getting-started.md)。

## 历史数据

`assets/gameplay/projects.json` 与旧工程插画保留来源记录，当前客户端不加载这些数据。自由世界的自然地点规则在 `Core/Systems/WildPlaces.cs`，扩展方式见 [世界内容扩展](extending.md)。

## 状态值与固定表现参数

`FootTraffic` 属于存档中的逐格状态，不是默认 JSON 配置项。脚步压力、恢复比例等当前在世界与模拟系统代码中定义；草丛的距离缩退、河岸插值和光照参数属于 Godot 表现层。调整这些值时分别更新状态规则或视觉说明，不在 `sim.default.json` 中添加无人读取的字段。

本机界面动态偏好保存为 `user://interface.cfg`，不进入核心摘要；API 令牌通过环境变量提供，也不写入配置或评测产物。配置文件位置见 [config](../../config/README.md)。

---

[文档导航](../README.md) · [项目首页](../../README.md)
