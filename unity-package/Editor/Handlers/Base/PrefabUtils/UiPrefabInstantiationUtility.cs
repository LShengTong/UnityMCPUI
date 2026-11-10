using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal static class UiPrefabInstantiationUtility
    {
        public static GameObject Load(string path, string destination)
        {
            UiPrefabBridge.ValidateAssetPath(path, ".prefab");
            if (path == destination || AssetDatabase.GetDependencies(path, true).Contains(destination))
                throw new Exception("Prefab insertion would create a circular dependency: " + path);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null || !PrefabUtility.IsPartOfPrefabAsset(source) || !(source.transform is RectTransform))
                throw new Exception("Source must be an existing UI prefab with a RectTransform root: " + path);
            int count = 0;
            UiPrefabBridge.CheckTree(source.transform, 0, ref count);
            return source;
        }

        public static GameObject Instantiate(string path, Transform parent, string destination)
        {
            var source = Load(path, destination);
            return (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
        }

        public static void ApplyLayout(RectTransform rect, UiPrefabNode node)
        {
            if (node.has_anchor) UiRectTransformUtility.SetPreset(rect, node.anchor);
            if (node.has_width) rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, node.width);
            if (node.has_height) rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, node.height);
            var position = rect.anchoredPosition;
            if (node.has_x) position.x = node.x;
            if (node.has_y) position.y = node.y;
            rect.anchoredPosition = position;
            if (node.has_offset_min) rect.offsetMin = new Vector2(node.offset_min.x, node.offset_min.y);
            if (node.has_offset_max) rect.offsetMax = new Vector2(node.offset_max.x, node.offset_max.y);
        }
    }
}
