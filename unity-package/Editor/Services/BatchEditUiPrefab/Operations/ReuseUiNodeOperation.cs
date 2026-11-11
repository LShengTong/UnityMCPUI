using System;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal static class ReuseUiNodeOperation
    {
        public static void Execute(Transform root, Transform source, Transform parent, UiBatchOperation operation, string destination)
        {
            UiNodeUtility.EnsureEditable(parent, root);
            UiNodeUtility.CheckInsertionIndex(parent, operation.sibling_index);
            int count = 0;
            UiPrefabAssetUtility.CheckTree(root, 0, ref count);
            string sourceName;
            GameObject asset = null;
            if (operation.action == "duplicate")
            {
                if (source == null || !source.IsChildOf(root) || source == root)
                    throw new Exception("Duplicate requires an existing non-root node");
                if (parent == source || parent.IsChildOf(source))
                    throw new Exception("Cannot duplicate a node into itself or its descendants");
                if (PrefabUtility.IsPartOfPrefabInstance(source.gameObject) && !PrefabUtility.IsAnyPrefabInstanceRoot(source.gameObject))
                    throw new Exception("Duplicate the nested prefab root instead of one of its internal nodes");
                sourceName = source.name;
            }
            else
            {
                asset = UiPrefabAssetUtility.LoadValidated(operation.prefab_path, destination);
                sourceName = asset.name;
            }
            string name = string.IsNullOrEmpty(operation.name)
                ? GameObjectUtility.GetUniqueNameForSibling(parent, sourceName) : operation.name;
            if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.IndexOfAny(new[] { '/', '\\' }) >= 0)
                throw new Exception("Invalid object name");
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name == name) throw new Exception("A sibling already has this name");
            var added = operation.action == "duplicate"
                ? UnityEngine.Object.Instantiate(source.gameObject, parent, false)
                : (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            added.name = name;
            if (operation.sibling_index >= 0) added.transform.SetSiblingIndex(operation.sibling_index);
        }
    }
}
