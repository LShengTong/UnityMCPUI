# Unity Editor 桥接

这是 Unity MCP 的 Editor-only UPM 包代码。安装和客户端配置见[项目说明](../../README.md)。

后台执行由 `Server/EditorBackgroundLoop` 提供：每 33ms 通过 Unity 内部的线程安全 `EditorApplication.SignalTick` 唤醒编辑器，Unity 资产与场景操作仍由主线程队列执行。整个 Play 会话临时启用 `Application.runInBackground`，退出后恢复原值，SessionState 保留跨程序集重载的恢复信息；原有 run_ui_test 不再单独开关该设置。未暂停的 Play 会额外在主线程调用 QueuePlayerLoopUpdate。此唤醒适配依赖内部 API，方法不可用时输出警告；模态对话框、断点或主线程阻塞仍会阻止执行。它不会把 Unity 窗口切到前台。

字体写接口放在 `Handlers/TypeHandlers`：`CreateTmpFontRequestHandler` 处理 `/ui/fonts/create`，`UpdateTmpFontCharactersRequestHandler` 处理 `/ui/fonts/characters`。两者调用 `Services/TmpFonts/TmpFontService`，由同目录的 TmpFontUtility 执行字形生成与资产保存。请求、响应和路由由协议生成；Python 工具分别为 `create_tmp_font`、`update_tmp_font_characters`。两者都通过主线程资产任务队列执行，仅用于 Edit 模式。创建拒绝覆盖；修改保留 GUID，异常恢复保存前字节。正常完成但源字体缺字或单图集装不下时会保存已生成部分，通过 `all_characters_present` 和 `missing_codepoints` 返回覆盖情况。

## 启动与停止

EditorServerLifecycle 通过 InitializeOnLoad 注册 Editor 更新、程序集重载和退出事件。重载后的首次空闲 update 自动启动，不依赖 delayCall；启动时依次创建任务队列、请求处理器和监听器，订阅 ContextReceived，再打开端口并发布发现文件。启动失败每 2 秒重试，相同错误只输出一次；重载前和退出时禁止再次启动旧域的监听器。Python 的状态查询会在等待期限内重新握手并读取状态，握手后再次断线也会继续等待，写操作不自动重试。

EditorPortListener 仅监听127.0.0.1动态端口，后台接收 HTTP 请求。EditorDiscoveryFile 将 port、token 原子发布到目标项目的 Library/UnityMcp/bridge.json。该文件不传递操作请求。

EditorInstanceInfo 保存协议常量、当前桥接生命周期的 InstanceId 和 Token。程序集重载或退出时退订事件、取消排队任务、关闭监听并清理属于本实例的发现文件；Python 需重新连接。

## 请求与主线程执行

创建 UI prefab 时，根节点仅为 RectTransform，默认全拉伸且边距为零；不会添加 Canvas、CanvasScaler、GraphicRaycaster 或 EventSystem。运行时挂在场景已有 Canvas 或其容器下，缩放和输入沿用场景配置。独立预览临时创建 Canvas，不写入 prefab。协议已移除 reference_width/reference_height，没有 Canvas 根模式。

EditorHttpRequestHandler 检查 Bearer Token，拒绝带 Origin 的请求，然后按路径分发。

- /health、/project/info：返回 Handler 在主线程初始化时捕获的快照，不入队。
- /editor/compilation：直接返回主线程更新的编译快照，不入队，编译期间也能查询。诊断通过 CompilationPipeline 事件收集，SessionState 保留跨重载结果。
- /editor/assets/refresh：通过任务队列在主线程直接执行 AssetDatabase.Refresh，finally 中调用 RefreshReturned，方法返回后再发送成功回执；不使用 delayCall。后续编译和重载状态仍需查询，重载可能中断响应。
- 其他接口：对应 Handler 读取并解析 JSON，检查请求 ID、截止时间和基础参数，再调用 EditorTaskQueue.EnqueueAndRespond。
- EditorTaskQueue.Pending 保存 Func<string> 执行委托和完成结果。容量32，每次主线程 Update 最多执行一个任务；编译、资源更新或 Play 模式期间暂停。
- 队列等待结果超时20秒；取消不能证明已经开始的操作没有完成。EditorHttpResponse 统一返回 JSON 和 X-Unity-Instance 标头。
- 请求体默认上限256 KiB，图片导入单独允许12 MiB传输体（源图片仍限制8 MiB）。

## 当前路由

