using System;
using System.Net;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal sealed class BatchEditUiPrefabRequestHandler : IEditorRequestHandler
    {
        readonly EditorTaskQueue tasks;

        public BatchEditUiPrefabRequestHandler(EditorTaskQueue tasks)
        {
            this.tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        }

        public Task Handle(HttpListenerContext context) =>
            HttpRequestUtility.Handle<BatchEditUiPrefabRequest>(context, tasks, Execute, Error, Validate);

        static void Validate(BatchEditUiPrefabRequest request) => RequestProtocolValidation.Validate(request);

        sealed class BoundOperation
        {
            public UiBatchOperation request;
            public Transform node, parent, viewport, content, target;
        }
        static string Error(string id, string message) => JsonUtility.ToJson(new BatchEditUiPrefabReply {
            id = id ?? "", instance_id = EditorInstanceInfo.InstanceId, error = message, phase = "request"
        });
        internal static string Execute(BatchEditUiPrefabRequest request)
        {
            UiPrefabEditScope edit = null;
            int index = -1, executed = 0, modified = 0, addedComponents = 0, addedScripts = 0, assignedReferences = 0;
            string phase = "prepare", action = "";
            var created = new List<(int Index, Transform Node)>();
            try
            {
                if (request.operations == null || request.operations.Length < 1 || request.operations.Length > 128)
                    throw new Exception("Provide 1..128 operations");
                edit = new UiPrefabEditScope(request.asset_path, request.expected_version, request.deadline);
                var root = edit.Root.transform;
                var operations = new BoundOperation[request.operations.Length];
                phase = "resolve";
                // Bind every existing address before executing any edit, including property changes.
                for (index = 0; index < operations.Length; index++)
                {
                    edit.CheckDeadline();
                    var operation = request.operations[index];
                    action = operation?.action ?? "";
                    operations[index] = Bind(root, operation);
                }
                phase = "execute";
                for (index = 0; index < operations.Length; index++)
                {
                    edit.CheckDeadline();
                    var bound = operations[index];
                    var operation = bound.request;
                    action = operation.action;
                    if (operation.action == "modify")
                    {
                        if (bound.node == null || (bound.node != root && !bound.node.IsChildOf(root)))
                            throw new Exception("Target was removed by an earlier operation");
                        ModifyUiNodePropertiesOperation.Execute(bound.node.gameObject, new UiPropertyChange {
                            index_path = operation.index_path, property = operation.property, value = operation.value
                        });
                        modified++;
                    }
                    else if (operation.action == "add_component")
                    {
                        AddUiComponentOperation.Execute(root, bound.node, bound.viewport, bound.content, operation);
                        addedComponents++;
                    }
                    else
                    {
                        switch (operation.action)
                        {
                            case "add": AddUiNodeOperation.Execute(root, bound.parent, operation, request.asset_path); break;
                            case "add_script": AddUiScriptOperation.Execute(root, bound.node, operation); addedScripts++; break;
                            case "set_reference": SetUiReferenceOperation.Execute(root, bound.node, bound.target, operation); assignedReferences++; break;
                            case "duplicate":
                            case "instantiate_prefab": ReuseUiNodeOperation.Execute(root, bound.node, bound.parent, operation, request.asset_path); break;
                            case "delete": DeleteUiNodeOperation.Execute(root, bound.node); break;
                            case "move": MoveUiNodeOperation.Execute(root, bound.node, bound.parent, operation); break;
                        }
                    }
                    if (operation.action == "add" || operation.action == "duplicate" || operation.action == "instantiate_prefab")
                    {
                        int addedIndex = operation.sibling_index < 0 ? bound.parent.childCount - 1 : operation.sibling_index;
                        created.Add((index, bound.parent.GetChild(addedIndex)));
                        int count = 0;
                        UiPrefabBridge.CheckTree(root, 0, ref count);
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
                return JsonUtility.ToJson(new BatchEditUiPrefabReply {
                    id = request.id, instance_id = EditorInstanceInfo.InstanceId, asset_path = request.asset_path,
                    version = version, ok = true, saved = true, phase = "complete",
                    backup_id = edit.BackupId, backup_warning = edit.BackupWarning,
                    created_nodes = createdNodes,
                    executed_operations = executed, changed_properties = modified, node_operations = nodeOperations, added_components = addedComponents,
                    added_scripts = addedScripts, assigned_references = assignedReferences
                });
            }
            catch (Exception e)
            {
                string message = index >= 0 ? "Operation " + index + " (" + action + "): " + e.Message : e.Message;
                if (edit != null && edit.Saved) message = "Prefab was saved, but result processing failed: " + message;
                return JsonUtility.ToJson(new BatchEditUiPrefabReply {
                    id = request.id, instance_id = EditorInstanceInfo.InstanceId, asset_path = request.asset_path,
                    error = message, phase = phase, failed_operation_index = index, failed_action = action,
                    executed_operations = executed, saved = edit != null && edit.Saved, backup_id = edit?.BackupId
                });
            }
            finally { edit?.Dispose(); }
        }
        static string IndexPath(Transform root, Transform node)
        {
            var indices = new Stack<int>();
            while (node != root) { indices.Push(node.GetSiblingIndex()); node = node.parent; }
            return "0/" + string.Join("/", indices);
        }
        static BoundOperation Bind(Transform root, UiBatchOperation operation)
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
