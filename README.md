# Unity MCP（Python）

通过标准 MCP 协议向兼容的客户端提供 Unity Editor 工具。

服务启动时不绑定项目。Agent 首次使用时调用 `connect_project(project_path)` 连接对应的 Unity Editor，后续工具复用 HTTP 连接。

## 当前进度

- Python 3.12 虚拟环境：`.venv`。
- 基础 stdio MCP 服务：`src/unity_mcp/server.py`。
- 已提供 `connect_project`、`unity_project_info`、`create_ui_prefab`，通过本地 HTTP 连接 Editor 并请求创建 uGUI Prefab。
- Unity Editor 插件：`unity-package/`。需先安装到目标项目，等待编译完成并保持 Editor 打开。当前 HTTP 改动尚未验证。
- SDK 暂固定为官方 Python SDK 1.x API（`mcp>=1.20,<2`），避免混用 v2 接口。

## 安装与启动

在当前目录执行（无需激活虚拟环境）：

```powershell
.\.venv\Scripts\python.exe -m pip install -e .
.\.venv\Scripts\python.exe -m unity_mcp.server
```

第二条命令会等待 MCP 客户端从 stdin 发来请求，终端没有欢迎消息是正常行为。
端到端检查（默认使用临时项目与 HTTP health 与项目信息桩服务检查 Python MCP 连接流程，不验证 Unity 插件或 Prefab 生成）：

```powershell
.\.venv\Scripts\python.exe scripts/check_mcp.py
```

也可以连接已安装插件、正在运行的真实 Unity 项目检查连接流程：

```powershell
.\.venv\Scripts\python.exe scripts/check_mcp.py --project '<Unity 项目绝对路径>'
```

检查脚本通过 HTTP 查询 Editor 返回的项目基本信息，不读取版本文件或包清单，也不要求固定 Unity 版本。
UPM 包中的 unity 字段表示插件的最低支持版本，与目标项目选择无关；当前兼容范围尚待 Editor 联调验证。

虚拟环境的解释器仍依赖本机基础 Python 安装；受限环境需允许访问该目录。


## 开发顺序

1. 当前阶段：虚拟环境、依赖、基础工具、MCP 握手。
2. 本地 HTTP 桥接与主线程调度代码已实现，待验证；域重载或 Editor 重启后需重新调用 connect_project。
3. 增加只读工具：Editor 状态、场景层级、控制台日志（有上限与分页）。
4. 增加可撤销编辑：创建对象、设置 Transform；逐步覆盖场景与资源。

桥接只监听回环地址，使用会话令牌并校验项目身份；请求设置超时与大小上限。
多项目运行时必须明确目标。第一版不开放任意 C# 执行。

## 官方资料

