using System;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal static class UiNodeUtility
    {
        public static Transform Resolve(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path)) throw new Exception("Missing index_path");
            var parts = path.Split('/');
            if (parts[0] != "0" || parts.Length > 65) throw new Exception("Invalid index_path");
            var node = root;
            foreach (string part in parts.Skip(1))
            {
                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                    || index < 0 || index >= node.childCount || index.ToString(CultureInfo.InvariantCulture) != part)
                    throw new Exception("Node does not exist: " + path);
                node = node.GetChild(index);
            }
            return node;
        }
        public static void EnsureEditable(Transform node, Transform root)
        {
            if (node == null || (node != root && !node.IsChildOf(root)))
                throw new Exception("Node was removed by an earlier operation");
            if (PrefabUtility.IsPartOfPrefabInstance(node.gameObject))
                throw new Exception("Structural edits inside nested prefab instances or variants are unsupported");
        }
        public static void CheckInsertionIndex(Transform parent, int index, Transform moving = null)
        {
            if (!(parent is RectTransform)) throw new Exception("UI parent requires RectTransform");
            int count = parent.childCount - (moving != null && moving.parent == parent ? 1 : 0);
            if (index < -1 || index > count) throw new Exception("Sibling insertion index out of range");
        }
    }
}