`Protocol/Generated/EditorRequestType` 和 `EditorRequestRoutes` 从 `protocol/routes.json` 生成，管理“HTTP 方法、路径 → Type”。`Handlers/EditorRequestRegistry` 保持手写，只管理“Type → Handler”及依赖构造；生成器不会覆盖它。入口先解析 Type，再取得 Handler。请求解析、校验和入队继续复用 `HttpRequestUtility`。新增接口需重新生成协议，并手动补充 Handler 绑定；漏绑会在注册表初始化时报错。

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| GET | /health | 实例快照 |
| GET | /project/info | 项目信息及握手 |
| GET | /editor/compilation | 编译、刷新状态和编译错误 |
| POST | /editor/assets/refresh | 请求资源刷新 |
| POST | /ui/assets/find | 统一批量搜索资源 |
| POST | /ui/assets/inspect | 统一批量读取资源详情 |
| POST | /ui/prefabs | 创建新 Prefab |
| POST | /ui/prefabs/batch | 批量编辑已有 Prefab |
| POST | /ui/prefabs/preview | PNG 预览 |
| POST | /ui/images/import | 图片导入为 Sprite |
| POST | /ui/backups/list | 查询备份 |
| POST | /ui/backups/restore | 原地恢复 |

## 目录职责

| 位置 | 职责 |
| --- | --- |
| Handlers/ | 各请求类型对应的 Handler |
| Server/ | Editor 服务生命周期、监听器、发现文件、实例信息和主线程任务队列 |
| Handlers/Base/ | HTTP 鉴权与分发入口、请求接口、通用解析、请求体读取和序列化工具 |
| Protocol/Generated/ | 从共用 JSON Schema 生成的请求、响应、查询参数、详情、批量动作及校验 |
| Services/ | 创建、批量编辑、预览、字体等业务流程，直接返回结构化结果 |
| Services/PrefabUtils/ | 共用的节点构建、布局、资源加载保存、备份和节点工具 |
| Handlers/TypeHandlers/CompilationStatusRequestHandler.cs、RefreshAssetsRequestHandler.cs | 编译状态查询和资源刷新接口 |
| Server/EditorCompilationState.cs | 编译事件监听、错误收集、刷新状态及跨重载状态保存 |
| Handlers/TypeHandlers/CreateUiPrefabRequestHandler.cs | 创建 Handler，引用生成的协议类型 |
| Handlers/TypeHandlers/BatchEditUiPrefabRequestHandler.cs | 批量编辑 Controller，处理 HTTP、调度、响应标识和 JSON |
| Services/BatchEditUiPrefab/ | 批量编辑 Service、BoundOperation、UiPrefabEditScope；Operations/ 放子操作 |
| AssetQueries/ | enum、字符串/enum/Query 注册表、共用搜索与各分类回复 |
| AssetQueries/Base/ | 查询基类、共用搜索、查询结果结构及资源查询工具 |
| AssetQueries/TypeQueries/ | 各资源类型的查询实现 |
| UiPrefabVersion.cs | Handler 与资源查询共用的 Prefab 版本工具 |

Handler 作为 Controller：读取请求、执行生成的协议校验、安排主线程工作，提取业务参数调用 Service，再将业务结果映射为 Reply，填入 id/instance_id 并转 JSON。Service 不接收 Request，也不返回 Reply；结果类型放在对应 Service 目录，不包含 HTTP 标识。UiPrefabSpec、UiBatchOperation、UiTestStep 等业务结构仍直接复用，避免重复定义。Play 状态由 Service 发布结构化快照，Handler 负责响应序列化。CreateUiPrefabService 管理完整创建；UiPrefabBridge 只保留共用节点描述校验和构建。批量编辑的绑定、Operations 和编辑 Scope 跟随 Service 放置；UiNodeUtility 同时被 Play 使用，归入共用 PrefabUtils。备份工具仍供批量编辑、备份列表和恢复共用。简单状态查询没有新增仅转调的 Service；HTTP 基础设施、Play 队列和监听生命周期保留原职责。

资源查询统一支持 ui_prefab、sprite、tmp_font。UiAssetQueryRegistry 的三列表管理字符串、enum 和 Query 的对应关系，各分类负责 Matches、专属检查与详情回复。Prefab 层级/版本在 results[i].prefab 中。

## 新增功能方法

所有接口已迁移到[共用请求与响应协议](../../protocol/README.md)。请求、响应、查询参数、详情、批量动作及共用节点字段从 Schema 生成，不能手改生成代码；业务校验继续手写。无请求体的快照接口使用生成的响应类型。Python 接收时严格校验成功、失败及嵌套响应，身份与资源校验继续保留。

