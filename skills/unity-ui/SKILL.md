---
name: unity-ui
description: 使用本项目 Unity MCP 查询、创建、修改和预览 uGUI Prefab。适用于菜单、HUD、面板及现有 UI 的布局调整；不用于 UI Toolkit 或 IMGUI 编辑器界面。
---

# Unity UI

通过 Unity MCP 操作当前目标项目的 uGUI Prefab。先理解已有界面，按用户要求修改，再用查询和图片确认结果。工具名可能带客户端前缀，按实际暴露的名称调用。

## 目标与连接

- 从用户明确给出的路径或当前工作区确定 Unity 项目根目录，调用 `connect_project`。没有明确目标时询问，不猜项目，不把 MCP 源码目录当成 Unity 项目。
- 后续复用连接；用户切换项目或 Editor 实例重载后重新连接。
- Unity 侧桥接包必须已安装且 Editor 已打开。缺少工具或连接失败时说明缺失条件，不把未执行的设计描述为已完成的资源。

- 项目与任务范围已明确时，直接执行项目详情、Prefab/资源搜索与详情、备份列表等只读查询，不额外询问“是否允许查询”。这些工具已声明只读 annotations。若客户端自身弹出授权框，遵循客户端权限设置，不声称 skill 或 MCP 标记能绕过授权。截图可能运行预览实例脚本，不属于纯查询标记。

## 选择操作

| 目的 | 工具 | 使用要点 |
| --- | --- | --- |
| 查找 UI 资源 | `find_ui_assets` | 批量搜索 ui_prefab / sprite / tmp_font，各条件独立分页 |
| 查看资源详情 | `inspect_ui_assets` | 批量读取 Prefab / Sprite / TMP 字体，每项独立返回错误 |
| 新建界面 | `create_ui_prefab` | 输出路径必须是尚不存在的 `Assets/.../*.prefab` |
| 组合编辑 | `batch_edit_ui_prefab` | 同一 Prefab 的属性修改和增删移动按顺序执行，成功后保存一次 |
| 导入图片 | `import_ui_image` | 本地 PNG/JPG 导入为新 Sprite，返回路径和 GUID，拒绝覆盖 |
| 查询备份 | `list_ui_backups` | 按 Prefab 路径分页列出修改前快照 |
| 恢复备份 | `restore_ui_prefab` | 选择 backup_id，使用当前版本原地恢复，恢复前再次备份 |
| 看实际效果 | `preview_ui_prefab` | 返回 PNG 图片；按目标分辨率预览 |

工具的实际 schema 是参数依据。复杂调用或首次使用结构编辑时，参阅 [工具语义与示例](references/examples.md)。

- Prefab、图片、字体统一使用 find_ui_assets / inspect_ui_assets，分别传 asset_type=ui_prefab、sprite、tmp_font。先检查 results[i].ok；Prefab 层级和版本在 results[i].prefab.nodes / results[i].prefab.version 中，后者用于 expected_version。max_nodes 控制每个 Prefab 的节点数，prefab.truncated 表示未完整返回。旧独立 Prefab 查询工具已移除。

## 创建与修改

- 用户指定现有界面时，先定位并 inspect；在项目中沿用命名、资源目录、字体、颜色和布局习惯。用户已给准确路径时可直接 inspect，无需强制搜索。
- 明确用户要求的内容、尺寸、分辨率和交互范围。其余细节可以根据上下文合理选择，不因每个视觉细节都中断工作。
- 先用 `find_ui_assets` 查找项目中可复用的 Sprite 和 TMP 字体，再用 `inspect_ui_assets` 比较尺寸、Border、PPU 或检查指定文字的字形。查询结果逐项检查 ok/error，顶层 ok 不代表每项成功。搜索每项独立分页。切片图中的 Sprite 用返回的 local_id 区分；目前 Prefab 赋值工具仅接受 sprite_path，不能按 local_id 选择图集子 Sprite，不要把查询支持当成赋值支持。
- 用户提供本地 PNG/JPG 时，使用 `import_ui_image` 导入为 Sprite；source_path 是 Python 服务所在机器上的绝对路径。Border 顺序为左、下、右、上。九宫格需先设置 Sprite Border，再将已有 Image 的 `image_type` 修改为 `Sliced`；仅指定 sprite_path 不会自动开启九宫格。导入超时后先检查目标文件，避免重复导入。
- 新建只支持 panel、image、text、button 和水平/垂直布局。文字使用 TMP；中文内容需要支持相关字形的字体。创建的是视觉 Prefab，按钮业务回调需要另行实现。
- 同一 Prefab 的相关调整涉及属性和结构时，优先使用 `batch_edit_ui_prefab` 合并请求；不合并无关资源或需要先查看中间结果的操作。全部已有节点路径在执行前统一解析，按数组顺序执行，保存前任一步失败则本批不保存。错误中的 failed_operation_index 为从 0 开始的操作序号，phase 区分准备、定位、执行和保存阶段；saved=true 表示已保存，不能按未执行重试。批量中新建节点暂不支持临时标识，需下批重新查询后引用。
- 修改现有 UI 时优先改目标属性。移动用 move；不以删除重建代替移动，不改动无关节点。
- 已有节点缺少组件时，可在 `batch_edit_ui_prefab` 中先 add_component 再 modify；组件使用明确白名单，拒绝重复组件和同节点多种 LayoutGroup。新建节点本批仍不能通过路径引用。ScrollRect 需要已存在的 Viewport/Content 层级和显式引用，Viewport 需 Image 等 Graphic 与 RectMask2D 或 Mask；该操作不自动生成层级或业务逻辑。
- `batch_edit_ui_prefab` 中 modify 操作的 value 是字符串；数值、布尔值和向量字符串格式见参考示例。按钮文字通常在 TMP 子节点，而非 Button 所在节点。
- `batch_edit_ui_prefab` 的所有路径对应批量执行前的层级；操作按数组顺序执行。新增节点本批次不可通过路径引用。需要继续编辑新节点时，完成本批次后重新 inspect。
- 删除会同时移除子树，可能使其他组件引用丢失；先检查目标范围。仅在用户任务包含删除时执行，不为方便修改扩大删除范围。

