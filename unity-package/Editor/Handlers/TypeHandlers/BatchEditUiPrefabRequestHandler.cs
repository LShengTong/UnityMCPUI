using System;
using System.Net;
using System.Threading.Tasks;
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

        static string Error(string id, string message) => JsonUtility.ToJson(new BatchEditUiPrefabReply {
            id = id ?? "", instance_id = EditorInstanceInfo.InstanceId, error = message, phase = "request"
        });
        internal static string Execute(BatchEditUiPrefabRequest request)
        {
            var result = BatchEditUiPrefabService.Execute(request.deadline, request.asset_path, request.expected_version, request.operations);
            return JsonUtility.ToJson(new BatchEditUiPrefabReply {
                id = request.id, instance_id = EditorInstanceInfo.InstanceId,
                ok = result.ok,
                error = result.error,
                asset_path = result.asset_path,
                version = result.version,
                phase = result.phase,
                failed_action = result.failed_action,
                backup_id = result.backup_id,
                backup_warning = result.backup_warning,
                saved = result.saved,
                executed_operations = result.executed_operations,
                changed_properties = result.changed_properties,
                node_operations = result.node_operations,
                added_components = result.added_components,
                failed_operation_index = result.failed_operation_index,
                created_nodes = result.created_nodes,
                added_scripts = result.added_scripts,
                assigned_references = result.assigned_references
            });
        }
    }
}
