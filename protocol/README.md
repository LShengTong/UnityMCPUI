# 共用请求与响应协议

所有 17 个 JSON 请求和 21 个接口的响应均从协议生成，字段、默认值、枚举和基础约束在这里维护。`ui.schema.json` 定义共用 UI 结构 UiOffset、UiPrefabNode、UiPrefabSpec；`requests.schema.json` 定义所有请求，包括 CreateUiPrefabRequest，以及查询参数、批量动作、字体创建/字符修改和 Play 测试步骤，通过跨文件引用复用 UI 结构；`responses.schema.json` 定义成功、失败响应以及资源详情、备份条目、编译诊断、字体字形覆盖、运行时节点及测试结果等嵌套数据。`routes.json` 是路由、方法和模型对应关系的唯一来源；Type 到 Handler 的绑定在 Unity 注册表中手写。无请求体的 GET health/project-info/compilation/play-status 保持快照实现，使用生成的响应类型。

在项目 Python 环境中执行：

```powershell
.\.venv\Scripts\python.exe scripts/update_protocol.py
```

该入口使用同一个 Python 环境依次生成代码、执行 `generate_protocol.py --check`、运行 `check_protocol.py`；任一步失败立即停止并返回非零退出码。从其他工作目录调用也会自动定位项目根目录。通过后仍需等待 Unity 编译并重连 MCP。

生成器仅依赖标准库。测试使用项目依赖中的 Pydantic、HTTPX、MCP 及 JSON Schema 校验器。生成结果需要提交：

- `src/unity_mcp/generated/requests.py`：Pydantic 字段、约束、枚举、端点模型表及偏移量传输适配。
- `unity-package/Editor/Protocol/Generated/RequestProtocol.cs`：JsonUtility 公开字段类型及基础校验。
- `src/unity_mcp/generated/responses.py`：严格类型响应模型、条件校验及 `RESPONSE_MODELS` 端点表。
- `unity-package/Editor/Protocol/Generated/ResponseProtocol.cs`：共用 Reply、详情结构及响应校验。
- `src/unity_mcp/generated/routes.py`：`Endpoints` 路径常量和 `HTTP_METHODS` 方法表。
- `unity-package/Editor/Protocol/Generated/EditorRequestType.cs`：请求枚举。
- `unity-package/Editor/Protocol/Generated/EditorRequestRoutes.cs`：方法、路径到 Type 的对应关系。

不要手动修改生成文件。全部 C# 生成代码位于 `Protocol/Generated/`，Python 生成代码位于 `src/unity_mcp/generated/`。`--check` 检查全部七个生成文件，现有 CI 自动执行。`Handlers/EditorRequestRegistry.cs` 不属于生成输出：其中的 entries 只维护 Type 与 Handler 实例，可手动修改且不会被覆盖。注册表构造时检查空绑定、重复 Type 和漏绑定。

例如预览接口在 `routes.json` 中定义为：

```json
{
  "name": "PreviewUiPrefab",
  "method": "POST",
  "path": "/ui/prefabs/preview",
  "request": "PreviewUiPrefabRequest",
  "response": "PreviewUiPrefabReply"
}
```

`name` 同时生成 C# 枚举和 Python 的 `Endpoints.PREVIEW_UI_PREFAB`。无请求体接口使用 GET 和 `request: null`。当前 Python 模型按路径查找，暂不允许同一路径注册多个方法；每个传输请求、响应都必须恰好被一条路由使用。新增路由生成后，在手写注册表中补一行：

```csharp
(EditorRequestType.PreviewUiPrefab, new PreviewUiPrefabRequestHandler(tasks)),
```

Handler 的构造参数、依赖注入和业务实现由手写代码管理；注册表仍在主线程构造，以支持捕获 Unity 数据的快照 Handler。接口的 HTTP 路径和方法不再手写在该表中。

Python 连接层统一引用生成的 `Endpoints` 和 `HTTP_METHODS`，请求、响应模型表也从同一条路由生成。修改路径或方法只改 `routes.json`，执行更新命令并等待 Unity 编译、重连 MCP 即可；MCP 工具名和业务实现不会自动更名。

生成器内部按阶段组织：`read_definitions` 读取，`normalize_definitions` 统一引用和可空写法，`validate_definitions` 检查支持范围，`validate_routes` 检查路由和模型覆盖，`emit_python`、`emit_csharp` 及路由生成函数分别生成文本。`generate()` 只在内存中汇总七个结果，`write_or_check_outputs` 负责写入或过期检查；修改某种规则时可直接定位对应阶段，命令用法不变。

## 业务校验与传输边界

图片导入和静态预览接口由 server.py 直接调用 EditorConnection.request，再使用 image_utils.py 完成图片转换。image_utils 只提供本地 PNG/JPEG 读取、Base64 编解码和 PNG 头部/结束标记、体积及声明尺寸检查（不是完整图片解码），不依赖 bridge、路由或具体工具接口。静态预览与 Play 截图使用同一检查函数；bridge 保留测试步骤、实例和请求尺寸的对应检查。每次测试的截图次数及宽高组合由 Unity RuntimeUiTestRunner 在执行前检查。

