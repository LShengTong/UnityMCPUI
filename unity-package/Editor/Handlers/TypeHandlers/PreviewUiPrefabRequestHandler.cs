using System;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal sealed class PreviewUiPrefabRequestHandler : IEditorRequestHandler
    {
        readonly EditorTaskQueue tasks;

        public PreviewUiPrefabRequestHandler(EditorTaskQueue tasks)
        {
            this.tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        }

        public Task Handle(HttpListenerContext context) =>
            HttpRequestUtility.Handle<PreviewUiPrefabRequest>(context, tasks, Execute, Error, Validate);

        static void Validate(PreviewUiPrefabRequest request) => RequestProtocolValidation.Validate(request);

        static string Error(string id, string message) => JsonUtility.ToJson(new PreviewUiPrefabReply {
            id = id ?? "", instance_id = EditorInstanceInfo.InstanceId, error = message
        });

        internal static string Execute(PreviewUiPrefabRequest request)
        {
            var result = PreviewUiPrefabService.Execute(request.deadline, request.asset_path, request.width, request.height, request.background);
            return JsonUtility.ToJson(new PreviewUiPrefabReply {
                id = request.id, instance_id = EditorInstanceInfo.InstanceId,
                ok = result.ok,
                error = result.error,
                asset_path = result.asset_path,
                version = result.version,
                png_base64 = result.png_base64,
                width = result.width,
                height = result.height
            });
        }
    }
}
