# 共用请求协议

所有 8 个 JSON 请求均从协议生成，字段、默认值、枚举和基础约束在这里维护。`create_ui_prefab.schema.json` 定义创建请求及共用节点结构；`requests.schema.json` 定义搜索、检查、预览、图片导入、批量编辑、备份列表和恢复请求，以及查询参数和批量动作。无请求体的 GET health/project-info 保持快照实现；响应结构继续由现有业务类型及 Handler 管理。

在项目 Python 环境中执行：

```powershell
.\.venv\Scripts\python.exe scripts/update_protocol.py
```

该入口使用同一个 Python 环境依次生成代码、执行 `generate_protocol.py --check`、运行 `check_protocol.py`；任一步失败立即停止并返回非零退出码。从其他工作目录调用也会自动定位项目根目录。通过后仍需等待 Unity 编译并重连 MCP。

生成器仅依赖标准库。测试使用项目依赖中的 Pydantic、HTTPX、MCP 及 JSON Schema 校验器。生成结果需要提交：

- `src/unity_mcp/generated/requests.py`：Pydantic 字段、约束、枚举、端点模型表及偏移量传输适配。
- `unity-package/Editor/Protocol/Generated/RequestProtocol.cs`：JsonUtility 公开字段类型及基础校验。

不要手动修改生成文件。`--check` 发现输出缺失或过期时返回非零退出码，且不写文件，可加入 CI。生成器读取 `protocol/*.schema.json` 并检查重复定义。新增接口在 Schema 中增加带 `x-request: true`、`x-endpoint` 的对象定义，在两端接入生成类型，再分别注册 HTTP 路由和 MCP 工具。生成器只支持已实现的 Schema 子集；新增关键字时应先扩展生成和测试逻辑。

## 业务校验与传输边界

Python 的 `ui_schema.py`、`asset_schema.py` 复用生成字段，继续维护树深度、总节点数、同级重名、文本节点、ScrollRect 引用以及 UTF-16 长度等业务校验。连接层先执行必要的业务校验，再根据生成的 `REQUEST_MODELS` 组装对应请求。MCP 只暴露业务参数，`id`、`deadline` 仍由连接层自动补齐。

所有普通 Unity Handler 引用生成请求类型，通过 `RequestProtocolValidation.Validate` 校验范围、枚举及嵌套字段，再调用既有业务工具。资源路径、字体、组件、保存和版本等校验仍由业务层负责。

批量操作使用 `oneOf` 定义按 action 区分的五种动作。Python 生成可区分联合类型，每种动作只接受自己的字段；Unity 的 JsonUtility 不支持这种联合，因此生成合并字段的 `UiBatchOperation`，校验时按 action 映射到对应动作结构。节点新增、移动和组件添加直接使用该结构，不再转换为额外的节点操作类型。节点新增之前仍执行子树业务校验，组件添加仍执行 ScrollRect 引用校验。

`offset_min/max` 在协议中是可空字段。Python 向 Unity 发包时根据是否为 null 生成 `has_offset_min/max`，这些是 Unity 传输适配字段，不是 MCP 参数。C# 对缺失的偏移分量仍使用 NaN 哨兵，由校验拒绝不完整的显式偏移。

JsonUtility 不会自动执行 JSON Schema：未知字段和某些“缺失/显式 null”的区别无法仅靠公开字段恢复。Python 入口拒绝未知字段，Unity 仍保留自己的协议及业务校验。生成的字段初始化值与 Python 默认值一致，但不能将独立 C# 测试替代 Unity 内的 JsonUtility 反序列化和保存联调。
