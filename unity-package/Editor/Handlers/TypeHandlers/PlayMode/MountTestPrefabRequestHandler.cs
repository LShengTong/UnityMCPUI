using System;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
namespace UnityMcp.Editor
{
    internal sealed class MountTestPrefabRequestHandler : IEditorRequestHandler
    {
        public Task Handle(HttpListenerContext context) => PlayRequestQueue.Handle<MountTestPrefabRequest>(context,
            request => RequestProtocolValidation.Validate(request), request => Task.FromResult(Execute(request)), Error);
        static string Error(string id, string message) => JsonUtility.ToJson(new MountTestPrefabReply {
            id = id ?? "", instance_id = EditorInstanceInfo.InstanceId, error = message });
        internal static string Execute(MountTestPrefabRequest request)
        {
            return JsonUtility.ToJson(PlayModeSession.Mount(request));
        }
    }
}
