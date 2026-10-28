# SandBoxSim 文档配图

此目录保留项目说明使用的原生 Godot 截图和动作演示，随文档提交。图片反映采样版本，不代表所有驱动、视角或当前帧率，也不作为模型质量、效率或 RSI 的评分。项目正式名称为 SandBoxSim。

最新配图包含可搜索的居民名录、按需展开的世界镜头预设与鼠标说明、随动作连续变形的肩袖、狼足四趾和鹿蹄分瓣，以及回嵌门窗；展示身份与可点击缩略肖像并排的居民手记、行式需求与可展开物资、概览／生活／决策人物页、柔光／侧光观察，以及带掌背分区的手部表面。地形图反映较宽连续覆盖场形成的岸坡、植被露土斑块和高矮草丛；前臂图册增加侧面观察。居民状态分组显示读数方向和文字状态，手记收起重复选择卡；人物工作室使用分组镜头工具、宽幅状态条和固定物资入口，近景发根与连接的芦苇花序采用当前运行网格。近景水面使用与长波分开的细波法线；建筑、植被和地形图同时反映当前客户端。动态演示使用下述当前采样，其他专题保留各自采样。截图之外的能力边界见 [模型系统](../development/models.md)。

## 当前配图

`resident-body-frames.png`、`resident-forearm-frames.png` 为当前成人穿衣体型、小臂肌腹与腕部过渡的实机对照，分别来自公开场景 `visual-body-frames.json`、`visual-forearm-frames.json` 的第 90 帧，1600×1000。两名居民为同衣色、肤色的身份 17 与 55；体型图正交尺寸 1.3，目标点 (0, 1.07, 0)，模型转向前侧，正／侧面相机相对位置分别为 (0.05, 0.04, 4)、(4, 0.04, 0.2)。小臂图正交尺寸 0.46，使用实际分离观察网格，不代表游戏中存在开放切口。动作静态图和生活图另按当前运行模型等待 120 帧采样；GIF 保留其上一轮动作采样，不用于说明此次体型调整。

动作演示使用当前手掌轨迹版本 palm-trajectory-v1，并以代码版本区分动作修正。resident-tool-motion.gif 为单人手锤抬手、使用与收放；resident-social-motion.gif 展示交谈、递交与进食；resident-social-side-motion.gif 为同样动作的侧面。三段均为 1280×800 原生客户端的 102 帧，程序每帧采样前按 60 Hz 推进四步，GIF 总时长 6.8 秒；每段覆盖图册的一个完整循环，不是帧率测试或人体实测。侧面相机相对 (0, 1, 0) 为 (4, 0.06, 1)，正面为 (0.12, 0.06, 4)，正交尺寸均为 1.6。resident-social.png 为生活动作第 30 个采样帧。画面使用上一帧手掌／肘部参考、较缓起手与连续收手的实际运行实现，没有用概念图替代。

当前人物观察窗、衣装、配件、动作和社交图使用 relaxed-yoke-v7：胸腹松量连续衔接，减弱胸前肌肉状褶皱，后肩拼接线由贴面短针脚表示；头部整体上移 18 毫米。衣装近景沿用相同镜头，便于观察颈部、下颌和领口关系。动物、建筑与环境专题保留各自采样。

人物观察窗、手部／全身视图、衣装、作业动作、配件与社交图使用 2026-10-28 的人体截面衣料支撑体：胸前与背部具有不同轮廓，袖根与衣身切面连接，左右三角化对应；抬臂包含有限肩点位移，肘部优先向下，再作满足握持腕角的调整。衣装图使用正交尺寸 0.72、观察目标 (0, 1.23, 0)、相对相机位置 (0.10, 0.035, 3)，显示实际游戏模型的胸肩近景，没有缩放身体局部。窗口分别记住面部、手部和全身的缩放，底部并排提供“跟随居民”和“查看目的地”。图册等待 120 帧再采样；静态图用于展示稳定姿势，不能证明整个动作过程的自然程度。

