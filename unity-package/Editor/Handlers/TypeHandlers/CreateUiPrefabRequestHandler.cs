using System;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal sealed class CreateUiPrefabRequestHandler : IEditorRequestHandler
    {
        readonly EditorTaskQueue tasks;

        public CreateUiPrefabRequestHandler(EditorTaskQueue tasks)
        {
            this.tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        }

        public Task Handle(HttpListenerContext context) =>
            HttpRequestUtility.Handle<CreateUiPrefabRequest>(context, tasks, Execute, Error,
                request => RequestProtocolValidation.Validate(request));

        static string Error(string id, string message) => JsonUtility.ToJson(new CreateUiPrefabReply {
            id = id ?? "", instance_id = EditorInstanceInfo.InstanceId, ok = false, error = message
        });

        internal static string Execute(CreateUiPrefabRequest request)
        {
            var result = CreateUiPrefabService.Execute(request.deadline, request.output_path, request.spec);
            return JsonUtility.ToJson(new CreateUiPrefabReply {
                id = request.id, instance_id = EditorInstanceInfo.InstanceId,
                ok = result.ok,
                error = result.error,
                asset_path = result.asset_path,
                warning = result.warning,
                node_count = result.node_count
            });
        }
    }
}
