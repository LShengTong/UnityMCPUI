# 工具语义与调用示例

下列 JSON 是工具参数示例，不是 HTTP 请求体。不要自行生成 id 或 deadline，Python 桥接层会添加。路径、节点地址、版本和字体都需替换为当前项目的真实值。

## 创建主菜单

先查找现有菜单以复用项目习惯；若确实要新建，使用未占用的输出路径。下面仅演示一个按钮，不代表完整菜单设计。

工具：`create_ui_prefab`

```json
{
  "output_path": "Assets/UI/MainMenu.prefab",
  "spec": {
    "name": "MainMenu",
    "reference_width": 1920,
    "reference_height": 1080,
    "children": [
      {
        "name": "StartButton",
        "kind": "button",
        "width": 320,
        "height": 80,
        "text": "Start",
        "font_size": 32,
        "color": "#397844",
        "text_color": "#FFFFFF"
      }
    ]
  }
}
```

省略 font_path 使用项目 TMP 默认字体；没有默认字体或缺字时，指定已确认存在的 TMP 字体资源。Button 自动创建 Label 子节点，不在 children 中另加标签。创建节点最多 128 个、嵌套 12 层，text/button 不接受自定义子节点或布局。

## 修改按钮文字与尺寸

先 inspect。假设返回按钮路径为 `0/1`、其 TMP 标签为 `0/1/0`，把实际 version 原样传入 expected_version。

工具：`batch_edit_ui_prefab`

```json
{
  "asset_path": "Assets/UI/MainMenu.prefab",
  "expected_version": "替换为本次查询返回的64位版本",
  "operations": [
    {
      "action": "modify",
      "index_path": "0/1/0",
      "property": "text",
      "value": "开始游戏"
    },
    {
      "action": "modify",
      "index_path": "0/1/0",
      "property": "font_size",
      "value": "32"
    },
    {
      "action": "modify",
      "index_path": "0/1",
      "property": "size_delta",
      "value": "{\"x\":360,\"y\":88}"
    },
    {
      "action": "modify",
      "index_path": "0/1",
      "property": "interactable",
      "value": "true"
    }
  ]
}
```

属性白名单：name、active、text、font_size、text_color、font_path、sprite_path、color、anchored_position、size_delta、anchor_min、anchor_max、pivot、spacing、padding、interactable、image_type。

- value 一律是字符串；布尔值用 `true`/`false`，向量是包含 x/y 的 JSON 字符串。
- padding 是四边统一的整数；spacing 要求已有水平或垂直 LayoutGroup。
- font_path 要求已有 TMP 字体资源；sprite_path 空串表示清除 Sprite。
- 不创建缺失组件；`color` 修改目标 Graphic，`text_color` 明确修改 TMP。
- 字号受 TMP 自动字号影响；尺寸受父布局或 LayoutElement 影响。工具不支持的属性不可伪装成已修改。

## 批量新增、移动与删除

工具：`batch_edit_ui_prefab`

```json
{
  "asset_path": "Assets/UI/MainMenu.prefab",
  "expected_version": "替换为本次查询返回的64位版本",
  "operations": [
    {"action": "add", "parent_path": "0", "node": {"name": "NewPanel", "kind": "panel"}},
    {"action": "move", "index_path": "0/1", "parent_path": "0/0", "sibling_index": 0, "world_position_stays": false},
    {"action": "delete", "index_path": "0/2"}
  ]
}
```

所有 index_path、parent_path 都对应修改前的层级，即使前面的操作改变兄弟顺序，后面的定位仍指向原对象。不能在后续操作引用本批次新建的 NewPanel；需要拆成两批并重新查询。

sibling_index 表示该步骤执行时的插入位置，同父级移动时先排除被移动节点；-1 表示末尾。world_position_stays=false 保留局部变换配置，true 按 Unity 的 SetParent 语义保留世界变换；父布局仍可能重排。

不能移动或删除根节点、形成循环父子关系或编辑嵌套 Prefab 实例内部结构；删除包含嵌套实例的子树也会被拒绝。删除某节点后，同批次继续操作它或已被删除的后代会失败。每批最多 128 项操作，最终层级最多 2000 节点、64 层。

## 预览与反馈

工具：`preview_ui_prefab`

```json
{
  "asset_path": "Assets/UI/MainMenu.prefab",
  "width": 1280,
  "height": 720,
  "background": "#202020"
}
```

返回 MCP PNG 图片，不写截图文件；长宽各为 64..2048 像素，背景不透明。无 Canvas 子 Prefab 用临时 Canvas，预览不模拟运行时状态。客户端不能展示图片时，说明未完成视觉检查，不以调用成功代替图片验证。
## 导入图片并用作九宫格按钮背景

工具：`import_ui_image`

