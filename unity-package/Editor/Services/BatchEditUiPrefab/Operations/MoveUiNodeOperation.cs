using System;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal static class MoveUiNodeOperation
    {
        public static void Execute(Transform root, Transform node, Transform parent, UiBatchOperation operation)
        {
            UiNodeUtility.EnsureEditable(node, root);
            UiNodeUtility.EnsureEditable(parent, root);
            if (node == root) throw new Exception("Cannot move the prefab root");
            UiNodeUtility.CheckInsertionIndex(parent, operation.sibling_index, node);
            if (parent == node || parent.IsChildOf(node))
                throw new Exception("Cannot move a node into itself or its descendants");
            node.SetParent(parent, operation.world_position_stays);
            node.SetSiblingIndex(operation.sibling_index < 0 ? parent.childCount - 1 : operation.sibling_index);
        }
    }
}
