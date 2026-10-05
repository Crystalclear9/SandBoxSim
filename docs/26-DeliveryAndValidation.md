# 本次交付、构建与验证
日期：2026-10-05。范围：现场 UI、居民关注与改名、三天土地工程、风险定位、相关存档与文档。

## 阅读入口与文件
- [玩家手册](24-PlayerHandbook.md)：玩法、操作、工程、试炼与存档。
- [开发指南](25-DeveloperGuide.md)：架构、状态边界、扩展和测试契约。
- [界面迭代](27-InterfaceIteration.md)：交互理由与实际游戏截图。
- [文档索引](00-Index.md)：全部设计资料。
- [原始任务书验收清单](20-FullDeliveryChecklist.md)：全部原始要求的状态。

src 为源码；tools 为安装、构建、测试脚本；docs/images 为实际截图；docs 为文档。runs 为本机验收输出，不提交大体积临时日志。工具和引擎安装在仓库外，不纳入 Git。

## 开发环境
使用 PowerShell 7、.NET 8 SDK 和 Godot 4.7.2 .NET。内核不要求 Godot。安装和启动：

    ./tools/install-sdk.ps1
    ./tools/setup-godot.ps1
    ./tools/godot.ps1 -Mode run

setup-godot 输出引擎文件路径；Linux/macOS 需要将该路径提供给 GODOT_EXE 环境变量或 godot.ps1 的 GodotPath 参数。Windows 已配置环境可双击 Play-3D.cmd。普通版 Godot 不包含 C# 运行环境。

## 验证命令
在仓库根目录运行：

    ./tools/build.ps1 -Mode build -Configuration Release -Channel sdk -ParallelBuild
    ./tools/build.ps1 -Mode test -Configuration Release -Channel sdk
    ./tools/build.ps1 -Mode build -Configuration Debug -Channel csc
    ./tools/test-build.ps1
    ./tools/godot.ps1 -Mode build -Configuration Release
    ./tools/godot.ps1 -Mode test

如需筛选测试，直接调用生成的 Tests DLL 并传入运行器支持的筛选参数；构建脚本没有任意筛选参数。CI 覆盖 Windows、Ubuntu、macOS 的内核与 Godot 导入、自检，另有 csc 和确定性检查。

## 本地验收结果
完整 Release 回归：320/320 通过，失败 0、跳过 0，总耗时 648.45 秒。随后修改不适用土地判断并增加专项用例，Debug 和最终 Release 工程测试均为 9/9 通过；组合覆盖 321 个不同用例。这不是一次性运行全部 321 个用例的结果。

工程专项覆盖安排不提前生效、三天推进、报告冻结、保护建筑与水域、湿地与防火效果、取消、上限、只读观察、读档续跑及无适用土地拒绝。Godot 最终无头自检通过，包括小地图坐标、关注和改名、工程落点、客户端存档、危机标记、镜头、矩形地图、三个窗口布局与只读摘要。

实际 Windows 游戏窗口，300 名居民、4 倍速、30.0068 秒：平均 88.38 FPS，结束时 300 名居民、1200 tick。数据来自 operations-fps.json；与完整测试并行运行，受当前设备和负载影响。它不是所有地图、速度和危机条件下的性能保证。设备为 RTX 4060 Laptop，OpenGL Compatibility。

验收日志位于 runs/full-delivery/all-tests-operations.log、project-tests-final.log、operations-client-final.log 和 operations-fps.json。实际截图收录在 [界面迭代](27-InterfaceIteration.md)。

最终 SDK Release 与 csc Debug 构建通过，构建脚本回归 12/12 通过，文档相对链接检查与 Git 空白检查通过。实际窗口检查包括 1440 × 900 和 1280 × 800。

## 尚未完成的原始验收
原始 200 人 / 100 日性能样本约 24.89 秒，尚未达成 10 秒目标；本次 FPS 不替代该基准。三平台独立发布包、完整 30 分钟稳定性验收以及原始 100 节逐项最终核验仍待完成。程序生成地形与建筑表现仍有美术改进空间。

本次上传至 codex/full-requirements 并更新现有草稿 PR，未自动合并主分支。最新 CI 状态以 GitHub 为准，不能用本地 Windows 检查替代远程三平台结果。
