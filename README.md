# Unity MCP

通过标准 MCP 协议，让兼容的 Agent 客户端查询、创建、编辑和预览 Unity uGUI Prefab。

项目由 Python MCP 服务和 Unity Editor 桥接包组成，不绑定具体客户端或 Unity 项目。服务启动时不选择项目；调用 `connect_project` 后，后续工具复用该 Editor 连接。

## 当前能力

- 批量搜索、读取 UI Prefab、Sprite 和 TMP 字体。
- 按结构化描述创建新的 uGUI Prefab，根节点仅使用 RectTransform，挂在场景已有 Canvas 下。
- 批量修改属性、新增/删除/移动节点、添加支持的组件。
- 导入本地 PNG/JPEG 为 Sprite，配置九宫格 Border。
- 在隔离预览场景中按指定分辨率生成 PNG，通过 MCP 返回图片。
- 修改前自动备份，查询备份并按 ID 恢复原资源。
- 配套 UI skill：指导资源复用、图片生成导入、锚点选择和多分辨率检查。

## 环境要求

- Python 3.12 或更高版本；下面的命令以 Windows PowerShell 为例。
- Unity 桥接包声明的最低版本为 Unity 6000.4，依赖 `com.unity.ugui` 2.0.0。
- 目标项目已安装桥接包，并保持 Unity Editor 打开。
- 使用文字节点时，需要可用的 TMP 默认字体，或提供已有 TMP 字体资产路径。

目前主要在 Windows、Unity 6000.4.0f1 上开发和联调，不代表已覆盖所有平台、渲染管线及组件组合。

## 安装 Python 服务

在仓库根目录执行一次：

```powershell
py -3.12 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -e .
```

`-e .` 安装项目及其声明的依赖，源码修改可直接生效，无需每次重新安装。虚拟环境不随仓库分发，下载者需要自行创建。

`pyproject.toml` 定义直接依赖；`requirements.lock.txt` 记录当前开发环境的具体版本，其中包含 Windows 平台依赖，不是通用的跨平台锁文件。需要复现该环境时，可先执行：

```powershell
.\.venv\Scripts\python.exe -m pip install -r requirements.lock.txt
```

## 安装 Unity 桥接包

1. 打开目标 Unity 项目。
2. 在 Package Manager 中选择 **Add package from disk**（从磁盘添加包）。
3. 选择本仓库的 `unity-package/package.json`。
4. 等待资源导入和 C# 编译完成。

这是本地 UPM 包引用，不需要将整个 Python 仓库复制进 Assets。开发过程中若 Unity 尚未加载修改，执行 **Assets → Refresh** 并等待编译。

## 接入 MCP 客户端

在客户端配置启动程序和参数。支持 `mcpServers` 格式的客户端可参考：

```json
{
  "mcpServers": {
    "unity": {
      "command": "D:/path/to/UnityMCP/.venv/Scripts/python.exe",
      "args": ["-m", "unity_mcp.server"]
    }
  }
}
```

将路径替换为本机仓库位置。不同客户端的配置位置和字段可能不同；本项目不识别客户端名称，也不要求在启动参数中绑定 Unity 项目。

客户端会启动服务并通过 stdin/stdout 交换 MCP 消息。手动调试启动命令为：

```powershell
.\.venv\Scripts\python.exe -m unity_mcp.server
```

该命令会等待协议输入，没有欢迎消息是正常行为。新增工具或修改工具参数后，重启或重新连接 MCP，让客户端刷新工具列表。

## 工具列表

