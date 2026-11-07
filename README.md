# Unity MCP

通过标准 MCP 协议，让兼容的 Agent 客户端查询、创建、编辑和预览 Unity uGUI Prefab。

项目由 Python MCP 服务和 Unity Editor 桥接包组成，不绑定具体客户端或 Unity 项目。服务启动时不选择项目；调用 `connect_project` 后，后续工具复用该 Editor 连接。

## 当前能力

- 批量搜索、读取 UI Prefab、Sprite 和 TMP 字体。
- 按结构化描述创建新的 uGUI Canvas Prefab。
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
| `create_ui_prefab` | 创建新的 uGUI Canvas Prefab，不覆盖已有资源 |
| `batch_edit_ui_prefab` | 按序组合属性修改、节点增删移动和组件添加 |
| `import_ui_image` | 导入本地 PNG/JPEG 为新的单 Sprite 资源 |
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
- 暂不支持挂载任意业务脚本、任意 C# 执行或完整事件绑定。

完整参数以 MCP 工具 schema 为准；示例见 [UI 工具示例](skills/unity-ui/references/examples.md)。

## 配套 skill

[SKILL.md](skills/unity-ui/SKILL.md) 提供 UI 工作流程，[响应式布局说明](skills/unity-ui/references/responsive-layout.md) 提供锚点和多分辨率参考。

skill 与仓库一起分发，不代表客户端自动加载；需要按客户端支持的方式安装或配置。图片生成由 Agent 可用的外部生成能力完成，MCP 本身只负责本地图片导入和 Unity 操作。

## 通信与权限

```text
Agent → MCP stdio → Python EditorConnection → 本机 HTTP
      → Unity Handler → 主线程队列 → Operation → Unity API
```

- Unity 仅监听 `127.0.0.1` 动态端口，通过 `Library/UnityMcp/bridge.json` 公布 port、token。
- Python 用 `/project/info` 握手，核对项目路径、协议、实例和运行版本；后续携带 Token 并校验响应实例。
- `/health` 是可选快照接口，正常连接不需要预先调用。
- 网络接收在后台；需要 Unity API 的操作由 Editor 主线程队列执行。编译、导入和 Play 模式期间暂停执行，排队仍受截止时间限制。
- 队列容量32，等待任务结果超时20秒。超时不证明操作未完成，应先查询输出再决定是否重试。
- 项目详情、统一资源查询和备份列表声明为 MCP 只读工具；审批是否自动放行由客户端策略决定。预览可能执行脚本，不声明纯只读。

## 项目结构

| 路径 | 职责 |
| --- | --- |
| `src/unity_mcp/server.py` | MCP 入口和工具注册 |
| `src/unity_mcp/bridge.py` | Editor 连接、HTTP 请求、身份及路径校验 |
| `src/unity_mcp/ui_schema.py` | UI 创建和批量操作参数 |
| `src/unity_mcp/asset_schema.py` | 资源查询类型 enum 与参数 |
| `src/unity_mcp/image_import.py` | 本地图片读取和格式检查 |
| `unity-package/Editor/Handler/` | HTTP 路由、解析与入队 |
| `unity-package/Editor/Operations/` | 实际操作与批量调度 |
| `unity-package/Editor/Requests/` | Unity 请求数据结构 |
| `unity-package/Editor/AssetQueries/` | 类型注册表及各资源分类的检测、校验和回复 |
| `unity-package/Editor/PrefabUtils/` | 节点构建、锚点、隔离编辑、安全保存、版本及备份 |
| `skills/unity-ui/` | Agent 的 UI 操作指引与示例 |
| `scripts/check_mcp.py` | 基础 MCP 连接流程检查 |

Unity 桥接内部说明见 [Editor/README.md](unity-package/Editor/README.md)。

## 检查与验证范围

基础 MCP 连接检查，默认使用临时项目和 HTTP 模拟服务：

```powershell
.\.venv\Scripts\python.exe scripts/check_mcp.py
```

连接真实运行中的 Editor 检查：

```powershell
.\.venv\Scripts\python.exe scripts/check_mcp.py --project 'D:/path/to/UnityProject'
```

该脚本覆盖连接、切换、失败恢复和进程隔离，不是所有 UI 工具的集成测试。

项目曾对截图及创建/批量保存的边距、根缩放和激活状态做过 Unity 实测，也有编译及模拟测试。后续新增能力、完整资源类型组合、恢复流程和交互行为仍需按具体改动联调；不能将历史检查通过视为全部功能持续通过。
