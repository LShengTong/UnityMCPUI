using System;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
namespace UnityMcp.Editor
{
    internal sealed class InspectRuntimeUiRequestHandler : IEditorRequestHandler
    {
        public Task Handle(HttpListenerContext context) => PlayRequestQueue.Handle<InspectRuntimeUiRequest>(context,
            request => RequestProtocolValidation.Validate(request), request => Task.FromResult(Execute(request)), Error);
        static string Error(string id, string message) => JsonUtility.ToJson(new InspectRuntimeUiReply {
            id = id ?? "", instance_id = EditorInstanceInfo.InstanceId, error = message });
        internal static string Execute(InspectRuntimeUiRequest request)
        {
            var result = RuntimeUiInspection.Inspect(request.test_instance_id, request.scene_name, request.path, request.max_nodes);
            return JsonUtility.ToJson(new InspectRuntimeUiReply {
                id = request.id, instance_id = EditorInstanceInfo.InstanceId,
                ok = result.ok,
                error = result.error,
                session_id = result.session_id,
                nodes = result.nodes,
                truncated = result.truncated
            });
        }
    }
}