| 工具 | 用途 |
| --- | --- |
| `connect_project` | 连接或切换到指定项目的运行中 Editor |
| `unity_project_info` | 读取项目基本信息与运行中的 Unity 版本 |
| `find_ui_assets` | 按条件批量搜索 `ui_prefab`、`sprite`、`tmp_font` |
| `inspect_ui_assets` | 批量读取资源详情、Prefab 层级和版本、字体已有字形覆盖情况 |
| `create_ui_prefab` | 创建 RectTransform 根节点的 uGUI Prefab，可引用已有 UI prefab，不覆盖已有资源 |
| `refresh_assets` | 写入 C# 等资源后请求 Unity 刷新，不代表编译成功 |
| `get_compilation_status` | 查询 / 等待刷新和编译状态，返回编译错误，重载后自动重连同一项目 |
| `get_play_status` | 查询 / 等待 Play 状态、会话、帧数及工具创建的测试实例 |
| `enter_play_mode` / `exit_play_mode` | 默认在独立测试场景进入 Play，可选 current 模式；退出后恢复并清理 |
| `inspect_runtime_ui` | 读取运行时节点、Canvas、EventSystem、组件及序列化引用 |
| `mount_test_prefab` | 将 UI prefab 挂到场景已有 Canvas 或容器下 |
| `run_ui_test` | 运行按钮回调、TMP 输入、条件等待、断言和 Game 截图 |
| `clear_test_instances` | 仅销毁工具创建的测试实例，不保存场景 |
| `batch_edit_ui_prefab` | 按序组合属性修改、节点增删移动、复制、插入已有 prefab 和组件添加 |
| `import_ui_image` | 导入本地 PNG/JPEG 为新的单 Sprite 资源 |
| `create_tmp_font` | 从项目内 TTF/OTF/TTC 创建 TMP 字体资产，可预生成中文字符 |
| `update_tmp_font_characters` | 追加或替换字体字符集，保留资产 GUID |
| `preview_ui_prefab` | 渲染指定分辨率的 Prefab 预览，返回 MCP 图片 |
| `list_ui_backups` | 按 Prefab 路径分页查询修改前快照 |
| `restore_ui_prefab` | 校验当前版本与 GUID 后，按备份 ID 原地恢复 |

旧的独立 Prefab 搜索/详情工具及单独编辑入口已移除，统一使用资源查询和批量编辑。

## 基本使用流程

### 1. 连接项目

调用 `connect_project`：

```json
{
  "project_path": "D:/path/to/UnityProject"
}
```

路径必须是 Unity 项目根目录的绝对路径。MCP 服务不会自动获得对话工作区目录，应由 Agent 根据客户端提供的上下文确定，不能默认使用 MCP 源码目录。

项目选择保存在当前服务进程中。切换项目时再次连接；失败连接不会替换原连接。Unity 程序集重载、Editor 重启或 MCP 进程重启后，需要重新连接。多个对话若共用同一进程，也会共享项目选择。

### 2. 查找并读取资源

调用 `find_ui_assets`：

```json
{
  "queries": [
    {"asset_type": "ui_prefab", "search": "Menu", "limit": 20},
    {"asset_type": "sprite", "search": "Button", "limit": 20},
    {"asset_type": "tmp_font", "search": "Chinese", "limit": 20}
  ]
}
```

每项独立分页，可设置 `folder`（默认 Assets）和 `offset`。`ui_prefab` 只匹配包含 RectTransform 的 Prefab，不代表所有 Prefab。

使用搜索得到的真实路径调用 `inspect_ui_assets`：

```json
{
  "assets": [
    {
      "asset_type": "ui_prefab",
      "asset_path": "Assets/UI/Menu.prefab",
      "max_nodes": 200
    }
  ]
}
```

逐项检查 `results[i].ok/error`。详情分别位于 `results[i].prefab`、`.sprite`、`.font`。Prefab 节点位于 `.prefab.nodes`，后续编辑使用 `.prefab.version`；节点路径以根节点 `0` 和子节点序号表示，例如 `0/1/0`。

### 3. 批量编辑

以下节点路径与版本都应替换为刚查询到的真实值：

```json
{
  "asset_path": "Assets/UI/Menu.prefab",
  "expected_version": "替换为查询返回的64位版本",
  "operations": [
    {"action": "modify", "index_path": "0/1/0", "property": "text", "value": "开始游戏"},
    {"action": "modify", "index_path": "0/2", "property": "anchor", "value": "top_right"}
  ]
}
```