### 新增 HTTP 接口与 MCP 工具

1. 在 `Handlers/TypeHandlers/` 添加实现 `IEditorRequestHandler` 的类。只有一个文件时直接放在该目录；需要专用辅助类时，像 `BatchEditUiPrefab/` 一样建立功能目录。
2. 在 `protocol/*.schema.json` 中添加带 `x-request: true` 的请求对象，定义 ID、截止时间及业务字段。在 `responses.schema.json` 添加带 `x-response: true` 的响应和成功/失败条件；路径不放在 Schema 中。
3. 按下一步在路由表绑定模型，执行 `scripts/update_protocol.py` 生成类型。Handler 引用生成请求和 Reply，定义 `Execute(ExampleRequest)`、`Error(string id, string message)` 和 `Validate(ExampleRequest)`；Validate 先调用 `RequestProtocolValidation.Validate`，再执行额外校验。`Handle` 可直接复用：

   ```csharp
   public Task Handle(HttpListenerContext context) =>
       HttpRequestUtility.Handle<ExampleRequest>(context, tasks, Execute, Error, Validate);
   ```

   `Read<T>` 只读取、反序列化并拒绝空请求；`Handle<T>` 还检查 32 位十六进制 ID、有效截止时间，再调用业务校验和主线程队列。接收时的 `Validate` 不应调用 Unity 主线程 API；资源读取和修改放到 `Execute`。默认请求体限制 256 KiB，确有需要时显式传入 `bodyLimit`。

4. 在 `protocol/routes.json` 添加协议绑定，例如以下名称均为待实现的新功能。更新命令生成枚举、方法路径映射和 Python 模型表：

   ```json
   {
     "name": "Example",
     "method": "POST",
     "path": "/ui/example",
     "request": "ExampleRequest",
     "response": "ExampleReply"
   }
   ```

   随后在手写的 `Handlers/EditorRequestRegistry.cs` 的 entries 中添加 `(EditorRequestType.Example, new ExampleRequestHandler(tasks))`。生成的 `EditorRequestType`、`EditorRequestRoutes` 均放在 `Protocol/Generated/`，不要手改。无请求体的快照协议使用 GET、`request: null`，Handler 如何构造由手写绑定决定。

5. 若要提供给 Agent，在 `src/unity_mcp/bridge.py` 增加对应连接方法，引用生成的 `Endpoints.EXAMPLE`；HTTP 方法和请求、响应类型均从生成表取，不再手写路径。普通 JSON 接口可调用 `_request_ui_query`，复用鉴权、请求 ID、截止时间和实例校验。在 `server.py` 用 `@mcp.tool` 注册工具，通过 `require_editor(ctx)` 获取连接，并按实际副作用填写 `ToolAnnotations`。
6. 更新工具示例、路由表和文档。在 Unity 验证编译及真实接口，重新连接 MCP 使新增工具和参数可见。

### 新增资源查询类型

1. 在 `AssetQueries/UiAssetType.cs` 添加枚举成员。
2. 在 `AssetQueries/TypeQueries/` 添加 `UiAssetQuery` 子类，实现 `Type`、`Filter`、`Matches`、`Inspect`，按需重写 `ValidateInspection`。搜索默认复用基类 `Find`。
3. 在 `UiAssetQueryRegistry` 的三列表中注册协议字符串、枚举和实例：

   ```csharp
   ("example", UiAssetType.Example, new ExampleAssetQuery()),
   ```

4. 在 `responses.schema.json` 的 `UiAssetInspectionResult` 中增加类型详情和成功条件，按现有映射定义具体派生 Reply，重新生成后由查询返回。Find/Inspect Handler 负责最终 JSON；Inspect 按运行时类型逐项序列化，避免 JsonUtility 丢失派生字段。
5. 在 `protocol/requests.schema.json` 同步查询参数的 asset_type 允许值并重新生成。额外业务校验放在对应 Unity Query 的 ValidateInspection/Inspect 中，Python 直接使用生成模型。通常无需新增 HTTP 路由和 MCP 工具，现有 find/inspect 已按类型分发。验证搜索分页、详情、错误类型和 Sprite 等子资源的 `local_id` 行为。

### 扩展批量编辑

