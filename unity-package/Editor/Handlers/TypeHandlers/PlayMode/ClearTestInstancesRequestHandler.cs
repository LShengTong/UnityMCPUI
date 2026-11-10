using System;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
namespace UnityMcp.Editor
{
    internal sealed class ClearTestInstancesRequestHandler : IEditorRequestHandler
    {
        public Task Handle(HttpListenerContext context) => PlayRequestQueue.Handle<ClearTestInstancesRequest>(context,
            request => RequestProtocolValidation.Validate(request), request => Task.FromResult(Execute(request)), Error);
        static string Error(string id, string message) => JsonUtility.ToJson(new ClearTestInstancesReply {
            id = id ?? "", instance_id = EditorInstanceInfo.InstanceId, error = message });
        internal static string Execute(ClearTestInstancesRequest request)
        {
            int count = PlayModeSession.Clear(request.test_instance_id);
            return JsonUtility.ToJson(new ClearTestInstancesReply { id = request.id, instance_id = EditorInstanceInfo.InstanceId, ok = true, cleared_instances = count });
        }
    }
}
