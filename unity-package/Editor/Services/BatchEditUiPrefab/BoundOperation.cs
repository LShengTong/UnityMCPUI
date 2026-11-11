using System;
using System.Linq;
using UnityEngine;

namespace UnityMcp.Editor
{
    /// <summary>Resolves all existing node addresses before batch edits change the hierarchy.</summary>
    internal sealed class BoundOperation
    {
        public UiBatchOperation request;
        public Transform node, parent, viewport, content, target;

        public static BoundOperation Bind(Transform root, UiBatchOperation operation)
        {
            if (operation == null) throw new Exception("Missing operation");
            if (!new[] { "modify", "add", "delete", "move", "add_component", "duplicate", "instantiate_prefab", "add_script", "set_reference" }.Contains(operation.action))
                throw new Exception("Unknown operation action");
            var bound = new BoundOperation { request = operation };
            if (operation.action == "set_reference" && !operation.clear)
                bound.target = UiNodeUtility.Resolve(root, operation.target_path);
            if (operation.action != "add" && operation.action != "instantiate_prefab")
            {
                bound.node = UiNodeUtility.Resolve(root, operation.index_path);
                if ((operation.action == "delete" || operation.action == "move" || operation.action == "duplicate") && bound.node == root)
                    throw new Exception("Cannot delete, move or duplicate the prefab root");
            }
            if (operation.action == "add" || operation.action == "move" || operation.action == "duplicate" || operation.action == "instantiate_prefab")
                bound.parent = UiNodeUtility.Resolve(root, operation.parent_path);
            if (operation.action == "add_component" && operation.component == "ScrollRect")
            {
                bound.viewport = UiNodeUtility.Resolve(root, operation.viewport_path);
                bound.content = UiNodeUtility.Resolve(root, operation.content_path);
            }
            return bound;
        }
    }
}