- 新增动作：更新协议中的动作对象及 `UiBatchOperation.oneOf`，重新生成后更新 Services/BatchEditUiPrefab 中 BoundOperation 的动作绑定和 Service 的执行分发；子操作放入该目录的 Operations/。业务规则统一在 Unity 执行前校验，Python 直接使用生成模型；同步 server.py 的操作联合类型。新增路径引用应在绑定阶段解析，保留“先绑定、再按顺序执行、最后统一保存”的行为。
- 新增可修改属性：更新 `ModifyUiNodePropertiesOperation` 和协议中 `UiPropertyModification`、`UiPropertyChange` 的 property 允许值，重新生成；补齐类型、范围及组件存在性检查。
- 新增组件：更新 `AddUiComponentOperation` 和协议中的 `UiComponentAddition`，重新生成；补齐组件冲突、引用绑定与参数校验，需要额外节点引用时同步 `Bind`。
- 验证版本冲突、过期请求、目标被前序动作删除、失败时未保存、成功后的版本和备份。保存与备份复用既有工具，不在每个子操作内单独保存。

基础验证命令见[项目说明](../../README.md#检查与验证范围)，连接脚本不覆盖资源操作。真实 JsonUtility 和 Prefab 编辑的 EditMode 测试位于 `../Tests/Editor/`，启用方式见[测试运行说明](../Tests/README.md)。新增业务行为应补充相应的 Unity 测试，并将相关参数和示例同步到 `skills/unity-ui/references/`。

## Prefab 编辑与保存

BatchEditUiPrefabRequestHandler 调用 BatchEditUiPrefabService，后者先通过 BoundOperation 解析全部已有节点引用，再按顺序调度属性、节点、组件、脚本和引用操作。新节点本批不能通过路径引用。Service 管理计数、失败阶段、最终路径及统一保存；Handler 只负责传输和响应封装。

UiPrefabEditScope 管理版本检查、隔离加载、保存前备份、保存及卸载。UiPrefabAssetUtility 在不激活根节点的情况下保存，再恢复持久化资源的激活状态和目标缩放，避免屏幕空间 Canvas 将隔离场景中的驱动缩放写入资源。创建和编辑共用此保存逻辑。

UiRectTransformUtility 共用16种锚点预设。可选 offset 通过显式 has_offset_min/has_offset_max 传输标记判断是否应用，避免 JsonUtility 将缺省嵌套对象误当成用户指定值；这些内部标记由 Python 生成。

UiPrefabBackupStore 将原 Prefab 快照及元数据放在 Library/UnityMcp/Backups。恢复只替换原文件，不改 .meta/GUID，恢复前再次备份；依赖资源不会回退。清理 Library 会丢失备份。

## Play 模式测试

`Handlers/TypeHandlers/PlayMode` 放置七个工具对应的 Handler，`Services/PlayMode` 管理会话、隔离场景、运行时查询和测试执行。HTTP 主线程调度队列位于 `Handlers/Base/PlayRequestQueue.cs`，独立于暂停 Play 执行的资产编辑队列，在主线程调用异步工作并逐帧恢复；排队最多 16 项，HTTP 等待 20 秒，测试执行最多 15 秒。队列与 Service 分别自动初始化，Service 不依赖 Handler 队列。后台唤醒位于 `Server/EditorBackgroundLoop.cs`。原有 prefab 编辑仍不能在 Play 中执行。

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| GET | /editor/play/status | 主线程快照，不入队 |
| POST | /editor/play/enter | 调度进入当前场景的 Play |
| POST | /editor/play/exit | 调度退出 Play |
| POST | /editor/play/inspect | 查询运行时层级、组件和引用 |
| POST | /editor/play/mount | 挂载测试 prefab 到已有 Canvas 下 |
| POST | /editor/play/run | 回调、输入、条件、断言及 Game 截图 |
| POST | /editor/play/clear | 仅清理本次会话工具创建的实例 |

切换由主线程队列直接请求 EnterPlaymode/ExitPlaymode，不依赖 delayCall，回执不表示已进入/退出。enter 默认 mode=isolated：IsolatedPlayScene 记录原场景配置和 playModeStartScene，在 Assets/__UnityMcpPlayScene_GUID 创建临时 UI 场景（Canvas、Content、EventSystem、背景相机），通过公开 playModeStartScene 从该场景进入 Play，原 Edit 场景无需先关闭。未保存/脏场景及 Prefab Mode 会拒绝准备。输入模块在 EnteredPlayMode 添加，启用 Input System 时用其 UI 模块，否则使用 StandaloneInputModule，不引入强制 Input System 程序集依赖。reference_width/height 配置临时 CanvasScaler，默认 1920×1080。

EnteredEditMode 后恢复原启动场景、核对/必要时恢复原场景布局，仅在 GUID 和路径所有权匹配时删除临时资源。恢复遇到缺失资源、当前脏场景或替换过的临时目录时保留记录并在状态中报告，不直接丢弃修改；再次 exit 可请求重试恢复。恢复记录在 SessionState 中跨域重载保留，不保证跨进程崩溃。项目全局初始化代码仍会执行；测试场景不唯一时拒绝自动挂载。

mode=current 保留原场景/原启动场景行为，挂载需显式父路径；isolated 模式可省略 parent_path，默认使用测试 Content。状态包含 test_mode、test_scene_path、test_parent_path、test_scene_error、scene_restore_pending/error。两种模式互切需先退出。SessionState 同时保存测试实例 ID/原生对象 ID，程序集重载后核对 GUID 根名称再恢复引用；退出 Play 清空实例会话。场景查询包含 DontDestroyOnLoad 根，测试内支持相对名称和索引路径；重名时拒绝猜测。源 prefab 根不得有 Canvas，挂载仍需活动 Canvas 与唯一活动 EventSystem/输入模块。

run 等待首帧推进后开始。`pointer_move`、`pointer_down`、`pointer_up`、`pointer_scroll` 向现有 UI 输入模块提供鼠标输入，由 EventSystem 处理射线命中、遮挡、点击、拖拽和滚轮。新版 Input System 使用临时虚拟 Mouse，旧版 StandaloneInputModule 使用临时 BaseInput 提供器。旧版模块需要 Unity 保持焦点，新版后台执行需要 Play 帧正常推进及 UI 动作绑定接受该虚拟鼠标。自定义输入模块暂不支持。

`pointer_move` 的 `x/y` 使用 Game view 左下角起算的像素坐标。指定 `target_path` 时改为相对目标 RectTransform 中心的像素偏移，路径相对于挂载的测试实例。`frames` 为 1～60，支持按住鼠标时跨帧移动。`pointer_down/up` 的 `button` 支持 left、right、middle，默认 left。`pointer_scroll` 的 `scroll_x/y` 使用滚轮刻度，y 正数向上。鼠标位置与按键保持到本次 run 结束，超时、失败、退出及重载时清理。不同 run 之间的拖拽需要合并为一次调用。输入步骤完成后使用 assert/wait_condition 验证业务结果。旧 `invoke_button` 直接调用 onClick，set_input_text 直接设置输入框内容。响应的 interaction_mode 区分 callback、pointer、mixed。

点击按钮可传入以下 steps，拖拽时在 down 和 up 之间插入多帧 pointer_move。

```json
[
  { "action": "pointer_move", "target_path": "Panel/ConfirmButton" },
  { "action": "pointer_down" },
  { "action": "pointer_up" },
  { "action": "assert", "path": "Panel/ConfirmDialog", "property": "active_self", "expected": "true" }
]
```

截图等待推进帧后调用 ScreenCapture，临时 PNG 位于 Library/UnityMcp 并按 GUID 路径清理。Game view 不推进或不能截图时返回失败。游戏脚本可以产生自身副作用。Python 校验截图编码和尺寸后以 JSON 和图片附件返回。

screenshot 的 width/height 为可选整数，必须成对设置为 1～2048；两者为 0 保留当前尺寸。Screen.SetResolution 后等待连续两帧尺寸稳定并更新 Canvas，再捕获并核对 PNG。finally 恢复原宽高/窗口模式并最多额外等待 2 秒确认；重载/退出时另有尽力恢复钩子。这里不操作内部 GameView 预设 API，因此不承诺恢复原 Free Aspect 等下拉模式。切换不生效或恢复失败会报告实际尺寸，不静默回退到静态预览。每次仍限一张截图，多尺寸对同一实例分次运行。

## 静态预览和限制

PreviewUiPrefabRequestHandler 使用 PreviewRenderUtility 的 Game 类型相机，隔离实例化后更新 Canvas、布局和 TMP，再读回 PNG，最后释放临时资源。无 Canvas 子 Prefab 使用临时 Canvas；根世界空间 Canvas 暂不支持。

插件不提供任意 C# 执行接口，内置组件添加使用白名单，自定义脚本通过 add_script 挂载已编译的运行时 MonoBehaviour。业务脚本由 Agent 的文件编辑能力编写；创建工具不自动生成按钮回调或场景 EventSystem。预览实例化仍可能运行项目自带的 ExecuteAlways/OnValidate 脚本，不等于完整游戏运行模拟。

Unity 实测范围随版本推进而变化；基础连接脚本不覆盖所有接口，功能变动后应针对目标 Editor、渲染管线和资源进行联调。
