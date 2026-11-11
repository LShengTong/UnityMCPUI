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
            var task = RuntimeUiTestRunner.Run(Deadline, mounted.test_instance_id, new[] {
                    new UiTestStep { action = "invoke_button", path = "Open" },
                    new UiTestStep { action = "wait_condition", path = "Content", property = "active_in_hierarchy", expected = "true", timeout_seconds = 1 },
                    new UiTestStep { action = "assert", path = "Content", property = "active_self", expected = "true" }
                });
            while (!task.IsCompleted) yield return null;
            Assert.That(task.IsFaulted, Is.False, task.Exception?.ToString());
            Assert.That(task.Result.passed, Is.True, task.Result.error);
            Assert.That(task.Result.interaction_mode, Is.EqualTo("callback"));
            var inspected = RuntimeUiInspection.Inspect(mounted.test_instance_id, "", "", 100);
            Assert.That(inspected.nodes.Any(node => node.name == "Content" && node.active_in_hierarchy), Is.True);
            var failed = RuntimeUiTestRunner.Run(Deadline, mounted.test_instance_id, new[] {
                    new UiTestStep { action = "assert", path = "Content", property = "active_self", expected = "false" } });
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
                var task = RuntimeUiTestRunner.Run(Deadline, mounted.test_instance_id, new[] {
                        new UiTestStep { action = "screenshot", width = size.x, height = size.y } });
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

        void ConfigurePointerModule()
        {
            events.GetComponent<RuntimeUiTestInputModule>().enabled = false;
            Type inputModule = null;
#if ENABLE_INPUT_SYSTEM
            inputModule = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
#endif
            if (inputModule != null) events.AddComponent(inputModule);
            else events.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            canvas.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        }

        [UnityTest]
        public IEnumerator PointerClickRespectsRaycastBlockerAndCleansUpHeldButtonAfterFailure()
        {
            if (EditorApplication.isPlaying) Assert.Ignore("Run from EditMode; test owns its Play transition");
            yield return new EnterPlayMode();
            entered = true;
            var mounted = MountFixture();
            ConfigurePointerModule();
            yield return null;
            var root = PlayModeSession.Root(mounted.test_instance_id).transform;
            var button = root.Find("Open").GetComponent<UnityEngine.UI.Button>();
            int clicks = 0;
            button.onClick.AddListener(() => clicks++);
            var blocker = new GameObject("Blocker", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            blocker.transform.SetParent(root, false);
            ((RectTransform)blocker.transform).sizeDelta = new Vector2(300, 300);
            var click = new[] {
                new UiTestStep { action = "pointer_move", target_path = "Open", frames = 1 },
                new UiTestStep { action = "pointer_down", button = "left" },
                new UiTestStep { action = "pointer_up", button = "left" }
            };
            var blocked = RuntimeUiTestRunner.Run(Deadline, mounted.test_instance_id, click);
            while (!blocked.IsCompleted) yield return null;
            Assert.That(blocked.IsFaulted, Is.False, blocked.Exception?.ToString());
            Assert.That(blocked.Result.passed, Is.True, blocked.Result.error);
            Assert.That(clicks, Is.Zero);
            blocker.SetActive(false);
            var failed = RuntimeUiTestRunner.Run(Deadline, mounted.test_instance_id, new[] {
                click[0], click[1], new UiTestStep { action = "assert", path = "Open", property = "active_self", expected = "false" }
            });
            while (!failed.IsCompleted) yield return null;
            Assert.That(failed.IsFaulted, Is.False, failed.Exception?.ToString());
            Assert.That(failed.Result.passed, Is.False);
            Assert.That(clicks, Is.Zero, "Cleanup must cancel the held press");
            var clicked = RuntimeUiTestRunner.Run(Deadline, mounted.test_instance_id, click);
            while (!clicked.IsCompleted) yield return null;
            Assert.That(clicked.IsFaulted, Is.False, clicked.Exception?.ToString());
            Assert.That(clicked.Result.passed, Is.True, clicked.Result.error);
            Assert.That(clicked.Result.interaction_mode, Is.EqualTo("pointer"));
            Assert.That(clicks, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator PointerWheelAndDragMoveScrollRectContent()
        {
            if (EditorApplication.isPlaying) Assert.Ignore("Run from EditMode; test owns its Play transition");
            yield return new EnterPlayMode();
            entered = true;
            var mounted = MountFixture();
            ConfigurePointerModule();
            var root = PlayModeSession.Root(mounted.test_instance_id).transform;
            var viewport = new GameObject("Scroll", typeof(RectTransform), typeof(UnityEngine.UI.Image),
                typeof(UnityEngine.UI.RectMask2D), typeof(UnityEngine.UI.ScrollRect));
            viewport.transform.SetParent(root, false);
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.sizeDelta = new Vector2(300, 200);
            var content = new GameObject("Items", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            content.transform.SetParent(viewportRect, false);
            var contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = Vector2.one;
            contentRect.pivot = new Vector2(.5f, 1);
            contentRect.sizeDelta = new Vector2(0, 800);
            var scroll = viewport.GetComponent<UnityEngine.UI.ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.inertia = false;
            scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 50;
            yield return null;
            Canvas.ForceUpdateCanvases();
            float initial = scroll.verticalNormalizedPosition;
            var wheel = RuntimeUiTestRunner.Run(Deadline, mounted.test_instance_id, new[] {
                new UiTestStep { action = "pointer_move", target_path = "Scroll", frames = 1 },
                new UiTestStep { action = "pointer_scroll", scroll_y = -2 }
            });
            while (!wheel.IsCompleted) yield return null;
            Assert.That(wheel.IsFaulted, Is.False, wheel.Exception?.ToString());
            Assert.That(wheel.Result.passed, Is.True, wheel.Result.error);
            Assert.That(scroll.verticalNormalizedPosition, Is.LessThan(initial));
            float afterWheel = scroll.verticalNormalizedPosition;
            var drag = RuntimeUiTestRunner.Run(Deadline, mounted.test_instance_id, new[] {
                new UiTestStep { action = "pointer_move", target_path = "Scroll", frames = 1 },
                new UiTestStep { action = "pointer_down", button = "left" },
                new UiTestStep { action = "pointer_move", target_path = "Scroll", y = 60, frames = 6 },
                new UiTestStep { action = "pointer_up", button = "left" }
            });
            while (!drag.IsCompleted) yield return null;
            Assert.That(drag.IsFaulted, Is.False, drag.Exception?.ToString());
            Assert.That(drag.Result.passed, Is.True, drag.Result.error);
            Assert.That(scroll.verticalNormalizedPosition, Is.LessThan(afterWheel));
        }

        MountTestPrefabResult MountFixture()
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
            var mounted = PlayModeSession.Mount(path, canvas.name, "");
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