```json
{
  "source_path": "D:/Design/button.png",
  "asset_path": "Assets/UI/Sprites/Button.png",
  "border": [16, 16, 16, 16],
  "pixels_per_unit": 100,
  "filter_mode": "bilinear"
}
```

路径需替换为真实文件。border 按左、下、右、上排列，单位为源图片像素；不需要九宫格时省略。像素画可选 point。首版支持 PNG/JPG，单图最多 8 MiB、每边最多 8192 像素且不超过 16 megapixels；不支持覆盖、下载 URL 或拆分图集。

导入后，查询目标 Prefab 的最新版本和 Image 节点，再调用 `batch_edit_ui_prefab`：

```json
{
  "asset_path": "Assets/UI/MainMenu.prefab",
  "expected_version": "替换为本次查询返回的64位版本",
  "operations": [
    {
      "action": "modify",
      "index_path": "0/1",
      "property": "sprite_path",
      "value": "Assets/UI/Sprites/Button.png"
    },
    {
      "action": "modify",
      "index_path": "0/1",
      "property": "image_type",
      "value": "Sliced"
    }
  ]
}
```

image_type 支持 Simple、Sliced、Tiled、Filled。设置为 Filled 不会自动配置填充方向或进度值。最后按目标分辨率预览，确认九宫格边缘和按钮尺寸。
## 用户没有提供图片时：生成、导入、赋值

例如用户要求“做一个带食材图标的烹饪菜单”，未提供图标。根据项目风格，使用当前可用的图片生成能力生成食材图标；需要独立图标时生成透明 PNG，不把菜单文字画在图标里。以下路径是调用结构示例，以真实生成结果和实际资源目录替换。

先确保生成文件在 Python 服务所在机器可读，再调用 `import_ui_image`：

```json
{
  "source_path": "D:/GeneratedUI/TomatoIcon.png",
  "asset_path": "Assets/UI/Sprites/TomatoIcon.png"
}
```

创建时，将返回的 asset_path 赋给节点的 sprite_path。以下是 `create_ui_prefab` 的 spec.children 节点片段，同样可用作 `batch_edit_ui_prefab` 的 add.node：

```json
{
  "name": "TomatoIcon",
  "kind": "image",
  "width": 96,
  "height": 96,
  "color": "#FFFFFF",
  "sprite_path": "Assets/UI/Sprites/TomatoIcon.png"
}
```

已有 Image 则先查询当前版本和实际节点路径，再调用 `batch_edit_ui_prefab`。假设图标节点为 0/0：

```json
{
  "asset_path": "Assets/UI/CookingMenu.prefab",
  "expected_version": "替换为本次查询返回的64位版本",
  "operations": [
    {
      "action": "modify",
      "index_path": "0/0",
      "property": "sprite_path",
      "value": "Assets/UI/Sprites/TomatoIcon.png"
    },
    {
      "action": "modify",
      "index_path": "0/0",
      "property": "color",
      "value": "#FFFFFF"
    }
  ]
}
```

节点宽高应匹配图片比例；需要改宽高时先检查父布局是否控制尺寸。赋值后重新预览，不把导入成功当成图片已正确显示。创建和新增节点目前没有 image_type 字段，九宫格需在创建后通过属性修改设为 Sliced。
## 组合属性修改、删除与移动

工具：`batch_edit_ui_prefab`。先 inspect，确认下述节点和目标父级确实存在。这里展示 modify → delete → move 的顺序：

```json
{
  "asset_path": "Assets/UI/MainMenu.prefab",
  "expected_version": "替换为本次查询返回的64位版本",
  "operations": [
    {"action": "modify", "index_path": "0/1/0", "property": "text", "value": "开始游戏"},
    {"action": "delete", "index_path": "0/2"},
    {"action": "move", "index_path": "0/3", "parent_path": "0/0", "sibling_index": 0}
  ]
}
```

删除 0/2 后，第三项仍指向修改前的 0/3，不会误定位到原 0/4。所有路径先解析再执行；若先删除 0/1，再修改其子节点 0/1/0，第二步会报错，整批不会保存。新增使用 action=add、parent_path 和 node 字段；新节点本批不可引用。

每批 1..128 项；属性 value 继续使用字符串格式。成功返回新版本、执行数量和 saved=true。失败报告 phase、failed_operation_index（从 0 开始；不对应单项时为 -1）、failed_action 和 error；executed_operations 表示已在隔离副本执行的数量，不代表已写入磁盘。保存前失败 saved=false；保存后结果处理失败可能 saved=true，先重新查询再决定下一步。网络超时也先检查结果，避免重复执行。


## 批量搜索和检查资源

工具：`find_ui_assets`。支持 ui_prefab、sprite、tmp_font。每个搜索条件独立分页，按资源名称进行不区分大小写的字面匹配，只搜索 Assets：