- 支持 `modify`、`add`、`delete`、`move`、`add_component`，每批1..128项。
- 所有已有节点路径在执行前解析，操作按数组顺序执行；前面删除或移动节点不会让后续地址错位。
- 新增节点本批暂不能通过路径引用，需要重新查询后在下一批操作。
- 属性 `value` 是字符串；向量使用 JSON 字符串。文字修改作用于 TMP 所在节点，按钮标签通常是子节点。
- 保存前失败会放弃隔离副本；保存已开始时按响应中的 `saved` 和错误信息判断，不承诺所有写入失败都自动无痕撤销。
- 成功后返回新版本和 `backup_id`。错误中的 `failed_operation_index` 从0开始，`phase` 表示失败阶段。

### 4. 预览与确认

调用 `preview_ui_prefab`：

```json
{
  "asset_path": "Assets/UI/Menu.prefab",
  "width": 1280,
  "height": 720,
  "background": "#202020"
}
```

返回 PNG 图片，不额外写入截图文件。每边支持64..2048像素。响应式布局应按目标范围选择不同宽高比，例如1280×720和1600×1200；同宽高比的两张截图不能替代跨比例检查。

预览不模拟运行时数据或动画，根世界空间 Canvas 暂不支持。实例化可能执行项目的 ExecuteAlways/OnValidate 脚本，不能把截图视为完整的游戏运行验证。

创建时引用已有组件，例如在 `spec.children` 中填写：

```json
{"name":"ConfirmButton","kind":"prefab","prefab_path":"Assets/UI/Common/Button.prefab","x":0}
```

保留嵌套 prefab 关联，未填写的布局字段沿用源资源；仅显式填写的 anchor、width、height、x、y、offset_min/max 会覆盖布局。width/height 表示实际矩形尺寸，x/y 表示 anchoredPosition 分量；源组件外观保留，不支持在引用节点中定义 children/layout。

批量编辑的 `operations` 可加入：

```json
[
  {"action":"duplicate","index_path":"0/0","parent_path":"0","name":"CardCopy"},
  {"action":"instantiate_prefab","prefab_path":"Assets/UI/Common/Button.prefab","parent_path":"0","name":"ConfirmButton","sibling_index":-1}
]
```

省略 name 时自动生成不冲突的名称。复制包含子树及组件；插入保留 prefab 关联和布局。拒绝复制根节点、复制到自身子树、复制嵌套 prefab 内部节点、向嵌套实例内部插入及循环 prefab 依赖。所有已有节点地址在执行前绑定，新节点需下次请求再编辑。成功响应 `created_nodes` 返回仍存在的新建根节点的 `operation_index` 和最终 `index_path`。

创建普通节点和 `batch_edit_ui_prefab` 的 `modify` 均支持以下属性（引用 prefab 的创建节点仍只覆盖显式布局）：

| 分类 | 属性 |
| --- | --- |
| 文字 / 按钮标签 | `text_alignment`、`word_wrap`、`auto_size`、`font_size_min`、`font_size_max`、`text_overflow`、`line_spacing` |
| 图片 | `image_type`、`preserve_aspect`、`fill_method`、`fill_origin`、`fill_amount`、`fill_clockwise`、`raycast_target` |
| 横向 / 纵向布局 | `child_alignment`、`child_control_width/height`、`child_force_expand_width/height`、`child_scale_width/height` |
| Grid 布局 | `grid_cell_size`、`grid_spacing`、`grid_constraint`、`grid_constraint_count`、`grid_start_corner`、`grid_start_axis`，以及 `child_alignment` |

例如创建一个固定四列的网格容器：

```json
{"name":"Inventory","layout":"grid","grid_constraint":"FixedColumnCount","grid_constraint_count":4,"grid_cell_size":{"x":80,"y":80},"grid_spacing":{"x":8,"y":8},"child_alignment":"UpperLeft"}
```

