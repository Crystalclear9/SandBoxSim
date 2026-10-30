# SandBoxSim 文档配图

此目录保留项目说明使用的原生 Godot 截图和动作演示，随文档提交。图片反映采样版本，不代表所有驱动、视角或当前帧率，也不作为模型质量、效率或 RSI 的评分。项目正式名称为 SandBoxSim。

最新配图包含可搜索的居民名录、按需展开的世界镜头预设与鼠标说明、随动作连续变形的肩袖、狼足四趾和鹿蹄分瓣，以及回嵌门窗；展示身份与可点击缩略肖像并排的居民手记、行式需求与可展开物资、概览／生活／决策人物页、柔光／侧光观察，以及带掌背分区的手部表面。地形图反映较宽连续覆盖场形成的岸坡、植被露土斑块和高矮草丛；前臂图册增加侧面观察。居民状态分组显示读数方向和文字状态，手记收起重复选择卡；人物工作室使用分组镜头工具、宽幅状态条和固定物资入口，近景发根与连接的芦苇花序采用当前运行网格。近景水面使用与长波分开的细波法线；建筑、植被和地形图同时反映当前客户端。动态演示使用下述当前采样，其他专题保留各自采样。截图之外的能力边界见 [模型系统](../development/models.md)。

## 当前配图


`resident-captured-stretch.png` 与同名 GIF 使用 `visual-captured-stretch.json` 的实际游戏模型，读取 CMU 143_30 的上／前臂方向及手腕朝向。两排为两种成人外观，三列为正面、侧面和背面；正交尺寸 1.70、目标 (0, 1.10, 0)，相机相对位置为 (0.03, 0.03, 4)、(-4, 0.03, 0.10)、(0.03, 0.03, -4)。PNG 是逻辑帧 110、1600×1000；GIF 为同分辨率、203 个原生采样，逻辑帧 30–636 每三帧取样，20 fps、总长 10.15 秒，分七次等配置回放编码。来源记录为 120 Hz，播放周期包含 3.075 秒捕捉和两秒休息；不是原始演员视频，也没有用概念图或重绘替代模型。来源、条款和重建见[人物素材](../development/human-assets.md)。


`resident-handling-transitions.png` 与 `resident-handling-transitions.gif` 来自公开 `visual-handling-transitions.json` 的实际控制器，使用 `handlingMotion=wholebody-motion-v7`、`toolMotion=contact-transfer-v3`、`socialMotion=prepared-gesture-v5`、`residentLocomotion=settled-arrival-v3`。上排为男性外观，下排为女性外观；三列依次展示根节点靠近后拾取、持锤到进食／饮水再换锄与逃离、双手搬运与交接。角色状态文字由实际 `Handling` 读数生成。当前重采样还包含实际屈肘平面、肩袖摆动／扭转分离、共同修整的袖窿，以及带平滑速度上限的徒手轨迹和收回约束；前伸时的躯干参与、行走肩肘协调及劳动相位保留。PNG 为逻辑帧 222、1600×1000；GIF 为 1600×1000、180 个原生帧，逻辑帧 30–746 每四步采样，总长十二秒，受单次最多 32 张限制，分六次等配置回放后编码。六格均使用正交尺寸 2.10、目标 (0, 0.82, 0)、相机相对 (1.3, 0.06, 4)，固定 60 Hz、零模拟步进；首列读取其实际局部根节点轨迹与剩余距离，另两列为独立展示姿态。没有重绘模型、插帧或缩放身体部位；旧专题保留各自采样版本，不能从此图推断所有世界事件都具备完整物品物理。

既有成人全身体型、衣装、身体动态和行走配图保留 `adult-proportions-v13`，显示方向性胸腰衣料、骨盆／大腿连续裤装，以及保留关节位置的肩袖支撑。全身三视图的身份、衣色、肤色与镜头保持一致；其他专题保留各自采样版本。

