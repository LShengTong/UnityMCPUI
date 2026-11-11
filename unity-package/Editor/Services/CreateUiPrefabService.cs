using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMcp.Editor
{
    internal static class CreateUiPrefabService
    {
        static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        public static CreateUiPrefabResult Execute(double deadline, string output_path, UiPrefabSpec spec)
        {
            try
            {
                if (deadline <= Now) throw new Exception("Request expired before execution");
                return Create(output_path, spec, deadline);
            }
            catch (Exception error) { return new CreateUiPrefabResult { error = error.Message }; }
        }

        static void EnsureFolder(string folder)
        {
            string parent = "Assets";
            foreach (string part in folder.Substring("Assets".Length).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string child = parent + "/" + part;
                if (!AssetDatabase.IsValidFolder(child) && string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, part))) throw new Exception("Cannot create folder: " + child);
                parent = child;
            }
        }
        static CreateUiPrefabResult Create(string outputPath, UiPrefabSpec spec, double deadline)
        {
            string output = UiPrefabAssetUtility.ValidateAssetPath(outputPath, ".prefab");
            if (File.Exists(output) || File.Exists(output + ".meta") || AssetDatabase.LoadMainAssetAtPath(output) != null)
                throw new Exception("Output exists; overwrite is not supported");
            if (spec == null) throw new Exception("Missing UI spec");
            UiPrefabBridge.ValidateName(spec.name);
            if (spec.children == null || spec.children.Length == 0)
                throw new Exception("Missing UI children");
            int count = 0; UiPrefabBridge.Validate(spec.children, 1, ref count);
            var scene = EditorSceneManager.NewPreviewScene();
            bool saved = false;
            try
            {
                var root = new GameObject(spec.name, typeof(RectTransform));
                root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, scene);
                var rootRect = (RectTransform)root.transform;
                rootRect.anchorMin = Vector2.zero;
                rootRect.anchorMax = Vector2.one;
                rootRect.pivot = new Vector2(.5f, .5f);
                rootRect.sizeDelta = Vector2.zero;
                rootRect.anchoredPosition3D = Vector3.zero;
                foreach (var node in spec.children)
                {
                    UiPrefabBridge.Build(node, root.transform, false, output);
                    int builtCount = 0;
                    UiPrefabAssetUtility.CheckTree(root.transform, 0, ref builtCount);
                }
                int actualCount = 0;
                UiPrefabAssetUtility.CheckTree(root.transform, 0, ref actualCount);
                if (deadline < Now) throw new Exception("CreateUiPrefabRequest expired before saving");
                EnsureFolder(Path.GetDirectoryName(output).Replace('\\', '/'));
                if (File.Exists(output) || File.Exists(output + ".meta")) throw new Exception("Output appeared during creation; refusing overwrite");
                var prefab = UiPrefabAssetUtility.Save(root, output, Vector3.one, true, () => saved = true);
                if (prefab == null || !(prefab.transform is RectTransform) || prefab.GetComponent<Canvas>() != null)
                    throw new Exception("Saved prefab verification failed");
                if (prefab.transform.localScale != Vector3.one)
                    throw new Exception("Saved root scale differs from intended unit scale");
                return new CreateUiPrefabResult { ok = true, asset_path = output, node_count = count,
                    warning = "Mount this RectTransform prefab under the scene Canvas; CanvasScaler, GraphicRaycaster and EventSystem belong to the scene. Generated button callbacks are empty. Verify font glyph coverage and layout." };
            }
            catch
            {
                if (saved) AssetDatabase.DeleteAsset(output);
                throw;
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
