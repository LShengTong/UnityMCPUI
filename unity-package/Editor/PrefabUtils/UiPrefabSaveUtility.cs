using System;
using UnityEditor;
using UnityEngine;
namespace UnityMcp.Editor
{
    /// <summary>Do not serialize live screen-space Canvas-driven transforms from preview scenes.</summary>
    internal static class UiPrefabSaveUtility
    {
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
