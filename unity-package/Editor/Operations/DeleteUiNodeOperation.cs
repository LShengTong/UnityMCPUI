using System;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal static class DeleteUiNodeOperation
    {
        public static void Execute(Transform root, Transform node)
        {
            UiNodeUtility.EnsureEditable(node, root);
            if (node == root) throw new Exception("Cannot delete the prefab root");
            if (node.GetComponentsInChildren<Transform>(true)
                .Any(t => PrefabUtility.IsPartOfPrefabInstance(t.gameObject)))
                throw new Exception("Cannot delete a subtree containing nested prefab instances");
            UnityEngine.Object.DestroyImmediate(node.gameObject);
        }
    }
}
