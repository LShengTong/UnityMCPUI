using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal static class BatchEditUiPrefabService
    {
        public static BatchEditUiPrefabResult Execute(double deadline, string asset_path, string expected_version, UiBatchOperation[] operations)
        {
            UiPrefabEditScope edit = null;
            int index = -1, executed = 0, modified = 0, addedComponents = 0, addedScripts = 0, assignedReferences = 0;
            string phase = "prepare", action = "";
            var created = new List<(int Index, Transform Node)>();
            try
            {
                if (operations == null || operations.Length < 1 || operations.Length > 128)
                    throw new Exception("Provide 1..128 operations");
                edit = new UiPrefabEditScope(asset_path, expected_version, deadline);
                var root = edit.Root.transform;
                var boundOperations = new BoundOperation[operations.Length];
                phase = "resolve";
                // Bind every existing address before executing any edit, including property changes.
                for (index = 0; index < operations.Length; index++)
                {
                    edit.CheckDeadline();
                    var operation = operations[index];
                    action = operation?.action ?? "";
                    boundOperations[index] = BoundOperation.Bind(root, operation);
                }
                phase = "execute";
                for (index = 0; index < operations.Length; index++)
                {
                    edit.CheckDeadline();
                    var bound = boundOperations[index];
                    var operation = bound.request;
                    action = operation.action;
                    switch (operation.action)
                    {
                        case "modify":
                            if (bound.node == null || (bound.node != root && !bound.node.IsChildOf(root)))
                                throw new Exception("Target was removed by an earlier operation");
                            ModifyUiNodePropertiesOperation.Execute(bound.node.gameObject, new UiPropertyChange {
                                index_path = operation.index_path, property = operation.property, value = operation.value
                            });
                            modified++;
                            break;
                        case "add_component":
                            AddUiComponentOperation.Execute(root, bound.node, bound.viewport, bound.content, operation);
                            addedComponents++;
                            break;
                        case "add": AddUiNodeOperation.Execute(root, bound.parent, operation, asset_path); break;
                        case "add_script": AddUiScriptOperation.Execute(root, bound.node, operation); addedScripts++; break;
                        case "set_reference": SetUiReferenceOperation.Execute(root, bound.node, bound.target, operation); assignedReferences++; break;
                        case "duplicate":
                        case "instantiate_prefab": ReuseUiNodeOperation.Execute(root, bound.node, bound.parent, operation, asset_path); break;
                        case "delete": DeleteUiNodeOperation.Execute(root, bound.node); break;
                        case "move": MoveUiNodeOperation.Execute(root, bound.node, bound.parent, operation); break;
                    }
                    if (operation.action == "add" || operation.action == "duplicate" || operation.action == "instantiate_prefab")
                    {
                        int addedIndex = operation.sibling_index < 0 ? bound.parent.childCount - 1 : operation.sibling_index;
                        created.Add((index, bound.parent.GetChild(addedIndex)));
                        int count = 0;
                        UiPrefabAssetUtility.CheckTree(root, 0, ref count);
                    }
                    executed++;
                }
                phase = "save";
                index = -1;
                action = "";
                var createdNodes = created.Where(item => item.Node != null && item.Node.IsChildOf(root))
                    .Select(item => new UiCreatedNode { operation_index = item.Index, index_path = IndexPath(root, item.Node) }).ToArray();
                int nodeOperations = executed - modified - addedComponents - addedScripts - assignedReferences;
                string version = edit.Save("Batch edit: " + modified + " property changes, " + nodeOperations + " structural operations, " + addedComponents + " added components, " + addedScripts + " scripts, " + assignedReferences + " references");
                return new BatchEditUiPrefabResult {
                    asset_path = asset_path,
                    version = version, ok = true, saved = true, phase = "complete",
                    backup_id = edit.BackupId, backup_warning = edit.BackupWarning,
                    created_nodes = createdNodes,
                    executed_operations = executed, changed_properties = modified, node_operations = nodeOperations, added_components = addedComponents,
                    added_scripts = addedScripts, assigned_references = assignedReferences
                };
            }
            catch (Exception e)
            {
                string message = index >= 0 ? "Operation " + index + " (" + action + "): " + e.Message : e.Message;
                if (edit != null && edit.Saved) message = "Prefab was saved, but result processing failed: " + message;
                return new BatchEditUiPrefabResult {
                    asset_path = asset_path,
                    error = message, phase = phase, failed_operation_index = index, failed_action = action,
                    executed_operations = executed, saved = edit != null && edit.Saved, backup_id = edit?.BackupId
                };
            }
            finally { edit?.Dispose(); }
        }
        static string IndexPath(Transform root, Transform node)
        {
            var indices = new Stack<int>();
            while (node != root) { indices.Push(node.GetSiblingIndex()); node = node.parent; }
            return "0/" + string.Join("/", indices);
        }
    }
}
