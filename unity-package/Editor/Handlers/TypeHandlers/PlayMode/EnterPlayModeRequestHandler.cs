using System;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
namespace UnityMcp.Editor
{
    internal sealed class EnterPlayModeRequestHandler : IEditorRequestHandler
    {
        public Task Handle(HttpListenerContext context) => PlayRequestQueue.Handle<EnterPlayModeRequest>(context,
            request => RequestProtocolValidation.Validate(request), request => Task.FromResult(Execute(request)), Error);
        static string Error(string id, string message) => JsonUtility.ToJson(new EnterPlayModeReply {
            id = id ?? "", instance_id = EditorInstanceInfo.InstanceId, error = message });
        internal static string Execute(EnterPlayModeRequest request)
        {
            var changed = PlayModeSession.Enter(request);
            return JsonUtility.ToJson(new EnterPlayModeReply { id = request.id, instance_id = EditorInstanceInfo.InstanceId, ok = true,
                transition_requested = changed, session_id = PlayModeSession.Id,
                test_mode = IsolatedPlayScene.Active ? "isolated" : "current", test_scene_path = IsolatedPlayScene.ScenePath,
                test_parent_path = IsolatedPlayScene.ParentPath, scene_restore_pending = IsolatedPlayScene.RestorePending,
                scene_restore_error = IsolatedPlayScene.RestoreError, test_scene_error = IsolatedPlayScene.TestError });
        }
    }
}
