using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal static class RuntimeUiTestRunner
    {
        static bool resolutionChanged;
        static int previousWidth, previousHeight;
        static FullScreenMode previousMode;
        static RuntimePointerInput activePointer;
        static RuntimeUiTestRunner()
        {
            AssemblyReloadEvents.beforeAssemblyReload += RestoreTemporarySettings;
            EditorApplication.quitting += RestoreTemporarySettings;
            EditorApplication.playModeStateChanged += change => {
                if (change == PlayModeStateChange.ExitingPlayMode) RestoreTemporarySettings();
            };
        }
        static void RestoreTemporarySettings()
        {
            try { activePointer?.Dispose(); }
            finally { activePointer = null; }
            if (resolutionChanged) Screen.SetResolution(previousWidth, previousHeight, previousMode);
            resolutionChanged = false;
        }
        static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        static void Check(double deadline, string session)
        {
            PlayModeSession.NeedPlay();
            if (Now >= deadline) throw new Exception("Runtime wait/test deadline exceeded");
            if (PlayModeSession.Id != session) throw new Exception("Play session changed during the test");
        }
        internal static async Task<RuntimeUiTestResult> Run(double deadline, string test_instance_id, UiTestStep[] steps)
        {
            PlayModeSession.NeedPlay();
            if (PlayModeSession.TestRunning) throw new Exception("A runtime test is already running");
            if (steps.Count(step => step.action == "screenshot") > 1) throw new Exception("At most one screenshot per test");
            foreach (var step in steps.Where(step => step.action == "screenshot")) ValidateScreenshotSize(step.width, step.height);
            var root = PlayModeSession.Root(test_instance_id).transform;
            var session = PlayModeSession.Id;
            deadline = Math.Min(deadline, Now + 15);
            var results = new List<UiTestStepResult>();
            var reply = new RuntimeUiTestResult { 
                session_id = session, test_instance_id = test_instance_id, interaction_mode = "callback" };
            bool usesPointer = steps.Any(step => step.action.StartsWith("pointer_", StringComparison.Ordinal));
            if (usesPointer)
                reply.interaction_mode = steps.Any(step => step.action == "invoke_button" || step.action == "set_input_text")
                    ? "mixed" : "pointer";
            PlayModeSession.TestRunning = true;
            try
            {
                if (resolutionChanged) await RestoreResolution(session);
                // Start/first-frame initialization must run before invoking callbacks.
                await NextFrame(deadline, session);
                for (int i = 0; i < steps.Length; i++)
                {
                    var step = steps[i];
                    var result = new UiTestStepResult { index = i, action = step.action, path = step.path ?? "",
                        expected = step.expected ?? "", actual = "", error = "", png_base64 = "", interaction_mode = "callback" };
                    results.Add(result);
                    try
                    {
                        Check(deadline, session);
                        if (root == null) throw new Exception("Test root was destroyed");
                        if (step.action == "screenshot") await Capture(step, result, deadline, session);
                        else if (step.action.StartsWith("pointer_", StringComparison.Ordinal))
                        {
                            result.interaction_mode = "pointer";
                            result.path = step.action == "pointer_move" ? step.target_path : "";
                            if (activePointer == null) activePointer = new RuntimePointerInput();
                            await PointerStep(root, step, deadline, session);
                            result.actual = activePointer.Backend + " (" + activePointer.Position.x + ", " + activePointer.Position.y + ")";
                        }
                        else
                        {
                            var node = step.action == "wait_condition" ? null : PlayModeSession.Relative(root, step.path);
                            switch (step.action)
                            {
                                case "invoke_button":
                                    var button = node.GetComponent<UnityEngine.UI.Button>();
                                    if (button == null || !button.IsActive() || !button.IsInteractable())
                                        throw new Exception("Button must be active and interactable");
                                    button.onClick.Invoke();
                                    break;
                                case "set_input_text":
                                    var input = node.GetComponent<TMPro.TMP_InputField>();
                                    if (input == null || !input.IsActive() || !input.IsInteractable())
                                        throw new Exception("TMP_InputField must be active and interactable");
                                    if (step.notify) input.text = step.text; else input.SetTextWithoutNotify(step.text);
                                    result.actual = input.text;
                                    break;
                                case "wait_condition":
                                    double until = Math.Min(deadline, Now + step.timeout_seconds);
                                    do
                                    {
                                        Check(deadline, session);
                                        try { node = PlayModeSession.Relative(root, step.path); }
                                        catch { node = null; }
                                        result.actual = node == null ? "<missing>" : RuntimeUiInspection.Value(node, step);
                                        if (result.actual == step.expected) break;
                                        if (Now >= until) throw new Exception("Condition timed out");
                                        EditorApplication.QueuePlayerLoopUpdate();
                                        await Task.Delay(30);
                                    } while (true);
                                    break;
                                case "assert":
                                    result.actual = RuntimeUiInspection.Value(node, step);
                                    if (result.actual != step.expected) throw new Exception("Assertion failed");
                                    break;
                                default: throw new Exception("Unknown runtime test action");
                            }
                        }
                        result.ok = true;
                        result.frame_count = Time.frameCount;
                    }
                    catch (Exception error)
                    {
                        result.frame_count = EditorApplication.isPlaying ? Time.frameCount : 0;
                        result.error = error.Message.Substring(0, Math.Min(error.Message.Length, 2048));
                        reply.failed_step_index = i;
                        reply.error = "Step " + i + " (" + step.action + "): " + error.Message;
                        break;
                    }
                }
                reply.ok = reply.passed = reply.failed_step_index < 0;
                foreach (var result in results)
                    if (result.actual.Length > 4096) result.actual = result.actual.Substring(0, 4096);
                reply.steps = results.ToArray();
                return reply;
            }
            finally
            {
                try { activePointer?.Dispose(); }
                finally { activePointer = null; PlayModeSession.TestRunning = false; }
            }
        }
        static async Task PointerStep(Transform root, UiTestStep step, double deadline, string session)
        {
            switch (step.action)
            {
                case "pointer_move":
                    if (step.frames < 1 || step.frames > 60) throw new Exception("Pointer movement requires 1..60 frames");
                    var start = activePointer.Position;
                    var target = activePointer.Target(root, step);
                    for (int frame = 1; frame <= step.frames; frame++)
                    {
                        Check(deadline, session);
                        activePointer.Move(Vector2.Lerp(start, target, (float)frame / step.frames));
                        await NextFrame(deadline, session);
                    }
                    break;
                case "pointer_down": activePointer.Button(step.button, true); break;
                case "pointer_up": activePointer.Button(step.button, false); break;
                case "pointer_scroll": activePointer.Scroll(step.scroll_x, step.scroll_y); break;
                default: throw new Exception("Unknown pointer action");
            }
            // Allow input processing and EventSystem.Update to finish before the next assertion.
            await NextFrame(deadline, session);
            await NextFrame(deadline, session);
        }
        internal static void ValidateScreenshotSize(int width, int height)
        {
            if (width < 0 || height < 0 || width > 2048 || height > 2048 || (width == 0) != (height == 0))
                throw new Exception("Screenshot width and height must both be 1..2048, or both 0 for current size");
        }
        static async Task Capture(UiTestStep step, UiTestStepResult result, double deadline, string session)
        {
            ValidateScreenshotSize(step.width, step.height);
            if (step.width == 0)
            {
                await CaptureCurrent(result, deadline, session, 0, 0);
                return;
            }
            previousWidth = Screen.width;
            previousHeight = Screen.height;
            if (previousWidth < 1 || previousHeight < 1) throw new Exception("Game view is not initialized; open it before screenshot");
            previousMode = Screen.fullScreenMode;
            resolutionChanged = true;
            Exception failure = null;
            try
            {
                Screen.SetResolution(step.width, step.height, FullScreenMode.Windowed);
                await WaitResolution(step.width, step.height, Math.Min(deadline, Now + 4), session);
                Canvas.ForceUpdateCanvases();
                await NextFrame(deadline, session);
                await CaptureCurrent(result, deadline, session, step.width, step.height);
            }
            catch (Exception error) { failure = error; }
            finally
            {
                try
                {
                    // Public API restores dimensions/mode, not the original Game View dropdown preset.
                    await RestoreResolution(session);
                }
                catch (Exception restoreError)
                {
                    failure = new Exception((failure == null ? "" : failure.Message + "; ")
                        + "Could not confirm original Game view dimensions were restored: " + restoreError.Message);
                }
            }
            if (failure != null) throw failure;
        }
        static async Task RestoreResolution(string session)
        {
            Screen.SetResolution(previousWidth, previousHeight, previousMode);
            await WaitResolution(previousWidth, previousHeight, Now + 2, session);
            resolutionChanged = false;
        }
        static async Task WaitResolution(int width, int height, double deadline, string session)
        {
            // Resolution changes take effect at frame end. Confirm two advancing frames at the target size.
            try
            {
                for (int stableFrames = 0; stableFrames < 2;)
                {
                    Check(deadline, session);
                    await NextFrame(deadline, session);
                    if (Screen.width == width && Screen.height == height) stableFrames++;
                    else stableFrames = 0;
                }
            }
            catch (Exception error)
            {
                throw new Exception("Game view requested " + width + "x" + height
                    + ", actual " + Screen.width + "x" + Screen.height + ": " + error.Message);
            }
        }
        static async Task CaptureCurrent(UiTestStepResult result, double deadline, string session, int width, int height)
        {
            await NextFrame(deadline, session);
            if (Screen.width > 2048 || Screen.height > 2048 || Screen.width < 1 || Screen.height < 1)
                throw new Exception("Set Game view resolution to 1..2048 per side before screenshot");
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/UnityMcp"));
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "PlayCapture_" + Guid.NewGuid().ToString("N") + ".png");
            ScreenCapture.CaptureScreenshot(file);
            try
            {
                while (true)
                {
                    Check(deadline, session);
                    byte[] bytes = null;
                    try { if (File.Exists(file)) bytes = File.ReadAllBytes(file); } catch (IOException) { }
                    if (bytes != null && bytes.Length >= 36 && bytes[0] == 137 && bytes[1] == 80
                        && bytes.Skip(bytes.Length - 8).SequenceEqual(new byte[] { 73, 69, 78, 68, 174, 66, 96, 130 }))
                    {
                        if (bytes.Length > 12 * 1024 * 1024) throw new Exception("Game screenshot exceeds size limit");
                        result.width = ReadInt(bytes, 16); result.height = ReadInt(bytes, 20);
                        if (width != 0 && (result.width != width || result.height != height))
                            throw new Exception("Screenshot dimensions do not match requested size: " + result.width + "x" + result.height);
                        result.png_base64 = Convert.ToBase64String(bytes);
                        return;
                    }
                    EditorApplication.QueuePlayerLoopUpdate();
                    await Task.Delay(30);
                }
            }
            finally { DeleteCapture(file); }
        }
        static async Task NextFrame(double deadline, string session)
        {
            int frame = Time.frameCount;
            while (Time.frameCount <= frame)
            {
                Check(deadline, session);
                EditorApplication.QueuePlayerLoopUpdate();
                await Task.Delay(30);
            }
        }
        static void DeleteCapture(string file)
        {
            try { if (File.Exists(file)) { File.Delete(file); return; } } catch (IOException) { }
            {
                // Unity can finish writing after a timeout. Only this capture's GUID path is removed.
                double until = EditorApplication.timeSinceStartup + 60;
                EditorApplication.CallbackFunction cleanup = null;
                cleanup = () => {
                    if (File.Exists(file)) { try { File.Delete(file); } catch (IOException) { return; } }
                    else if (EditorApplication.timeSinceStartup < until) return;
                    EditorApplication.update -= cleanup;
                };
                EditorApplication.update += cleanup;
            }
        }
        static int ReadInt(byte[] data, int offset) => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
    }
}