批量修改仍使用字符串 value，例如 `{"action":"modify","index_path":"0/0","property":"fill_amount","value":"0.5"}`。修改只作用于目标节点上的组件；按钮文字通常位于其 Label 子节点。

文字对齐可选 TopLeft、Top、TopRight、Left、Center、Right、BottomLeft、Bottom、BottomRight、Justified、Flush；溢出可选 Overflow、Ellipsis、Masking、Truncate、Page。自动字号范围为 1～512，按保持 min≤max 的顺序设置上下限；line_spacing 范围为 -512～512。

图片填充需 `image_type: "Filled"`。fill_method 可选 Horizontal、Vertical、Radial90、Radial180、Radial360；fill_origin 沿用 Unity 对应方式的原点编号（直线 0～1，径向 0～3），fill_amount 为 0～1。切换为直线填充时，超出范围的旧 origin 会重置为 0。raycast_target 在创建时省略则保留原规则：按钮背景为 true，普通文字/图片为 false。

child_alignment 使用 Unity TextAnchor 名称（如 UpperLeft、MiddleCenter、LowerRight）。grid_constraint 为 Flexible、FixedColumnCount 或 FixedRowCount；grid_constraint_count 为 1～2000，固定列/行模式时生效。grid_start_corner 为 UpperLeft、UpperRight、LowerLeft 或 LowerRight；grid_start_axis 为 Horizontal 或 Vertical。布局父级可能驱动子节点尺寸，Grid 由 cellSize 控制格子尺寸。

脚本驱动的界面可以按以下流程制作：

1. 创建 prefab，再用 inspect 确认节点路径和组件。
2. 使用普通文件编辑能力编写 `Assets/.../*.cs`，定义运行时 MonoBehaviour 及 `[SerializeField]` 字段。
3. 调用 `refresh_assets`，再调用 `get_compilation_status`，可传 `wait_seconds: 60` 等待。确认 is_compiling、is_updating、refresh_pending 都为 false；检查 errors、has_compilation_result 和 compilation_revision。没有观测到完成的编译时不能仅凭错误列表为空认定编译成功；没有代码变化的刷新也可能不产生新一轮编译。
4. 重新 inspect 获取最新版本，用同一个批量请求挂脚本并绑定引用：

```json
[
  {"action":"add_script","index_path":"0","script_path":"Assets/Scripts/UI/DialogView.cs"},
  {"action":"set_reference","index_path":"0","component_type":"MyGame.UI.DialogView","field_path":"confirmButton","target_path":"0/1/0","target_component":"UnityEngine.UI.Button"},
  {"action":"set_reference","index_path":"0","component_type":"MyGame.UI.DialogView","field_path":"content","target_path":"0/1","target_component":"UnityEngine.CanvasGroup"}
]
```

脚本必须已经编译为非抽象、非泛型的运行时 MonoBehaviour，拒绝重复挂载和 Editor-only 脚本。字段使用 C# 序列化字段名，例如 `[SerializeField] private Button confirmButton` 对应 confirmButton。支持单个对象引用及嵌套序列化字段，不支持数组、列表或属性 setter。省略 target_component 则引用目标 GameObject；clear=true 清空字段且无需 target_path。同类型组件存在多个时，用 component_index / target_component_index 指定其在完全相同类型组件中的零基序号。类型名使用完整命名空间。

挂载脚本可能触发其 Editor 回调及 RequireComponent 行为。当前批量的已有节点路径仍在执行前绑定，新节点仍需下一批操作；可以在同批中挂组件后再绑定字段。绑定失败则不保存本批编辑。编译结果最多返回 200 条错误，errors_truncated 表示是否截断；结果通过 SessionState 跨程序集重载保存，但不保证跨 Editor 重启保留。

编译状态查询可在重载后重新读取项目发现文件并更新当前连接；写操作不会自动重试。刷新成功响应表示主线程中的 AssetDatabase.Refresh 已返回，不表示后续编译成功；若响应因重载中断，应先查询状态再决定是否重发。

Play 功能测试可按以下顺序执行：

