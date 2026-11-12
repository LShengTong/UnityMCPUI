---
name: unity-ui
description: 使用本项目 Unity MCP 查询、创建、复用、修改和预览 uGUI Prefab，并按需要编写驱动脚本、检查编译、挂载组件和绑定引用。适用于菜单、HUD、面板及现有 UI；不用于 UI Toolkit 或 IMGUI 编辑器界面。
---

# Unity UI

通过 Unity MCP 操作当前目标项目的 uGUI Prefab。先理解已有界面，按用户要求修改，再用查询和图片确认结果。工具名可能带客户端前缀，按实际暴露的名称调用。

## 目标与连接

- 从用户明确给出的路径或当前工作区确定 Unity 项目根目录，调用 `connect_project`。没有明确目标时询问，不猜项目，不把 MCP 源码目录当成 Unity 项目。
- 后续复用连接；用户切换项目时重新 connect_project。程序集重载后可用 get_compilation_status 自动重连同一项目；其他调用提示实例变化时先恢复连接，不自动重试写操作。
- Unity 侧桥接包必须已安装且 Editor 已打开。缺少工具或连接失败时说明缺失条件，不把未执行的设计描述为已完成的资源。

- 项目与任务范围已明确时，直接执行项目详情、Prefab/资源搜索与详情、备份列表等只读查询，不额外询问“是否允许查询”。这些工具已声明只读 annotations。若客户端自身弹出授权框，遵循客户端权限设置，不声称 skill 或 MCP 标记能绕过授权。截图可能运行预览实例脚本，不属于纯查询标记。

## 选择操作

| 目的 | 工具 | 使用要点 |
| --- | --- | --- |
| 查找 UI 资源 | `find_ui_assets` | 批量搜索 ui_prefab / sprite / tmp_font，各条件独立分页 |
| 查看资源详情 | `inspect_ui_assets` | 批量读取 Prefab / Sprite / TMP 字体，每项独立返回错误 |
| 新建界面 | `create_ui_prefab` | 输出路径必须尚未存在；支持普通节点和引用已有 UI prefab |
| 组合编辑 | `batch_edit_ui_prefab` | 属性、结构、组件、脚本和引用按顺序处理，成功后保存一次 |
| 导入脚本改动 | `refresh_assets` | 主线程直接刷新资源，方法返回后响应；不代表后续编译成功 |
| 查询 / 等待编译 | `get_compilation_status` | 编译错误含文件和行号；可等待最多 60 秒，重载后自动重连同一项目 |
| 导入图片 | `import_ui_image` | 本地 PNG/JPG 导入为新 Sprite，返回路径和 GUID，拒绝覆盖 |
| 查询备份 | `list_ui_backups` | 按 Prefab 路径分页列出修改前快照 |
| 恢复备份 | `restore_ui_prefab` | 选择 backup_id，使用当前版本原地恢复，恢复前再次备份 |
| 看实际效果 | `preview_ui_prefab` | 返回 PNG 图片；按目标分辨率预览 |
| 创建 Edit 测试场景 | `create_ui_test_scene` | 保存并附加打开新场景，包含 Canvas/Content、EventSystem/输入模块和背景相机，可同时放入 UI prefab |
| Game view 分辨率 | `get_game_view_resolution`、`set_game_view_resolution`、`restore_game_view_resolution` | Edit/Play 共用，查询预设、按宽高或索引切换，结束后恢复原预设 |
| Play 功能验证 | `get_play_status`、`enter_play_mode`、`exit_play_mode`、`inspect_runtime_ui`、`mount_test_prefab`、`run_ui_test`、`clear_test_instances` | 默认独立临时场景；mode=current 验证实际场景集成；测试实例独立追踪 |

工具的实际 schema 是参数依据。复杂调用或首次使用结构编辑时，参阅 [工具语义与示例](references/examples.md)。
需要确认创建、批量编辑或运行时测试的详细字段语义和限制时，按需读取 [详细工具语义](references/tool-semantics.md) 中对应工具的小节。
需要脚本驱动、挂载自定义 MonoBehaviour 或绑定引用时，参阅 [脚本、编译与引用绑定](references/script-workflow.md)。
任务包含运行时功能验证时，参阅 [Play 功能测试](references/play-testing.md)，区分回调测试、实际输入和静态预览。