- [MCP Python SDK](https://github.com/modelcontextprotocol/python-sdk)
- [Unity UPM 包结构](https://docs.unity3d.com/6000.4/Documentation/Manual/cus-layout.html)

## 客户端接入

服务通过标准 MCP stdio 通信，无需识别客户端名称。
在任意兼容客户端中配置以下启动信息，具体格式由客户端决定：

- 启动程序：本项目 `.venv/Scripts/python.exe` 的绝对路径。
- 参数列表：`["-m", "unity_mcp.server"]`，无需填写 Unity 项目路径。
- 项目路径在调用 `connect_project` 时传入，必须是 Unity 项目根目录的绝对路径。

客户端负责启动服务进程并通过 stdin/stdout 交换 MCP 消息。

## 选择项目与会话

首次操作时，Agent 根据对话的工作区上下文调用：

```text
connect_project(project_path="<Unity 项目根目录的绝对路径>")
unity_project_info()
```

服务不会自动获得对话目录。工具说明与服务指引会引导 Agent 先选择项目；
客户端需将工作区信息提供给 Agent，无法确定路径时应询问用户。
目标项目需安装桥接插件并打开 Editor；连接入口只通过桥接发现信息和 HTTP 握手确认实例。

切换项目时再次调用 connect_project。无效路径不会覆盖之前的选择；
未选择项目时，项目工具会提示先调用 connect_project。
connect_project 读取插件公布的端口，通过 HTTP /project/info 校验项目身份、协议、版本信息和 Editor 实例，不读取项目版本和依赖。连接失败会保留原连接；项目信息由 unity_project_info 按需通过 HTTP 从 Editor 获取，不再读取磁盘上的版本和依赖文件。

当前仅支持 stdio：每个服务进程对应一个 MCP 连接，状态保存在进程内生命周期中，
重启后需要重新选择。客户端应为不同对话启动独立进程；若多个对话共用同一进程，
它们也会共享项目选择，不能将 MCP 连接等同于客户端的对话。

## Unity 本地 HTTP 桥接

在 Unity Package Manager 中选择 Add package from disk，打开 unity-package/package.json。
插件只监听 127.0.0.1 的动态端口，通过 Library/UnityMcp/bridge.json 公布端口、
会话令牌。实例 ID、项目和版本通过首次 HTTP 握手获取。文件仅用于发现服务，操作请求不再通过文件队列传递。

Python 的生命周期状态只保存 EditorConnection（项目路径由连接对象持有）；连接使用 Bearer 令牌，并检查
项目路径与实例身份。关闭 MCP 时释放 HTTP 客户端；Unity 域重载/退出时关闭监听。
重载后调用 connect_project 重新发现端口。不会自动重试创建请求，避免重复生成。

- GET /health：可选的实例身份查询，不返回 ready；Python 不再在请求前调用它。
- GET /project/info：返回项目路径、目录名、运行中的 Unity 版本、实例 ID 和来源 editor；不查询包依赖。
- POST /ui/prefabs：创建新的 Canvas Prefab，最大请求 256 KiB。
- 网络接收在后台执行；实际 Unity API 调用经队列交给 EditorApplication.update 主线程。
- 忙碌、编译、导入或 Play 模式下请求可入队，Unity 就绪后执行；等待仍受超时约束。域重载会关闭服务并取消内存队列，需重新连接。超时后检查输出，再决定是否重试。

支持 panel/image/text/button、TMP 字体、锚点以及横纵布局；不覆盖已有资产，
不生成按钮业务回调。使用时目标场景需有适配其输入系统的 EventSystem。
本次按要求仅修改代码，尚未运行 HTTP 或 Unity 验证。

Ready 仅为 Unity 主线程内部调度状态，不传给 Python。首次连接检查项目、协议、版本信息与实例；未设置具体 Unity 版本兼容限制。后续请求携带 Token，响应使用 X-Unity-Instance 标头核对实例，创建响应同时携带 instance_id。

发现文件现仅包含 port 和 token，文件操作由 EditorDiscoveryFile.cs 管理；退出时按 token 确认归属后删除。


### Unified UI asset queries

`find_ui_assets(queries)` searches `ui_prefab`, `sprite` or `tmp_font`, with independent pagination per query.
`inspect_ui_assets(assets)` reads a mixed batch. Every result has index, asset_type and its own ok/error.
Prefab details live in results[i].prefab (version, nodes, total, truncated); Sprite details in sprite;
TMP font details in font. Use results[i].prefab.version as expected_version for prefab edits/restores.
ui_prefab only matches .prefab assets containing RectTransform, not arbitrary GameObject assets.

Unity's AssetQueries folder holds the enum, registry, shared search/response machinery and three
category implementations. Each category owns matching, specific validation and its inspection reply.
Wire type names remain strings; enum values are internal and never sent as numeric JSON ordinals.

### Modify existing UI prefabs

Call inspect_ui_assets with asset_type=ui_prefab first, then pass results[i].prefab.version as expected_version to batch_edit_ui_prefab.
Changes address existing nodes by index_path; no components or nodes are added or deleted.
Example tool arguments:

```json
{
  "asset_path": "Assets/UI/Menu.prefab",
  "expected_version": "<version from inspect>",
  "operations": [
    {
      "action": "modify",
      "index_path": "0/1",
      "property": "text",
      "value": "Start game"
    },
    {
      "action": "modify",
      "index_path": "0/1",
      "property": "font_size",
      "value": "32"
    },
    {
      "action": "modify",
      "index_path": "0/2",
      "property": "size_delta",
      "value": "{\"x\":320,\"y\":80}"
    }
  ]
}
```

All values are strings. Supported properties: name, active, text, font_size, text_color,
font_path, sprite_path, color, anchored_position, size_delta, anchor_min, anchor_max,
pivot, spacing, padding, interactable. Text settings apply to TMP on the exact addressed node.
The operation checks version/deadline, loads isolated prefab contents, applies up to 128 changes,
then saves once and unloads. Validation failures before saving leave the asset unchanged.
Prefab editing stage must be closed for that asset. Layout groups and TMP auto sizing can override
manual size or font settings; inspect those components first. Re-inspect after success or transport
failure before retrying. There is no automatic retry, disk backup or persistent Undo history.


### Add, delete and move UI nodes

Use `batch_edit_ui_prefab(asset_path, expected_version, operations)` after inspecting the prefab.
Example operations (paths refer to the hierarchy returned by that inspection):

```json
[
  {"action":"add","parent_path":"0","node":{"name":"NewPanel","kind":"panel"}},
  {"action":"move","index_path":"0/1","parent_path":"0/0","sibling_index":0,"world_position_stays":false},
  {"action":"delete","index_path":"0/2"}
]
```

All existing-node paths are resolved before the batch, so earlier edits do not shift their targets.
Operations run in order. sibling_index is the insertion index at that step (after excluding the moved
node for same-parent moves); -1 appends. Newly added nodes cannot be addressed until a new inspection.
Add uses the same node schema/defaults as create_ui_prefab, including child trees. Move retains object
identity and component references. world_position_stays=false retains local RectTransform settings;
true requests preservation of world transform. Layout groups can override final placement.
Delete removes a subtree and references to deleted objects may be lost. Root deletion/move, cycles,
and structural edits involving nested prefab instances/variants are rejected. An invalid batch is
unloaded without saving. The resulting tree is bounded to 2000 nodes and 64 levels. After success,
inspect again to obtain updated paths/version. No automatic retries or persistent Undo history.


### UI prefab screenshots

`preview_ui_prefab(asset_path="Assets/UI/Menu.prefab", width=1280, height=720, background="#202020")`
returns an MCP PNG image, without writing a screenshot file. Request, handler and operation are
separate files named PreviewUiPrefab*. HTTP endpoint: POST /ui/prefabs/preview. Uses the existing
main-thread queue, PreviewRenderUtility and an isolated scene; temporary objects and render targets
are cleaned up. Image dimensions are 64..2048, opaque background only. Canvas-less subprefabs get a
temporary Canvas. Root world-space canvases are currently unsupported. Saved scaling/layout is used;
runtime state, data binding and animations are not simulated. Custom ExecuteAlways/OnValidate scripts
may run when instantiating the preview. Requires Editor graphics support and a client that displays
MCP image content. Unity rendering/pipeline compatibility still requires live Editor verification.

### 导入 UI 图片

使用 `import_ui_image(source_path, asset_path, border=None, pixels_per_unit=100, filter_mode="bilinear")`，将 Python 服务所在机器上的本地 PNG/JPG 传给 Unity，导入为 Single Sprite，返回路径、GUID、源尺寸、Border 和 PPU。

- 输出为全新的 `Assets/.../*.png`、`.jpg` 或 `.jpeg`，扩展名须与内容匹配；拒绝覆盖文件或孤立 `.meta`。
- border 顺序是左、下、右、上，默认全零；两侧总和不能超过图片对应尺寸。
- 支持 bilinear 或 point；关闭 Mipmap，Clamp，FullRect，不压缩，保留 PNG 透明度。
- 最大 8 MiB、8192 像素/边、16 megapixels。Python 预检头部，Unity 在写入前解码验证。
- HTTP 路由是 `POST /ui/images/import`，只对此请求放宽读取上限至 12 MiB（包含 base64）；其他请求保持原限制。
- 失败时尝试清理本次新文件和元数据，已创建的目录可能保留；超时后先检查目标再重试。项目 AssetPostprocessor 仍会运行。

九宫格使用分两步：导入时设置 Border，再给目标 Image 设置 sprite_path 和 `image_type="Sliced"`。属性修改另支持 Simple、Tiled、Filled。首版不包含覆盖更新、图集拆分或网络下载。
### 统一批量编辑

`batch_edit_ui_prefab(asset_path, expected_version, operations)` 支持 modify/add/delete/move 按序组合，HTTP 路由为 `POST /ui/prefabs/batch`。四类实际操作继续各自独立；`BatchEditUiPrefabOperation` 统一预解析路径、调度执行、保存与返回结果。

所有已有节点路径指向批量执行前的层级，新节点暂不能在本批引用。1..128 项操作，成功后保存一次；保存前任一步失败则卸载隔离副本。错误含 failed_operation_index（0-based）、failed_action、phase、saved；没有单项错误时序号为 -1。executed_operations 仅记录副本中已执行的数量。Prefab 编辑只保留 batch_edit_ui_prefab 和 /ui/prefabs/batch 入口；单项编辑传入一个操作即可。UiPrefabEditScope 接收路径、版本和截止时间，不再依赖具体请求 DTO。
### 批量 UI 资源查询

- `find_ui_assets(queries)`：1..16 个 Sprite/TMP 字体搜索条件，asset_type 为 sprite 或 tmp_font，可指定 search、folder、limit、offset；按资源名称字面匹配，每项独立分页。
- `inspect_ui_assets(assets)`：1..32 个资源，指定 asset_type、asset_path，可附 local_id、text、search_fallbacks；读取 Sprite 尺寸、Border、PPU，或字体配置及现有字形覆盖情况。

两者均只读并经主线程队列执行。顶层 ok 表示批次已处理，results 中每项按 index 独立表示成功或错误。搜索范围是 Assets，不包含 Packages。Sprite 图集切片使用字符串 local_id 精确定位；Prefab 图片赋值仍仅支持路径，尚不能指定图集切片 local_id。

字体查询不会向动态图集添加字形。search_fallbacks 可检查 TMP 回退链和全局设置，返回的 fallbacks 仅列直接配置的回退字体。missing_characters 表示当前图集缺少字形，不证明动态字体源无法生成。请求仍受 256 KiB 上限和主线程队列超时约束，可拆小批查询。
### 修改备份与恢复

每次批量编辑调用 UiPrefabEditScope.Save 前，UiPrefabBackupStore 将原 Prefab 内容和元数据写入 Library/UnityMcp/Backups。备份失败则不保存；成功响应返回 backup_id，代表修改前的状态。每批一份备份，包含 UTC 时间、摘要、原 GUID、前后版本和 SHA-256。元数据完成更新失败时返回 backup_warning，快照仍保留。

- list_ui_backups(asset_path, limit=20, offset=0)：按资源原路径分页查询，最新优先。
- restore_ui_prefab(backup_id, expected_version)：校验当前版本、GUID 和备份校验值后原地恢复，只替换 Prefab 内容，保留 .meta；恢复前自动备份当前内容，返回新的 backup_id 和 restored_from。

查询及恢复有独立 Request、Handler、Operation；HTTP 路由为 /ui/backups/list 和 /ui/backups/restore。恢复重新导入失败时尝试使用恢复前快照还原，失败信息携带安全备份 ID。超时后先检查资源再重试。

备份只覆盖 Prefab 文件，不覆盖 Sprite、字体、脚本或嵌套依赖；原资源必须存在且路径/GUID 保持匹配，不支持另存为。首版单个快照最大 64 MiB，无自动保留清理策略；Library 清理会丢失记录，长期历史仍需版本管理。保存前冲突或失败可能留下可用快照，after_version 为空不表示快照损坏。
### 已有节点添加组件

batch_edit_ui_prefab 增加 action=add_component，传入 index_path 和 component。支持 Image、Button、CanvasGroup、LayoutElement、ContentSizeFitter、HorizontalLayoutGroup、VerticalLayoutGroup、GridLayoutGroup、RectMask2D、ScrollRect，实际处理在独立 AddUiComponentOperation 中。

ScrollRect 另传 viewport_path、content_path、horizontal、vertical（默认仅纵向）。引用在批量执行前定位，组件添加按序执行；Viewport 必须有 Graphic 及裁剪组件。不会自动生成 Viewport/Content。Button 要求已有 Graphic；拒绝重复组件和布局/Graphic/Selectable 冲突。目标必须有 RectTransform，不支持任意脚本类型或移除组件。

属性修改补充 LayoutElement 的 preferred/min/flexible 尺寸、ContentSizeFitter 的 fit 模式、GridLayoutGroup 单元尺寸/间距和 ScrollRect 滚动方向。新增组件使用默认配置，再通过 modify 配置已开放属性。新组件也可以通过 inspect 查看，截图和交互仍需在实际目标场景验证。
### 锚点配置

创建/新增节点和批量属性修改共用 UiRectTransformUtility 的16种 anchor 预设：九个固定位置，横向与纵向各三种拉伸，以及 stretch。创建默认仍为 center，Agent 应按实际布局显式指定；skill 已增加锚点选择规则。

创建时，固定轴的 x/y 为位置，拉伸轴的 x/y 为对称边距；offset_min/offset_max 可显式覆盖几何设置。批量 modify 开放 anchor、offset_min、offset_max，保留原有 anchor_min/anchor_max/pivot。预设修改只更新锚点和 pivot，保留 sizeDelta 与 anchoredPosition 数值；需要保持边距时应随后设置 offsets。父级布局依然可驱动子节点 RectTransform，不能通过锚点预设绕过布局控制。
### 查询工具权限提示

项目详情、Prefab 搜索/详情、Sprite/字体搜索/详情和备份列表已通过 MCP ToolAnnotations 声明 readOnlyHint=true、destructiveHint=false、idempotentHint=true、openWorldHint=false。客户端可以据此对只读工具自动放行，skill 也要求在项目和任务明确时直接执行这些查询，不额外口头确认。

连接会改变会话选择；创建、导入、编辑、恢复会改变状态，截图实例化可能执行用户脚本，所以这些工具没有声明纯只读。annotations 是能力提示，不能覆盖客户端的审批策略；更新后需重启/重连 MCP，客户端仍弹框时需在客户端设置只读工具的允许策略。
### 资源查询分类结构

统一入口仅保留 find_ui_assets / inspect_ui_assets。asset_type 可选 ui_prefab、sprite、tmp_font；UI Prefab 搜索仍要求包含 RectTransform，详情包含原有节点、组件和版本信息，每项可指定 max_nodes。

Unity 的 AssetQueries 文件夹包含 UiAssetType enum、UiAssetQueryRegistry 分发、UiAssetQuery 共用搜索/定位、UiAssetQueryReply 批量封装，以及 UiPrefabAssetQuery、SpriteAssetQuery、TmpFontAssetQuery 三个分类。分类各自定义 Matches、专属检查和具体回复，新增分类无需向批量 Operation 塞入详情分支。UiAssetQueryUtility 仅保留公共路径、截止时间及资源身份工具。

回复保持逐项成功/错误：results[i].prefab / sprite / font 为对应详情；Prefab 版本现在是 results[i].prefab.version。原独立 Prefab 查询 MCP 工具、HTTP 路由、Handler、Operation 和请求文件已移除。重启 MCP 刷新工具列表，Unity 需重新编译。