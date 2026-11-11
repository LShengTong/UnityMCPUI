using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal static class UiPrefabAssetUtility
    {
        static readonly string Project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        internal static string ValidateAssetPath(string value, string suffix = null)
        {
            if (string.IsNullOrEmpty(value) || !value.StartsWith("Assets/", StringComparison.Ordinal) ||
                value.Contains('\\') || value.Contains(':') || value.Split('/').Any(p => p == ".." || p == "." || p == "") ||
                (suffix != null && !value.EndsWith(suffix, StringComparison.Ordinal)))
                throw new Exception("Invalid Assets/ asset path: " + value);
            string current = Project;
            foreach (string part in value.Split('/'))
            {
                current = Path.Combine(current, part);
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new Exception("Asset paths through symlinks/junctions are unsupported");
            }
            return value;
        }

        internal static void CheckTree(Transform node, int depth, ref int count)
        {
            if (depth > 64 || ++count > 2000) throw new Exception("Prefab exceeds 64 levels or 2000 nodes");
            for (int i = 0; i < node.childCount; i++) CheckTree(node.GetChild(i), depth + 1, ref count);
        }

        public static GameObject LoadValidated(string path, string destination)
        {
            ValidateAssetPath(path, ".prefab");
            if (path == destination || AssetDatabase.GetDependencies(path, true).Contains(destination))
                throw new Exception("Prefab insertion would create a circular dependency: " + path);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null || !PrefabUtility.IsPartOfPrefabAsset(source) || !(source.transform is RectTransform))
                throw new Exception("Source must be an existing UI prefab with a RectTransform root: " + path);
            int count = 0;
            CheckTree(source.transform, 0, ref count);
            return source;
        }

        /// <summary>Save while preserving intended root state and avoiding live Canvas-driven transforms.</summary>
        public static GameObject Save(GameObject root, string path, Vector3 scale, bool activeSelf, Action onWritten)
        {
            bool previousActive = root.activeSelf;
            try
            {
                // Canvas/CanvasScaler must remain inactive throughout scene-object serialization.
                root.SetActive(false);
                root.transform.localScale = scale;
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                if (!success) throw new Exception("Prefab save failed");
                onWritten();
                if (prefab == null) prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) throw new Exception("Saved prefab unavailable");
                // Persistent prefab assets do not participate in scene Canvas layout. Restore only
                // these serialized fields here, keeping every child/component's enabled state intact.
                using (var serializedRoot = new SerializedObject(prefab))
                {
                    serializedRoot.FindProperty("m_IsActive").boolValue = activeSelf;
                    serializedRoot.ApplyModifiedPropertiesWithoutUndo();
                }
                using (var serializedTransform = new SerializedObject(prefab.transform))
                {
                    serializedTransform.FindProperty("m_LocalScale").vector3Value = scale;
                    serializedTransform.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SavePrefabAsset(prefab, out bool finalized);
                if (!finalized) throw new Exception("Prefab state finalization failed");
                if (prefab.activeSelf != activeSelf || prefab.transform.localScale != scale)
                    throw new Exception("Saved prefab active state or scale differs from intended state");
                return prefab;
            }
            finally { if (root != null) root.SetActive(previousActive); }
        }
    }
}