1. `get_play_status` 记录初始状态。默认 `enter_play_mode` 使用独立模式，创建临时 UI 场景及 Canvas、EventSystem/输入模块、背景相机；原 Edit 场景不关闭、不自动保存。进入前要求已保存且无未保存修改，并关闭 Prefab Mode。通过 `playModeStartScene` 从测试场景启动，不让原场景对象一起运行。reference_width/reference_height（默认 1920×1080）只设置新测试 Canvas 的参考分辨率。
2. 用 `get_play_status(wait_for="playing", wait_seconds=60)` 确认切换完成，并检查 test_mode、test_scene_error。独立模式下，挂载可只传 prefab：

```json
{"prefab_path":"Assets/UI/Dialog.prefab"}
```

需要实际场景集成测试时，显式使用 `enter_play_mode` 的 `mode: "current"`；它沿用原场景/Play 启动场景设置。用 inspect_runtime_ui 查明 Canvas、EventSystem 和容器后，挂载时传父路径：

```json
{"prefab_path":"Assets/UI/Dialog.prefab","parent_path":"UIRoot/Canvas/PanelContainer","scene_name":"Main"}
```

3. 用返回的 test_instance_id 执行 `run_ui_test`：

```json
{
  "test_instance_id":"替换为mount返回的ID",
  "steps":[
    {"action":"invoke_button","path":"OpenButton"},
    {"action":"wait_condition","path":"SettingsPanel","property":"active_in_hierarchy","expected":"true","timeout_seconds":2},
    {"action":"assert","path":"SettingsPanel","property":"active_self","expected":"true"},
    {"action":"screenshot","width":1280,"height":720}
  ]
}
```

条件 expected 一律为字符串，布尔条件使用 "true"/"false"。path 是测试根内的相对名称路径，也支持 "0" 根、"0/1" 索引路径。按钮操作直接调用 onClick，属于 interaction_mode=callback，不验证鼠标射线、遮挡或键盘导航。TMP 输入使用 set_input_text，text 为字符串，notify 控制是否触发值变化通知。reference_is_set 和 reference_path 条件需 component_type、field_path，后者比较 inspect 返回的完整场景路径。

每次最多 32 步，总执行时间最多 15 秒，单次条件等待最多 10 秒；失败停在首个失败步骤并返回 expected/actual/error/frame_count。最多一张真实 Game 截图，Game view 每边需 ≤2048 像素且帧数必须推进；返回 JSON 加图片附件，不是静态 prefab 预览。游戏暂停时先恢复再测试。

screenshot 可成对指定 width/height（1～2048），省略或两者均为 0 则截取当前尺寸。内部使用公开的 Screen.SetResolution，等待目标尺寸连续两帧稳定并更新布局后截图，校验 PNG 尺寸，最后尝试恢复原宽高和窗口模式（恢复最多额外等待 2 秒）。尺寸切换或恢复无法确认时返回失败，不会缩放旧截图冒充目标分辨率。恢复宽高不等于恢复 Game View 原来的 Free Aspect / 固定分辨率下拉预设。多分辨率验证对同一个 test_instance_id 分别调用，例如 1280×720、1600×1200；游戏仍在运行，两次调用之间不会自动冻结或重置状态。

4. `clear_test_instances` 只清理工具创建的对象；如果本次进入了 Play，结束后 `exit_play_mode` 并等待 stopped。退出会结束整个游戏会话，不能把它当作只清理一个测试实例。

挂载需要活动 Canvas 和唯一启用的 EventSystem/输入模块。current 模式使用现成的场景配置；isolated 模式使用工具创建的环境，省略父路径时默认挂到 UnityMcpTestCanvas/Content。测试实例最多 16 个，退出 Play 后旧 ID 失效；为核对所有权，实例根会命名为 __UnityMcpTest_ID，脚本不要重命名该根。查询状态在重载后可自动重连，写操作不自动重试。

