using System;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
namespace UnityMcp.Editor
{
    internal sealed class ExitPlayModeRequestHandler : IEditorRequestHandler
    {
        public Task Handle(HttpListenerContext context) => PlayRequestQueue.Handle<ExitPlayModeRequest>(context,
            request => RequestProtocolValidation.Validate(request), request => Task.FromResult(Execute(request)), Error);
        static string Error(string id, string message) => JsonUtility.ToJson(new ExitPlayModeReply {
            id = id ?? "", instance_id = EditorInstanceInfo.InstanceId, error = message });
        internal static string Execute(ExitPlayModeRequest request)
        {
            var changed = PlayModeSession.Transition(false);
            return JsonUtility.ToJson(new ExitPlayModeReply { id = request.id, instance_id = EditorInstanceInfo.InstanceId, ok = true, transition_requested = changed });
        }
    }
}
