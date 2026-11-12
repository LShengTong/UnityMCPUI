using System.Net;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal sealed class GameViewResolutionRequestHandler : IEditorRequestHandler
    {
        public Task Handle(HttpListenerContext context) => PlayRequestQueue.Handle<GameViewResolutionRequest>(context,
            request => RequestProtocolValidation.Validate(request), Execute, Error);

        static string Error(string id, string message) => JsonUtility.ToJson(new GameViewResolutionReply {
            id = id ?? "", instance_id = EditorInstanceInfo.InstanceId, error = message
        });

        internal static async Task<string> Execute(GameViewResolutionRequest request)
        {
            var result = await GameViewResolutionService.Execute(request.action, request.width, request.height,
                request.preset_index, request.preset_group, request.deadline);
            return JsonUtility.ToJson(new GameViewResolutionReply {
                id = request.id, instance_id = EditorInstanceInfo.InstanceId, ok = result.ok, error = result.error,
                group = result.group, selected_index = result.selected_index,
                actual_width = result.actual_width, actual_height = result.actual_height,
                is_playing = result.is_playing, can_restore = result.can_restore, presets = result.presets
            });
        }
    }
}