人物观察窗及其手部／全身图现显示两行状态卡，分别说明动作和当前手持物。`resident-cargo-detail.png` 是 1000×750 的原生独立视口近景：复用动作图册中的木材捆和人物，冻结当时表现姿势，正交尺寸 0.52，观察点相对物资中心为 (0.35, 0.25, 2)。作业动作图同时更新闭合绑绳与木材端面；暂停图册用于看清接触关系，不作为动作流畅度证据。

2026-10-28 更新人物观察窗及手部／全身视图、作业动作和 `resident-equipment.png`：观察窗左上显示实时／暂停与实际握持状态；工具近景反映带倒角敲击面的手锤与弯曲薄刃锄头。观察窗三图来自 `ui-portrait-window` 的第 90／130／170 帧，工具与动作图来自对应原生图册等待 120 帧后的采样，均为 1600×1000。另验证 1280×800 布局。动作 GIF 的当前采样方式见本节开头。

人物面部、衣装、动作近景与人物观察窗反映贴合衣身的领口折边及较低主补光；观察窗底部可见连续距离滑条和主动环视入口。生态图展示七根拱垂枝条与三／五小叶的野果灌丛，独立近景展示叶缘、叶面和聚合果实。工具库展示最近使用的四个实际工具及其紧凑菜单。动物、建筑与地形保留各自最近采样，动作 GIF 使用本节说明的当前采样。

| 文件 | 用途与采样入口 |
|---|---|
| `resident-body-frames.png` | 男性／女性成人外观的同衣色胸腰与上肢正侧面；`body-frames` |
| `resident-forearm-frames.png` | 两种外观的小臂掌侧、背侧与侧面；`forearm-frames` |
| `world-camera-controls.png` | 按需展开的镜头预设、真实选中反馈与鼠标说明；实际点击“视角与操作” |
| `tool-palette.png` | 416 像素工具库、分类和搜索、最近四种工具；实际跨分类选择后采样 |
| `tool-palette-compact.png` | 1280×800 下标题区“最近”菜单展开状态；实际鼠标与键盘回放 |
| `free-world-ui.png`、`ui-detail-refined.png` | 自由探索现场手记、人物手记；`field`、`person` |
| `resident-portrait.png` | 当前展开人物观察窗；`person-expanded` |
| `resident-roster.png` | 实际打开居民名录后的搜索、当前身份与可点击整行；名录在模型区域临时展开 |
| `resident-life.png`、`resident-decision.png` | 实际鼠标切换的生活／决策人物页 |
| `resident-raking.png` | 实际鼠标切换侧光后的同一人物模型 |
| `resident-hand-inspector.png`、`resident-body-inspector.png` | 当前展开窗口中的人物双手与全身；公开 `ui-portrait-window` 第 130/170 帧 |
| `character-detail.png`、`resident-face-detail.png` | 人物、动物与面部；`characters`、`faces` |
| `wardrobe-detail.png`、`resident-garment-actions.png` | 三种衣装的正背面与作业动作；`wardrobe`、`actions` |
| `animal-head-detail.png` | 鹿狼正面和侧面的眼位、耳根、额鼻及下颌轮廓；`animal-heads` |
| `animal-detail.png` | 授权鹿狼侧面、独立眼眶近景与源足部表面；`animal-details` |
| `ecology-detail.png` | 扫描树木、蕨类、岩石与程序草本、灌木；`ecology-details` |
| `bramble-detail.png` | 同一野果灌丛的叶片、连枝果梗与聚合果实；原生独立视口近景 |
| `model-surface-detail.png` | 衣料、靴子、动物与建筑近景；`model-details` |
| `resident-tool-motion.gif` | 当前手锤携带、作业与收纳动作；`motion` 的 102 帧原生采样，覆盖 6.8 秒循环 |
| `resident-social-motion.gif` | 交谈、递交与进食；`social` 的 102 帧原生采样，覆盖 6.8 秒循环 |
| `resident-social-side-motion.gif` | 同一组生活动作的侧面，便于观察肘部前后深度；镜头参数见本节开头 |
| `terrain-modern.png`、`terrain-closeup.png` | 普通世界与 `--view=near` 的地形、植被和水岸 |
| `architecture-detail.png`、`resident-forearm-detail.png` | 建筑和连续前臂掌侧／背侧／侧面；`architecture`、`forearms` |
| `resident-hand-detail.png` | 原网格掌纹、甲床及工具握持近景；`hands` |
| `resident-handedness.png` | 左右手与远近景模型；`handedness` |
| `living-homes.png`、`living-farms.png`、`living-paths.png` | 住房生活、农业与逐渐累积的土地痕迹 |
| `nature-detail.png`、`free-exploration.png` | 较早的自然模型与特殊地点专题 |
| `immersive-world.png` | F10 隐藏界面后的普通世界；保留角落返回按钮 |
| `wild-meadow.png` | 当前通用草花、逐株接地与真实世界花甸；`meadow` |
| 其他 `resident-*.png`、`resident-*.gif` | 较早的人物、配件、手部与生活动作专题 |

