# Unity Editor HTTP 桥接

EditorServerLifecycle.cs 启动 127.0.0.1 动态端口监听，公布连接信息到
Library/UnityMcp/bridge.json。该文件只用于发现，不用于传递操作请求。

使用前通过 Package Manager 的 Add package from disk 选择上级 package.json。
等待编译完成并保持 Editor 打开。目标项目需要 uGUI 2.0.0。

GET /health 返回实例身份，不暴露 Ready；GET /project/info 返回 Editor 项目基本信息；POST /ui/prefabs 接收创建请求。
请求需要 Authorization: Bearer <token>，不接受浏览器 Origin 请求。
后台线程只处理网络，UI 创建经内存队列交给 Editor 主线程。
域重载或退出时关闭监听；Python 需重新 connect_project。

UiPrefabBridge.cs 负责构建和保存 uGUI Canvas Prefab。
支持 panel、image、text、button 和横纵布局；文本使用 TMP，需要默认字体或 font_path。
不覆盖已有资产，不添加按钮业务回调，不向目标场景添加 EventSystem。

当前 HTTP 改动尚未完成编译与运行验证。

Ready 仅在 Unity 内部使用：未就绪时保留排队请求，就绪后执行；未就绪时 HTTP 等待超时会标记取消，恢复就绪后由队列清理。Python 无需预先请求 health。所有 HTTP 响应携带 X-Unity-Instance，创建响应也包含 instance_id。

代码职责：
- EditorServerLifecycle.cs：注册 Editor 事件、启动与停止服务、发布和清理发现文件。
- EditorInstanceInfo.cs：仅保留协议常量，以及类内初始化的只读 InstanceId 和 Token，不保存项目路径或 Unity 版本。
- UiPrefabBridge.cs：实际构建和保存 uGUI Prefab。

- EditorPortListener.cs：尝试选择并绑定回环端口、后台接收循环、ContextReceived 事件以及关闭监听；由生命周期类持有和调用。

- Http/EditorHttpRequestHandler.cs：请求认证、接口路由、读取请求体、等待任务结果和发送响应。

- EditorTaskQueue.cs：任务类型、线程安全队列、容量预留和原子计数、取消任务清理及停止时取消；停止后不再接收入队。


发现文件的 Descriptor 与文件操作在 EditorDiscoveryFile；项目信息和 health 的响应结构与序列化在 EditorHttpRequestHandler。路径和版本在使用处读取；HTTP 响应 JSON 仍在主线程构造时生成，供后台安全返回。

- EditorDiscoveryFile.cs：写入仅含 port、token 的发现文件，替换发布，并按 token 校验归属后删除。实例 ID、项目路径和协议通过 HTTP 握手取得。

ContextReceived 在后台线程触发，EditorServerLifecycle 订阅事件后异步调用 handler；不阻塞接收循环。关闭服务时退订事件，关闭监听会解除 GetContextAsync 等待。每个 context 应只有一个负责响应的订阅者。

监听启动顺序：创建任务队列与请求处理器 → 创建监听并订阅 ContextReceived → Open 绑定端口并自动启动接收循环 → 发布发现文件。不再单独调用 StartReceiving。

HTTP 协调逻辑已合并到 EditorServerLifecycle：接收事件后调用 handler、在主线程就绪时调度 tasks.Update()，关闭时退订事件并取消任务。

生命周期类直接订阅 handler.OnContextReceived；事件入口、异步处理及异常收尾均由 EditorHttpRequestHandler 管理。

就绪判断现由 EditorTaskQueue.Update() 内部负责；生命周期只在服务未停止时调用 tasks.Update()，不再以端口是否监听作为任务调度条件。

各 HTTP 接口已独立：
- Http/CreateUiPrefabRequestHandler.cs：POST /ui/prefabs 的请求读取、入队和结果等待。
- Http/EditorHttpRequestHandler.cs：统一认证、路由和异常收尾。

接收处理与实际操作：
- Handler/：认证、路由、请求解析与校验，选择立即响应或放入队列。
- Operations/：具体操作方法。HealthOperation、ProjectInfoOperation 在主线程生成快照，接收时立即返回；CreateUiPrefabOperation 由队列在主线程执行。
- EditorTaskQueue.Pending 保存 Func<string> 执行委托，不保存请求 JSON，也不依赖 Prefab 类型。
- 队列仅检查就绪状态、管理容量与取消，并调用 job.Execute()、完成或失败通知。
- 创建请求入队前校验请求 ID 和截止时间，执行前再次检查是否过期；实际层级构建仍在 UiPrefabBridge。
本轮仅修改代码，未运行验证。

统一接收流程：
- Handler/CreateUiPrefabRequestHandler.cs：仅保留 UI 请求解析与参数校验，将实际 Execute 委托交给通用流程。
- 立即查询由路由直接调用 HealthOperation.Execute() / ProjectInfoOperation.Execute()；Operation 在主线程构造快照，后台 Execute 不访问 Unity API。
本轮未运行验证。


UI 请求在 CreateUiPrefabRequestHandler.Handle 内直接读取、解析和校验，成功后调用 queued.Handle(context, request, CreateUiPrefabOperation.Execute)。队列接收已解析参数，不再接收解析委托或定义解析异常。容量限制从解析后开始；读取阶段仍有单请求大小及超时限制。创建操作执行前再次检查截止时间。

排队与响应流程已统一到 EditorTaskQueue.EnqueueAndRespond(context, execute)：
预留名额、入队、等待、超时取消、释放名额及发送响应均由队列负责。
CreateUiPrefabRequestHandler 只读取和校验，再传入实际 Operation 执行委托。
EditorHttpResponse.cs 在 Editor 根目录封装 HTTP 发送，由队列及立即处理入口共用；不属于业务 Operations。
当前队列明确承担 HTTP 响应职责，实际操作仍不依赖 HttpListenerContext。本轮未运行验证。
