# Unity Editor 桥接

这是 Unity MCP 的 Editor-only UPM 包代码。安装和客户端配置见[项目说明](../../README.md)。

## 启动与停止

EditorServerLifecycle 通过 InitializeOnLoad 注册 Editor 更新、程序集重载和退出事件。启动时依次创建任务队列、请求处理器和监听器，订阅 ContextReceived，再打开端口并发布发现文件。

EditorPortListener 仅监听127.0.0.1动态端口，后台接收 HTTP 请求。EditorDiscoveryFile 将 port、token 原子发布到目标项目的 Library/UnityMcp/bridge.json。该文件不传递操作请求。

EditorInstanceInfo 保存协议常量、当前桥接生命周期的 InstanceId 和 Token。程序集重载或退出时退订事件、取消排队任务、关闭监听并清理属于本实例的发现文件；Python 需重新连接。

## 请求与主线程执行

EditorHttpRequestHandler 检查 Bearer Token，拒绝带 Origin 的请求，然后按路径分发。

- /health、/project/info：返回 Handler 在主线程初始化时捕获的快照，不入队。
- 其他接口：对应 Handler 读取并解析 JSON，检查请求 ID、截止时间和基础参数，再调用 EditorTaskQueue.EnqueueAndRespond。
- EditorTaskQueue.Pending 保存 Func<string> 执行委托和完成结果。容量32，每次主线程 Update 最多执行一个任务；编译、资源更新或 Play 模式期间暂停。
- 队列等待结果超时20秒；取消不能证明已经开始的操作没有完成。EditorHttpResponse 统一返回 JSON 和 X-Unity-Instance 标头。
- 请求体默认上限256 KiB，图片导入单独允许12 MiB传输体（源图片仍限制8 MiB）。

## 当前路由

`EditorRequestType` 定义请求类型；`Handlers/EditorRequestRegistry` 集中注册 HTTP 方法、路径、枚举和实现 `IEditorRequestHandler` 的处理器。入口先解析枚举，再从注册表取得处理器。所有 JSON Handler 使用生成的请求类型，通过 `IEditorRequest` 提供 ID 和截止时间。`HttpRequestUtility.Read<T>` 将 context 中的 JSON 转为指定类型；`Handle<T>` 统一执行公共校验、接口校验、错误响应和主线程入队。具体 Handler 提供 `Validate`、`Execute` 和 `Error`。快照 Handler 直接响应，不入队。新增接口时需添加枚举和注册行，注册表会检查重复与漏注册。

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| GET | /health | 实例快照 |
| GET | /project/info | 项目信息及握手 |
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
| Protocol/Generated/ | 从共用 JSON Schema 生成的全部请求、查询参数、批量动作和基础校验 |
| Handlers/Base/PrefabUtils/ | 多个 Handler 共用的节点构建、布局、保存和备份工具 |
| Handlers/TypeHandlers/CreateUiPrefabRequestHandler.cs | 创建 Handler，引用生成的协议类型 |
| Handlers/TypeHandlers/BatchEditUiPrefab/ | 批量编辑 Handler、编辑生命周期和节点工具；Operations/ 放子操作，参数引用生成的协议类型 |
| AssetQueries/ | enum、字符串/enum/Query 注册表、共用搜索与各分类回复 |
| AssetQueries/Base/ | 查询基类、共用搜索、查询结果结构及资源查询工具 |
| AssetQueries/TypeQueries/ | 各资源类型的查询实现 |
| UiPrefabVersion.cs | Handler 与资源查询共用的 Prefab 版本工具 |

