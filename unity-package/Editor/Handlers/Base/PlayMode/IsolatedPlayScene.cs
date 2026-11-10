using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMcp.Editor
{
    /// <summary>Owns only its GUID scene folder and temporary Play start-scene override.</summary>
    [InitializeOnLoad]
    internal static class IsolatedPlayScene
    {
        [Serializable] sealed class Entry { public string path; public bool loaded, active; }
        [Serializable] sealed class Recovery
        {
            public string folder, folderGuid, scene, previousStart, phase, error = "";
            public Entry[] original;
        }
        const string Key = "UnityMcp.IsolatedPlayScene";
        const string Prefix = "Assets/__UnityMcpPlayScene_";
        const string Parent = "UnityMcpTestCanvas/Content";
        static Recovery state;
        static bool recoverOnUpdate;
        internal static bool Active => state != null;
        internal static string ScenePath => state?.scene ?? "";
        internal static string ParentPath => Active ? Parent : "";
        internal static bool RestorePending => state != null && (!EditorApplication.isPlaying || state.phase != "playing");
        internal static string RestoreError => state?.phase == "restore_failed" ? state.error : "";
        internal static string TestError => EditorApplication.isPlaying && state?.phase == "playing" ? state.error : "";
        static IsolatedPlayScene()
        {
            try { state = JsonUtility.FromJson<Recovery>(SessionState.GetString(Key, "")); } catch { }
            recoverOnUpdate = Active && !EditorApplication.isPlayingOrWillChangePlaymode;
            EditorApplication.playModeStateChanged += change => {
                if (change == PlayModeStateChange.EnteredPlayMode && Active)
                {
                    state.phase = "playing";
                    try { ConfigureRuntimeInput(); }
                    catch (Exception error) { state.error = error.Message; }
                    Persist();
                }
                if (change == PlayModeStateChange.EnteredEditMode && Active) RequestRestore();
            };
            EditorApplication.update += () => {
                if (recoverOnUpdate && !EditorApplication.isPlayingOrWillChangePlaymode
                    && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
                {
                    recoverOnUpdate = false;
                    try { RestoreNow(); } catch { /* Exposed by the status snapshot; do not discard recovery data. */ }
                }
            };
        }
        static void Persist() => SessionState.SetString(Key, state == null ? "" : JsonUtility.ToJson(state));
        internal static void ValidateSavedScenes()
        {
            if (PrefabStageUtility.GetCurrentPrefabStage() != null) throw new Exception("Close Prefab Mode before isolated testing");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && (scene.isDirty || string.IsNullOrEmpty(scene.path)))
                    throw new Exception("Save scene changes before isolated testing: " + scene.name);
            }
        }
        internal static void Prepare(float width, float height)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Exit Play before preparing an isolated scene");
            if (Active) RestoreNow();
            ValidateSavedScenes();
            if (float.IsNaN(width) || float.IsInfinity(width) || width <= 0 || width > 16384
                || float.IsNaN(height) || float.IsInfinity(height) || height <= 0 || height > 16384)
                throw new Exception("Invalid test Canvas reference resolution");
            string folder = Prefix + Guid.NewGuid().ToString("N");
            var original = EditorSceneManager.GetSceneManagerSetup();
            state = new Recovery { folder = folder, scene = folder + "/UiTest.unity", phase = "preparing",
                previousStart = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
                original = original.Select(item => new Entry { path = item.path, loaded = item.isLoaded, active = item.isActive }).ToArray() };
            Persist();
            Scene temporary = default;
            var active = SceneManager.GetActiveScene();
            try
            {
                state.folderGuid = AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
                if (string.IsNullOrEmpty(state.folderGuid)) throw new Exception("Cannot create temporary test scene folder");
                Persist();
                temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                var canvas = new GameObject("UnityMcpTestCanvas", typeof(RectTransform));
                SceneManager.MoveGameObjectToScene(canvas, temporary);
                canvas.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = canvas.AddComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(width, height); scaler.matchWidthOrHeight = .5f;
                canvas.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                var content = new GameObject("Content", typeof(RectTransform));
                content.transform.SetParent(canvas.transform, false);
                var rect = (RectTransform)content.transform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero;
                var events = new GameObject("UnityMcpTestEventSystem", typeof(UnityEngine.EventSystems.EventSystem));
                SceneManager.MoveGameObjectToScene(events, temporary);
                // Input module is added on EnteredPlayMode, so transient default input actions are not serialized.
                var cameraObject = new GameObject("UnityMcpTestCamera", typeof(Camera));
                SceneManager.MoveGameObjectToScene(cameraObject, temporary);
                var camera = cameraObject.GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .12f, .12f);
                camera.cullingMask = 0;
                if (!EditorSceneManager.SaveScene(temporary, state.scene)) throw new Exception("Cannot save temporary test scene");
                EditorSceneManager.CloseScene(temporary, true); temporary = default;
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(state.scene)
                    ?? throw new Exception("Temporary test scene was not imported");
                state.phase = "ready"; Persist();
            }
            catch
            {
                if (temporary.IsValid() && temporary.isLoaded) EditorSceneManager.CloseScene(temporary, true);
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                RestoreNow();
                throw;
            }
        }
        static Scene TestScene()
        {
            var scene = SceneManager.GetSceneByPath(state.scene);
            if (!scene.IsValid() || !scene.isLoaded || SceneManager.sceneCount != 1)
                throw new Exception("Isolated scene is not the only loaded scene; check scene reload settings/project startup scripts");
            return scene;
        }
        static void ConfigureRuntimeInput()
        {
            var scene = TestScene();
            var events = scene.GetRootGameObjects().Single(go => go.name == "UnityMcpTestEventSystem");
#if ENABLE_INPUT_SYSTEM
            var module = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem")
                ?? throw new Exception("Input System is enabled but its UI input module is unavailable");
            events.AddComponent(module); // OnEnable assigns default input actions using the package's public API.
#else
            events.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
        }
        internal static Transform MountParent()
        {
            if (!Active) throw new Exception("parent_path is required for current-scene tests");
            if (!string.IsNullOrEmpty(TestError)) throw new Exception(TestError);
            var canvas = TestScene().GetRootGameObjects().Single(go => go.name == "UnityMcpTestCanvas");
            return canvas.transform.Find("Content") ?? throw new Exception("Isolated test container was removed");
        }
        internal static void RequestRestore()
        {
            if (!Active) return;
            state.phase = "restoring"; state.error = ""; Persist(); recoverOnUpdate = true;
        }
        internal static void RestoreNow()
        {
            if (!Active) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Exit Play before scene recovery");
            try
            {
                var previousStart = string.IsNullOrEmpty(state.previousStart) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(state.previousStart);
                if (!string.IsNullOrEmpty(state.previousStart) && previousStart == null) throw new Exception("Original Play start scene is missing: " + state.previousStart);
                EditorSceneManager.playModeStartScene = previousStart;
                var desired = state.original.Select(item => new SceneSetup { path = item.path, isLoaded = item.loaded, isActive = item.active }).ToArray();
                var current = EditorSceneManager.GetSceneManagerSetup();
                bool matches = current.Length == desired.Length && current.Zip(desired, (a, b) => a.path == b.path && a.isLoaded == b.isLoaded && a.isActive == b.isActive).All(value => value);
                if (!matches)
                {
                    ValidateSavedScenes();
                    foreach (var item in desired) if (AssetDatabase.LoadAssetAtPath<SceneAsset>(item.path) == null)
                        throw new Exception("Original scene is missing: " + item.path);
                    EditorSceneManager.RestoreSceneManagerSetup(desired);
                }
                string suffix = state.folder.StartsWith(Prefix, StringComparison.Ordinal) ? state.folder.Substring(Prefix.Length) : "";
                if (!Guid.TryParseExact(suffix, "N", out _) || state.scene != state.folder + "/UiTest.unity")
                    throw new Exception("Temporary scene ownership could not be verified");
                if (AssetDatabase.IsValidFolder(state.folder))
                {
                    if (AssetDatabase.AssetPathToGUID(state.folder) != state.folderGuid) throw new Exception("Temporary test folder was replaced");
                    if (!AssetDatabase.DeleteAsset(state.folder)) throw new Exception("Could not delete owned temporary test scene");
                }
                state = null; recoverOnUpdate = false; Persist();
            }
            catch (Exception error)
            {
                state.phase = "restore_failed"; state.error = error.Message; recoverOnUpdate = false; Persist();
                throw;
            }
        }
    }
}
