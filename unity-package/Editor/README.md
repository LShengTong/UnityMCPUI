# Unity Editor 桥接

这是 Unity MCP 的 Editor-only UPM 包代码。安装和客户端配置见[项目说明](../../README.md)。

## 启动与停止

EditorServerLifecycle 通过 InitializeOnLoad 注册 Editor 更新、程序集重载和退出事件。启动时依次创建任务队列、请求处理器和监听器，订阅 ContextReceived，再打开端口并发布发现文件。

EditorPortListener 仅监听127.0.0.1动态端口，后台接收 HTTP 请求。EditorDiscoveryFile 将 port、token 原子发布到目标项目的 Library/UnityMcp/bridge.json。该文件不传递操作请求。

EditorInstanceInfo 保存协议常量、当前桥接生命周期的 InstanceId 和 Token。程序集重载或退出时退订事件、取消排队任务、关闭监听并清理属于本实例的发现文件；Python 需重新连接。

## 请求与主线程执行

EditorHttpRequestHandler 检查 Bearer Token，拒绝带 Origin 的请求，然后按路径分发。

- /health、/project/info：返回 Operation 在主线程初始化时捕获的快照，不入队。
- 其他接口：对应 Handler 读取并解析 JSON，检查请求 ID、截止时间和基础参数，再调用 EditorTaskQueue.EnqueueAndRespond。
- EditorTaskQueue.Pending 保存 Func<string> 执行委托和完成结果。容量32，每次主线程 Update 最多执行一个任务；编译、资源更新或 Play 模式期间暂停。
- 队列等待结果超时20秒；取消不能证明已经开始的操作没有完成。EditorHttpResponse 统一返回 JSON 和 X-Unity-Instance 标头。
- 请求体默认上限256 KiB，图片导入单独允许12 MiB传输体（源图片仍限制8 MiB）。

## 当前路由

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
| Handler/ | HTTP 请求解析、基础校验和入队 |
| Requests/ | 可序列化请求数据 |
| Operations/ | 具体操作和批量调度，不依赖 HttpListenerContext |
| AssetQueries/ | enum、字符串/enum/Query 注册表、共用搜索与各分类回复 |
| PrefabUtils/ | 构建、锚点、定位、编辑生命周期、安全保存、版本及备份 |

资源查询统一支持 ui_prefab、sprite、tmp_font。UiAssetQueryRegistry 的三列表管理字符串、enum 和 Query 的对应关系，各分类负责 Matches、专属检查与详情回复。Prefab 层级/版本在 results[i].prefab 中。

## Prefab 编辑与保存

BatchEditUiPrefabOperation 先解析全部已有节点引用，再按顺序调度 modify、add、delete、move、add_component。新节点本批不能通过路径引用。

UiPrefabEditScope 管理版本检查、隔离加载、保存前备份、保存及卸载。UiPrefabSaveUtility 在不激活根节点的情况下保存，再恢复持久化资源的激活状态和目标缩放，避免屏幕空间 Canvas 将隔离场景中的驱动缩放写入资源。创建和编辑共用此保存逻辑。

UiRectTransformUtility 共用16种锚点预设。可选 offset 通过显式 has_offset_min/has_offset_max 传输标记判断是否应用，避免 JsonUtility 将缺省嵌套对象误当成用户指定值；这些内部标记由 Python 生成。

UiPrefabBackupStore 将原 Prefab 快照及元数据放在 Library/UnityMcp/Backups。恢复只替换原文件，不改 .meta/GUID，恢复前再次备份；依赖资源不会回退。清理 Library 会丢失备份。

## 预览和限制

PreviewUiPrefabOperation 使用 PreviewRenderUtility 的 Game 类型相机，隔离实例化后更新 Canvas、布局和 TMP，再读回 PNG，最后释放临时资源。无 Canvas 子 Prefab 使用临时 Canvas；根世界空间 Canvas 暂不支持。

插件不执行任意 C#，组件添加限白名单，不生成业务脚本、按钮回调或场景 EventSystem。预览实例化仍可能运行项目自带的 ExecuteAlways/OnValidate 脚本，不等于完整游戏运行模拟。

Unity 实测范围随版本推进而变化；基础连接脚本不覆盖所有接口，功能变动后应针对目标 Editor、渲染管线和资源进行联调。
