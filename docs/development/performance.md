# 性能采样与渲染维护

性能运行采用 [评测接口](benchmark.md) 的固定帧预算和独立墙钟。游戏画面、模型和模拟规则共同约束优化：在相同输入下保留模拟摘要、模型细节与交互行为，再比较耗时。帧率门槛定义在场景配置中，不依赖文档中的某次本机数值。

## 采样方法

先编译当前源码，使用固定机器、渲染器、窗口尺寸和驱动。纯性能样例不采集截图，避免把磁盘开销混入渲染对照：

```powershell
./tools/godot.ps1 -Mode build
python tools/evaluate.py run benchmarks/scenarios/render-population.json --output runs/evaluation/baseline-1 --label baseline
python tools/evaluate.py run benchmarks/scenarios/render-population.json --output runs/evaluation/baseline-2 --label baseline
python tools/evaluate.py run benchmarks/scenarios/render-population.json --output runs/evaluation/baseline-3 --label baseline
```

修改实现、重新编译后，用三个新的目录采集候选。配置、限帧与显示同步选项保持一致。每个目录保存实际请求、代码/程序集哈希、环境和结果；输出目录不覆盖旧样本。比较单组结果：

```powershell
python tools/evaluate.py compare runs/evaluation/baseline-1 runs/evaluation/candidate-1
```

汇总同一组至少三次的 FPS 与 p95 中位数、范围，并保留失败样本。不同种子、人口、推进步数、驱动或设备的数据不能作为同条件配对。超过 30 FPS 的平均值也不等于每一帧都在 33.3 ms 内；还要看 p95/p99/max 和实际交互。

## 交互长帧与时间预算

普通游玩用 4 ms 的主线程追赶预算，每推进一个完整 tick 后检查时间并让出本帧。累计追赶欠账限制为约四分之一秒，单次输入 `delta` 最多取 0.1 秒，避免切回窗口或卡顿后连续追赶大量 tick。逻辑 tick 保持原顺序；负载过高时实际模拟速度会低于选择的倍速。预算无法中断一个正在执行的重 tick。

地形、自然物和建筑重建分到不同帧；建筑每帧最多创建或替换一个模型，拆除和已有节点的位置/朝向仍在同步时处理。待执行工作每次重读当前状态，避免已经拆除的建筑在队列里重新出现。世界切换清空待刷新标记。批量编辑或高速推进时，图形可能短暂落后于内核；评测状态的 `worldVisuals` 明确记录三类待刷新标记与已缓存建筑数量。

地图阴影采用两级级联，距离随镜头距离在 45–160 米内调整。近景使用较短阴影范围，减少远处阴影提交；全景保留较远范围。评测的 `renderQuality` 记录阴影模式、距离和 MSAA；参数不同的样本会被比较器拒绝。它不降低近景模型网格，但会改变远处阴影覆盖。模型图册使用独立灯光，不受地图阴影范围影响。

`frameMs` 除分位数外，还包含 `over33MsCount`（超过 1000/30 ms）和 `over50MsCount`。次数与采样帧数一起解读，平均 FPS 不能替代长帧频率。固定步长评测仍按 `ticksPerFrame` 推进全部请求 tick，不使用交互追赶预算；这保证模拟工作量不因机器快慢变化，也意味着评测与普通游戏是不同调度方式。

## CPU 区段

`result.json` 的 `cpuScopes` 在预热结束时清空，从主线程采样以下区段：

| 区段 | 包含的工作 |
|---|---|
| `SimulationTicks` | 内核 tick 与自然地点的推进 |
| `CheckTerrain` | 变化签名检查及它触发的地形、自然物、建筑同步 |
| `BuildTerrain` | 地形与水面网格、纹理数据更新 |
| `BuildNature` | 自然物与地点表现、空间批次构造 |
| `BuildBuildings` | 建筑差异同步、位置/朝向及聚落标签更新 |
| `CreateBuilding` | 新建或变更建筑的近远景网格与节点 |
| `AnimateActors` | 角色位置、朝向、携带物与姿态更新 |
| `SyncEntities` | 角色增删、职业/年龄模型变化及危险表现同步 |

每项有 `calls`、`totalMs` 和 `maxMs`。父区段包含子区段耗时，例如 `CheckTerrain` 包含 `BuildBuildings`，后者包含 `CreateBuilding`，不能把所有区段相加当成总 CPU 时间。这不是 GPU 时间或完整引擎 profiler；墙钟还包含引擎渲染、模拟、UI 和调度。普通游戏不记录这些区段数据。

实现位于 [RenderProfile.cs](../../src/SandBoxSim.Godot/RenderProfile.cs)。新增采样应保持同一基线和候选使用相同计时范围，不读取或改写模拟状态。

## 建筑的增量表现

[WorldView3D.Buildings.cs](../../src/SandBoxSim.Godot/WorldView3D.Buildings.cs) 按建筑槽位保留表现节点；生成号、身份变体、种类或完工状态变化时只替换该建筑。附近道路变化只重新决定朝向，地形高度变化只更新位置。拆除时立即从场景移除旧节点；槽位复用会核对生成号，防止显示旧建筑。切换世界或读档时清空此缓存。

聚落标签按身份保留，位置与名称变化会触发同步。近远景仍沿用 24/32 米的切换滞后，材质、瓦片与近景细节保持原有模型定义。

[NatureModels.Buildings.cs](../../src/SandBoxSim.Godot/NatureModels.Buildings.cs) 在合并网格已缓存时直接创建独立场景节点，不再重新创建随后丢弃的门、屋瓦和木作临时节点。网格共享、节点独立，原有 140 个稳定身份组合与缩放规则保持一致。

## 合并网格

[NatureModels.MeshBatching.cs](../../src/SandBoxSim.Godot/NatureModels.MeshBatching.cs) 按原材质组，在 C# 中批量变换输入的索引顶点，并一次提交顶点、法线、UV、索引及需要的顶点色。它替代逐三角形角点调用 `SurfaceTool` 的流程，保留三角形、材质分组、面部颜色和法线的逆转置变换。

这种合并用于静态零件和关节下的固定零件，不跨越独立运动关节，不取代手部或肩部蒙皮。实现保留输入网格的索引布局，不额外合并跨零件的重复顶点；缓存网格的顶点数量和内存应与耗时一起观察。

## 回归检查

```powershell
./tools/godot.ps1 -Mode test
python -m unittest discover -s tools -p test_evaluate.py
python tools/evaluate.py run benchmarks/scenarios/visual-architecture.json --label geometry-review
python tools/evaluate.py run benchmarks/scenarios/visual-faces.json --label geometry-review
python tools/evaluate.py run benchmarks/scenarios/ui-portrait.json --label interface-review
```

自检包括建筑节点复用、道路/高度变化、完工、拆除、槽位复用、身份变体缓存、模型几何预算和人物握持。固定图册裁切图可以与同机器基线逐像素对照；光照、字体、驱动或动画变化时，像素不一致仍需结合语义与人工观察解释，不能直接宣判视觉退化。性能优化不能靠改变门槛、种子或测量预算取得通过。
