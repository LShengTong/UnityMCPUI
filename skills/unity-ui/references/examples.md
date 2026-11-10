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

省略 font_path 使用项目 TMP 默认字体；没有默认字体或缺字时，指定已确认存在的 TMP 字体资源。Button 自动创建 Label 子节点，不在 children 中另加标签。创建节点最多 128 个、嵌套 12 层，text/button 不接受自定义子节点或布局。资源根节点仅为 RectTransform，全拉伸、零边距，使用场景已有 Canvas 和 EventSystem；spec 不再有 reference_width/reference_height 字段。

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

基础可修改属性包括 name、active、text、font_size、text_color、font_path、sprite_path、color、anchored_position、size_delta、anchor、anchor_min、anchor_max、pivot、offset_min、offset_max、spacing、padding、interactable、image_type。文字、图片、布局的扩展属性见下文；完整白名单以实际工具 schema 为准。

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

成功返回 MCP PNG 图片，不写截图文件；业务失败返回 ok=false 的 JSON。长宽各为 64..2048 像素，背景不透明。无 Canvas 子 Prefab 用临时 Canvas，预览不模拟运行时状态。客户端不能展示图片时，说明未完成视觉检查，不以调用成功代替图片验证。
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

image_type 支持 Simple、Sliced、Tiled、Filled。填充可继续设置 fill_method、fill_origin、fill_amount、fill_clockwise，详见下文。最后按目标分辨率预览，确认九宫格边缘和按钮尺寸。
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

节点宽高应匹配图片比例；需要改宽高时先检查父布局是否控制尺寸。赋值后重新预览，不把导入成功当成图片已正确显示。创建和新增普通节点可直接指定 image_type=Sliced，前提是 Sprite 已配置 Border。
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

## 复制节点和引用已有 prefab

创建工具的 spec.children 或批量 add.node 可使用以下节点片段：

```json
{"name":"ConfirmButton","kind":"prefab","prefab_path":"Assets/UI/Common/Button.prefab","x":0}
```

源 prefab 根节点必须是 RectTransform。保留其嵌套关联、组件和子树；不接受 children/layout 覆盖，也不把 text、color 等普通节点字段当成源组件的覆盖能力。仅显式填写的 anchor、width、height、x、y、offset_min/max 修改布局，其他沿用源值。这里 width/height 设置实际矩形尺寸，x/y 设置 anchoredPosition；没有普通 stretch 节点的对称内边距换算。

已有 prefab 中复用节点时，工具为 batch_edit_ui_prefab：

```json
{
  "asset_path":"Assets/UI/Dialog.prefab",
  "expected_version":"替换为最近inspect返回的64位版本",
  "operations":[
    {"action":"duplicate","index_path":"0/0","parent_path":"0","name":"CardCopy"},
    {"action":"instantiate_prefab","prefab_path":"Assets/UI/Common/Button.prefab","parent_path":"0","name":"ConfirmButton","sibling_index":-1}
  ]
}
```

duplicate 复制非根节点及完整子树，instantiate_prefab 插入已有 prefab；默认保留源布局，parent_path 需是已有可编辑父节点。省略 name 时生成不冲突名称。不能复制到自身子树、复制嵌套实例内部节点、向嵌套实例内部插入，或形成 prefab 循环依赖。

成功返回 created_nodes，每项为 operation_index 和最终 index_path；后续操作导致的兄弟顺序变化会反映在最终路径中，被删除的新节点不返回。它不是本批的临时引用机制，后续修改先重新 inspect。脚本挂载和序列化字段绑定见 [脚本工作流](script-workflow.md)。

## 文字、图片和布局属性

以下属性可用于创建普通节点，以及批量 modify。创建时传原生 JSON 类型；modify 的 value 始终是字符串。引用 prefab 的创建节点仅覆盖显式布局；调整其内部属性另用后续批量修改。

| 分类 | 字段及值 |
| --- | --- |
| TMP 文字 | text_alignment：TopLeft、Top、TopRight、Left、Center、Right、BottomLeft、Bottom、BottomRight、Justified、Flush；word_wrap、auto_size：布尔值 |
| 字号和行距 | font_size_min/max：1..512，min≤max；line_spacing：-512..512；text_overflow：Overflow、Ellipsis、Masking、Truncate、Page |
| Image | preserve_aspect、fill_clockwise、raycast_target：布尔值；image_type：Simple、Sliced、Tiled、Filled；fill_amount：0..1 |
| 填充方式 | fill_method：Horizontal、Vertical、Radial90、Radial180、Radial360；fill_origin：直线方式 0..1，径向 0..3，沿用 Unity 原点编号 |
| LayoutGroup | child_alignment：UpperLeft、UpperCenter、UpperRight、MiddleLeft、MiddleCenter、MiddleRight、LowerLeft、LowerCenter、LowerRight |
| 横向 / 纵向布局 | child_control_width/height、child_force_expand_width/height、child_scale_width/height：布尔值 |
| Grid | grid_cell_size、grid_spacing：x/y 对象；grid_constraint：Flexible、FixedColumnCount、FixedRowCount；grid_constraint_count：整数 1..2000 |
| Grid 排列 | grid_start_corner：UpperLeft、UpperRight、LowerLeft、LowerRight；grid_start_axis：Horizontal、Vertical |

表中的 width/height 简写表示两个独立字段，例如 child_control_width、child_control_height。设置自动字号上下限时，每一步都要满足 min≤max；如需提高到超出当前上限的最小字号，先提高上限。切换到 Horizontal/Vertical 填充时，超出范围的旧 fill_origin 会重置为 0。填充显示需 image_type=Filled。

例如创建固定四列的容器及一张进度图，以下为节点片段：

