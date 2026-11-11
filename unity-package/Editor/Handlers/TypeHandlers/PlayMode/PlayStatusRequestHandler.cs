using System.Net;
using UnityEngine;
using System.Threading.Tasks;
namespace UnityMcp.Editor
{
    internal sealed class PlayStatusRequestHandler : IEditorRequestHandler
    {
        public PlayStatusRequestHandler() { PlayModeSession.Initialize(); }
        public Task Handle(HttpListenerContext context)
        {
            var status = PlayModeSession.Snapshot;
            return EditorHttpResponse.Send(context, 200, JsonUtility.ToJson(new PlayStatusReply {
                instance_id = EditorInstanceInfo.InstanceId,
                is_playing = status.is_playing,
                is_changing = status.is_changing,
                is_paused = status.is_paused,
                session_id = status.session_id,
                frame_count = status.frame_count,
                test_running = status.test_running,
                test_instances = status.test_instances,
                test_mode = status.test_mode,
                test_scene_path = status.test_scene_path,
                test_parent_path = status.test_parent_path,
                scene_restore_pending = status.scene_restore_pending,
                scene_restore_error = status.scene_restore_error,
                test_scene_error = status.test_scene_error
            }));
        }
    }
}
