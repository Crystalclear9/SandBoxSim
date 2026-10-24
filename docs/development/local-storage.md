# 本地环境与运行目录

SandBoxSim 的源码、运行资源与正式文档由 Git 管理；开发工具、构建缓存和独立游戏包位于项目本地目录，不进入源码提交。默认安装方式不需要更改系统 SDK 或引擎安装。

| 目录 | 项目中的用途 |
|---|---|
| `.tools/net8/` | .NET 8 SDK 和运行时 |
| `.tools/godot-4.7.2/` | Godot .NET 编辑器、便携配置及导出模板 |
| `.tools/blender-4.5.14/` | 可选 Blender，供离线素材转换使用 |
| `.tools/nuget-packages/` | 项目依赖包缓存 |
| `.tools/sources/` | 来源网格、原图、许可及转换器需要的输入 |
| `.tools/tmp/` | 构建、素材转换与开发采样使用的工作目录 |
| `artifacts/game/windows/` | Windows 独立游戏 EXE 与对应数据目录 |
| `artifacts/Debug/`、`artifacts/Release/` | 控制台与内核构建产物 |
| `src/SandBoxSim.Godot/.godot/` | 编辑器索引、导入纹理与 C# 缓存 |
| `saves/` | 项目内保存的世界与会话 |
| `runs/` | 用户主动执行的实验输出与评测产物 |
| `docs/images/` | 项目说明所引用的正式实机配图 |

游戏通过 Godot 的 `user://` 保存界面偏好；实际位置由 `OS.GetUserDataDir()` 根据平台解析。世界和会话可通过游戏中的保存／载入入口管理，格式与兼容规则见[存档](saving.md)。独立包不依赖 Blender、源码或 `.tools/` 才能运行，具体构建方式见[构建与运行](build.md)。

`-ReimportTextures` 会通过编辑器插件重建实际 mip 链，避免导入配置与缓存不一致；正式导出也执行该检查。通常运行不进行离线建模，只有重建素材时才使用 `.tools/sources/` 与 Blender。

[目录职责](repository-layout.md) · [开发指南](developer-guide.md) · [文档导航](../README.md)