退出独立测试会恢复原 Play 启动场景和原场景配置，并删除工具生成的 Assets/__UnityMcpPlayScene_GUID 临时资源。用 get_play_status(wait_for="stopped", wait_seconds=60) 等到恢复完成，scene_restore_pending/error 表示恢复状态；失败时保留恢复记录和临时资源，不丢弃当前未保存修改。修复错误后可再次 exit_play_mode 请求恢复。SessionState 支持跨程序集重载恢复，但不承诺进程崩溃后自动恢复，遗留临时资源需人工核对。切换两种模式前先退出 Play。

独立场景不等于独立进程：RuntimeInitializeOnLoadMethod 等项目全局启动逻辑仍会执行，可能创建额外场景、EventSystem 或常驻对象。检测到非预期场景或输入配置时会报告错误，而不会擅自删掉这些对象。测试会执行 prefab 脚本和回调，可能影响实例外状态；工具不提供游戏沙箱。

### 5. 备份与恢复

每次批量编辑保存前，在目标项目的 `Library/UnityMcp/Backups/` 保存原 Prefab 快照；备份失败则不提交该次保存。

`list_ui_backups` 按原资源路径查询快照。选择 `backup_id` 后，先查询当前 Prefab 版本，再调用 `restore_ui_prefab(backup_id, expected_version)`。

恢复保留 `.meta` 和 GUID，并先备份恢复前状态；返回的新 `backup_id` 可用于撤销本次恢复，`restored_from` 是选中的旧快照。只恢复 Prefab 文件，不回退图片、字体、脚本或嵌套依赖。不支持恢复已删除/移动的原资源或另存为副本。

备份当前无自动清理策略，单个快照最大64 MiB；清理 Library 会丢失备份，长期历史应交给版本管理。

## UI 配置范围

- 创建/新增节点：panel、image、text、button，水平/垂直布局；文字使用 TMP，不生成按钮业务回调。
- 锚点：九个固定位置、横向/纵向各三种拉伸及全拉伸。支持边缘偏移；父级 LayoutGroup 仍可能驱动锚点和尺寸。
- 组件添加：Image、Button、CanvasGroup、LayoutElement、ContentSizeFitter、HorizontalLayoutGroup、VerticalLayoutGroup、GridLayoutGroup、RectMask2D、ScrollRect。拒绝重复和不支持的冲突组合。
- ScrollRect：需显式绑定已存在的 Viewport/Content，不自动生成层级或 EventSystem。
- 图片：本地 PNG/JPEG，最大8 MiB、每边8192像素且不超过16 megapixels；导入为单 Sprite，拒绝覆盖。Border 顺序为左、下、右、上，九宫格 Image 另设 `image_type="Sliced"`。
- 资源查询：搜索范围为 Assets；Sprite 子资源使用 `local_id` 区分，但当前 Prefab 赋值仍只接受 `sprite_path`，不能按 local_id 选择切片。
- 字体检查：检查已有图集字形，可包含 TMP 回退链，不向动态图集加字；缺字不代表字体源一定无法动态生成。
- 字体创建：`create_tmp_font` 从已导入的 Assets 字体文件生成新的 `.asset`，输出目录须存在，拒绝覆盖。需要 Include Font Data 和 TMP Essential Resources。默认 dynamic、48 点采样、1024 单图集、5 像素 padding，并启用构建时清除动态数据；可选 static（必须提供字符）。atlas_size 支持 512/1024/2048。每次最多 4096 UTF-16 字符，忽略换行和制表符、按 Unicode 码点去重。中文可直接传实际 UI 文案，无需生成整个汉字库。
- 字符修改：`update_tmp_font_characters` 默认 append，`mode="replace"` 清空原字形后生成新字符，影响所有使用该字体的 UI；动态字体之后仍可自动生成其他字符。保留 GUID 和原 static/dynamic 模式。仅支持已保存、源字体可用、材质与图集内嵌于同一资产、单图集且每边不超过 2048 的字体，不支持 Dynamic OS。异常时恢复修改前文件；正常完成但缺字时保存部分覆盖，务必检查 `all_characters_present` 和 `missing_codepoints`。这两种方法不修改节点文字，节点换字体仍使用已有 `font_path`。

