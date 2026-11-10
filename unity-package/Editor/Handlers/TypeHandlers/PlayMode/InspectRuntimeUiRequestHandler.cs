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
            return JsonUtility.ToJson(RuntimeUiInspection.Inspect(request));
        }
    }
}