`resident-shoulder-motion.png` 来自公开 `visual-shoulder-motion.json` 的逻辑帧 156，1600×1000；同名 GIF 为 1600×1000、102 个原生帧，逻辑帧 30–434 每四步采样、总长 6.8 秒，分四次等配置回放编码。上排男性外观、下排女性外观，三列为正面、工作臂侧面和背面；正交尺寸 1.04、目标 (0, 1.08, 0)，相机相对位置分别为 (0.03, 0.04, 4)、(-4, 0.04, 0.15)、(-1.5, 0.04, -4)。

`resident-shoulder-elevation.png` 为公开 `visual-shoulder-elevation.json` 的逻辑帧 108、1600×1000；同名 GIF 为 1600×1000、180 个原生帧，逻辑帧 30–746 每四步采样、总长十二秒，分六次等配置回放编码。两排仍为两种成人外观，三列为正面、固定侧面、背面；正交尺寸 1.65、目标 (0, 1.12, 0)，相机相对位置分别为 (0.03, 0.03, 4)、(-4, 0.03, 0.10)、(0.03, 0.03, -4)。左右臂各占六秒，在冠状面前方 30° 的平面内用 1.5 秒抬至 135°、保持一秒、1.5 秒放下、休息两秒。

上述两组、`resident-handling-transitions.png`／GIF 及 `resident-social-side-motion.gif` 于 2026-10-30 重采，使用 `bodyFrame=adult-proportions-v15`、`shoulderSkin=anatomical-sleeve-v6`、`shoulderMotion=captured-arm-motion-v8`，来源观察另记录 `motionReference=cmu-143-30-v1`、`shoulderSurface=source-weighted-surface-v1`。它们显示分离摆动／轴向扭转的肩袖、实际屈肘平面、共同修整的袖窿及来源上臂体积，以及后斜至前向四平面、三个高度及扭转驱动的七十二个位置／法线修正。生活侧面 GIF 同为 1600×1000、102 帧和 6.8 秒，来自 `visual-social-side.json` 逻辑帧 30–434 每四步采样，分四次回放编码。均冻结模拟、固定 60 Hz，没有重绘、插帧或对身体局部另作缩放；GIF 只作统一调色板编码。其他既有动作专题保留原采样版本。


既有衣装近景、全身体型、身体动态、作业静态图与行走／摆臂 PNG 和 GIF 保留 `bodyFrame=adult-proportions-v13`、`garmentMaterial=faired-panel-linen-v15` 的采样网格。衣装图来自公开 `visual-wardrobe` 第 90 帧，1600×1000，正交尺寸 0.88、目标 (0, 1.13, 0)、相机相对位置 (0.10, 0.035, 3)；展示三种职业衣装的正背面、连续袖山与较薄卷边。成人身材图同步展示按实际成衣高度贴合的胸腰／上髋、连续臀腿及同步适配的上臂与袖口；配对身份、衣色、布光和镜头保持相同。其他专题保留各自的采样版本。

`resident-arm-swing.png` 来自公开 `visual-arm-swing.json` 第 120 帧，请求 1600×1600、实际 1600×1410，版本 `armSwing=relaxed-countermotion-v2`。六格配对展示正面、侧面和掌腕取向。`resident-arm-swing.gif` 将同一运行第 90–138 帧、每两帧采样的 25 组正面及侧面原生视口，各取中央 300×338 像素、不缩放，排列成 612×688 的两行画面，约 0.83 秒；未重绘人物或插帧。新摆臂下的身体动态及移动演示同步采样，其他专题保留各自版本。

`resident-locomotion.png` 来自公开 `visual-locomotion.json` 第 240 帧，请求 1600×1600，系统窗口范围内实际采样为 1600×1410，版本 `residentLocomotion=support-reach-v2`、`bodyFrame=adult-proportions-v13`。同速度配对展示两种成人外观的启停转向、双手搬运与持锤行走／工作。`resident-locomotion.gif` 使用同一运行第 90–138 帧、每两帧采样的 25 个原生区域图，将第一行两幅画面并排编码为 1528×338、约 0.83 秒的行走循环；未重绘人物或插入生成帧。静态图与 GIF 均冻结模拟、按 60 Hz 推进动画；启动、停止和作业过渡以公开场景的其他采样帧及原生检查为准。其他动作专题保留各自采样版本。

