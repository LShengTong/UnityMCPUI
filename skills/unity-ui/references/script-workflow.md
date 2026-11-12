# 脚本、编译与引用绑定

适用于 Agent 判断需要脚本来实现目标效果的 uGUI，例如按钮交互、运行时内容更新、动画或动态布局。无需用户明确提出脚本要求；根据需求复用或编写代码，同时遵守用户明确的范围限制和项目已有架构。下列 JSON 是 MCP 工具参数；不要自行提供传输层 id、deadline 或 has_ 标记。

## 从视觉资源到脚本驱动

1. 创建或定位 prefab，用 inspect 确认已有节点、组件类型及序列化配置。根据需求确定脚本字段，不在运行时代码中依赖易变的 index_path。
2. 用可用的文件编辑能力编写目标项目 Assets 下的 C#。类名与文件名匹配，定义非抽象、非泛型的运行时 MonoBehaviour；字段应为 public 或带 SerializeField。根据用户任务编写逻辑，不把只声明字段的组件当成已经实现交互。
3. 调用 refresh_assets，参数为 `{}`。记录成功回执中的 refresh_id；成功表示主线程中的 AssetDatabase.Refresh 已返回，不表示后续脚本编译和重载已经完成。刷新可能导致响应中断，此时先查询状态，不直接重发。
4. 调用 get_compilation_status，例如：

```json
{"wait_seconds":60}
```

确认 is_compiling、is_updating、refresh_pending 均为 false，并检查：

- refresh_id 是否对应本次刷新；发现不同 ID 时核对是否有其他刷新请求。
- has_compilation_result 是否为 true，以及 compilation_revision 是否对应写入后观测到的编译。可在刷新前查询一次作为比较基准。
- errors 是否有错误；每项包含 file、line、column、message、assembly。最多 200 项，errors_truncated=true 表示不完整。按具体编译错误修复，再刷新和查询。

errors 为空且 has_compilation_result=false 表示没有观测到已完成的编译，不是编译成功。刷新没有代码变化时可以没有新一轮编译；不要因此无限刷新。若无法确认新脚本可用，明确未验证，不继续声称脚本已挂载。工具等待超时后检查当前状态，不能据此认为编译失败或操作未执行。

查询在程序集重载后会尝试重新读取发现文件，重连同一个已选项目并更新当前会话连接。桥接尚未恢复时可以在状态变化后重查；不要自动重发 prefab 写操作或刷新请求。编译错误来自已观测的编译事件，SessionState 可跨 domain reload 保留，不保证跨 Editor 重启保留，也不等于完整 Console 日志。

5. 编译完成后重新 inspect 获取 expected_version，再用批量动作挂载脚本和设置引用。成功后按本次改动核对相关组件及引用；已有证据足够的项目可复用结果。由 Agent 判断是否需要运行被修改的交互和结果断言，需要时使用 [Play 功能测试](play-testing.md)。验证结论对应实际执行范围，仍未确认的行为简要说明。

## 同批挂脚本并绑定字段

假设编写了 Assets/Scripts/UI/DialogView.cs，组件完整类型名是 MyGame.UI.DialogView，有两个字段：

```csharp
[SerializeField] private UnityEngine.UI.Button confirmButton;
[SerializeField] private UnityEngine.CanvasGroup content;
```

工具：batch_edit_ui_prefab。下列节点需已经存在于 inspect 返回的层级中，路径仅为示例：

```json
{
  "asset_path":"Assets/UI/Dialog.prefab",
  "expected_version":"替换为最近inspect返回的64位版本",
  "operations":[
    {"action":"add_script","index_path":"0","script_path":"Assets/Scripts/UI/DialogView.cs"},
    {"action":"set_reference","index_path":"0","component_type":"MyGame.UI.DialogView","field_path":"confirmButton","target_path":"0/1/0","target_component":"UnityEngine.UI.Button"},
    {"action":"set_reference","index_path":"0","component_type":"MyGame.UI.DialogView","field_path":"content","target_path":"0/1","target_component":"UnityEngine.CanvasGroup"}
  ]
}
```

add_script 通过 MonoScript.GetClass 解析实际类型，拒绝未编译的无效类型、Editor-only 脚本及重复组件。挂载可能触发 Editor 回调或 RequireComponent；不要把它描述成无副作用的字段声明。已有脚本组件只需 set_reference，不重复挂载。

set_reference 的定位规则：

- index_path 是持有字段的组件所在节点；component_type 是组件的完整 C# 类型名（含命名空间），按精确类型匹配。
- field_path 使用真实序列化字段名，支持单字段和 `settings.icon` 这样的嵌套字段；不支持数组、列表、属性 setter 或任意方法调用。
- target_path 是被引用对象所在节点。省略 target_component 则赋值该 GameObject；需要 Button、RectTransform 等组件时填写完整类型名。
- 同一节点有多个完全相同类型的组件时，明确传 component_index 或 target_component_index，序号从 0 开始且只计该精确类型。默认 -1 仅在类型唯一时可用。
- clear=true 将字段设为 null，无需 target_path；它不会删除节点或组件。

例如清空一个引用，工具仍为 batch_edit_ui_prefab：

```json
{
  "asset_path":"Assets/UI/Dialog.prefab",
  "expected_version":"替换为最近inspect返回的64位版本",
  "operations":[
    {"action":"set_reference","index_path":"0","component_type":"MyGame.UI.DialogView","field_path":"content","clear":true}
  ]
}
```

字段必须是可序列化的 UnityEngine.Object 引用且目标类型兼容。缺失字段、非引用字段、类型不匹配及未消除的组件歧义会失败。所有已有节点地址在批量开始前解析，组件在动作执行时查找，因此可以同批 add_script 后 set_reference，但不能引用本批新建的节点。保存前失败不写回本批编辑；检查 ok、phase、failed_operation_index、saved 再决定后续操作。

成功响应 added_scripts 和 assigned_references 分别报告添加脚本和绑定引用动作数。它们不证明运行时逻辑已通过验证。
