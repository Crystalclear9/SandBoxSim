# SandBoxSim 评测场景

`scenarios/` 保存版本化的本地评测配置，支持渲染性能、模型图像识别和界面回放。运行入口、配置协议、结果格式与研究边界见 [评测接口与实验运行](../docs/development/benchmark.md)。

| 场景 | 内容 |
|---|---|
| `render-population` | 300 人、预演 3 天、每帧 2 tick，无截图，30 FPS 墙钟门槛 |
| `visual-architecture` | 五种住房与仓库，生成无标题裁切图与对象识别题 |
| `visual-characters` | 成人、儿童、鹿和狼，对象识别题 |
| `visual-faces` | 面部近景，供表面细节检查 |
| `visual-hands` | 锤柄、斧柄和锄柄的两次实际姿态采样 |
| `ui-portrait` | 人物构图、俯仰、复位、设置打开与 Esc 关闭顺序 |

这些是公开开发样例；正式实验的留出集由评测者在仓库外管理。结果、日志与临时图片写入忽略提交的 `runs/evaluation/`，不放到场景目录。

外部调用从 `python tools/evaluate.py describe` 获取能力清单；请求、预测与结果分别定义在 [request.schema.json](request.schema.json)、[predictions.schema.json](predictions.schema.json) 和 [result.schema.json](result.schema.json)。接口采用本地 CLI/文件，未提供在线逐步交互或 HTTP 服务。