动物近景、头部和 `animal-motion.png` 为 2026-10-29 的 `articulated-paws-v12` 原生渲染。动作图来自公开 `visual-animal-motion.json` 第 180 帧，1600×1000；同一物种的全身与足部使用相同实际控制器，图册从静止进入行走再回到静止。单张图用于看清轮廓与接合，不能独自证明全程流畅。

`resident-face-frames.png` 为男女成人外观的正面及三刻面，来自公开 `visual-face-frames` 第 90 帧，1600×1000。身份 17 与 55 使用相同衣色、肤色及布光；头宽独立于身体随机宽度，来源额部 RGB 参考及曝光在线性颜色空间校准，保留照片局部色差。`resident-work-effort.gif` 为同一手锤的男女外观、良好／疲劳状态四格对照，1280×1000、102 帧、6.8 秒，按 60 Hz 每四步采样一次；`resident-work-effort.png` 为公开场景第 180 帧，1600×1000。静态头脸、体型和作业图反映当前成人分区轮廓，GIF 同步使用当前关节协调与携带姿态。作业对照镜头为正交尺寸 1.15、目标 (0, 1.05, 0)，展示上身与握柄。两套公开场景均冻结模拟，动画照常推进；系数是表现参数，不能由图像推断真实力量。

`resident-body-frames.png` 使用 `adult-proportions-v13`，来自公开 `visual-body-frames.json` 第 90 帧，1600×1000；宽屏下为单排六列，依次配对展示男女外观的正面、侧面、背面；小于 1500 像素宽时回到三列多排布局。身份 17 与 55 使用无职业装备的标准衣装、相同衣色、肤色与布光，保留真实头脸及身体模型；腰带按实际体型处于不同高度，女性上衣下缘约为 0.852 米，实际裤装显露骨盆及臀腿过渡；胸腰衣料由有松量的来源截面凸包支撑，下摆与髋部独立适配；成人下身使用当前连续骨盆—臀腿裤装与实际五骨蒙皮。正交尺寸 1.80，目标 (0, 0.82, 0)，相机相对位置依次为 (0.05, 0.04, 4)、(4, 0.04, 0.2)、(0.05, 0.04, −4)，完整显示膝部与鞋底。旧四格胸腰构图不再是这张配图的采样基线。`resident-work-effort.png` 同步使用当前完整躯干拟合。

全身对照、身体动态、作业静态图与移动演示使用 2026-10-29 的 `adult-proportions-v13` 采样；其他人物观察窗保留各自采样版本。

`resident-body-motion.png` 为公开 `visual-body-motion.json` 第 180 帧，1600×1000；同视角配对站立、行走和拾取，实际 `adult-proportions-v13` 蒙皮、`support-reach-v2` 支撑步态与放松站姿。镜头正交尺寸 2.25、目标 (0, 0.82, 0)、相对位置 (1.8, 0.08, 4)，不移动或缩放模型。

`resident-forearm-frames.png` 为公开 `visual-forearm-frames.json` 第 90 帧的小臂对照，正交尺寸 0.46，1600×1000，使用实际分离观察网格，不代表游戏中存在开放切口。头脸、小臂专题及动作 GIF 保留各自采样，`regional-adult-v2` 头脸与小臂映射未改变；GIF 展示控制器的运动节奏，成人躯干局部轮廓以全身三视图与当前作业静态图为准。

