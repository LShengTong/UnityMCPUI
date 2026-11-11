using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMcp.Editor
{
    [InitializeOnLoad]
    internal static class PlayModeSession
    {
        [Serializable] sealed class Owned
        {
            public string id, prefab;
            public ulong entityId;
        }
        [Serializable] sealed class State
        {
            public string session = "";
            public bool transition;
            public List<Owned> roots = new List<Owned>();
        }
        const string Key = "UnityMcp.PlaySession";
        static State state;
        static volatile PlayModeStatus snapshot;
        public static bool TestRunning;
        public static PlayModeStatus Snapshot => snapshot;
        public static string Id => state.session;
        static PlayModeSession()
        {
            try { state = JsonUtility.FromJson<State>(SessionState.GetString(Key, "")); } catch { }
            if (state == null) state = new State();
            if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode) state = new State();
            else if (EditorApplication.isPlaying && EditorApplication.isPlayingOrWillChangePlaymode) state.transition = false;
            if (EditorApplication.isPlaying && string.IsNullOrEmpty(state.session)) state.session = Guid.NewGuid().ToString("N");
            EditorApplication.playModeStateChanged += change => {
                if (change == PlayModeStateChange.EnteredPlayMode)
                { state = new State { session = Guid.NewGuid().ToString("N") }; Persist(); }
                if (change == PlayModeStateChange.EnteredEditMode)
                { state = new State(); TestRunning = false; Persist(); }
                Publish();
            };
            EditorApplication.update += Publish;
            Publish();
        }
        // Explicit initialization for the status Handler/tests; HTTP queue initializes independently.
        internal static void Initialize() { }
        static void Persist() => SessionState.SetString(Key, JsonUtility.ToJson(state));
        internal static void NeedPlay()
        {
            if (!EditorApplication.isPlaying || state.transition) throw new Exception("Requires settled Play mode");
            if (EditorApplication.isPaused) throw new Exception("Play mode is paused; resume it before testing");
        }
        static GameObject OwnedObject(Owned owned)
        {
            var go = EditorUtility.EntityIdToObject(EntityId.FromULong(owned.entityId)) as GameObject;
            return go != null && go.name == "__UnityMcpTest_" + owned.id && go.scene.IsValid() ? go : null;
        }
        static void Publish()
        {
            var instances = state.roots.Select(owned => (owned, go: OwnedObject(owned))).Where(item => item.go != null)
                .Select(item => new PlayTestInstanceInfo { test_instance_id = item.owned.id,
                    prefab_path = item.owned.prefab, scene_path = ScenePath(item.go.transform) }).ToArray();
            snapshot = new PlayModeStatus {
                is_playing = EditorApplication.isPlaying, is_changing = state.transition ||
                    (EditorApplication.isPlaying != EditorApplication.isPlayingOrWillChangePlaymode)
                    || (IsolatedPlayScene.RestorePending && string.IsNullOrEmpty(IsolatedPlayScene.RestoreError)),
                is_paused = EditorApplication.isPaused, session_id = state.session,
                frame_count = EditorApplication.isPlaying ? Time.frameCount : 0, test_running = TestRunning, test_instances = instances,
                test_mode = IsolatedPlayScene.Active ? "isolated" : "current", test_scene_path = IsolatedPlayScene.ScenePath,
                test_parent_path = IsolatedPlayScene.ParentPath, scene_restore_pending = IsolatedPlayScene.RestorePending,
                scene_restore_error = IsolatedPlayScene.RestoreError, test_scene_error = IsolatedPlayScene.TestError };
        }
        internal static bool Enter(double deadline, string mode, float reference_width, float reference_height)
        {
            if (state.transition || EditorApplication.isPlaying != EditorApplication.isPlayingOrWillChangePlaymode)
                throw new Exception("Play mode transition already in progress");
            if (mode != "isolated" && mode != "current") throw new Exception("Unknown test mode");
            if (EditorApplication.isPlaying)
            {
                if ((mode == "isolated") != IsolatedPlayScene.Active) throw new Exception("Exit Play before switching test modes");
                return false;
            }
            if (IsolatedPlayScene.Active) IsolatedPlayScene.RestoreNow();
            if (mode == "isolated") IsolatedPlayScene.Prepare(reference_width, reference_height);
            try
            {
                double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                if (double.IsNaN(deadline) || double.IsInfinity(deadline)
                    || deadline <= now || deadline > now + 60)
                    throw new Exception("Expired or invalid request deadline");
                if (mode == "isolated") IsolatedPlayScene.ValidateSavedScenes();
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) throw new Exception("Editor became busy while preparing Play; query status before retrying");
                return Transition(true);
            }
            catch { IsolatedPlayScene.RequestRestore(); throw; }
        }
        internal static bool Transition(bool play)
        {
            if (state.transition || EditorApplication.isPlaying != EditorApplication.isPlayingOrWillChangePlaymode)
                throw new Exception("Play mode transition already in progress; query status");
            if (EditorApplication.isPlaying == play)
            {
                if (!play && IsolatedPlayScene.Active) { IsolatedPlayScene.RequestRestore(); Publish(); return true; }
                return false;
            }
            state.transition = true;
            Persist(); Publish();
            // Already running on the main thread. Enter/Exit requests schedule the native
            // transition themselves; delayCall can stall indefinitely in an unfocused Editor.
            try { if (play) EditorApplication.EnterPlaymode(); else EditorApplication.ExitPlaymode(); }
            catch { state.transition = false; IsolatedPlayScene.RequestRestore(); Persist(); Publish(); throw; }
            return true;
        }
        internal static GameObject Root(string id)
        {
            NeedPlay();
            var owned = state.roots.FirstOrDefault(item => item.id == id);
            var go = owned == null ? null : OwnedObject(owned);
            if (go == null) throw new Exception("Test instance unavailable or ownership changed; query Play status");
            return go;
        }
        internal static string ScenePath(Transform node)
        {
            var names = new Stack<string>();
            var scene = node.gameObject.scene;
            while (node != null) { names.Push(node.name); node = node.parent; }
            return scene.name + ":/" + string.Join("/", names);
        }
        internal static Transform Relative(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path) || path == "." || path == "0") return root;
            if (path.StartsWith("0/", StringComparison.Ordinal)) return UiNodeUtility.Resolve(root, path);
            var current = root;
            foreach (string part in path.Split('/'))
            {
                var matches = Enumerable.Range(0, current.childCount).Select(current.GetChild).Where(child => child.name == part).ToArray();
                if (matches.Length != 1) throw new Exception("Missing or ambiguous relative node: " + path);
                current = matches[0];
            }
            return current;
        }
        internal static Transform SceneNode(string path, string sceneName)
        {
            var parts = path.Split('/');
            if (parts.Length == 0 || parts.Any(string.IsNullOrEmpty)) throw new Exception("Expected root/child scene path");
            var roots = SceneRoots(sceneName).Where(go => go.name == parts[0]).ToArray();
            if (roots.Length != 1) throw new Exception("Missing or ambiguous scene root; specify scene_name and full path");
            return parts.Length == 1 ? roots[0].transform : Relative(roots[0].transform, string.Join("/", parts.Skip(1)));
        }
        internal static GameObject[] SceneRoots(string sceneName)
        {
            // Includes DontDestroyOnLoad roots, which are absent from SceneManager.sceneCount.
            return UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
                .Where(node => node.parent == null && node.gameObject.scene.IsValid() && node.gameObject.scene.isLoaded
                    && (node.gameObject.hideFlags & HideFlags.HideAndDontSave) == 0
                    && (string.IsNullOrEmpty(sceneName) || node.gameObject.scene.name == sceneName || node.gameObject.scene.path == sceneName))
                .Select(node => node.gameObject).ToArray();
        }
        internal static MountTestPrefabResult Mount(string prefab_path, string parent_path, string scene_name)
        {
            NeedPlay();
            if (state.roots.Count >= 16) throw new Exception("Clear test instances before mounting more (limit 16)");
            var parent = string.IsNullOrEmpty(parent_path) ? IsolatedPlayScene.MountParent()
                : SceneNode(parent_path, scene_name);
            if (IsolatedPlayScene.Active && parent.gameObject.scene.path != IsolatedPlayScene.ScenePath)
                throw new Exception("Isolated tests must mount into their own test scene");
            var canvas = parent.GetComponentInParent<Canvas>();
            if (!(parent is RectTransform) || !parent.gameObject.activeInHierarchy || canvas == null || !canvas.isActiveAndEnabled)
                throw new Exception("Parent must be an active RectTransform under an existing active Canvas");
            var events = UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>()
                .Where(system => system.isActiveAndEnabled).ToArray();
            if (events.Length != 1 || !events[0].GetComponents<UnityEngine.EventSystems.BaseInputModule>().Any(module => module.isActiveAndEnabled))
                throw new Exception("Scene must contain exactly one active EventSystem with an enabled input module");
            var source = UiPrefabAssetUtility.LoadValidated(prefab_path, null);
            if (source.GetComponent<Canvas>() != null) throw new Exception("Test prefab root must not contain a Canvas");
            string id = Guid.NewGuid().ToString("N");
            GameObject instance = null;
            try
            {
                instance = UnityEngine.Object.Instantiate(source, parent, false);
                instance.name = "__UnityMcpTest_" + id;
                state.roots.Add(new Owned { id = id, prefab = prefab_path, entityId = EntityId.ToULong(instance.GetEntityId()) });
                Persist(); Publish();
                return new MountTestPrefabResult { ok = true,
                    session_id = Id, test_instance_id = id, scene_path = ScenePath(instance.transform) };
            }
            catch { if (instance != null) UnityEngine.Object.Destroy(instance); throw; }
        }
        internal static int Clear(string id)
        {
            NeedPlay();
            var selected = state.roots.Where(item => string.IsNullOrEmpty(id) || item.id == id).ToArray();
            if (!string.IsNullOrEmpty(id) && selected.Length == 0) throw new Exception("Unknown test instance");
            int cleared = 0;
            foreach (var owned in selected)
            {
                var go = OwnedObject(owned);
                if (go != null) { UnityEngine.Object.Destroy(go); cleared++; }
                state.roots.Remove(owned);
            }
            Persist(); Publish();
            return cleared;
        }
    }
}
