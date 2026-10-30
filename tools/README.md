# 工具入口

从仓库根目录运行。PowerShell 脚本使用 PowerShell 7；Python 控制与评测工具使用 Python 3.10+ 标准库；模型导出脚本在 Blender 自带 Python 中执行。子目录位置固定，命令中的 `tools/` 路径与 CI 保持一致。

## 安装、运行与构建

| 文件 | 用途 |
|---|---|
| `install-sdk.ps1` | 安装 .NET 8 SDK 到 `.tools/net8/`，下载临时文件也留在 `.tools/` |
| `setup-godot.ps1` | 按平台安装并校验 Godot 4.7.2 .NET 编辑器到 `.tools/`，启用便携模式 |
| `local_environment.py` | Python 启动器共用的项目内依赖、CLI 状态与临时路径；Godot 子进程的独立用户目录策略 |
| `test_local_environment.py` / `test-godot-runtime.ps1` | 缓存环境不影响父进程、平台路径、便携编辑器证书配置范围、私人配置保留和证书完整性 |
| `build.ps1` / `build-lib.ps1` | Core、Console、Tests 的 SDK/csc 构建与执行 |
| `godot.ps1` / `godot-lib.ps1` | Godot 构建、导入、运行与自检；`-ReimportTextures` 通过编辑器插件重建并核验实际 mip 链 |
| `test-godot-manifest.ps1` | 检查 NuGet.config 规范大小写与包源配置变化对构建身份的影响 |
| `export-game.ps1` / `test-godot-export.ps1` | Windows 项目本地模板导出、零退出码错误防护、成品自检后更新与日志判定回归；同时验证运行自检不能被脚本／着色器错误或缺失完成标记蒙混通过 |
| `bake-models.ps1` | 重建人体解剖手部与保留的程序动物校准网格；不是当前授权动物导出入口 |
| `export_authored_animals.py` | 在项目本地 Blender 中核对源哈希、转换 CC0 鹿 / CC BY-SA 狼、转移鹿 14 / 狼 15 关节权重并导出运行 UV 表面；用法与许可见 [模型系统](../docs/development/models.md) |
| `bake-garments.ps1` / `garment_pattern.py` / `bake_garment_surfaces.py` | 导出三种衣片采样，共顶点生成袖片与左右权重，在项目本地 Blender 中统一面朝向并校验成品/源哈希 |
| `export_upper_arm_support.py` | 从核验过的 MakeHuman CC0 肩肘关节及基础网格重建男女上臂截面支撑；对应 `test_upper_arm_support.py` 检查来源一致性与截面有效性 |
| `export_human_torso.py` | 从已校验的 MakeHuman CC0 人体离线采样支撑体，支持 `--frame male/female`；默认 `--region chest` 保留胸肩导出契约，`--region trunk` 生成含腰背与骨盆的 113×128 平面截面包络；固定 LF 输出；用法见 [人物素材](../docs/development/human-assets.md) |
| `export_resident_trousers.py` / `test_resident_trousers.py` | 从项目本地 MakeHuman CC0 服装辅助几何分别导出连续男女裤装，拟合骨盆／膝部、去除离散片并输出五骨权重；验证来源、连通性和开放边界，见 [模型系统](../docs/development/models.md) |
| `fetch-surface-materials.py` | 下载并校验八种 CC0 扫描表面；`--verify` 离线核对运行贴图大小与 SHA-256，源文件缓存仅放 `.tools/` |
| `export_human_forearm.py` / `export_human_hands.py` / `join_human_arm.py` / `export_hand_lod.py` / `bake_animal_surfaces.py` | 同源肘/前臂截取、掌指重定向、近景腕环连接、同源手部减面、腕口贴合、旧程序动物校准表面的体素缝合与蒙皮转移；由 Blender 执行 |
| `run.ps1` | TUI、headless、digest、snapshot 和 batch 模式 |
| `test.ps1` | 自带测试运行器与名称过滤 |
| `dev.ps1` | 内核构建、测试与可选 TUI 开发流程 |
| `check-docs.ps1` | 本地 Markdown 文件链接与大小写检查 |
| `test-build.ps1` / `test-godot-path.ps1` | 构建探测与平台引擎路径回归 |

```powershell
./tools/build.ps1 -Mode build -Configuration Release -Channel sdk -ParallelBuild
./tools/godot.ps1 -Mode run
./tools/check-docs.ps1
```

SDK 与 Godot 工具链由参数或环境变量覆盖，不提交本机绝对路径。详细参数见 [构建与运行](../docs/development/build.md)。

## 模型素材转换