## 版本与复现

建筑、人物动物、面部、衣装、生态、网格近景、地形远近景、工具库、现场手记、人物手记、镜头面板、展开观察窗及其手部／全身视图更新于 2026-10-27；花甸专题保留 2026-10-26 采样，均为原生 Godot 渲染。普通配图为 1600×1000；紧凑工具库为 1280×800，灌丛独立近景为 1200×900，使用同一图册模型、正交尺寸 0.27、目标点 (0.229, 0.591, -0.022)，等待 120 帧后采样。工具库两图在实际选择四种工具后采样，紧凑图记录菜单展开状态。镜头面板另检查实际按钮焦点下的空格暂停／继续、Esc 返回和全景平移后的选中反馈。图册使用实际游戏网格、独立展示环境，模拟暂停，等待 120 帧后采样；普通世界预览等待 2 秒。生活／决策／侧光、物资与工具状态由实际命中检查的鼠标事件回放后采样，回放先用空格暂停世界，并核对工具库实际可见、手记收起；缩略入口另检查 Enter 打开、Esc 焦点返回，以及手部背面观察关闭后仍显示面部；改名检查实际键盘保存、取消和换居民丢弃草稿。沉浸图使用 F10 后采样。手部与全身分别来自公开 `ui-portrait-window` 场景第 130／170 帧。界面和普通世界使用种子 839102、40 人，无预先天数推进；花甸预览暂停模拟。

字体策略为 `fontProfile=noto-packaged-v1`。对应 `interface=resident-framing-memory-v28`、`architecture=inset-joinery-v13`、`animalPresentation=source-paw-contours-v11`、`landscapeSurface=coverage-mosaic-v6`、`habitatDetail=stratified-meadow-v10`、`residentSurface=orbital-gaze-v6`、`vegetationSurface=engine-faced-v2`、`wildFlora=arched-bramble-v1`、`waterSurface=constrained-bank-v6`、`handSurface=anatomical-regions-v1`、`portrait.studio=continuous-inspector-v9`、`wristContour=source-oval-v1` 、`hairline=source-root-v1`、`reedPanicle=connected-axis-v1` 和 `garmentMaterial=relaxed-yoke-linen-v14`。动物半闭眼／全闭眼的强制参数图仅用于本地检查，不替代这里的正常运行配图。

地形远景使用普通世界入口；近景加 `--view=near`。两图用于查看实际扫描草叶地面、山坡、岸线和贴地，属于普通客户端采样，不作为固定帧评测成绩。

前臂、左右手与动作静态专题随当前模型重采；动作 GIF 使用本节说明的当前采样；较早的住房、农业、小径与世界生活图只说明对应玩法。未更新图片中的 UI 不代表当前布局。所有图片均来自实际客户端，没有用概念图替代模型。

复现入口见表格及 [渲染与视觉评测](../development/benchmark.md)。截图用于项目说明，不用于证明模型达到写实品质；效率对照必须使用同一场景、资产、渲染条件与版本标识。

[模型说明](../development/models.md) · [美术资源](../development/assets.md) · [人物素材](../development/human-assets.md) · [环境素材](../development/environment-assets.md) · [目录职责](../development/repository-layout.md)

旧程序动物的毛束和眨眼说明不适用于当前授权动物。

木材捆独立近景保留搬运功能版本的采样；腰侧工具挂环的当前位置以新版配件图和人物观察窗为准。