除本节开头说明的当前肩袖与生活侧面专题外，工具／作业／生活的既有动作演示保留 `limbCoordination=hinge-carry-v4` 和 `shoulderMotion=source-fitted-reach-v3`，保留工具握柄的实际绑定方向，协调上臂屈伸轴与肘部平面；生活动作另使用 `socialMotion=mouth-facing-v4`，按实际食指三段骨骼末端估计指腹位置，控制靠近嘴部与返回的轨迹，不表示已经实现食物、杯具或嘴唇接触物理。`resident-tool-motion.gif` 为单人手锤取出、作业与返回低位携带；`resident-work-effort.gif` 为同工具、不同成人外观及疲劳状态的四格对照；`resident-social-motion.gif` 展示交谈、递交与进食，`resident-social-side-motion.gif` 为同组生活动作的侧面。旧工具与生活正面两段为 1280×800，当前生活侧面与作业状态对照为 1280×1000，均为原生客户端的 102 帧，固定 60 Hz、冻结模拟，在逻辑帧 30–434 每四步采样；受单次最多 32 张采样限制，每段分为四次等配置回放后按逻辑帧拼接，总时长 6.8 秒，没有重绘或插帧，不是帧率测试或人体实测。侧面相机相对 (0, 1, 0) 为 (4, 0.06, 1)，交谈正面为 (0.12, 0.06, 4)，两段交谈图正交尺寸均为 1.6。手锤全身演示使用正交尺寸 1.85、目标 (0, 0.83, 0)、相对位置 (0, 0.07, 4)。`resident-social.png` 为生活动作逻辑帧 150。公开复现配置为 `visual-motion.json`、`visual-work-effort.json`、`visual-social.json` 与 `visual-social-side.json`；完整 102 帧演示采用上述分批采样。

人物观察窗与配件专题保留 `relaxed-yoke-v7` 采样：胸腹松量连续衔接，后肩拼接线由贴面短针脚表示；头部整体上移 18 毫米。既有衣装、全身体型与较早动作图保留 `faired-panel-linen-v15` 和 `adult-proportions-v13`。动物、建筑与环境专题保留各自采样。

人物观察窗、手部／全身视图与配件专题保留 2026-10-28 的人体截面衣料支撑体采样：胸前与背部具有不同轮廓，袖根与衣身切面连接，左右三角化对应；抬臂包含有限肩点位移，肘部优先向下，再作满足握持腕角的调整。当前衣装图使用本节开头记录的 0.88 正交尺寸；窗口内的手部与全身视图保留各自独立镜头，没有缩放身体局部。窗口分别记住面部、手部和全身的缩放，底部并排提供“跟随居民”和“查看目的地”。图册等待 120 帧再采样；静态图用于展示稳定姿势，不能证明整个动作过程的自然程度。

人物观察窗及其手部／全身图现显示两行状态卡，分别说明动作和当前手持物。`resident-cargo-detail.png` 是 1000×750 的原生独立视口近景：复用动作图册中的木材捆和人物，冻结当时表现姿势，正交尺寸 0.52，观察点相对物资中心为 (0.35, 0.25, 2)。作业动作图同时更新闭合绑绳与木材端面；暂停图册用于看清接触关系，不作为动作流畅度证据。

2026-10-28 更新人物观察窗及手部／全身视图、作业动作和 `resident-equipment.png`：观察窗左上显示实时／暂停与实际握持状态；工具近景反映带倒角敲击面的手锤与弯曲薄刃锄头。观察窗三图来自 `ui-portrait-window` 的第 90／130／170 帧，工具与动作图来自对应原生图册等待 120 帧后的采样，均为 1600×1000。另验证 1280×800 布局。动作 GIF 的当前采样方式见本节开头。

人物面部、衣装、动作近景与人物观察窗反映贴合衣身的领口折边及较低主补光；观察窗底部可见连续距离滑条和主动环视入口。生态图展示七根拱垂枝条与三／五小叶的野果灌丛，独立近景展示叶缘、叶面和聚合果实。工具库展示最近使用的四个实际工具及其紧凑菜单。动物、建筑与地形保留各自最近采样，动作 GIF 使用本节说明的当前采样。