## 图片来源、生成与使用

- 当界面需要图标、插画、背景或装饰图片，而用户没有指定图片时，主动根据需求、已有 UI 风格和目标尺寸尝试生成，不默认要求用户提供素材。纯色面板和普通文字直接使用组件即可，无需为了用图片而生成图片。
- 用户已指定素材、品牌资源或明确要求复用项目图片时，优先使用指定内容；不擅自替换。生成前先明确图片用途、比例、风格、透明背景需求，结合现有界面保持一致。
- 使用当前客户端可用的图片生成工具；若提供相应生成 skill，按其指导调用。透明图标和独立装饰优先生成透明 PNG；需要动态文字时保留为 TMP，不把按钮文案烘焙进背景图。
- 将生成结果保存或取得为 Python MCP 服务所在机器可读取的本地 PNG/JPG，再调用 `import_ui_image`。工具返回的 URL、资源 ID 或其他机器上的路径不能直接充当 source_path；先用可用的下载或文件传输能力取得本地文件。不要把图片 base64 填入 Prefab spec。
- 导入目标采用项目已有资源目录和有意义的名称，使用未占用路径。文件超过导入限制时，用可用图片工具调整大小/格式后再导入；仅改扩展名不等于转换格式。
- 创建或新增 image、panel、button 节点时，把导入结果的 asset_path 填入该节点的 `sprite_path`，设置合适的宽高。默认使用 `color="#FFFFFF"` 保留图片原色；Image 的颜色会与 Sprite 相乘，只有有意染色时才用其他颜色。
- 修改已有 Image 时，先 inspect 获取最新版本和节点路径，再用 `batch_edit_ui_prefab` 将 `sprite_path` 设为导入的资源路径。空字符串清除 Sprite。Button 背景位于按钮 Image 节点，文字通常在 TMP 子节点；text 节点本身不能通过 sprite_path 显示图片。
- 九宫格背景导入时设置 Border，再把已有 Image 的 `image_type` 设为 `Sliced`。创建/新增节点的 schema 目前没有 image_type 字段，需先创建，再 inspect，最后修改显示类型。
- 生成并导入后，调用 Prefab 预览并查看实际效果，检查比例、透明边缘、色调和拉伸。未达到效果时按具体问题修正；相同生成或导入失败后，不在条件未变化时重复重试。
- 没有可用生成工具，或生成失败且无法继续时，明确说明原因。能用已有资源或组件继续的部分继续完成，必要的临时占位应明确标记，不把占位描述成已生成素材。
## 锚点与响应式工作流程

- 创建或调整布局前，先确定主要区域在父级变化时应“贴边、居中、拉伸还是由布局排列”。为不受父布局驱动的背景、导航栏、侧栏、内容区、角落按钮显式选择 anchor；不要省略 anchor 后靠默认 center 和大量绝对坐标复刻单张参考图。
- 把贴边/拉伸约束放在合适的区域容器上，容器内部用局部锚点或 LayoutGroup。居中弹窗、居中固定卡片可以使用 center；不要求每个后代都换成拉伸锚点。
- 新建时将选择写进 spec；修改现有界面时先 inspect，必要时通过 modify 的 anchor 及 offsets 设置。先定锚点/Pivot，再定尺寸、边距或布局参数。无法从单张参考图确认响应式规则时，结合区域用途做合理选择，并在多分辨率预览中验证。
- 完成视觉布局前，按下文多分辨率规则预览；发现错位先排查父容器锚点、边距和布局控制，不只修改当前分辨率的 x/y。具体选型、检查步骤和调用示例见 [响应式布局与多分辨率验证](references/responsive-layout.md)。
## 布局判断