```json
{
  "name":"Grid","layout":"grid","child_alignment":"UpperLeft",
  "grid_constraint":"FixedColumnCount","grid_constraint_count":4,
  "grid_cell_size":{"x":80,"y":80},"grid_spacing":{"x":8,"y":8},
  "children":[
    {"name":"Progress","kind":"image","image_type":"Filled","fill_method":"Horizontal","fill_amount":0.5,"preserve_aspect":true,"raycast_target":false}
  ]
}
```

填充图的 sprite_path 应使用已确认的项目 Sprite，示例省略实际资源路径。Grid 由 cellSize 控制子节点尺寸；child_control/force_expand/scale 只适用于 Horizontal/VerticalLayoutGroup。未存在的组件不会由 modify 自动补齐。

例如修改 TMP 标签排版及进度，工具为 batch_edit_ui_prefab：

```json
{
  "asset_path":"Assets/UI/Dialog.prefab",
  "expected_version":"替换为最近inspect返回的64位版本",
  "operations":[
    {"action":"modify","index_path":"0/0/0","property":"text_alignment","value":"Center"},
    {"action":"modify","index_path":"0/0/0","property":"word_wrap","value":"true"},
    {"action":"modify","index_path":"0/0/0","property":"auto_size","value":"true"},
    {"action":"modify","index_path":"0/0/0","property":"text_overflow","value":"Ellipsis"},
    {"action":"modify","index_path":"0/1","property":"fill_amount","value":"0.75"}
  ]
}
```

创建 button 的文字属性应用于自动生成的 Label；批量修改必须直接定位 TMP 节点。创建时省略 raycast_target 保留默认：按钮背景为 true，其他普通文字和图片为 false；显式设置时按钮字段控制背景，Label 不接收射线。预览后检查文字裁切、自动字号和父布局影响，不能只根据设置值判断效果。

## 主动拆分背包格子并组装

用户要求背包时，先识别格子是重复单元，搜索现有 InventorySlot/ItemSlot 等资源并 inspect。可用时沿用其结构；没有合适资源时，在任务范围内先创建 InventorySlot.prefab，再创建引用它的 InventoryPanel.prefab，不必等待用户明确提出子 prefab。路径按项目目录习惯选择，不覆盖已有资源。

第一步，工具 create_ui_prefab，创建可独立维护的格子；以下仅示意视觉结构，实际 Sprite 和 TMP 字体需先查询确认：

```json
{
  "output_path":"Assets/UI/Inventory/InventorySlot.prefab",
  "spec":{
    "name":"InventorySlot",
    "children":[
      {
        "name":"Background","kind":"image","anchor":"stretch",
        "offset_min":{"x":0,"y":0},"offset_max":{"x":0,"y":0},"color":"#30343B",
        "children":[
          {"name":"Icon","kind":"image","anchor":"stretch","offset_min":{"x":8,"y":8},"offset_max":{"x":-8,"y":-8},"preserve_aspect":true},
          {"name":"Quantity","kind":"text","anchor":"bottom_right","width":48,"height":24,"x":-4,"y":4,"text":"1","font_size":18,"text_color":"#FFFFFF","text_alignment":"BottomRight"}
        ]
      }
    ]
  }
}
```

格子根只有 RectTransform，默认全拉伸；图标、数量等位于它的子树中。先检查格子资源的层级和属性，再组装父界面。数量和图标若需脚本驱动，可先为子 prefab 添加 ItemSlotView 并绑定内部引用，然后创建父 prefab，避免在每个实例重复添加同一套组件。

第二步，工具 create_ui_prefab，下面用三个实例演示固定布局，实际格子数按需求确定：

```json
{
  "output_path":"Assets/UI/Inventory/InventoryPanel.prefab",
  "spec":{
    "name":"InventoryPanel",
    "children":[
      {
        "name":"Slots","kind":"panel","anchor":"stretch",
        "offset_min":{"x":24,"y":24},"offset_max":{"x":-24,"y":-24},
        "layout":"grid","grid_constraint":"FixedColumnCount","grid_constraint_count":3,
        "grid_cell_size":{"x":80,"y":80},"grid_spacing":{"x":8,"y":8},"child_alignment":"UpperLeft",
        "children":[
          {"name":"Slot01","kind":"prefab","prefab_path":"Assets/UI/Inventory/InventorySlot.prefab"},
          {"name":"Slot02","kind":"prefab","prefab_path":"Assets/UI/Inventory/InventorySlot.prefab"},
          {"name":"Slot03","kind":"prefab","prefab_path":"Assets/UI/Inventory/InventorySlot.prefab"}
        ]
      }
    ]
  }
}
```

Grid 控制实例格子尺寸；格子内部背景随根拉伸。不要在引用节点补默认宽高来覆盖模板，也不要把子 prefab 展开为重复的 Background/Icon/Quantity 树。共同样式保留在源 InventorySlot，个别图标或数量用脚本数据或实例属性覆盖。所有依赖须先创建成功，父资源才引用它们，不产生循环依赖。

如果数量随数据变化，使用项目已有的资源加载/对象池机制，或在父界面中放一个模板实例、将其设为 inactive，再用 set_reference 把该 GameObject/组件绑定给控制脚本。模板可放在 Grid 容器外，运行时克隆到 Grid、绑定数据并激活。当前 set_reference 只能绑定 prefab 内的节点或组件，不能直接赋 InventorySlot.prefab 资源；不要虚构资源引用参数。不要同时保留完整静态格子组再由脚本生成同一批条目。

完成后 inspect 父资源确认嵌套实例和布局，并预览；涉及数据填充或交互时按脚本/Play 流程验证。反馈列出父、子 prefab 路径及各自职责，不把嵌套关联或运行时行为仅凭创建成功判定为验证通过。