```json
{
  "queries": [
    {
      "asset_type": "ui_prefab",
      "search": "Menu",
      "limit": 20
    },
    {
      "asset_type": "sprite",
      "search": "Button",
      "folder": "Assets",
      "limit": 20
    },
    {
      "asset_type": "tmp_font",
      "search": "Chinese",
      "limit": 20
    }
  ]
}
```

返回 results 与输入顺序一致，每项带 index、ok/error、assets、total、next_offset。某一项失败不影响其他项，next_offset=-1 表示该条件查询结束。最多 16 个搜索条件。Sprite 切片会各自返回 name、asset_path、guid 和字符串 local_id。

工具：`inspect_ui_assets`。路径使用实际搜索结果；同一路径下有多个同类型子资源时，必须携带搜索返回的 local_id：

```json
{
  "assets": [
    {
      "asset_type": "ui_prefab",
      "asset_path": "Assets/UI/MainMenu.prefab",
      "max_nodes": 200
    },
    {
      "asset_type": "sprite",
      "asset_path": "Assets/UI/Sprites/Button.png"
    },
    {
      "asset_type": "tmp_font",
      "asset_path": "Assets/Fonts/Chinese SDF.asset",
      "text": "开始游戏",
      "search_fallbacks": true
    }
  ]
}
```

最多 32 项，每项独立返回详情或错误。ui_prefab 的结果放在 prefab 中，包含 version、nodes、total、truncated；编辑和恢复使用 prefab.version，不是顶层版本。Sprite 返回 rect/size、pivot、border（x/y/z/w 对应左/下/右/上）、PPU、texture_size 等；字体返回族名、样式、动态图集模式和直接回退字体。text_checked=true 时才检查 all_characters_present、missing_characters 和 missing_codepoints；检查只看已存在的字形，不向动态图集加字。换行、回车和 tab 不参与字形检查；text 最多 1024 UTF-16 单元。search_fallbacks=true 使用 TMP 本身的回退链，包括全局回退和默认字体。直接回退列表最多返回 64 项，fallbacks_truncated 表示截断。

参数结构无效会拒绝请求；合法请求中某个路径不存在、类型错误或查不到 local_id 时返回该项错误。批次受请求大小和队列截止时间限制，超时可拆小批重查。图集子 Sprite 的 local_id 目前仅用于查询定位，Prefab 的 sprite_path 赋值尚不能按 local_id 选择子资源。
## 备份与恢复

一次批量编辑保存前会备份原内容，成功返回的 backup_id 指向修改前状态。先用 `list_ui_backups` 查看历史快照（最新优先）：

```json
{
  "asset_path": "Assets/UI/MainMenu.prefab",
  "limit": 20,
  "offset": 0
}
```

每条记录包含 backup_id、asset_path、asset_guid、created_at、summary、before_version、after_version 和内容校验值。after_version 为空可能表示写入尚未完成、保存失败或完成元数据未更新，不代表备份内容一定无效；恢复时仍会检查备份内容校验值。分页 next_offset=-1 表示结束。

选择目标 backup_id 后，用 inspect 获取当前 Prefab 版本，再调用 `restore_ui_prefab`：

```json
{
  "backup_id": "替换为选中快照的32位backup_id",
  "expected_version": "替换为当前Prefab查询返回的64位版本"
}
```

恢复到备份原来的资源路径，原文件必须存在且 GUID 匹配，关闭该资源的 Prefab 编辑窗口。恢复前再次备份当前状态，成功响应中的 backup_id 是这份新备份，restored_from 才是被恢复的旧备份 ID；可用新 ID 撤销本次恢复。

恢复不覆盖 .meta，不回退依赖资源。导入失败会尝试回写恢复前内容；错误中的 rolled_back 表示是否成功还原，saved 表示是否仍留下本次写入。发生错误或传输超时先重新查询状态及备份列表，不能盲目重试。备份位于 Library/UnityMcp/Backups，清理 Library 会丢失；首版无自动清理、不支持恢复已删除/移动的原资源或另存为副本。
## 为已有节点添加组件

通过 `batch_edit_ui_prefab` 添加组件，不需要另开工具。先 inspect 确认节点和现有组件，避免重复添加。例如给已有节点增加 LayoutElement 并设置首选宽度：

```json
{
  "asset_path": "Assets/UI/MainMenu.prefab",
  "expected_version": "替换为当前Prefab查询返回的64位版本",
  "operations": [
    {"action": "add_component", "index_path": "0/1", "component": "LayoutElement"},
    {"action": "modify", "index_path": "0/1", "property": "preferred_width", "value": "360"}
  ]
}
```

