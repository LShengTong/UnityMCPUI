using System;
using System.Net;
using System.Threading.Tasks;
using System.Linq;
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

        [Serializable] sealed class Reply
        {
            public string id, instance_id, asset_path, version, error, phase, failed_action, backup_id, backup_warning;
            public bool ok, saved;
            public int executed_operations, changed_properties, node_operations, added_components;
            public int failed_operation_index = -1;
        }
        sealed class BoundOperation
        {
            public UiBatchOperation request;
            public Transform node, parent, viewport, content;
        }
        static string Error(string id, string message) => JsonUtility.ToJson(new Reply {
            id = id ?? "", instance_id = EditorInstanceInfo.InstanceId, error = message, phase = "request"
        });
        static string Execute(BatchEditUiPrefabRequest request)
        {
            UiPrefabEditScope edit = null;
            int index = -1, executed = 0, modified = 0, addedComponents = 0;
            string phase = "prepare", action = "";
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
                            case "add": AddUiNodeOperation.Execute(root, bound.parent, operation); break;
                            case "delete": DeleteUiNodeOperation.Execute(root, bound.node); break;
                            case "move": MoveUiNodeOperation.Execute(root, bound.node, bound.parent, operation); break;
                        }
                    }
                    executed++;
                }
                phase = "save";
                index = -1;
                action = "";
                string version = edit.Save("Batch edit: " + modified + " property changes, " + (executed - modified - addedComponents) + " structural operations, " + addedComponents + " added components");
                return JsonUtility.ToJson(new Reply {
                    id = request.id, instance_id = EditorInstanceInfo.InstanceId, asset_path = request.asset_path,
                    version = version, ok = true, saved = true, phase = "complete",
                    backup_id = edit.BackupId, backup_warning = edit.BackupWarning,
                    executed_operations = executed, changed_properties = modified, node_operations = executed - modified - addedComponents, added_components = addedComponents
                });
            }
            catch (Exception e)
            {
                string message = index >= 0 ? "Operation " + index + " (" + action + "): " + e.Message : e.Message;
                if (edit != null && edit.Saved) message = "Prefab was saved, but result processing failed: " + message;
                return JsonUtility.ToJson(new Reply {
                    id = request.id, instance_id = EditorInstanceInfo.InstanceId, asset_path = request.asset_path,
                    error = message, phase = phase, failed_operation_index = index, failed_action = action,
                    executed_operations = executed, saved = edit != null && edit.Saved, backup_id = edit?.BackupId
                });
            }
            finally { edit?.Dispose(); }
        }
        static BoundOperation Bind(Transform root, UiBatchOperation operation)
        {
            if (operation == null) throw new Exception("Missing operation");
            if (!new[] { "modify", "add", "delete", "move", "add_component" }.Contains(operation.action))
                throw new Exception("Unknown operation action");
            var bound = new BoundOperation { request = operation };
            if (operation.action != "add")
            {
                bound.node = UiNodeUtility.Resolve(root, operation.index_path);
                if ((operation.action == "delete" || operation.action == "move") && bound.node == root)
                    throw new Exception("Cannot delete or move the prefab root");
            }
            if (operation.action == "add" || operation.action == "move")
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