文件按实际调用链归属：`UiPrefabSpec` 仅用于创建，与共用的 `UiPrefabNode`、`UiOffset` 一起由协议生成；`UiPrefabBridge` 的 Create 和 AddNode 分别供创建、批量编辑使用，因此保留为 Handler 共用工具。`UiPrefabNode`、`UiOffset`、`UiRectTransformUtility`、`UiPrefabSaveUtility` 由创建和批量编辑共用；`UiPrefabBackupStore` 由批量编辑、备份列表和恢复共用。`UiPrefabVersion` 同时用于 Prefab 查询、预览、编辑与恢复；`UiAssetQueryUtility` 按资源查询职责归入 AssetQueries/Base，并供相关 Handler 复用。HTTP 响应封装 `EditorHttpResponse` 归于 Handlers/Base；监听器、生命周期、发现文件、实例信息和主线程队列仍是 Editor 桥接基础设施。

资源查询统一支持 ui_prefab、sprite、tmp_font。UiAssetQueryRegistry 的三列表管理字符串、enum 和 Query 的对应关系，各分类负责 Matches、专属检查与详情回复。Prefab 层级/版本在 results[i].prefab 中。

## 新增功能方法

所有 JSON 接口已迁移到[共用请求协议](../../protocol/README.md)。请求、查询参数、批量动作及共用节点字段从 Schema 生成，不能手改生成代码；业务校验继续手写。无请求体的快照接口保持原实现。

### 新增 HTTP 接口与 MCP 工具

1. 在 `Handlers/TypeHandlers/` 添加实现 `IEditorRequestHandler` 的类。只有一个文件时直接放在该目录；需要专用辅助类时，像 `BatchEditUiPrefab/` 一样建立功能目录。
2. 在 `protocol/*.schema.json` 中添加请求对象，设置 `x-request: true` 和 `x-endpoint`，定义 ID、截止时间及业务字段。执行 `scripts/update_protocol.py`，生成的 C# 请求自动实现 `IEditorRequest`；Handler 引用生成类型，不再重复定义 Request。
3. 定义 `Execute(ExampleRequest)`、`Error(string id, string message)` 和 `Validate(ExampleRequest)`；Validate 先调用 `RequestProtocolValidation.Validate`，再执行额外校验。Handler 组装并序列化响应。`Handle` 可直接复用：

   ```csharp
   public Task Handle(HttpListenerContext context) =>
       HttpRequestUtility.Handle<ExampleRequest>(context, tasks, Execute, Error, Validate);
   ```

   `Read<T>` 只读取、反序列化并拒绝空请求；`Handle<T>` 还检查 32 位十六进制 ID、有效截止时间，再调用业务校验和主线程队列。接收时的 `Validate` 不应调用 Unity 主线程 API；资源读取和修改放到 `Execute`。默认请求体限制 256 KiB，确有需要时显式传入 `bodyLimit`。

4. 在 `Handlers/EditorRequestType.cs` 添加枚举成员，并在 `Handlers/EditorRequestRegistry.cs` 添加注册行。例如下面两个名称均为待实现的新功能：

   ```csharp
   ("POST", "/ui/example", EditorRequestType.Example, new ExampleRequestHandler(tasks)),
   ```

   注册表检查重复路由、重复枚举和漏注册。无需修改 `EditorHttpRequestHandler` 的分发逻辑。构造 Handler 在主线程进行；类似 health/project-info 的快照接口可参考现有实现，在构造时捕获数据，`Handle` 直接发送快照，避免后台访问 Unity API。

5. 若要提供给 Agent，在 `src/unity_mcp/bridge.py` 增加对应连接方法，复用现有鉴权、请求 ID、截止时间和实例校验流程；普通 JSON 业务接口可参照 `_request_ui_query` 的调用方式。在 `server.py` 的 `create_server()` 内用 `@mcp.tool` 注册工具，通过 `require_editor(ctx)` 获取连接。按实际副作用填写 `ToolAnnotations`，参数定义放在适当的 schema 文件中。
6. 更新工具示例、路由表和文档。在 Unity 验证编译及真实接口，重新连接 MCP 使新增工具和参数可见。

### 新增资源查询类型

1. 在 `AssetQueries/UiAssetType.cs` 添加枚举成员。
2. 在 `AssetQueries/TypeQueries/` 添加 `UiAssetQuery` 子类，实现 `Type`、`Filter`、`Matches`、`Inspect`，按需重写 `ValidateInspection`。搜索默认复用基类 `Find`。
3. 在 `UiAssetQueryRegistry` 的三列表中注册协议字符串、枚举和实例：

   ```csharp
   ("example", UiAssetType.Example, new ExampleAssetQuery()),
   ```