- Prefab、图片、字体统一使用 find_ui_assets / inspect_ui_assets，分别传 asset_type=ui_prefab、sprite、tmp_font。先检查 results[i].ok；Prefab 层级和版本在 results[i].prefab.nodes / results[i].prefab.version 中，后者用于 expected_version。max_nodes 控制每个 Prefab 的节点数，prefab.truncated 表示未完整返回。旧独立 Prefab 查询工具已移除。

## 创建与修改

- 用户指定现有界面时，先定位并 inspect；在项目中沿用命名、资源目录、字体、颜色和布局习惯。用户已给准确路径时可直接 inspect，无需强制搜索。
- 创建前主动识别可复用单元，例如背包格子、列表项、卡片、页签和通用按钮。重复出现、需要统一维护样式或具有独立内容/行为的单元，优先复用已有子 prefab；没有合适资源时，在当前任务内先创建子 prefab，再组装父界面，无需用户专门要求“拆 prefab”。不要默认把每个重复单元展开成独立节点树。一次性的简单装饰可留在父树内，不为拆分而拆分。
- 可复用单元需要独立展示或交互时，可配套 View/组件脚本，例如 InventorySlotView，封装自身子节点引用、内容绑定和点击回调。父 Controller 通过明确的方法传入数据、接收事件，不反复查找子单元内部节点；已有合适脚本时直接复用。
- 确认框、提示框、物品详情窗等具有独立内容或交互的弹窗，优先复用已有弹窗 prefab；需要新建时拆为独立弹窗 prefab，并配套对应的 View/组件脚本，例如 ConfirmDialogView。脚本管理弹窗内部序列化引用、内容绑定、显示与关闭，以及确认/取消等事件；父 Controller 传入内容并处理业务结果。沿用项目已有弹窗框架和脚本，遵守用户仅制作静态效果或不要脚本等范围限制。
- 弹窗按子 prefab 的依赖顺序创建、编译脚本、挂载并绑定内部引用，再通过项目现有方式装入父界面的弹窗容器，父级绑定弹窗组件。默认保持关闭，按交互流程打开；重复打开时更新内容并清理上次绑定的回调。按需求配置遮罩、点击拦截、层级与关闭方式，验证打开、确认/取消、关闭及再次打开。弹窗 prefab 根使用 RectTransform，复用场景 Canvas 和 EventSystem。
- 按依赖顺序创建并检查：叶子单元 → 组合单元 → 父界面。父级用 kind=prefab 或 instantiate_prefab 保留对子资源的关联；需要共同更新的重复项使用同一源 prefab。duplicate 复制普通节点不能代替独立可复用资源。实例差异在父 prefab 的实例上覆盖，公共样式改源资源；复用已有资源时先确认修改源资源是否属于任务范围。
- 根据内容来源决定数量：固定格子/固定条目可由父 prefab 的 Grid/布局容器排列多个实例；随数据变化的列表或背包由脚本按项目机制创建、更新和回收实例，不同时保留一整组占位和重复生成的运行时条目。动态模板可用一个子 prefab 实例作为模板节点，再绑定给控制脚本；当前 set_reference 不支持直接赋 prefab 资源引用。背包示例见 [工具语义与示例](references/examples.md#主动拆分背包格子并组装)。
- 明确用户要求的内容、尺寸、分辨率和交互范围。其余细节可以根据上下文合理选择，不因每个视觉细节都中断工作。
- 先用 `find_ui_assets` 查找项目中可复用的 Sprite 和 TMP 字体，再用 `inspect_ui_assets` 比较尺寸、Border、PPU 或检查指定文字的字形。查询结果逐项检查 ok/error，顶层 ok 不代表每项成功。搜索每项独立分页。切片图中的 Sprite 用返回的 local_id 区分；目前 Prefab 赋值工具仅接受 sprite_path，不能按 local_id 选择图集子 Sprite，不要把查询支持当成赋值支持。
- 用户提供本地 PNG/JPG 时，使用 `import_ui_image` 导入为 Sprite；source_path 是 Python 服务所在机器上的绝对路径。Border 顺序为左、下、右、上。九宫格需先设置 Sprite Border，再将已有 Image 的 `image_type` 修改为 `Sliced`；仅指定 sprite_path 不会自动开启九宫格。导入超时后先检查目标文件，避免重复导入。
- 普通节点支持 panel、image、text、button，布局支持水平、垂直和 grid。文字使用 TMP；中文内容需要支持相关字形的字体。按钮的业务回调不由视觉创建自动实现。
- 创建的根节点仅为 RectTransform，默认全拉伸、零边距；挂到场景已有 Canvas 或其容器下使用。不会创建 Canvas、CanvasScaler、GraphicRaycaster 或 EventSystem，也没有 Canvas 根模式。画布缩放和输入使用场景配置；独立预览的临时 Canvas 不会保存进资源。
- 复用已有 UI prefab 时，创建节点用 kind=prefab 和 prefab_path；它保留源组件、子树和嵌套关联，不接受自定义 children/layout。仅显式填写的 anchor、width、height、x、y、offset_min/max 覆盖源布局：width/height 是实际矩形尺寸，x/y 是 anchoredPosition 分量，不沿用普通 stretch 节点的对称内边距语义。未指定的布局不要补默认值。
- 批量 duplicate 复制非根节点及子树；instantiate_prefab 插入已有 UI prefab 并保留关联。两者接受 parent_path、可选 name 和 sibling_index；省略 name 时自动避免同名。拒绝循环依赖、复制到自身子树、复制嵌套实例内部节点及向嵌套实例内部插入。不要把保留关联等同于修改源 prefab。
- 同一 Prefab 的相关调整涉及属性和结构时，优先使用 `batch_edit_ui_prefab` 合并请求；不合并无关资源或需要先查看中间结果的操作。全部已有节点路径在执行前统一解析，按数组顺序执行，保存前任一步失败则本批不保存。错误中的 failed_operation_index 为从 0 开始的操作序号，phase 区分准备、定位、执行和保存阶段；saved=true 表示已保存，不能按未执行重试。批量中新建节点暂不支持临时标识，需下批重新查询后引用。
- 修改现有 UI 时优先改目标属性。移动用 move；不以删除重建代替移动，不改动无关节点。
- 已有节点缺少组件时，可在 `batch_edit_ui_prefab` 中先 add_component 再 modify；组件使用明确白名单，拒绝重复组件和同节点多种 LayoutGroup。新建节点本批仍不能通过路径引用。ScrollRect 需要已存在的 Viewport/Content 层级和显式引用，Viewport 需 Image 等 Graphic 与 RectMask2D 或 Mask；该操作不自动生成层级或业务逻辑。
- `batch_edit_ui_prefab` 中 modify 操作的 value 是字符串；数值、布尔值和向量字符串格式见参考示例。按钮文字通常在 TMP 子节点，而非 Button 所在节点。
- add、duplicate、instantiate_prefab 成功后，created_nodes 返回仍存在的新建根节点的 operation_index 和最终 index_path。它用于后续请求；本批中仍不能引用新节点。使用后续路径前重新 inspect 并获取最新版本。
- 文字支持对齐、换行、自动字号及上下限、溢出方式、行距；图片支持保持比例、填充方式/原点/进度/方向和 raycast_target；布局支持子元素对齐、尺寸控制、扩展及 Grid 行列约束。创建按钮的文字字段作用于自动生成的 Label，批量修改需定位实际 TMP 子节点。字段和值见参考，勿以字符串 value 的自由格式绕过属性白名单。
- 删除会同时移除子树，可能使其他组件引用丢失；先检查目标范围。仅在用户任务包含删除时执行，不为方便修改扩大删除范围。

## 图片来源、生成与使用

- 当界面需要图标、插画、背景或装饰图片，而用户没有指定图片时，主动根据需求、已有 UI 风格和目标尺寸尝试生成，不默认要求用户提供素材。纯色面板和普通文字直接使用组件即可，无需为了用图片而生成图片。
- 用户已指定素材、品牌资源或明确要求复用项目图片时，优先使用指定内容；不擅自替换。生成前先明确图片用途、比例、风格、透明背景需求，结合现有界面保持一致。
- 使用当前客户端可用的图片生成工具；若提供相应生成 skill，按其指导调用。透明图标和独立装饰优先生成透明 PNG；需要动态文字时保留为 TMP，不把按钮文案烘焙进背景图。
- 将生成结果保存或取得为 Python MCP 服务所在机器可读取的本地 PNG/JPG，再调用 `import_ui_image`。工具返回的 URL、资源 ID 或其他机器上的路径不能直接充当 source_path；先用可用的下载或文件传输能力取得本地文件。不要把图片 base64 填入 Prefab spec。
- 导入目标采用项目已有资源目录和有意义的名称，使用未占用路径。文件超过导入限制时，用可用图片工具调整大小/格式后再导入；仅改扩展名不等于转换格式。
- 创建或新增 image、panel、button 节点时，把导入结果的 asset_path 填入该节点的 `sprite_path`，设置合适的宽高。默认使用 `color="#FFFFFF"` 保留图片原色；Image 的颜色会与 Sprite 相乘，只有有意染色时才用其他颜色。
- 修改已有 Image 时，先 inspect 获取最新版本和节点路径，再用 `batch_edit_ui_prefab` 将 `sprite_path` 设为导入的资源路径。空字符串清除 Sprite。Button 背景位于按钮 Image 节点，文字通常在 TMP 子节点；text 节点本身不能通过 sprite_path 显示图片。
- 九宫格背景导入时设置 Border，创建/新增普通节点可直接指定 image_type=Sliced，已有 Image 用 modify/image_type。填充图需 image_type=Filled，再设置 fill_method、fill_origin、fill_amount 等。
- 根据图片替换的实际影响判断是否补充 Prefab 预览；尺寸、比例、透明边缘或拉伸效果不确定时，查看受影响画面。未达到效果时按具体问题修正；相同生成或导入失败后，不在条件未变化时重复重试。
- 没有可用生成工具，或生成失败且无法继续时，明确说明原因。能用已有资源或组件继续的部分继续完成，必要的临时占位应明确标记，不把占位描述成已生成素材。
## 锚点与响应式工作流程

- 创建或调整布局前，先确定主要区域在父级变化时应“贴边、居中、拉伸还是由布局排列”。为不受父布局驱动的背景、导航栏、侧栏、内容区、角落按钮显式选择 anchor；不要省略 anchor 后靠默认 center 和大量绝对坐标复刻单张参考图。
- 把贴边/拉伸约束放在合适的区域容器上，容器内部用局部锚点或 LayoutGroup。居中弹窗、居中固定卡片可以使用 center；不要求每个后代都换成拉伸锚点。
- 新建时将选择写进 spec；修改现有界面时先 inspect，必要时通过 modify 的 anchor 及 offsets 设置。先定锚点/Pivot，再定尺寸、边距或布局参数。无法从单张参考图确认响应式规则时，结合区域用途做合理选择；涉及跨比例适配时再选取代表性尺寸验证。
- 布局验证按下文的改动影响范围选择；发现错位先排查父容器锚点、边距和布局控制，再重测受影响的尺寸。需要响应式检查时，参阅 [响应式布局与多分辨率验证](references/responsive-layout.md)。
## 布局判断

- 根据布局意图显式选择 anchor：顶部/底部条用横向拉伸，侧栏用纵向拉伸，角落控件用对应角锚点，全屏背景用 stretch；不要把所有节点都留在默认 center，再靠大幅 x/y 模拟位置。创建和批量修改共用16种预设。检查父级 LayoutGroup 是否控制节点位置、尺寸或锚点；布局管理的子节点优先调整布局和 LayoutElement，而非与其争夺 RectTransform。
- 水平/垂直布局父级下，创建时 width/height 用作 LayoutElement 的首选和最小尺寸。已有节点若由 LayoutElement 控制，只改 size_delta 可能无效；使用 preferred_width/preferred_height 等属性调整布局尺寸，缺少 LayoutElement 时可先添加。
- 创建时 stretch 的 x/y 是横纵对称内边距；横向拉伸用 x 表示左右边距、y 表示纵向位置，height 固定；纵向拉伸用 y 表示上下边距、x 表示横向位置，width 固定。固定锚点下 x/y 为 anchoredPosition。offset_min/offset_max 可覆盖计算出的几何，用于不对称边距。
- 修改预设使用 modify 的 property=anchor，value 为预设名；它设置 anchorMin/anchorMax/pivot 并保留 sizeDelta/anchoredPosition 数值，不保持屏幕边界。先设预设，再设位置/尺寸或 offset_min/offset_max。拉伸时左/下正偏移表示内缩，右/上内缩使用负偏移。具体示例见参考。
- 中文或符号缺字时，用 `inspect_ui_assets` 的 text 和 search_fallbacks 检查。它只检查已有图集字形（可包含 TMP 全局回退），不会动态添加字符；缺字不代表动态字体源一定不支持。fallbacks 列表仅展示该字体直接配置的回退资源。
- TMP 自动字号、CanvasScaler、ContentSizeFitter 都会影响最终效果；先读已有配置，再决定如何调整。不要把保存的 RectTransform 数值当成最终屏幕像素。
- 根据目标效果和项目现状自主判断是否需要脚本；交互、动画、动态内容或运行时布局需要代码时，可直接编写或复用脚本，无需用户明确说“写脚本”或为此单独确认。遵守用户明确的范围限制，例如仅制作静态效果或不要脚本，并沿用项目已有组件和架构。目标场景的交互需要 EventSystem 和匹配的输入模块；创建工具不会嵌入 EventSystem。

## 验证与异常

- Agent 自主根据本次改动、用户目标和现有证据选择验证方式、节点范围及次数，无需为常规验证范围逐项询问用户。以能确认改动结果的最少必要检查为目标，用户明确要求的验证范围优先。
- 局部文字、颜色或资源替换，优先确认目标属性或保存结果；仅在排版、溢出、比例或显示效果存在疑问时补看相关画面。字号、换行、尺寸及影响父布局的改动，扩大到目标和受影响的父容器。锚点、布局组或大范围结构调整，检查相关子树，并按适配目标选择代表性分辨率。
- 脚本或交互改动，检查相关编译结果、组件引用，以及被修改的交互和结果断言；新界面根据实际组成选取主要区域与关键流程。已经验证且未受本轮改动影响的区域和流程，复用已有结果。
- 优先复用本轮已有的查询、保存回执和验证结果。仅在信息过期、证据不足、出现失败或改动影响扩大时补充检查。验证足以确认当前目标后结束，避免无新依据地重复全量 inspect、多分辨率截图、Play 测试或编译轮询。
- JSON 业务响应先检查顶层 ok；失败时保留并使用 error、phase、saved、backup_id 等信息。搜索/检查还要逐项检查 results[i].ok。预览成功返回图片，业务失败返回错误 JSON；不能把“工具没有抛异常”当成成功。项目快照、编译状态和 Play 状态没有 ok，按各自字段判断。连接、传输和协议校验异常仍为工具错误。
- 判断需要脚本后，用普通文件编辑能力写 C#，随后刷新、检查编译，再挂脚本和绑定引用。编译空闲或 errors 为空均不能单独证明新脚本编译成功；确认 has_compilation_result、compilation_revision 和刷新状态，具体流程见脚本参考。
- 生成或修改 Controller 等 C# 脚本时，使用正常分行和统一缩进：每条语句独立一行，方法与控制流代码块展开，方法之间留空行，较长的字段声明、对象初始化和表达式按逻辑换行。沿用项目格式约定；没有约定时使用 4 空格缩进，不将整个方法或多个语句压缩到同一行。
- 数据模型与演示假数据独立于 Controller：物品等数据类放到单独文件，假数据由独立的 DemoData/数据提供类创建；Controller 负责交互协调与刷新，不内嵌成批示例记录。沿用项目已有数据来源，演示数据不冒充真实业务数据；创建可变演示数据时，每次返回新的对象，避免不同界面实例共享修改状态。
- 写入已有 Prefab 前使用最近一次 inspect 返回的版本。版本冲突后重新查询，确认变化再提交，不绕过校验。
- 保存后按需要查询目标属性或相关层级，确认本次改动。需要视觉检查时，优先选择最可能暴露问题的目标分辨率；涉及响应式布局或用户要求跨比例适配时，再覆盖相关宽高比，并实际查看图片。
- 查看画面时重点检查本次改动可能影响的文字完整性、对齐、贴边距离、拉伸、遮挡及越界。发现问题后修正并重测受影响项。完成反馈简要说明实际验证范围和仍存在的不确定性，结论与已有证据对应。
- 请求超时或传输中断后，先查询资源和备份列表确认是否已保存或恢复，再决定是否重试。新建时先确认输出是否存在，避免重复创建。
- 首版预览针对屏幕空间 UI，不支持根世界空间 Canvas；无 Canvas 子 Prefab 会放进临时 Canvas。运行时数据和动画不模拟，ExecuteAlways/OnValidate 脚本可能运行，截图不等同于完整游戏运行验证。
- 成功批量编辑会返回 backup_id，指向本次修改前的 Prefab 内容；一次批量只备份一次。需要撤销时先 list_ui_backups 选择目标，再 inspect 获取当前版本，调用 restore_ui_prefab。恢复前也自动备份当前内容，返回的新 backup_id 可用于撤销本次恢复。恢复只替换原路径 Prefab 内容，保留 .meta 和 GUID，不回退图片、字体或嵌套依赖。Library 清理会删除备份；不是长期版本管理或持久 Undo。单次隔离编辑在保存前失败时不写入资源。
- 完成后简要告知资源路径、实际改动和验证情况。仅在实际查看截图或运行验证后，才声称视觉或运行效果已确认。