- 根据布局意图显式选择 anchor：顶部/底部条用横向拉伸，侧栏用纵向拉伸，角落控件用对应角锚点，全屏背景用 stretch；不要把所有节点都留在默认 center，再靠大幅 x/y 模拟位置。创建和批量修改共用16种预设。检查父级 LayoutGroup 是否控制节点位置、尺寸或锚点；布局管理的子节点优先调整布局和 LayoutElement，而非与其争夺 RectTransform。
- 水平/垂直布局父级下，创建时 width/height 用作 LayoutElement 的首选和最小尺寸。已有节点若由 LayoutElement 控制，只改 size_delta 可能无效；使用 preferred_width/preferred_height 等属性调整布局尺寸，缺少 LayoutElement 时可先添加。
- 创建时 stretch 的 x/y 是横纵对称内边距；横向拉伸用 x 表示左右边距、y 表示纵向位置，height 固定；纵向拉伸用 y 表示上下边距、x 表示横向位置，width 固定。固定锚点下 x/y 为 anchoredPosition。offset_min/offset_max 可覆盖计算出的几何，用于不对称边距。
- 修改预设使用 modify 的 property=anchor，value 为预设名；它设置 anchorMin/anchorMax/pivot 并保留 sizeDelta/anchoredPosition 数值，不保持屏幕边界。先设预设，再设位置/尺寸或 offset_min/offset_max。拉伸时左/下正偏移表示内缩，右/上内缩使用负偏移。具体示例见参考。
- 中文或符号缺字时，用 `inspect_ui_assets` 的 text 和 search_fallbacks 检查。它只检查已有图集字形（可包含 TMP 全局回退），不会动态添加字符；缺字不代表动态字体源一定不支持。fallbacks 列表仅展示该字体直接配置的回退资源。
- TMP 自动字号、CanvasScaler、ContentSizeFitter 都会影响最终效果；先读已有配置，再决定如何调整。不要把保存的 RectTransform 数值当成最终屏幕像素。
- 用户未要求业务逻辑时，不额外添加脚本。目标场景的交互需要 EventSystem 和匹配的输入模块；创建工具不会嵌入 EventSystem。

## 验证与异常

- 写入已有 Prefab 前使用最近一次 inspect 返回的版本。版本冲突后重新查询，确认变化再提交，不绕过校验。
- 成功后重新 inspect，确认目标属性、主要区域的实际锚点和层级。视觉创建或布局调整默认至少使用两个不同宽高比的目标范围内分辨率调用 preview_ui_prefab，并实际查看每张图片；同宽高比的两张缩放图不算跨比例适配验证。优先使用用户给出的目标尺寸；未给出时按横屏/竖屏用途选择合理尺寸。若用户仅要求固定尺寸、纯文本/资源替换或明确暂不验证，可缩小验证范围，并说明实际验证范围。
- 各分辨率逐一检查文字完整性、对齐、贴边距离、拉伸范围、遮挡和内容是否越界；窄屏重点检查导航/按钮溢出。根据观察修正约束或布局，然后重测受影响的尺寸，避免只让一张图好看。完成反馈列出实际预览尺寸及未通过/未验证项，不把单尺寸截图称为响应式验证通过。
- 请求超时或传输中断后，先查询资源和备份列表确认是否已保存或恢复，再决定是否重试。新建时先确认输出是否存在，避免重复创建。
- 首版预览针对屏幕空间 UI，不支持根世界空间 Canvas；无 Canvas 子 Prefab 会放进临时 Canvas。运行时数据和动画不模拟，ExecuteAlways/OnValidate 脚本可能运行，截图不等同于完整游戏运行验证。
- 成功批量编辑会返回 backup_id，指向本次修改前的 Prefab 内容；一次批量只备份一次。需要撤销时先 list_ui_backups 选择目标，再 inspect 获取当前版本，调用 restore_ui_prefab。恢复前也自动备份当前内容，返回的新 backup_id 可用于撤销本次恢复。恢复只替换原路径 Prefab 内容，保留 .meta 和 GUID，不回退图片、字体或嵌套依赖。Library 清理会删除备份；不是长期版本管理或持久 Undo。单次隔离编辑在保存前失败时不写入资源。
- 完成后简要告知资源路径、实际改动和验证情况。仅在实际查看截图或运行验证后，才声称视觉或运行效果已确认。
