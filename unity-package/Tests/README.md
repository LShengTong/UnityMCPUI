# Unity EditMode 测试

这套测试在真实 Unity Editor 中运行，使用实际的 JsonUtility、AssetDatabase 和 Prefab API。它不属于 `scripts/check_protocol.py` 的 Python 模拟测试，也不会由当前 GitHub 协议检查工作流执行。

## 在项目中启用

使用 Unity 6000.4 或包声明支持的版本，先按主 README 安装本地 UPM 包。在目标 Unity 项目的 `Packages/manifest.json` 中合并以下配置，保留原有依赖和 testables 项，不要用示例覆盖整个文件：

```json
{
  "dependencies": {
    "com.local.unity-mcp": "file:E:/PycharmProjects/UnityMCPUI/unity-package",
    "com.unity.test-framework": "1.6.0"
  },
  "testables": [
    "com.local.unity-mcp"
  ]
}
```

把本地包路径改成你的路径；已安装兼容的 Test Framework 时保留项目现有版本。`testables` 让 Unity 发现该包内的测试程序集。普通包使用者无需启用测试，也无需把测试框架作为此包的运行依赖。

等待包导入和 C# 编译完成，打开 **Window → General → Test Runner → EditMode**，查找 `UnityMcp.Editor.Tests`。可以只运行某个测试类，也可以点击 **Run All**。

已经安装 Unity 官方 CLI 时，也可从命令行运行：

```powershell
unity test 'D:/path/to/UnityProject' --mode EditMode --filter 'UnityMcp.Editor.Tests' --output './.validation/unity-editmode.xml' --timeout 300
```

使用独立测试项目最方便；不要并发启动多个进程对同一个项目运行资产测试。协议 Python 测试仍通过 `scripts/update_protocol.py` 独立运行。

## 测试内容

| 文件 | 验证行为 |
| --- | --- |
| `Editor/ProtocolSerializationTests.cs` | JsonUtility 的缺省值、嵌套请求、偏移量存在标记、非法参数、图片导入默认值，以及成功/失败响应往返 |
| `Editor/PrefabIntegrationTests.cs` | 创建仅含 RectTransform 根节点的 UI Prefab，验证无 Canvas 基础组件，执行编辑动作、保存备份并恢复，校验 GUID 和原始内容；还覆盖版本冲突、前序动作删除目标后的失败、根激活状态和 Sprite 详情派生字段 |
| `Editor/RuntimeUiPathTests.cs` | 相对名称/索引寻址、重名拒绝和 Edit 模式运行时操作拒绝 |
| `Editor/RuntimeUiPlayTests.cs` | 从 EditMode 测试进入 Play，挂载到已有测试 Canvas，触发按钮回调、断言、运行时查询和清理，最后退出 Play |

Prefab 集成测试调用 Handler 的 `Execute` 路径，先执行生成的请求校验，再检查真实资源和返回的 JSON；它不覆盖 HTTP 鉴权、监听器或后台排队，也不检查 UI 视觉效果。`AssemblyInfo.cs` 仅向命名为 `UnityMcp.Editor.Tests` 的测试程序集开放 internal 访问。

RuntimeUiPlayTests 仍在 Test Runner 的 EditMode 页运行，由 EnterPlayMode/ExitPlayMode yield 指令切换。请在适合运行游戏的测试项目中执行，当前场景的项目脚本也会运行。测试临时禁用原 EventSystem，创建自己的 Canvas 和测试输入模块（生产 mount 工具不会创建这些；独立模式由 enter 工具创建），生成自己的 Assets/__UnityMcpPlayTests_GUID 资源并清理。不覆盖真实指针命中、Game 截图渲染或域重载中断恢复；截图传输与自动重连另有 Python stdio 模拟回归。

IsolatedPlaySceneTests 检查未保存修改会阻止准备、临时场景的 UI 环境、原场景列表及 Play 启动场景设置的保留与恢复、临时资源删除。需要已保存且无修改的 Edit 场景，并关闭 Prefab Mode，否则跳过。完整进入/退出 Play 及域重载恢复还应在 Unity 中实际验证；离线编译和 Python 模拟不能代替这些验证。

TmpFontTests 在 EditMode 下验证 static/dynamic 字体创建、追加/替换后的持久化、GUID 保留、缺失码点和拒绝覆盖。需要 TMP Essential Resources，以及项目内可找到的 TTF/OTF（包含 A/B/C）；缺少资源时跳过。测试复制源字体到专用 GUID 目录，结束后清理，不修改原字体。

实际多分辨率截图另有显式测试 ScreenshotsSwitchLandscapePortraitAndRestoreDimensions：需打开可渲染的 Game view，在 EditMode 页单独选择该测试执行，验证 640×480、720×1280 的 PNG 尺寸及每次恢复原宽高。它标记为 Explicit，不由普通 Run All 或无图形批处理自动执行；目标 Unity 版本若不支持实际尺寸切换，该测试应失败并保留诊断。

每个资源测试创建独有的 `Assets/__UnityMcpTests_<随机ID>/`。结束时只删除 GUID 仍属于本次测试的目录，以及本次响应返回的备份 ID 对应文件，不清空整个 `Library/UnityMcp/Backups`。使用 Panel 和测试生成的 PNG，不依赖项目字体、场景或现有 UI 资源。如果 Editor 被强制关闭，TearDown 可能来不及执行，留下的测试目录可按随机 ID 定位。

这些用例对预期协议行为做真实断言。例如默认值测试若失败，就意味着 JsonUtility 的实际行为与生成模型预期不同，需要据此修正实现；不能把离线编译通过当成这些测试已运行通过。