例如先调用 `create_tmp_font`：

```json
{"source_font_path":"Assets/Fonts/NotoSansSC-Regular.ttf","asset_path":"Assets/Fonts/ChineseUI.asset","characters":"背包装备数量确定取消","population_mode":"static"}
```

新增文案后调用 `update_tmp_font_characters`：

```json
{"asset_path":"Assets/Fonts/ChineseUI.asset","characters":"强化升级","mode":"append"}
```
- 暂不支持挂载任意业务脚本、任意 C# 执行或完整事件绑定。

完整参数以 MCP 工具 schema 为准；示例见 [UI 工具示例](skills/unity-ui/references/examples.md)。

## 配套 skill

[SKILL.md](skills/unity-ui/SKILL.md) 提供 UI 工作流程，[响应式布局说明](skills/unity-ui/references/responsive-layout.md) 提供锚点和多分辨率参考。

skill 与仓库一起分发，不代表客户端自动加载；需要按客户端支持的方式安装或配置。图片生成由 Agent 可用的外部生成能力完成，MCP 本身只负责本地图片导入和 Unity 操作。

## 通信与权限

```text
Agent → MCP stdio → Python EditorConnection → 本机 HTTP
      → Unity Handler.Handle → 主线程队列 → Handler.Execute → Unity API
```

- Unity 仅监听 `127.0.0.1` 动态端口，通过 `Library/UnityMcp/bridge.json` 公布 port、token。
- Python 用 `/project/info` 握手，核对项目路径、协议、实例和运行版本；后续携带 Token 并校验响应实例。
- `/health` 是可选快照接口，正常连接不需要预先调用。
- 网络接收在后台；需要 Unity API 的操作由 Editor 主线程队列执行。编译、导入期间暂停执行，资产编辑队列在 Play 时暂停；Play 工具使用独立队列，排队仍受截止时间限制。
- 桥接包会以约 30Hz 唤醒后台编辑器，整个 Play 会话临时启用后台运行，退出后恢复原值，重载后继续生效，不抢窗口焦点。唤醒依赖 Unity 内部 SignalTick，版本不支持时会输出警告；弹窗、断点或主线程阻塞仍需要处理。首次升级时，旧代码若已卡在后台，需要切回 Unity 完成一次编译。
- 队列容量32，等待任务结果超时20秒。超时不证明操作未完成，应先查询输出再决定是否重试。
- 项目详情、统一资源查询和备份列表声明为 MCP 只读工具；审批是否自动放行由客户端策略决定。预览可能执行脚本，不声明纯只读。

## 项目结构

