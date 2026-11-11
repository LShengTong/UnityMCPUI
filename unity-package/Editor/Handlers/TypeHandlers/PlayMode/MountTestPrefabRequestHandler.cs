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
            var result = PlayModeSession.Mount(request.prefab_path, request.parent_path, request.scene_name);
            return JsonUtility.ToJson(new MountTestPrefabReply {
                id = request.id, instance_id = EditorInstanceInfo.InstanceId,
                ok = result.ok,
                error = result.error,
                session_id = result.session_id,
                test_instance_id = result.test_instance_id,
                scene_path = result.scene_path
            });
        }
    }
}
