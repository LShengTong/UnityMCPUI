# Play 功能测试

用于确认用户要求的 UI 交互和运行时行为。先记录初始 Play 状态；用户只要求静态视觉时，不为截图额外启动游戏。测试执行现有项目脚本，可能影响实例以外的游戏状态，不是隔离游戏沙箱。

由 Agent 根据本次修改选择需要运行的交互、断言和截图。局部交互改动优先检查对应流程和直接受影响的状态；已有结果可覆盖未受影响的流程。仅在失败、依赖关系不清或影响扩大时增加测试，确认目标后结束验证。多分辨率交互检查用于涉及适配或命中区域变化的改动。

## 连接与场景

需要在 Edit 模式中查看层级、调整测试环境或保存可复用测试场景时，调用 `create_ui_test_scene`。

```json
{"scene_path":"Assets/Scenes/UiTest.unity","prefab_path":"Assets/UI/Dialog.prefab","reference_width":1920,"reference_height":1080}
```

目标文件夹需已存在，scene_path 使用尚未占用的 Assets/*.unity 路径。prefab_path 可省略，传入时使用根为 RectTransform 且不带 Canvas 的 UI prefab。工具保存场景、以附加方式打开并设为活动场景，保留原有场景及未保存修改。返回 scene_path、parent_path、prefab_instance_path、saved，检查 ok 后使用。场景含 Overlay Canvas、CanvasScaler、GraphicRaycaster、Content、EventSystem/项目输入模块和背景相机。该场景长期保留，后续可用 mode=current 进入 Play 验证。创建超时先检查输出资源，避免直接重试。

调用 get_play_status。需要进入 Play 时默认调用 enter_play_mode（mode="isolated"），自动创建临时测试场景，包含 Canvas/Content、EventSystem/输入模块和背景相机。进入前要求原场景已保存且无未保存修改、关闭 Prefab Mode；不要擅自保存用户修改。reference_width/reference_height 默认 1920×1080，只设置测试 Canvas 的参考分辨率。再调用：

```json
{"wait_for":"playing","wait_seconds":60}
```

此 JSON 属于 get_play_status。确认 is_playing=true、is_changing=false、is_paused=false；回执只表示已调度，不能直接判断切换成功。重载后状态查询自动重连同一个已选项目。写操作响应中断后先查状态和 test_instances，不盲目重发。

用 inspect_runtime_ui 读取场景和挂载位置：

```json
{"max_nodes":200}
```

空 path 枚举场景根；包含不激活节点及 DontDestroyOnLoad 根。检查 truncated，必要时缩小 path。指定节点时使用根到子级的名称路径，如 UIRoot/Canvas/PanelContainer；scene_name 可用场景名或场景资源路径消歧。返回 scene_path 含场景前缀，是诊断值，不能原样当作 path/parent_path 参数。

独立模式使用工具创建的环境，原 Edit 场景保持打开，进入 Play 时由临时启动场景替代。需要验证实际场景集成时显式传 mode="current"，该模式使用现成的活动 Canvas，以及唯一启用的 EventSystem 和输入模块，挂载时必须指定 parent_path。全局启动脚本仍可能执行或加载其他场景；独立模式不提供进程隔离。若条件缺失，按真实问题说明或在用户任务范围内配置；不要把挂载失败当成功。

## 挂载与操作

需要先切换分辨率再进行交互测试时，在 Edit 或 Play 调用 `get_game_view_resolution` 查询当前平台的预设、selected_index、actual_width/actual_height 和 can_restore。通过 `set_game_view_resolution` 传入 `{"width":720,"height":1280}`，或使用查询结果中的 `{"preset_index":0,"preset_group":"Standalone"}` 选择已有预设。固定宽高范围为 10～2048；不存在的固定尺寸会新增到 Unity 自定义预设。Play 下等待实际尺寸和帧数推进，Edit 下等待 Game view 目标尺寸稳定。检查 ok 和实际尺寸后继续操作，结束后调用 `restore_game_view_resolution`。首次切换保留原预设作为恢复点，连续切换不会覆盖，支持恢复 Free Aspect；退出 Play 也会尝试恢复。预设索引只适用于返回的当前平台组，内部 API 不兼容、原窗口关闭或原预设被删除时会返回失败。工具新增的自定义预设会保留。

工具 mount_test_prefab：

```json
{"prefab_path":"Assets/UI/Dialog.prefab"}
```

检查 ok，记录 test_instance_id 和 session_id。源 prefab 根不能带 Canvas；每个会话最多 16 个实例。实例根由工具命名为 __UnityMcpTest_ID，脚本需保留此名称，工具才会按原生对象 ID 和名称核对所有权。退出 Play 后旧 ID 失效。

工具 inspect_runtime_ui 可以用 test_instance_id 查询实例；空 path 或 "0" 指根，"0/1" 是当前索引路径，Panel/Label 是相对名称路径。名称重名会拒绝定位，先查询再选索引。run_ui_test 使用同样的实例内定位规则，每一步按当前运行时层级定位，区别于资产批量编辑的预绑定路径。

工具 run_ui_test：

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

- invoke_button：要求按钮活动且可交互，直接调用 onClick。它是回调测试，interaction_mode=callback，不证明鼠标能点到、不检查遮挡或射线。
- set_input_text：对 TMP_InputField 设置 text，notify=false 不通知值变化；这不是键盘输入、焦点或提交测试。
- assert / wait_condition：property 支持 active_self、active_in_hierarchy、text、interactable、reference_is_set、reference_path。expected 一律为字符串，布尔使用 "true" / "false"。引用条件还需 component_type 和 field_path，精确匹配唯一组件；reference_path 与 inspect 的完整场景路径比较。
- screenshot：真实 Game view 图片，每次最多一张。指定固定尺寸时成对设置 width/height 为 10～2048，省略或两者均为 0 使用当前尺寸。切换后等待连续两帧尺寸稳定、更新布局、核对 PNG 实际宽高，再在 finally 中恢复进入截图前选中的预设，包含 Free Aspect，额外最多等待 2 秒。切换或恢复无法确认则测试失败，不能缩放截图或用静态预览冒充。测试开始和截图前会检查帧推进；卡住、暂停或 Game view 不渲染时，打开 Game view 并恢复推进后再重测。

多分辨率验证使用同一个 test_instance_id 多次调用 run_ui_test，每次传一个目标尺寸，例如先 1280×720，再 1600×1200。游戏状态会继续推进，不自动冻结或重置；需要相同业务内容时先用断言确认。若要验证不同尺寸下的点击和拖拽，先独立切换分辨率，再运行交互步骤。

每批最多 32 步、总执行最多 15 秒；单步 wait_condition 最多 10 秒。遇到失败停止，返回 failed_step_index、steps 中的 expected/actual/error/frame_count。检查 ok 与 passed；返回图片附件时同时读取 JSON 结果，不能将附件存在当成全部通过。暂停或切换 Play 时应先恢复状态。桥接包会以约 30Hz 唤醒后台编辑器，在整个 Play 会话临时启用 Application.runInBackground，退出后恢复，并跨程序集重载保留恢复信息。不抢焦点；内部 SignalTick 不可用时会有警告，弹窗、断点和主线程阻塞仍可能卡住。检查 frame_count 确认实际推进，不能只凭 is_playing 或截图认定游戏在运行。

## 清理和反馈

clear_test_instances 可传 test_instance_id 只清理一个实例，或传 `{}` 清理所有工具实例。仅销毁核对所有权的根，销毁在帧末完成，不销毁场景已有 Canvas/EventSystem。根被重命名时不会擅自删除，cleared_instances 反映实际安排销毁的数量。

若本任务进入了 Play，结束后 exit_play_mode，再 get_play_status(wait_for="stopped", wait_seconds=60)。若用户原本就在运行游戏，默认保留其 Play 会话；退出会丢弃整个运行时场景状态，不仅是测试实例。

反馈明确列出检查过的回调和断言、是否观察到帧推进和 Game 截图、失败步骤及未覆盖项。不要声称通过真实鼠标输入、遮挡或完整游戏验证。回调脚本自行创建的外部对象、网络或存档副作用不由实例清理回滚。