`verify_skin_colour.py` / `verify_skin_colour.gd` 在实际 Compatibility 图形设备上读取生产头脸与远景着色器，用已知颜色纹理、染色、曝光及远景顶点色检查 GPU 像素。需要先构建 Debug 客户端和可用图形显示，不能替换为无头渲染。运行 `python tools/verify_skin_colour.py --output .tools/tmp/skin-colour`；输出目录必须位于项目 `.tools/tmp` 内且为空，日志仅写入该目录，不生成截图。成功输出 `SKIN_COLOUR_RENDER_PASS`，通道误差门槛为 0.008。此检查验证颜色运算，不评判美感或证明所有图形后端表现一致。

`measure_head_tones.py` 使用 Pillow，只读三套已固定的头脸 JSON 与颜色图，输出额部三角形面积加权的线性 RGB、亮度及相对曝光增益。用于复核运行时皮肤、嘴唇和头部 LOD 的来源曝光校准；不修改原图、不下载素材，也不生成临时图片。

`fetch-human-assets.py` 将 23 个固定 CC0 输入校验并下载到 `.tools/sources/makehuman`，通过 HTTP Range 仅提取需要的 ZIP 成员。`export_resident_faces.py` 在 Blender 后台重建头颈、原生 UV、皮肤／发片、闭眼形变与远景头脸；旧头部导出入口转发到新流程。`human_eyelids.py` 采样实际眼裂，`human_head_lod.py` 生成同源远景头脸。普通构建不需要 Blender 或联网，见 [人物素材](../docs/development/human-assets.md)。

`export_environment_models.py` 在 Blender 后台转换六种 CC0 环境模型，按完整叶片抽样并独立简化木质表面。来源、原文件哈希、贴图及运行路径见 [环境素材](../docs/development/environment-assets.md)。

## 实验与接口

| 文件 | 用途与依赖 |
|---|---|
| `online.py` | 启动已构建的 Console HTTP 服务，以及固定步进 Python 客户端 |
| `evaluate.py` | Godot 场景运行、产物验证、视觉评分和渲染配对 |
| `efficiency.py` | 真实 HTTP 程序性能对照、留出套件及性能历史 |
| `observe_benchmark.py` | 完整观察工作负载的程序集与客户端配对 |
| `sandbox_research.py` / `sandbox_research_report.py` | 真实 C# 源码研究、编译、语义检查、开发/留出世界与 diff 报告；需要 Git、.NET 8 SDK |
| `test_sandbox_research.py` | 修改范围、源码哈希、事务性编辑与种子隔离回归 |
| `rsi_benchmark.py` | 代理自修改、迁移、冻结对照、后代分叉与方法撤销 |
| `rsi_stateful.py` | 库存、带权道路、实体世代事件流与独立参考 |
| `rsi_report.py` | 回放验证后的自包含 HTML 证据报告 |
| `rsi_efficiency_study.py` / `test_rsi_efficiency_study.py` | 时间预算、后代收益率、部署回收与拒绝推断回归 |
| `test_rsi_stateful.py` | 连续状态、缓存失效、正确性保留与任务规模回归 |
| `rsi_tasks.py` | 内核策略后端的独立任务、答案与工作量 |
| `rsi_code.py` / `rsi_code_worker.py` | 实际 Python 候选的正确性、CPU/墙钟和分配峰值测量 |
| `sandbox_research_compare.py` / `test_sandbox_research_compare.py` | [真实沙盒中冻结父/子/撤销程序对照](../docs/development/sandbox-research-comparison.md)，保留失败与成本 |
| `sandbox_research_series.py` / `test_sandbox_research_series.py` | [重复真实研究对照](../docs/development/sandbox-research-series.md)，运行块区间、顺序轮换与逐世界回归检查 |
| `godot-build-manifest.ps1` | 图形构建的源码与程序集绑定，供原生评测核对 |
| `test_evaluate.py`、`test_efficiency.py` | 渲染协议与程序效率回归 |
| `test_rsi_benchmark.py`、`test_rsi_code.py` | 代理证据与代码后端回归 |
| `test_online_service.py` | 启动两份真实 .NET 服务检查 API 与配对证据 |

配置与 Schema 在 [benchmarks](../benchmarks/README.md)，结果写入 [runs](../runs/README.md)。代理和候选执行仅面向可信本地代码，工具不提供操作系统安全隔离；完整契约见 [代理协议](../docs/development/rsi-improver.md)。

按入口选择说明：[渲染评测](../docs/development/benchmark.md)、[真实程序效率](../docs/development/online-efficiency.md)、[连续世界代理实验](../docs/development/stateful-rsi.md)、[改进器成本研究](../docs/development/rsi-efficiency.md)、[真实工程源码研究](../docs/development/sandbox-research.md)。源码研究不会自动采用候选，成本报告不会把流程通过转成真实模型 RSI 结果。

修改工具时同步对应协议、样例、文档和检查。文档整理不要求重跑耗时模拟或改动判分器；源码与测量范围改变后须执行实际回归和重新采样。