| 路径 | 职责 |
| --- | --- |
| `src/unity_mcp/server.py` | MCP 入口和工具注册 |
| `src/unity_mcp/bridge.py` | Editor 连接、HTTP 请求、身份及路径校验 |
| `src/unity_mcp/generated/requests.py` | 自动生成的请求模型；UI 业务规则在 Unity 执行前校验 |
| `src/unity_mcp/asset_schema.py` | 资源查询类型 enum 与参数 |
| `src/unity_mcp/image_import.py` | 本地图片读取和格式检查 |
| `unity-package/Editor/Handlers/` | 各请求类型对应的 Handler |
| `unity-package/Editor/Server/` | Editor 服务生命周期、监听器、发现文件、实例信息和主线程任务队列 |
| `unity-package/Editor/Handlers/Base/` | HTTP 鉴权与分发入口、请求接口、通用解析、请求体读取和序列化工具 |
| `protocol/` | 请求与响应 Schema，以及唯一的接口绑定表 `routes.json` |
| `src/unity_mcp/generated/` | 生成的 Python 协议模型 |
| `unity-package/Editor/Protocol/Generated/` | 生成的 C# 请求、响应、共用数据类型及校验 |
| `unity-package/Editor/Handlers/Base/PrefabUtils/` | 多个 Handler 共用的节点构建、布局、保存和备份工具 |
| `unity-package/Editor/Handlers/TypeHandlers/CreateUiPrefabRequestHandler.cs` | 创建 Handler，引用生成的协议类型 |
| `unity-package/Editor/Handlers/TypeHandlers/BatchEditUiPrefab/` | 批量编辑 Handler、编辑生命周期、节点工具及 Operations 子操作；参数引用生成的协议类型 |
| `unity-package/Editor/AssetQueries/` | 类型注册表及各资源分类的检测、校验和回复 |
| `unity-package/Editor/AssetQueries/Base/` | 查询基类、共用搜索、查询结果结构及资源查询工具 |
| `unity-package/Editor/AssetQueries/TypeQueries/` | 各资源类型的查询实现 |
| `unity-package/Editor/UiPrefabVersion.cs` | Handler 与资源查询共用的 Prefab 版本工具 |
| `skills/unity-ui/` | Agent 的 UI 操作指引与示例 |
| `scripts/check_mcp.py` | 基础 MCP 连接流程检查 |
| `unity-package/Tests/Editor/` | Unity EditMode 协议序列化与真实 Prefab 编辑测试 |

请求和响应的字段、默认值及基础约束统一维护在 `protocol/*.schema.json`。`protocol/routes.json` 生成 Python 路径、方法和模型表，以及 Unity 的“方法、路径 → Type”；生成代码集中在两端的 Generated 目录。`Handlers/EditorRequestRegistry.cs` 中的“Type → Handler”保持手写，生成时不会覆盖。连接层引用生成常量，无需重复修改路径。Python 接收响应时严格校验；业务逻辑继续手写。协议约定见 [Protocol README](protocol/README.md)，扩展步骤见 [Editor 扩展说明](unity-package/Editor/README.md#新增功能方法)。

## 检查与验证范围

修改协议后，一键生成两端代码、检查生成文件并运行协议测试，任一步失败即停止：

```powershell
.\.venv\Scripts\python.exe scripts/update_protocol.py
```

生成文件随协议一起提交，不要手改。通过后等待 Unity 编译完成，重连 MCP 刷新参数，并验证真实请求的成功、失败及保存行为。

仓库的 `.github/workflows/protocol-check.yml` 在推送、创建或更新 PR 时自动运行 `generate_protocol.py --check` 和 `check_protocol.py`，也可在 GitHub Actions 页面手动运行。CI 只检查，不重新生成文件；缺失、过期或测试失败时，`Protocol checks` 会报错。将这些文件提交并推送到 GitHub 后生效；若要阻止检查失败的 PR 合并，在分支保护或规则集中把 `Protocol checks` 设为必需检查。本地复现 CI 可先执行 `python -m pip install ".[test]"`，再运行上述两条检查命令。

基础 MCP 连接检查，默认使用临时项目和 HTTP 模拟服务：

```powershell
.\.venv\Scripts\python.exe scripts/check_mcp.py
```

连接真实运行中的 Editor 检查：

```powershell
.\.venv\Scripts\python.exe scripts/check_mcp.py --project 'D:/path/to/UnityProject'
```

该脚本覆盖连接、切换、失败恢复和进程隔离，不是所有 UI 工具的集成测试。

Unity 内的自动化测试位于 `unity-package/Tests/Editor/`，覆盖真实 JsonUtility 解析、Prefab 创建、五种编辑动作、备份恢复和派生响应序列化。按[测试运行说明](unity-package/Tests/README.md)在目标项目启用包测试，然后在 Test Runner 的 EditMode 中运行；当前 GitHub 协议 CI 不运行这些需要 Unity Editor 的测试。

项目曾对截图及创建/批量保存的边距、根缩放和激活状态做过 Unity 实测，也有编译及模拟测试。后续新增能力、完整资源类型组合、恢复流程和交互行为仍需按具体改动联调；不能将历史检查通过视为全部功能持续通过。