4. `Inspect` 返回现有 `UiAssetInspectionResult` 或派生结构，按实际类型添加详情字段；查询层返回结构，Find/Inspect Handler 负责最终 JSON 响应。派生项继续经过 `JsonReplySerializer.Array` 逐项序列化，避免丢失派生字段。
5. 在 `protocol/requests.schema.json` 同步查询参数的 asset_type 允许值并重新生成。额外业务校验留在 `asset_schema.py`。通常无需新增 HTTP 路由和 MCP 工具，现有 find/inspect 已按类型分发。验证搜索分页、详情、错误类型和 Sprite 等子资源的 `local_id` 行为。

### 扩展批量编辑

- 新增动作：更新协议中的动作对象及 `UiBatchOperation.oneOf`，重新生成后更新 Handler 的动作白名单、`Bind` 和执行分发；子操作放入 `Operations/`。同步 `ui_schema.py` 的业务校验包装、`bridge.py` 的动作校验映射和 `server.py` 的操作联合类型。新增路径引用应在绑定阶段解析，保留“先绑定、再按顺序执行、最后统一保存”的行为。
- 新增可修改属性：更新 `ModifyUiNodePropertiesOperation` 和协议中 `UiPropertyModification`、`UiPropertyChange` 的 property 允许值，重新生成；补齐类型、范围及组件存在性检查。
- 新增组件：更新 `AddUiComponentOperation` 和协议中的 `UiComponentAddition`，重新生成；补齐组件冲突、引用绑定与参数校验，需要额外节点引用时同步 `Bind`。
- 验证版本冲突、过期请求、目标被前序动作删除、失败时未保存、成功后的版本和备份。保存与备份复用既有工具，不在每个子操作内单独保存。

基础验证命令见[项目说明](../../README.md#检查与验证范围)，该脚本只覆盖连接流程。新增业务行为还需要在真实 Unity 项目中验证，并将相关参数和示例同步到 `skills/unity-ui/references/`。

## Prefab 编辑与保存

BatchEditUiPrefabRequestHandler 先解析全部已有节点引用，再按顺序调度 modify、add、delete、move、add_component。新节点本批不能通过路径引用。

UiPrefabEditScope 管理版本检查、隔离加载、保存前备份、保存及卸载。UiPrefabSaveUtility 在不激活根节点的情况下保存，再恢复持久化资源的激活状态和目标缩放，避免屏幕空间 Canvas 将隔离场景中的驱动缩放写入资源。创建和编辑共用此保存逻辑。

UiRectTransformUtility 共用16种锚点预设。可选 offset 通过显式 has_offset_min/has_offset_max 传输标记判断是否应用，避免 JsonUtility 将缺省嵌套对象误当成用户指定值；这些内部标记由 Python 生成。

UiPrefabBackupStore 将原 Prefab 快照及元数据放在 Library/UnityMcp/Backups。恢复只替换原文件，不改 .meta/GUID，恢复前再次备份；依赖资源不会回退。清理 Library 会丢失备份。

## 预览和限制

PreviewUiPrefabRequestHandler 使用 PreviewRenderUtility 的 Game 类型相机，隔离实例化后更新 Canvas、布局和 TMP，再读回 PNG，最后释放临时资源。无 Canvas 子 Prefab 使用临时 Canvas；根世界空间 Canvas 暂不支持。

插件不执行任意 C#，组件添加限白名单，不生成业务脚本、按钮回调或场景 EventSystem。预览实例化仍可能运行项目自带的 ExecuteAlways/OnValidate 脚本，不等于完整游戏运行模拟。

Unity 实测范围随版本推进而变化；基础连接脚本不覆盖所有接口，功能变动后应针对目标 Editor、渲染管线和资源进行联调。
