using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.Editor
{
    [InitializeOnLoad]
    internal static class GameViewResolutionService
    {
        [Serializable]
        internal sealed class Selection
        {
            public ulong window;
            public string group, name, sizeType;
            public int index, width, height;
        }
        const string RestoreKey = "UnityMcp.GameViewResolution.Restore";
        static bool busy;
        static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        static GameViewResolutionService()
        {
            EditorApplication.playModeStateChanged += change => {
                if (change == PlayModeStateChange.EnteredEditMode) RestoreOnExit();
            };
            EditorApplication.quitting += RestoreOnExit;
        }

        internal static Selection CaptureSelection()
        {
            var adapter = new GameViewSizeAdapter(true);
            var preset = adapter.Presets()[adapter.Selected];
            return new Selection { window = adapter.WindowId, group = adapter.Group, index = preset.index,
                name = preset.name, sizeType = preset.size_type, width = preset.width, height = preset.height };
        }

        internal static void RestoreSelection(Selection selection)
        {
            var adapter = new GameViewSizeAdapter(false, selection.window);
            if (adapter.Group != selection.group) throw new Exception("Switch back to the original Game view platform group before restoring");
            var matches = adapter.Presets().Where(preset => preset.name == selection.name && preset.size_type == selection.sizeType
                && preset.width == selection.width && preset.height == selection.height).ToArray();
            var match = matches.FirstOrDefault(preset => preset.index == selection.index);
            if (match == null && matches.Length == 1) match = matches[0];
            if (match == null) throw new Exception("Original Game view preset was removed or became ambiguous");
            adapter.Select(match.index);
        }

        internal static async Task SetTemporary(int width, int height, double deadline, Selection selection)
        {
            var adapter = new GameViewSizeAdapter(false, selection.window);
            if (adapter.Group != selection.group) throw new Exception("Game view platform group changed before screenshot");
            adapter.Select(adapter.FixedPreset(width, height));
            await WaitStable(adapter, deadline);
        }

        internal static async Task WaitAfterRestore(Selection selection, double deadline)
        {
            await WaitStable(new GameViewSizeAdapter(false, selection.window), deadline);
        }

        internal static async Task<GameViewResolutionResult> Execute(string action, int width, int height,
            int presetIndex, string presetGroup, double deadline)
        {
            if (busy) return new GameViewResolutionResult { error = "A Game view resolution operation is already running" };
            busy = true;
            GameViewSizeAdapter adapter = null;
            try
            {
                CheckDeadline(deadline);
                if (EditorApplication.isPlaying != EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new Exception("Wait for the Play mode transition before changing Game view resolution");
                if (PlayModeSession.TestRunning) throw new Exception("Wait for the runtime test before changing Game view resolution");
                if (action == "restore")
                {
                    var original = Original() ?? throw new Exception("There is no saved Game view selection to restore");
                    RestoreSelection(original);
                    adapter = new GameViewSizeAdapter(false, original.window);
                    await WaitStable(adapter, deadline);
                    SessionState.EraseString(RestoreKey);
                }
                else
                {
                    adapter = new GameViewSizeAdapter(action == "set");
                    if (action == "set")
                    {
                        if (!string.IsNullOrEmpty(presetGroup) && presetGroup != adapter.Group)
                            throw new Exception("Preset group changed; query the current presets again");
                        if (presetIndex >= 0 && (width != 0 || height != 0))
                            throw new Exception("Choose a preset index or fixed dimensions");
                        if (presetIndex < 0 && (width < 10 || height < 10))
                            throw new Exception("Provide width and height together, at least 10 pixels per side");
                        int selected = presetIndex >= 0 ? presetIndex : adapter.FixedPreset(width, height);
                        if (selected >= adapter.Presets().Length) throw new Exception("Game view preset index is out of range");
                        if (Original() == null) SessionState.SetString(RestoreKey, JsonUtility.ToJson(CaptureSelection()));
                        adapter.Select(selected);
                        await WaitStable(adapter, deadline);
                    }
                    else if (action != "query") throw new Exception("Unknown Game view action");
                }
                var result = Read(adapter);
                result.ok = true;
                return result;
            }
            catch (Exception error)
            {
                GameViewResolutionResult result;
                try { result = adapter == null ? new GameViewResolutionResult() : Read(adapter); }
                catch { result = new GameViewResolutionResult(); }
                result.error = error.GetBaseException().Message;
                result.can_restore = Original() != null;
                return result;
            }
            finally { busy = false; }
        }

        static GameViewResolutionResult Read(GameViewSizeAdapter adapter)
        {
            var actual = adapter.Actual;
            return new GameViewResolutionResult { group = adapter.Group, selected_index = adapter.Selected,
                actual_width = Mathf.RoundToInt(actual.x), actual_height = Mathf.RoundToInt(actual.y),
                is_playing = EditorApplication.isPlaying, can_restore = Original() != null, presets = adapter.Presets() };
        }

        static async Task WaitStable(GameViewSizeAdapter adapter, double deadline)
        {
            deadline = Math.Min(deadline, Now + 6);
            bool playing = EditorApplication.isPlaying;
            int stable = 0, frame = Time.frameCount;
            Vector2 previous = Vector2.zero;
            while (stable < 2)
            {
                CheckDeadline(deadline);
                adapter.CheckGroup();
                if (EditorApplication.isPlaying != playing) throw new Exception("Play mode changed while resizing Game view");
                adapter.Window.Repaint();
                EditorApplication.QueuePlayerLoopUpdate();
                await Task.Delay(30);
                Vector2 actual = adapter.Actual;
                if (playing && Time.frameCount == frame) continue;
                var preset = adapter.Presets()[adapter.Selected];
                bool matches = actual.x > 0 && actual.y > 0 && (preset.size_type != "FixedResolution"
                    || (actual.x == preset.width && actual.y == preset.height));
                if (matches)
                    stable = actual == previous ? stable + 1 : 1;
                else stable = 0;
                previous = actual;
                frame = Time.frameCount;
            }
        }

        static void CheckDeadline(double deadline)
        {
            if (double.IsNaN(deadline) || double.IsInfinity(deadline) || Now >= deadline)
                throw new Exception("Game view resolution did not stabilize before the deadline; query actual dimensions before retrying");
        }

        static Selection Original()
        {
            string value = SessionState.GetString(RestoreKey, "");
            return string.IsNullOrEmpty(value) ? null : JsonUtility.FromJson<Selection>(value);
        }

        static void RestoreOnExit()
        {
            var original = Original();
            if (original == null) return;
            try { RestoreSelection(original); SessionState.EraseString(RestoreKey); }
            catch (Exception error) { Debug.LogWarning("Unity MCP Game view restore: " + error.GetBaseException().Message); }
        }
    }
}
