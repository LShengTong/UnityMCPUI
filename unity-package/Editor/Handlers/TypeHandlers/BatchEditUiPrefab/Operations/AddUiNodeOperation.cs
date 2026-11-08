using System;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal static class AddUiNodeOperation
    {
        public static void Execute(Transform root, Transform parent, UiBatchOperation operation)
        {
            UiNodeUtility.EnsureEditable(parent, root);
            UiNodeUtility.CheckInsertionIndex(parent, operation.sibling_index);
            if (operation.node == null) throw new Exception("Missing node specification");
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name == operation.node.name)
                    throw new Exception("A sibling already has this name");
            UiPrefabBridge.AddNode(operation.node, parent);
            var added = parent.GetChild(parent.childCount - 1);
            if (operation.sibling_index >= 0) added.SetSiblingIndex(operation.sibling_index);
        }
    }
}
