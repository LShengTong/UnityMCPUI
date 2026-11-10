using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityMcp.Editor.Tests
{
    public sealed class RuntimeUiPlayTests
    {
        string folder;
        string folderGuid;
        bool entered;
        GameObject canvas, events;
        UnityEngine.EventSystems.EventSystem[] previousEvents;
        static double Deadline => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 + 20;

        [UnityTest]
        public IEnumerator MountInvokeAssertInspectAndClearUsesExistingCanvas()
        {
            if (EditorApplication.isPlaying) Assert.Ignore("Run from EditMode; test owns its Play transition");
            yield return new EnterPlayMode();
            entered = true;
            yield return null;
            Assert.That(Application.runInBackground, Is.True, "Bridge enables background execution for the whole Play session");
            var mounted = MountFixture();
            var root = PlayModeSession.Root(mounted.test_instance_id);
            Assert.That(root.GetComponent<Canvas>(), Is.Null);
            Assert.That(root.transform.parent, Is.EqualTo(canvas.transform));
            var content = root.transform.Find("Content").gameObject;
            root.transform.Find("Open").GetComponent<UnityEngine.UI.Button>().onClick.AddListener(() => content.SetActive(true));
            var task = RuntimeUiTestRunner.Run(new RunUiTestRequest { id = Guid.NewGuid().ToString("N"), deadline = Deadline,
                test_instance_id = mounted.test_instance_id, steps = new[] {
                    new UiTestStep { action = "invoke_button", path = "Open" },
                    new UiTestStep { action = "wait_condition", path = "Content", property = "active_in_hierarchy", expected = "true", timeout_seconds = 1 },
                    new UiTestStep { action = "assert", path = "Content", property = "active_self", expected = "true" }
                } });
            while (!task.IsCompleted) yield return null;
            Assert.That(task.IsFaulted, Is.False, task.Exception?.ToString());
            Assert.That(task.Result.passed, Is.True, task.Result.error);
            Assert.That(task.Result.interaction_mode, Is.EqualTo("callback"));
            var inspected = RuntimeUiInspection.Inspect(new InspectRuntimeUiRequest { id = "test", deadline = Deadline,
                test_instance_id = mounted.test_instance_id, max_nodes = 100 });
            Assert.That(inspected.nodes.Any(node => node.name == "Content" && node.active_in_hierarchy), Is.True);
            var failed = RuntimeUiTestRunner.Run(new RunUiTestRequest { id = "test", deadline = Deadline,
                test_instance_id = mounted.test_instance_id, steps = new[] {
                    new UiTestStep { action = "assert", path = "Content", property = "active_self", expected = "false" } } });
            while (!failed.IsCompleted) yield return null;
            Assert.That(failed.Result.ok, Is.False);
            Assert.That(failed.Result.failed_step_index, Is.EqualTo(0));
            Assert.That(failed.Result.steps[0].actual, Is.EqualTo("true"));
            Assert.That(PlayModeSession.Clear(mounted.test_instance_id), Is.EqualTo(1));
            yield return null;
            Assert.That(root == null, Is.True);
            Assert.That(canvas != null && events != null, Is.True);
        }

        [UnityTest, Explicit("Requires a rendering Game view; select this test explicitly, not headless")]
        public IEnumerator ScreenshotsSwitchLandscapePortraitAndRestoreDimensions()
        {
            if (EditorApplication.isPlaying) Assert.Ignore("Test owns its Play transition");
            yield return new EnterPlayMode();
            entered = true;
            var mounted = MountFixture();
            int originalWidth = Screen.width, originalHeight = Screen.height;
            foreach (var size in new[] { new Vector2Int(640, 480), new Vector2Int(720, 1280) })
            {
                var task = RuntimeUiTestRunner.Run(new RunUiTestRequest { id = Guid.NewGuid().ToString("N"), deadline = Deadline,
                    test_instance_id = mounted.test_instance_id, steps = new[] {
                        new UiTestStep { action = "screenshot", width = size.x, height = size.y } } });
                while (!task.IsCompleted) yield return null;
                Assert.That(task.IsFaulted, Is.False, task.Exception?.ToString());
                Assert.That(task.Result.passed, Is.True, task.Result.error);
                Assert.That(task.Result.steps[0].width, Is.EqualTo(size.x));
                Assert.That(task.Result.steps[0].height, Is.EqualTo(size.y));
                Assert.That(task.Result.steps[0].png_base64, Is.Not.Empty);
                Assert.That(Screen.width, Is.EqualTo(originalWidth));
                Assert.That(Screen.height, Is.EqualTo(originalHeight));
            }
        }

        MountTestPrefabReply MountFixture()
        {
            PlayModeSession.Initialize();
            previousEvents = UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>()
                .Where(item => item.enabled).ToArray();
            foreach (var item in previousEvents) item.enabled = false;
            canvas = new GameObject("__UnityMcpPlayCanvas_" + Guid.NewGuid().ToString("N"), typeof(RectTransform), typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            events = new GameObject("__UnityMcpPlayEvents", typeof(UnityEngine.EventSystems.EventSystem));
            // A fixture input module avoids depending on either project's input backend.
            events.AddComponent<RuntimeUiTestInputModule>();
            folder = "Assets/__UnityMcpPlayTests_" + Guid.NewGuid().ToString("N");
            folderGuid = AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            string path = folder + "/Panel.prefab";
            var source = new GameObject("Panel", typeof(RectTransform));
            try
            {
                var button = new GameObject("Open", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
                button.transform.SetParent(source.transform, false);
                var panel = new GameObject("Content", typeof(RectTransform));
                panel.transform.SetParent(source.transform, false);
                panel.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(source, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
            var mounted = PlayModeSession.Mount(new MountTestPrefabRequest { id = Guid.NewGuid().ToString("N"),
                deadline = Deadline, prefab_path = path, parent_path = canvas.name });
            Assert.That(mounted.ok, Is.True);
            return mounted;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (!entered) yield break;
            if (EditorApplication.isPlaying)
            {
                if (canvas != null) UnityEngine.Object.Destroy(canvas);
                if (events != null) UnityEngine.Object.Destroy(events);
                if (previousEvents != null) foreach (var item in previousEvents) if (item != null) item.enabled = true;
                yield return null;
            }
            if (!string.IsNullOrEmpty(folderGuid) && AssetDatabase.AssetPathToGUID(folder) == folderGuid) AssetDatabase.DeleteAsset(folder);
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
            entered = false;
        }
    }
}