白名单：Image、Button、CanvasGroup、LayoutElement、ContentSizeFitter、HorizontalLayoutGroup、VerticalLayoutGroup、GridLayoutGroup、RectMask2D、ScrollRect。目标必须有 RectTransform。拒绝重复组件、已有 Graphic 上再加 Image、已有 Selectable 上加 Button、已有 LayoutGroup 上再加另一种布局、Mask 与 RectMask2D 共存。Button 要求已有 Graphic，自动绑定 targetGraphic 并启用 raycastTarget；可在同一批中先添加 Image，再添加 Button。

ScrollRect 绑定已有层级。例如 ScrollView 为 0/2，Viewport 为 0/2/0，Content 为 0/2/0/0；假设 Viewport 已有 Image 但没有裁剪组件，ScrollView 尚无 ScrollRect：

```json
{
  "asset_path": "Assets/UI/MainMenu.prefab",
  "expected_version": "替换为当前Prefab查询返回的64位版本",
  "operations": [
    {"action": "add_component", "index_path": "0/2/0", "component": "RectMask2D"},
    {
      "action": "add_component",
      "index_path": "0/2",
      "component": "ScrollRect",
      "viewport_path": "0/2/0",
      "content_path": "0/2/0/0",
      "horizontal": false,
      "vertical": true
    }
  ]
}
```

Viewport 必须是 ScrollView 后代，Content 必须是 Viewport 后代；Viewport 需 Graphic 和 RectMask2D/Mask。ScrollRect 添加时自动绑定引用和方向，启用 Viewport 的 raycastTarget，不自动创建滚动条、层级、业务回调或 EventSystem。Content 需要大于视口才有滚动空间，可用布局和 ContentSizeFitter 驱动内容尺寸，但要避免父布局与 Fitter 同时控制同一轴的尺寸。

新增可修改属性：preferred_width、preferred_height、min_width、min_height、flexible_width、flexible_height（LayoutElement，-1 表示不参与该值）；horizontal_fit、vertical_fit（Unconstrained/MinSize/PreferredSize）；grid_cell_size、grid_spacing（JSON Vector2 字符串）；scroll_horizontal、scroll_vertical（布尔字符串）。其他未开放属性仍不能通过工具配置。所有添加和配置复用版本校验、一次保存和自动备份；失败按整批处理。
## 锚点预设与拉伸边距

创建、新增节点的 anchor 与批量 modify 的 anchor 属性使用同样的16种预设：

- 固定位置：top_left、top_center、top_right、center_left、center、center_right、bottom_left、bottom_center、bottom_right。
- 横向拉伸：stretch_horizontal_top、stretch_horizontal_center、stretch_horizontal_bottom。
- 纵向拉伸：stretch_vertical_left、stretch_vertical_center、stretch_vertical_right。
- 双向拉伸：stretch。

例如创建顶部工具条，用节点片段：

```json
{
  "name": "TopBar",
  "kind": "panel",
  "anchor": "stretch_horizontal_top",
  "x": 24,
  "y": -16,
  "height": 80
}
```

这表示左右各留24、顶部向下16、固定高度80；横向拉伸时 width 不参与该轴的最终尺寸。纵向拉伸同理：y 是上下对称边距，x 是横向位置，width 固定。全拉伸时 x/y 都是对称边距。没有父级布局控制时，这些设置随父级尺寸变化。

创建时可直接传 offset_min/offset_max 覆盖计算值。例如全拉伸区域左24、下16、右40、上80：

```json
{
  "name": "ContentArea",
  "kind": "panel",
  "anchor": "stretch",
  "offset_min": {"x": 24, "y": 16},
  "offset_max": {"x": -40, "y": -80}
}
```

已有节点用 batch_edit_ui_prefab；下面展示相同的全拉伸边距：

```json
{
  "asset_path": "Assets/UI/MainMenu.prefab",
  "expected_version": "替换为当前Prefab查询返回的64位版本",
  "operations": [
    {"action": "modify", "index_path": "0/1", "property": "anchor", "value": "stretch"},
    {"action": "modify", "index_path": "0/1", "property": "offset_min", "value": "{\"x\":24,\"y\":16}"},
    {"action": "modify", "index_path": "0/1", "property": "offset_max", "value": "{\"x\":-40,\"y\":-80}"}
  ]
}
```

修改 anchor 预设会同步 pivot，保留 sizeDelta 和 anchoredPosition 数值，因此拉伸预设后要继续设置 offset 或尺寸，不能认为已有可见边界会自动保留。仍可单独修改 anchor_min、anchor_max、pivot 来配置自定义锚点范围。offset_min/offset_max 是 Unity 的边缘偏移，也可影响固定轴上的位置/尺寸；它们不是与布局无关的 padding。

父级 Horizontal/Vertical/GridLayoutGroup 可以驱动子节点锚点和尺寸，覆盖手工配置。创建不再悄悄跳过 stretch 参数，但布局管理结果仍以父布局为准。修改后 inspect 检查实际锚点，并按需求预览不同分辨率；默认 center 只作为参数兼容默认值，不是所有 UI 的布局策略。