Python 的 EditorConnection.request 统一按生成的路由选择 GET/POST、请求和响应模型，完成请求 ID/截止时间、默认值、体积限制、HTTP 发送、实例/请求身份及响应校验。创建和批量添加节点的显式字段标记也在此统一编码。普通工具由 server.py 组装参数后直接调用 request，不在 bridge 中增加只转调的同名方法；图片导入保留本地文件读取与 Base64 编码，预览/运行时截图保留图片解码及尺寸核对。路径有效性、资源是否存在、拒绝覆盖等执行规则由 Unity 检查，Python 不再单独实现创建请求的发送流程，也不手写批量动作到模型的对应表。写请求不会自动重试，通信失败时仍需先检查目标资源。

Python 的 UI 和资源查询参数直接使用生成模型，检查协议字段类型、枚举和基础约束；树深度、总节点数、同级重名、非空文字、节点类型组合、字号关系及 Grid/ScrollRect 等业务规则统一由 Unity 执行前检查，通过结构化失败响应返回。字体查询的 UTF-16 长度限制由 Unity 的 TmpFontAssetQuery 检查；Python 不再维护 `ui_schema.py`、`asset_schema.py` 包装层。连接层根据生成的 `REQUEST_MODELS` 组装请求，MCP 只暴露业务参数，`id`、`deadline` 仍由连接层自动补齐。

所有普通 Unity Handler 引用生成请求类型，通过 `RequestProtocolValidation.Validate` 校验范围、枚举及嵌套字段，再调用既有业务工具。资源路径、字体、组件、保存和版本等校验仍由业务层负责。

Handler 和查询工具直接构造生成的响应数据，由 Handler 转 JSON；不再重复维护 Reply、资源详情或备份条目的字段定义。Python 的 `validate_response` 根据端点选择生成模型，严格检查字段类型、必填字段、嵌套数据和成功/失败条件，不把字符串数字自动转换成数字，也不修改原始返回字典。`ok=true` 时必须有该接口的结果字段，`ok=false` 时必须有非空错误信息；编辑失败时的 saved、backup_id 等恢复信息仍被保留。HTTP 401、413、500 等传输错误继续由原有 HTTP 异常流程处理。

成功条件用标准 Schema 的 `allOf`、`if`、`then` 表达，生成器同时输出 Python 和 C# 条件校验。响应类型生成不替代业务身份检查：请求 ID、实例 ID、资源路径、PNG 尺寸及输出文件存在性仍由连接层核对。Unity 实际保存或恢复成功与否，仍需业务逻辑和真实 Editor 联调验证。

检查结果保留类型专属数据：Python 的 `UiAssetInspectionResult` 校验 sprite/font/prefab 详情；C# 通过生成的具体派生 Reply 表达这些字段，由 Inspect Handler 按运行时类型序列化，避免 JsonUtility 丢失派生字段。向量字段映射到 Unity 原生 Vector2/3/4，Sprite 的 rect 使用显式的 x/y/width/height 结构，不依赖 Unity 原生 Rect 的内部序列化布局。`x-csharp-base`、`x-csharp-omit` 和 `x-csharp-external` 是这些映射的生成元数据。

批量操作使用 `oneOf` 定义按 action 区分的九种动作：modify、add、delete、move、add_component、duplicate、instantiate_prefab、add_script、set_reference。Python 生成可区分联合类型，每种动作只接受自己的字段；Unity 的 JsonUtility 不支持这种联合，因此生成合并字段的 `UiBatchOperation`，校验时按 action 映射到对应动作结构。节点新增之前仍执行子树业务校验，组件添加仍执行 ScrollRect 引用校验。

`offset_min/max` 在协议中是可空字段。Python 向 Unity 发包时根据是否为 null 生成 `has_offset_min/max`，这些是 Unity 传输适配字段，不是 MCP 参数。C# 对缺失的偏移分量仍使用 NaN 哨兵，由校验拒绝不完整的显式偏移。

创建节点支持 `kind: "prefab"` 和 `prefab_path`，保留源 prefab 的组件、子树和嵌套关联；不接受自定义 children/layout。此类节点只有显式填写的 anchor、width、height、x、y 和 offsets 才覆盖源布局，width/height 设置实际矩形尺寸，x/y 设置 anchoredPosition 分量。Python 保留字段显式性并生成 has_width/height/x/y/anchor；直接发送 Unity HTTP 请求时也需携带对应 has_ 标记。普通节点仍使用原有默认布局。

所有 Python JSON 工具保留 Unity 的完整业务失败响应，调用方检查 `ok`；预览成功返回图片，业务失败返回 JSON。连接、传输及协议校验异常仍为工具错误。批量成功响应的 `created_nodes` 包含新建根节点的 operation_index 和最终 index_path；被后续操作删除的节点不返回，批量执行中仍不能引用新节点。

JsonUtility 不会自动执行 JSON Schema：未知字段和某些“缺失/显式 null”的区别无法仅靠公开字段恢复。Python 入口拒绝未知字段，Unity 仍保留自己的协议及业务校验。生成的字段初始化值与 Python 默认值一致，但不能将独立 C# 测试替代 Unity 内的 JsonUtility 反序列化和保存联调。