| 文件 | 用途与采样入口 |
|---|---|
| `resident-handling-transitions.png`、`resident-handling-transitions.gif` | 到站后拾取、工具与生活切换、双手搬运交接的十二秒六格循环；`handling-transitions` |
| `resident-shoulder-motion.png`、`resident-shoulder-motion.gif` | 同工具正面／工作臂侧面／背面的肩肘近景、两种成人外观与连续返回；`shoulder-motion` |
| `resident-shoulder-elevation.png`、`resident-shoulder-elevation.gif` | 两种成人外观连续抬起、保持与放下的正侧背面；`shoulder-elevation` |
| `resident-face-frames.png` | 当前成人眉弓、下颌与颈部的正面和三刻面；`face-frames` |
| `resident-work-effort.png`、`resident-work-effort.gif` | 当前作业节奏与状态对照；`work-effort` |
| `resident-body-frames.png` | 男性／女性成人外观的同衣色全身正侧背面；`body-frames` |
| `resident-body-motion.png` | 站立、行走、弯身拾取的六格配对；`body-motion` |
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
| `animal-motion.png` | 鹿四拍行走、狼对角小跑及爪蹄落地近景；`animal-motion` |
| `animal-detail.png` | 授权鹿狼侧面、独立眼眶近景与源足部表面；`animal-details` |
| `ecology-detail.png` | 扫描树木、蕨类、岩石与程序草本、灌木；`ecology-details` |
| `bramble-detail.png` | 同一野果灌丛的叶片、连枝果梗与聚合果实；原生独立视口近景 |
| `model-surface-detail.png` | 衣料、靴子、动物与建筑近景；`model-details` |
| `resident-tool-motion.gif` | 当前手锤取出、作业与返回携带动作；`motion` 的 102 帧原生采样，覆盖 6.8 秒循环 |
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

当前运行另记录 `shoulderSkin=anatomical-sleeve-v6`、`shoulderMotion=captured-arm-motion-v8`、`reachMotion=palm-trajectory-v2`、`workEffort=phase-warp-v2`、`bodyFrame=adult-proportions-v15`、`faceContour=regional-adult-v2` 和 `toolMotion=contact-transfer-v3`，交互衔接专题另记录 `handlingMotion=wholebody-motion-v7`、`socialMotion=prepared-gesture-v5` 与 `residentLocomotion=settled-arrival-v3`；旧专题保留采样时实现，不能据新标签重解释旧图。字体策略为 `fontProfile=noto-packaged-v1`。对应 `interface=resident-framing-memory-v28`、`architecture=inset-joinery-v13`、`animalPresentation=articulated-paws-v12`、`landscapeSurface=coverage-mosaic-v6`、`habitatDetail=stratified-meadow-v10`、`residentSurface=linear-skin-v9`、`vegetationSurface=engine-faced-v2`、`wildFlora=arched-bramble-v1`、`waterSurface=constrained-bank-v6`、`handSurface=anatomical-regions-v1`、`portrait.studio=continuous-inspector-v9`、`wristContour=source-oval-v1` 、`hairline=source-root-v1`、`reedPanicle=connected-axis-v1` 和 `garmentMaterial=faired-panel-linen-v15`。动物半闭眼／全闭眼的强制参数图仅用于本地检查，不替代这里的正常运行配图。

地形远景使用普通世界入口；近景加 `--view=near`。两图用于查看实际扫描草叶地面、山坡、岸线和贴地，属于普通客户端采样，不作为固定帧评测成绩。

前臂、左右手与较早动作静态专题保留各自采样；肩袖与生活侧面 GIF 使用本节说明的当前采样；较早的住房、农业、小径与世界生活图只说明对应玩法。未更新图片中的 UI 不代表当前布局。所有图片均来自实际客户端，没有用概念图替代模型。

复现入口见表格及 [渲染与视觉评测](../development/benchmark.md)。截图用于项目说明，不用于证明模型达到写实品质；效率对照必须使用同一场景、资产、渲染条件与版本标识。

[模型说明](../development/models.md) · [美术资源](../development/assets.md) · [人物素材](../development/human-assets.md) · [环境素材](../development/environment-assets.md) · [目录职责](../development/repository-layout.md)

旧程序动物的毛束和眨眼说明不适用于当前授权动物。

木材捆独立近景保留搬运功能版本的采样；腰侧工具挂环的当前位置以新版配件图和人物观察窗为准。
